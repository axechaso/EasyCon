using EasyCon.Capture.Ocr.Frlg;
using EzCv;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace FrlgFfi;

public static unsafe partial class Exports
{
    private const string PluginVersion = "FRLG_FFI_1";
    private const string FailureImageFileName = "frlg_last_failure.png";
    private const string FailureTextFileName = "frlg_last_failure.txt";
    private const int MaximumFrameBase64Bytes = 20 * 1024 * 1024;
    private const int MaximumArgumentBytes = 64 * 1024;
    private static readonly object s_readerGate = new();
    private static FrlgTextReader? s_reader;
    private static string? s_modelDirectory;
    private static string? s_nativeDirectory;
    private static readonly Lazy<string> s_pluginDirectory = new(GetPluginDirectory);

    [ThreadStatic]
    private static nint t_returnBuffer;

    [ThreadStatic]
    private static int t_returnCapacity;

    [ThreadStatic]
    private static string? t_lastError;

    [ThreadStatic]
    private static string? t_lastDebug;

    [UnmanagedCallersOnly(EntryPoint = "frlg_version", CallConvs = [typeof(CallConvCdecl)])]
    public static nint Version() => CopyUtf8(PluginVersion);

    [UnmanagedCallersOnly(EntryPoint = "frlg_read", CallConvs = [typeof(CallConvCdecl)])]
    public static nint Read(byte* frameBase64, byte* scene, int x, int y, int width, int height,
        byte* modelDirectory)
    {
        try
        {
            string encoded = ReadUtf8(frameBase64, MaximumFrameBase64Bytes);
            string sceneName = ReadUtf8(scene, MaximumArgumentBytes);
            string models = ReadUtf8(modelDirectory, MaximumArgumentBytes);
            if (encoded.Length == 0 || sceneName.Length == 0 || models.Length == 0)
                return Fail("empty-argument");
            models = ResolveModelDirectory(models);
            if (!FrlgOcr.IsScene(sceneName))
                return Fail("unsupported-scene");

            lock (s_readerGate)
            {
                EnsureNativeLibraries(models);
                byte[] png = Convert.FromBase64String(encoded);
                using Mat frame = Mat.FromImageData(png);
                if (s_reader == null || !string.Equals(s_modelDirectory, models, StringComparison.Ordinal))
                {
                    s_reader?.Dispose();
                    s_reader = new FrlgTextReader(models);
                    s_modelDirectory = models;
                }
                FrlgReadResult result = FrlgOcr.ReadFrame(frame, new Rect(x, y, width, height),
                    sceneName, textReader: s_reader);
                t_lastError = result.Failure;
                t_lastDebug = FormatDebug(result);
                if (result.Text.Length == 0)
                {
                    string? saveError = TrySaveFailureArtifacts(png, sceneName, x, y, width, height,
                        models, t_lastError, t_lastDebug);
                    if (saveError != null)
                        t_lastDebug = AppendDebug(t_lastDebug, $"failure-artifact-error={saveError}");
                }
                return CopyUtf8(result.Text);
            }
        }
        catch (Exception ex)
        {
            return Fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "frlg_last_error", CallConvs = [typeof(CallConvCdecl)])]
    public static nint LastError() => CopyUtf8(t_lastError ?? string.Empty);

    [UnmanagedCallersOnly(EntryPoint = "frlg_last_debug", CallConvs = [typeof(CallConvCdecl)])]
    public static nint LastDebug() => CopyUtf8(t_lastDebug ?? string.Empty);

    [UnmanagedCallersOnly(EntryPoint = "frlg_release_thread", CallConvs = [typeof(CallConvCdecl)])]
    public static void ReleaseThread() => ReleaseThreadCore();

    [UnmanagedCallersOnly(EntryPoint = "frlg_shutdown", CallConvs = [typeof(CallConvCdecl)])]
    public static int Shutdown()
    {
        lock (s_readerGate)
        {
            s_reader?.Dispose();
            s_reader = null;
            s_modelDirectory = null;
        }
        ReleaseThreadCore();
        return 1;
    }

    private static void ReleaseThreadCore()
    {
        if (t_returnBuffer != 0)
            Marshal.FreeHGlobal(t_returnBuffer);
        t_returnBuffer = 0;
        t_returnCapacity = 0;
        t_lastError = null;
        t_lastDebug = null;
    }

    private static nint Fail(string error)
    {
        t_lastError = error;
        t_lastDebug = null;
        return CopyUtf8(string.Empty);
    }

