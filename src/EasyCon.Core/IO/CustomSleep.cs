using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>
/// Blocking delays used by the script and game-pad runtimes.
///
/// All deadlines are based on Stopwatch rather than wall-clock time. The
/// latter can jump when Windows synchronizes the system clock, which used to
/// make a high-resolution delay finish early or late.
/// </summary>
public sealed class CustomDelay
{
    // The high-resolution path spends the final couple of milliseconds
    // correcting the coarse sleep. The low-CPU path only yields in the last
    // fraction of a millisecond.
    private const double HighResolutionSpinMilliseconds = 2.0;
    private const double LowCpuSpinMilliseconds = 0.2;

    /// <summary>Delay for the requested duration and observe cancellation promptly.</summary>
    public static void Delay(int millisecondsTimeout, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        DelayCore(millisecondsTimeout, token, HighResolutionSpinMilliseconds);
    }

    /// <summary>Delay with low CPU usage while retaining a monotonic deadline.</summary>
    public static void AISleep(int milliseconds)
    {
        if (milliseconds <= 0)
            return;

        DelayCore(milliseconds, CancellationToken.None, LowCpuSpinMilliseconds);
    }

    private static void DelayCore(int milliseconds, CancellationToken token, double spinMilliseconds)
    {
        if (milliseconds <= 0)
            return;

        var deadline = Stopwatch.GetTimestamp() + MillisecondsToTicks(milliseconds);
        var spinTicks = MillisecondsToTicks(spinMilliseconds);

        while (true)
        {
            token.ThrowIfCancellationRequested();

            var remaining = deadline - Stopwatch.GetTimestamp();
            if (remaining <= 0)
                return;

            if (remaining > spinTicks)
            {
                // Leave a small margin for the scheduler. The final loop is
                // responsible for hitting the monotonic deadline exactly.
                var remainingMilliseconds = remaining * 1000.0 / Stopwatch.Frequency;
                var coarseMilliseconds = Math.Max(1, (int)Math.Floor(remainingMilliseconds - 1.0));
                WaitCoarsely(coarseMilliseconds, token);
                continue;
            }

            if (spinMilliseconds >= 1.0)
                Thread.SpinWait(64);
            else
                Thread.Yield();
        }
    }

    private static long MillisecondsToTicks(double milliseconds)
    {
        return Math.Max(1, (long)Math.Ceiling(milliseconds * Stopwatch.Frequency / 1000.0));
    }

    private static void WaitCoarsely(int milliseconds, CancellationToken token)
    {
        if (token.CanBeCanceled)
        {
            var canceled = false;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                TimeBeginPeriod(1);
                try
                {
                    canceled = token.WaitHandle.WaitOne(milliseconds);
                }
                finally
                {
                    TimeEndPeriod(1);
                }
            }
            else
            {
                canceled = token.WaitHandle.WaitOne(milliseconds);
            }

            if (canceled)
                token.ThrowIfCancellationRequested();
            return;
        }

        HighResolutionSleep(milliseconds);
    }

    /// <summary>
    /// Platform-specific coarse sleep. The monotonic deadline in DelayCore
    /// removes any residual scheduler overshoot.
    /// </summary>
    private static void HighResolutionSleep(int milliseconds)
    {
        if (milliseconds <= 0)
            return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            TimeBeginPeriod(1);
            try
            {
                Thread.Sleep(milliseconds);
            }
            finally
            {
                TimeEndPeriod(1);
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                 RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Nanosleep(milliseconds * 1_000_000L);
        }
        else
        {
            Thread.Sleep(milliseconds);
        }
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint period);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint period);

    [DllImport("libc", SetLastError = true, EntryPoint = "nanosleep")]
    private static extern int NanosleepNative(ref Timespec req, out Timespec rem);

    private static void Nanosleep(long nanoseconds)
    {
        var request = new Timespec
        {
            tv_sec = nanoseconds / 1_000_000_000,
            tv_nsec = nanoseconds % 1_000_000_000
        };

        while (NanosleepNative(ref request, out var remaining) != 0)
        {
            // nanosleep may be interrupted by a signal. Retry with the
            // remaining duration; for any other failure the deadline loop
            // still guarantees that the caller will not return early.
            if (Marshal.GetLastWin32Error() != 4) // EINTR
                break;
            request = remaining;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec
    {
        public long tv_sec;
        public long tv_nsec;
    }
}
