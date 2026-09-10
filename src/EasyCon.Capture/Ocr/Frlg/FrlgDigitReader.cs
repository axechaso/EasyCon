// Ported from PokemonAutomation's FRLG DigitReader and ExactImageMatcher.
// Copyright (c) 2021 Alexander J. Yee. MIT notice: docs/licenses/PokemonAutomation-MIT.txt.
// Source revisions and resource hashes: docs/frlg-ocr-resources.lock.json.
using EzCv;
using System.Reflection;
using System.Runtime.InteropServices;

namespace EasyCon.Capture.Ocr.Frlg;

internal static class FrlgDigitReader
{
    private sealed record DigitTemplate(int Width, int Height, byte[] Pixels, double[] Mean);
    private sealed record Component(Rect Bounds, int Area);

    private static readonly Lazy<DigitTemplate[]> _dialogTemplates = new(() => LoadTemplates("DialogDigits"));
    private static readonly int[] _thresholds = [175, 190, 205];
    private const double MaxRmsd = 85;
    private const double MinMargin = 8;

    // Cached managed pixel buffers own no native handles. Per-read Mats are always disposed.
    private static DigitTemplate[] LoadTemplates(string family)
    {
        Assembly assembly = typeof(FrlgDigitReader).Assembly;
        DigitTemplate[] templates = new DigitTemplate[10];
        for (int digit = 0; digit < 10; digit++)
        {
            string name = $"Frlg/{family}/{digit}.png";
            using Stream stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidDataException($"Missing embedded FRLG template: {name}");
            using MemoryStream bytes = new();
            stream.CopyTo(bytes);
            using Mat image = Mat.FromImageData(bytes.ToArray());
            byte[] pixels = ReadPixels(image);
            templates[digit] = new DigitTemplate(image.Width, image.Height, pixels, Mean(pixels));
        }
        return templates;
    }

    internal static FrlgReadAttempt[] Read(Mat image, string? debugDirectory)
    {
        using Mat firstBlur = new();
        using Mat blurred = new();
        Cv2.GaussianBlur(image, firstBlur, new Size(5, 5), 1.5);
        Cv2.GaussianBlur(firstBlur, blurred, new Size(5, 5), 1.5);
        byte[] blurredPixels = ReadPixels(blurred);
        byte[] original = ReadPixels(image);
        if (debugDirectory != null)
        {
            Directory.CreateDirectory(debugDirectory);
            File.WriteAllBytes(Path.Combine(debugDirectory, "normalized.png"), image.ToBytes());
            File.WriteAllBytes(Path.Combine(debugDirectory, "blurred.png"), blurred.ToBytes());
        }
        return _thresholds.Select(threshold => ReadThreshold(image, original, blurredPixels, threshold, debugDirectory)).ToArray();
    }

    private static FrlgReadAttempt ReadThreshold(Mat image, byte[] original, byte[] blurred, int threshold, string? debugDirectory)
    {
        int width = image.Width;
        int height = image.Height;
        bool[] foreground = new bool[width * height];
        for (int i = 0; i < foreground.Length; i++)
            foreground[i] = blurred[3 * i] <= threshold && blurred[3 * i + 1] <= threshold && blurred[3 * i + 2] <= threshold;
        if (debugDirectory != null)
        {
            using Mat binary = new(height, width, MatType.CV_8UC1);
            byte[] mask = foreground.Select(value => value ? (byte)0 : (byte)255).ToArray();
            CopyPixels(binary, mask, 1);
            File.WriteAllBytes(Path.Combine(debugDirectory, $"binary-{threshold}.png"), binary.ToBytes());
        }
        List<Component> allComponents = Components(foreground, width, height);
        Component[] components = allComponents
            .Where(c => c.Area >= 4 && c.Bounds.Width >= 5 && c.Bounds.Height >= 15)
            .OrderBy(c => c.Bounds.X).ToArray();
        List<FrlgDigitMatch> matches = [];
        FrlgReadAttempt Fail(string reason) => new(threshold, "", reason, matches.ToArray());
        if (allComponents.Count > 128)
            return Fail("too-many-components");
        if (components.Length == 0 || components.Length > 5)
            return Fail("component-count");
        foreach (Component component in components)
        {
            Rect box = component.Bounds;
            if (box.X == 0 || box.Y == 0 || box.X + box.Width == width || box.Y + box.Height == height)
                return Fail("clipped-glyph");
            int expectedDigits = Math.Max(1, (int)Math.Ceiling((double)box.Width / box.Height / 0.6 - 0.5));
            if (expectedDigits > 5 || matches.Count + expectedDigits > 5)
                return Fail("merged-component-count");
            int splitWidth = box.Width / expectedDigits;
            for (int split = 0; split < expectedDigits; split++)
            {
                int x = box.X + split * splitWidth;
                int right = split == expectedDigits - 1 ? box.X + box.Width : x + splitWidth;
                Rect glyph = Tighten(original, width, new Rect(x, box.Y, right - x, box.Height));
                using Mat crop = new(image, glyph);
                (int Digit, double Score)[] scores = _dialogTemplates.Value
                    .Select((template, digit) => (Digit: digit, Score: Rmsd(crop, template)))
                    .OrderBy(item => item.Score).ToArray();
                FrlgDigitMatch match = new(scores[0].Digit, glyph, scores[0].Score, scores[1].Score);
                matches.Add(match);
                if (debugDirectory != null)
                    File.WriteAllBytes(Path.Combine(debugDirectory, $"digit-{threshold}-{matches.Count}.png"), crop.ToBytes());
                // Never silently drop a digit and concatenate the remainder.
                if (match.Rmsd > MaxRmsd)
                    return Fail("poor-template-match");
                if (match.RunnerUpRmsd - match.Rmsd < MinMargin)
                    return Fail("ambiguous-digit");
            }
        }
        if (matches.Count != 5)
            return Fail("digit-count");
        int minHeight = matches.Min(d => d.Bounds.Height);
        int maxHeight = matches.Max(d => d.Bounds.Height);
        if (minHeight < maxHeight * 0.65 || matches.Max(d => d.Bounds.Y) - matches.Min(d => d.Bounds.Y) > maxHeight * 0.25)
            return Fail("inconsistent-glyph-layout");
        for (int i = 1; i < matches.Count; i++)
        {
            Rect previous = matches[i - 1].Bounds;
            if (matches[i].Bounds.X < previous.X + previous.Width)
                return Fail("overlapping-glyphs");
        }
        string text = string.Concat(matches.Select(d => (char)('0' + d.Digit)));
        string failure = FrlgOcr.ValidateDigits(text);
        return new FrlgReadAttempt(threshold, failure.Length == 0 ? text : "", failure, matches.ToArray());
    }