    private static string? TrySaveFailureArtifacts(byte[] frame, string scene, int x, int y,
        int width, int height, string models, string? failure, string? debug)
    {
        try
        {
            string cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EasyCon", "Cache");
            Directory.CreateDirectory(cacheDirectory);
            File.WriteAllBytes(Path.Combine(cacheDirectory, FailureImageFileName), frame);

            string details = string.Join(Environment.NewLine,
            [
                $"time={DateTimeOffset.Now:O}",
                $"scene={scene}",
                $"roi={x},{y},{width},{height}",
                $"models={models}",
                $"failure={failure ?? string.Empty}",
                $"debug={debug ?? string.Empty}"
            ]);
            File.WriteAllText(Path.Combine(cacheDirectory, FailureTextFileName), details,
                new UTF8Encoding(false));
            return null;
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string AppendDebug(string? current, string addition)
    {
        return string.IsNullOrEmpty(current) ? addition : $"{current} | {addition}";
    }

    private static string FormatDebug(FrlgReadResult result)
    {
        IEnumerable<string> textAttempts = result.TextAttempts.Select(attempt =>
            $"{attempt.Backend}/{attempt.Threshold}:raw={attempt.Raw};candidate={attempt.Candidate};failure={attempt.Failure}");
        IEnumerable<string> digitAttempts = result.Attempts.Select(attempt =>
            $"threshold={attempt.Threshold};text={attempt.Text};failure={attempt.Failure};digits="
            + string.Join(",", attempt.Digits.Select(digit =>
                $"{digit.Digit}/{digit.RunnerUpDigit}@{digit.Bounds.X}:{digit.Bounds.Y}:{digit.Bounds.Width}:{digit.Bounds.Height}"
                + $"[{digit.Rmsd:F1}/{digit.RunnerUpRmsd:F1}]")));
        return string.Join(" | ", textAttempts.Concat(digitAttempts));
    }

    private static void EnsureNativeLibraries(string modelDirectory)
    {
        // The plugin package layout is <root>/FrlgFfi.dll and <root>/models/frlg.
        // The EasyCon host's AppContext.BaseDirectory is its own directory, so the
        // private image libraries must be loaded explicitly from the plugin root.
        string root = Path.GetFullPath(Path.Combine(modelDirectory, "..", ".."));
        if (string.Equals(s_nativeDirectory, root, StringComparison.Ordinal))
            return;
        NativeLibrary.Load(Path.Combine(root, "opencv_world500.dll"));
        NativeLibrary.Load(Path.Combine(root, "ezcv_native.dll"));
        NativeLibrary.Load(Path.Combine(root, "onnxruntime.dll"));
        s_nativeDirectory = root;
    }

    private static string ResolveModelDirectory(string modelDirectory)
    {
        if (Path.IsPathFullyQualified(modelDirectory))
            return Path.GetFullPath(modelDirectory);
        return Path.GetFullPath(Path.Combine(s_pluginDirectory.Value, modelDirectory));
    }

    private static string GetPluginDirectory()
    {
        if (!OperatingSystem.IsWindows())
            return AppContext.BaseDirectory;

        delegate* unmanaged[Cdecl]<nint> version = &Version;
        const uint fromAddress = 0x00000004;
        const uint unchangedRefCount = 0x00000002;
        if (GetModuleHandleExW(fromAddress | unchangedRefCount, (nint)version, out nint module) == 0)
            throw new InvalidOperationException($"GetModuleHandleExW failed: {Marshal.GetLastWin32Error()}");

        const int capacity = 32768;
        char* buffer = stackalloc char[capacity];
        uint length = GetModuleFileNameW(module, buffer, capacity);
        if (length == 0 || length >= capacity)
            throw new InvalidOperationException($"GetModuleFileNameW failed: {Marshal.GetLastWin32Error()}");

        string modulePath = new(buffer, 0, checked((int)length));
        return Path.GetDirectoryName(modulePath)
            ?? throw new InvalidOperationException("FRLG FFI module directory is unavailable.");
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleExW", SetLastError = true)]
    private static partial int GetModuleHandleExW(uint flags, nint address, out nint module);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleFileNameW", SetLastError = true)]
    private static partial uint GetModuleFileNameW(nint module, char* fileName, int size);

    private static string ReadUtf8(byte* pointer, int maximumBytes)
    {
        if (pointer == null)
            return string.Empty;
        int length = 0;
        while (length < maximumBytes && pointer[length] != 0)
            length++;
        if (length == maximumBytes)
            throw new ArgumentException("FFI string is too long or lacks a terminator.");
        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(pointer, length));
    }

    private static nint CopyUtf8(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        int required = checked(bytes.Length + 1);
        if (required > t_returnCapacity)
        {
            int capacity = Math.Max(256, required);
            nint replacement = Marshal.AllocHGlobal(capacity);
            if (t_returnBuffer != 0)
                Marshal.FreeHGlobal(t_returnBuffer);
            t_returnBuffer = replacement;
            t_returnCapacity = capacity;
        }
        Span<byte> destination = new((void*)t_returnBuffer, t_returnCapacity);
        bytes.CopyTo(destination);
        destination[bytes.Length] = 0;
        return t_returnBuffer;
    }
}