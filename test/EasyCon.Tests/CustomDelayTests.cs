using System.Diagnostics;

namespace EasyCon.Tests;

[TestFixture]
public sealed class CustomDelayTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(20)]
    [TestCase(40)]
    [TestCase(50)]
    public void HighResolutionDelay_DoesNotFinishEarly(int milliseconds)
    {
        Stopwatch watch = Stopwatch.StartNew();
        CustomDelay.HighResolutionDelay(milliseconds, default);
        Assert.That(watch.Elapsed.TotalMilliseconds, Is.GreaterThanOrEqualTo(milliseconds));
    }

    [TestCase(20)]
    [TestCase(5000)]
    public void HighResolutionDelay_CancellationPreservesExceptionContract(int milliseconds)
    {
        using CancellationTokenSource cancellation = new();
        cancellation.CancelAfter(5);
        Assert.Throws<OperationCanceledException>(() => CustomDelay.HighResolutionDelay(milliseconds, cancellation.Token));
    }

    [Test]
    public void HighResolutionDelay_PreCancelledZeroWaitStillThrows()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => CustomDelay.HighResolutionDelay(0, cancellation.Token));
    }

    [TestCase(20)]
    [TestCase(50)]
    public void Delay_DoesNotReturnBeforeRequestedDuration(int durationMilliseconds)
    {
        var stopwatch = Stopwatch.StartNew();

        global::CustomDelay.Delay(durationMilliseconds, CancellationToken.None);

        Assert.That(stopwatch.Elapsed.TotalMilliseconds,
            Is.GreaterThanOrEqualTo(durationMilliseconds - 1));
    }

    [Test]
    public void AISleep_DoesNotReturnBeforeRequestedDuration()
    {
        const int durationMilliseconds = 50;
        var stopwatch = Stopwatch.StartNew();

        global::CustomDelay.AISleep(durationMilliseconds);

        Assert.That(stopwatch.Elapsed.TotalMilliseconds,
            Is.GreaterThanOrEqualTo(durationMilliseconds - 1));
    }

    [Test]
    public void Delay_ObservesCancellationDuringCoarseWait()
    {
        using var cancellation = new CancellationTokenSource();
        var completed = new ManualResetEventSlim();
        var canceled = false;

        var worker = Task.Run(() =>
        {
            try
            {
                global::CustomDelay.Delay(5000, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }
            finally
            {
                completed.Set();
            }
        });

        Thread.Sleep(30);
        cancellation.Cancel();

        Assert.That(completed.Wait(TimeSpan.FromSeconds(1)), Is.True);
        Assert.That(canceled, Is.True);
        Assert.That(worker.Wait(TimeSpan.FromSeconds(1)), Is.True);
    }
}