    private static List<Component> Components(bool[] pixels, int width, int height)
    {
        List<Component> components = [];
        int[] queue = new int[pixels.Length];
        for (int start = 0; start < pixels.Length; start++)
        {
            if (!pixels[start])
                continue;
            int head = 0;
            int tail = 1;
            queue[0] = start;
            pixels[start] = false;
            int minX = start % width, maxX = minX, minY = start / width, maxY = minY;
            while (head < tail)
            {
                int pos = queue[head++];
                int x = pos % width;
                int y = pos / width;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
                if (x > 0) Visit(pos - 1);
                if (x + 1 < width) Visit(pos + 1);
                if (y > 0) Visit(pos - width);
                if (y + 1 < height) Visit(pos + width);
            }
            components.Add(new Component(new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1), tail));
            if (components.Count > 128)
                break;

            void Visit(int pos)
            {
                if (!pixels[pos])
                    return;
                pixels[pos] = false;
                queue[tail++] = pos;
            }
        }
        return components;
    }

    private static Rect Tighten(byte[] pixels, int strideWidth, Rect box)
    {
        int minBrightness = 765, maxBrightness = 0;
        for (int y = box.Y; y < box.Y + box.Height; y++)
            for (int x = box.X; x < box.X + box.Width; x++)
            {
                int brightness = Brightness(x, y);
                minBrightness = Math.Min(minBrightness, brightness);
                maxBrightness = Math.Max(maxBrightness, brightness);
            }
        int threshold = (minBrightness + maxBrightness) / 2;
        int minX = box.X + box.Width, minY = box.Y + box.Height, maxX = -1, maxY = -1;
        for (int y = box.Y; y < box.Y + box.Height; y++)
            for (int x = box.X; x < box.X + box.Width; x++)
            {
                if (Brightness(x, y) > threshold)
                    continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        if (maxX < minX || maxY < minY)
            return box;
        minX = Math.Max(box.X, minX - 1);
        minY = Math.Max(box.Y, minY - 1);
        maxX = Math.Min(box.X + box.Width, maxX + 2);
        maxY = Math.Min(box.Y + box.Height, maxY + 2);
        return new Rect(minX, minY, maxX - minX, maxY - minY);

        int Brightness(int x, int y)
        {
            int i = (y * strideWidth + x) * 3;
            return pixels[i] + pixels[i + 1] + pixels[i + 2];
        }
    }

    private static double Rmsd(Mat crop, DigitTemplate template)
    {
        using Mat scaled = new();
        Cv2.Resize(crop, scaled, new Size(template.Width, template.Height));
        byte[] pixels = ReadPixels(scaled);
        double[] mean = Mean(pixels);
        double[] factors = Enumerable.Range(0, 3).Select(c =>
            template.Mean[c] == 0 ? 1.0 : Math.Clamp(mean[c] / template.Mean[c], 0.85, 1.15)).ToArray();
        double squares = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            double delta = Math.Clamp(template.Pixels[i] * factors[i % 3], 0, 255) - pixels[i];
            squares += delta * delta;
        }
        return Math.Sqrt(squares / pixels.Length);
    }

    private static double[] Mean(byte[] pixels)
    {
        double[] mean = new double[3];
        for (int i = 0; i < pixels.Length; i++)
            mean[i % 3] += pixels[i];
        for (int c = 0; c < 3; c++)
            mean[c] /= pixels.Length / 3;
        return mean;
    }

    private static byte[] ReadPixels(Mat image)
    {
        int rowBytes = image.Width * 3;
        byte[] pixels = new byte[rowBytes * image.Height];
        IntPtr data = image.Data;
        int step = checked((int)image.Step());
        for (int row = 0; row < image.Height; row++)
            Marshal.Copy(IntPtr.Add(data, row * step), pixels, row * rowBytes, rowBytes);
        GC.KeepAlive(image);
        return pixels;
    }

    private static void CopyPixels(Mat image, byte[] pixels, int channels)
    {
        int rowBytes = image.Width * channels;
        IntPtr data = image.Data;
        int step = checked((int)image.Step());
        for (int row = 0; row < image.Height; row++)
            Marshal.Copy(pixels, row * rowBytes, IntPtr.Add(data, row * step), rowBytes);
        GC.KeepAlive(image);
    }
}