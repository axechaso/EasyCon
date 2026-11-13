using EasyCon.Core.Capabilities;
using EasyCon.Core.Hosting;
using EasyCon.Core.Maa;
using EasyCon.Core.Script;
using EasyDevice;
using EasyDevice.Connection;
using EasyScript;
using System.Diagnostics;
using System.Text.Json;

namespace EasyCon.Tests;

// Explicit measurements, not CI timing thresholds. Uses the real interpreter,
// input lifetime and device writer, but a fake transport (no console/MCU).
[TestFixture, NonParallelizable]
public sealed class HighResolutionTimingBenchmarks
{
    [Test, Explicit, Category("TimingBenchmark")]
    public void GoldRoute_RepeatedCycles()
    {
        const int rounds = 12;
        string source = """
            FOR 5
                B
                200
            NEXT
            FOR 12
                $start=TIME()
                LS DOWN,3600
                LS RESET
                500
                B DOWN
                LS LEFT,500
                LS UP,800
                LS LEFT,1500
                LS UP,1000
                LS RIGHT,1950
                LS UP,5500
                B UP
                LS RESET
                $route=TIME()
                FOR 6
                    A
                    800
                NEXT
                $a=TIME()
                FOR 6
                    B
                    500
                NEXT
                $b=TIME()
                $cycle=$b-$start
                $routeElapsed=$route-$start
                $aElapsed=$a-$route
                $bElapsed=$b-$a
                PRINT $cycle & "," & $routeElapsed & "," & $aElapsed & "," & $bElapsed
            NEXT
            """;
        Sink connection = new();
        using MaaControllerSession controller = new(new Device(connection));
        Assert.That(controller.TryConnect("fake:timing-benchmark"), Is.EqualTo(ControllerConnectResult.Success));
        using ControllerRunLease run = controller.BeginRun("timing-benchmark", default);
        Samples samples = new();
        using CapabilityLease lease = ScriptHostAssembler.Assemble(new ScriptHostContext
        {
            PadInput = controller.CreatePad(DelayType.HighResolution, run),
            HighResolutionTiming = true,
            Console = samples,
            EnableOcr = false,
            EnableInference = false,
        });
        IScriptSession session = new EasyScriptEngine().FromSource(source, new ScriptHostOptions
        {
            Compile = ScriptCompileProfiles.Interactive(),
        });
        Assert.That(session.Info.Diagnostics.Where(d => d.IsError), Is.Empty);
        session.Run(run.Token, lease.Capabilities);
        Assert.That(samples.Rows, Has.Count.EqualTo(rounds));
        Assert.That(run.Binding.HasNative, Is.False);
        WriteReport("gold-route", new
        {
            Transport = "fake (no MCU or game)",
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Stopwatch.Frequency,
            Rows = samples.Rows,
            CycleSummary = Summarize(samples.Rows.Select(r => (double)r[0]).ToArray()),
            Writes = connection.Writes,
        });
    }

    [Test, Explicit, Category("TimingBenchmark")]
    public void WaitShapes_InterleavedSamples()
    {
        (string Name, Action<int, CancellationToken> Delay)[] paths =
        [
            ("1.6.3-source-wait", LegacyWait),
            ("170-previous-wait", CustomDelay.Delay),
            ("170-candidate-wait", CustomDelay.HighResolutionDelay),
        ];
        List<double>[] samples = [[], [], []];
        using CancellationTokenSource cancellation = new();
        foreach (var path in paths) path.Delay(50, cancellation.Token);
        // Rotate order to avoid making one implementation always run cold.
        for (int n = 0; n < 200; n++)
            for (int offset = 0; offset < paths.Length; offset++)
            {
                int i = (n + offset) % paths.Length;
                long started = Stopwatch.GetTimestamp();
                paths[i].Delay(50, cancellation.Token);
                samples[i].Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        WriteReport("wait-shapes", paths.Select((path, i) => new
        {
            path.Name,
            Summary = Summarize(samples[i].ToArray()),
            Samples = samples[i],
        }));
    }

    private static void LegacyWait(int milliseconds, CancellationToken token)
    {
        // Exact high-resolution algorithm from upstream tag 1.6.3.
        token.ThrowIfCancellationRequested();
        int started = (int)(DateTime.Now.Ticks / 10_000);
        if (milliseconds >= 40) Task.Delay(milliseconds - 20, token).Wait(token);
        while (!token.IsCancellationRequested)
        {
            if ((int)(DateTime.Now.Ticks / 10_000) - started >= milliseconds) break;
            Thread.Yield();
        }
    }

    private static object Summarize(double[] samples)
    {
        double[] ordered = samples.Order().ToArray();
        double median = ordered[ordered.Length / 2];
        return new
        {
            Count = samples.Length,
            MedianMs = median,
            MinMs = ordered[0],
            MaxMs = ordered[^1],
            MaxAdjacentJumpMs = samples.Zip(samples.Skip(1), (a, b) => Math.Abs(b - a)).DefaultIfEmpty().Max(),
            OutsideMedian1Ms = samples.Count(v => Math.Abs(v - median) > 1),
            OutsideMedian5Ms = samples.Count(v => Math.Abs(v - median) > 5),
        };
    }

    private static void WriteReport(string name, object data)
    {
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "timing-reports");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        TestContext.AddTestAttachment(path);
        TestContext.Progress.WriteLine("Timing report: " + path);
    }

    private sealed class Samples : IConsoleIo
    {
        public List<int[]> Rows { get; } = [];
        public void Print(string message, bool newline = true)
        {
            Rows.Add(message.Split(',').Select(int.Parse).ToArray());
            TestContext.Progress.WriteLine($"Cycle {Rows.Count}: {message}");
        }
        public void Alert(string message) => throw new InvalidOperationException(message);
    }

    private sealed class Device(Sink connection) : NintendoSwitch
    {
        protected override IConnection CreateConnection(string endpoint, int baudrate) => connection;
    }

    private sealed class Sink : IConnection
    {
        public int Writes;
        public override event BytesTransferedHandler BytesSent = delegate { };
        public override event StatusChangedHandler StatusChanged = delegate { };
        public override Status CurrentStatus { get; protected set; } = Status.Connecting;
        public override void Connect() { CurrentStatus = Status.Connected; StatusChanged(CurrentStatus); }
        public override void Disconnect() { CurrentStatus = Status.Connecting; }
        public override void ClearQueue() { }
        public override void Write(params byte[] bytes)
        {
            Interlocked.Increment(ref Writes);
            BytesSent("fake", bytes);
        }
    }
}