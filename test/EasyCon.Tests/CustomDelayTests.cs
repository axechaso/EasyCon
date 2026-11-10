using System.Diagnostics;

namespace EasyCon.Tests;

[TestFixture]
public sealed class CustomDelayTests
{
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
