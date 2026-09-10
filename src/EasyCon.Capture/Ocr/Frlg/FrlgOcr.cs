using EzCv;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace EasyCon.Capture.Ocr.Frlg;

public sealed record FrlgDigitMatch(int Digit, Rect Bounds, double Rmsd, double RunnerUpRmsd);
public sealed record FrlgReadAttempt(int Threshold, string Text, string Failure, FrlgDigitMatch[] Digits);

/// <summary>Quality is a template separation score, not a calibrated probability.</summary>
public sealed record FrlgReadResult(string Scene, string Text, string Failure, int Quality,
    double ElapsedMilliseconds, FrlgReadAttempt[] Attempts)
{
    public bool Success => Failure.Length == 0 && Text.Length == 5;
}

/// <summary>FRLG scene routing. Coordinates supplied to OCR are capture-frame pixels.</summary>
public static class FrlgOcr
{
    public const string JapaneseTid = "FRLG_JPN_TID";
    public const string EnglishTid = "FRLG_EN_TID";
    public const string Version = "170a-frlg-tid-r1";

    public static bool IsScene(string scene) => scene == JapaneseTid || scene == EnglishTid;

    /// <summary>PokemonAutomation's default Switch game box composed with its TID region.</summary>
    public static Rect DefaultRegion(string scene, int width, int height)
    {
        if (!IsScene(scene))
            throw new ArgumentException("Unsupported FRLG scene.", nameof(scene));
        double x = scene == JapaneseTid ? 0.712981 : 0.742683;
        double y = scene == JapaneseTid ? 0.118836 : 0.117314;
        double w = scene == JapaneseTid ? 0.207212 : 0.129734;
        double h = scene == JapaneseTid ? 0.077373 : 0.076006;
        int padding = Math.Max(1, (int)Math.Ceiling(4.0 * height / 1080));
        return new Rect((int)((0.09375 + x * 0.8125) * width) - padding,
            (int)((0.00462963 + y * 0.962963) * height) - padding,
            (int)Math.Round(w * 0.8125 * width) + 2 * padding,
            (int)Math.Round(h * 0.962963 * height) + 2 * padding);
    }

    public static FrlgReadResult ReadFrame(Mat? frame, Rect region, string scene, string? debugDirectory = null)
    {
        Stopwatch timer = Stopwatch.StartNew();
        FrlgReadResult Fail(string reason) => new(scene, "", reason, 0, timer.Elapsed.TotalMilliseconds, []);
        if (!IsScene(scene))
            return Fail("unsupported-scene");
        if (frame == null || frame.Empty())
            return Fail("no-frame");
        if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0
            || (long)region.X + region.Width > frame.Width || (long)region.Y + region.Height > frame.Height)
            return Fail("invalid-region");

        // Normalize the font scale before using the upstream 5x5 blur and pixel-size gates.
        double scale = 1080.0 / frame.Height;
        int width = (int)Math.Round(region.Width * scale);
        int height = (int)Math.Round(region.Height * scale);
        if (width < 5 || height < 15 || width > 1500 || height > 400)
            return Fail("region-size-out-of-range");

        using Mat roi = new(frame, region);
        using Mat color = new();
        if (roi.Channels() == 4)
            Cv2.CvtColor(roi, color, ColorConversionCodes.BGRA2BGR);
        else if (roi.Channels() == 1)
            Cv2.CvtColor(roi, color, ColorConversionCodes.GRAY2BGR);
        else if (roi.Channels() != 3)
            return Fail("unsupported-pixel-format");
        using Mat normalized = new();
        Cv2.Resize(roi.Channels() == 3 ? roi : color, normalized, new Size(width, height));

        FrlgReadAttempt[] attempts = FrlgDigitReader.Read(normalized, debugDirectory);
        FrlgReadAttempt[] accepted = attempts.Where(a => a.Failure.Length == 0).ToArray();
        string[] candidates = accepted.Select(a => a.Text).Distinct(StringComparer.Ordinal).ToArray();
        string failure = candidates.Length > 1 ? "threshold-conflict"
            : accepted.Length < 2 ? "insufficient-threshold-agreement" : "";
        string text = failure.Length == 0 ? candidates[0] : "";
        int quality = text.Length == 0 ? 0 : (int)Math.Clamp(accepted.Min(a => a.Digits.Min(d =>
            (d.RunnerUpRmsd - d.Rmsd) / Math.Max(d.RunnerUpRmsd, 1))) * 100, 0, 100);
        FrlgReadResult result = new(scene, text, failure, quality, timer.Elapsed.TotalMilliseconds, attempts);
        if (debugDirectory != null)
        {
            Directory.CreateDirectory(debugDirectory);
            File.WriteAllBytes(Path.Combine(debugDirectory, "region.png"), roi.ToBytes());
            File.WriteAllText(Path.Combine(debugDirectory, "result.json"),
                JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        }
        return result;
    }

    internal static string ValidateDigits(string text)
    {
        if (text.Length != 5)
            return "digit-count";
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value > 65535)
            return "tid-out-of-range";
        return "";
    }
}