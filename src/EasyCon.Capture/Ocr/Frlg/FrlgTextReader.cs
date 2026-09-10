// Paddle preprocessing follows PokemonAutomation ML_PaddleOCRPipeline (MIT).
// Pinned model provenance and notices: docs/FRLG-OCR-SOURCES.md.
using EzCv;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Diagnostics;
using System.Text;

namespace EasyCon.Capture.Ocr.Frlg;

public sealed record FrlgTextAttempt(string Backend, int Threshold, string Raw, double Confidence,
    string Candidate, int Distance, bool Accepted, string Failure);

/// <summary>Owns lazily initialized text engines. Calls are serialized; native engines are disposed by the owner.</summary>
public sealed class FrlgTextReader : IDisposable
{
    private readonly object _sync = new();
    private readonly string _modelDirectory;
    private InferenceSession? _paddle;
    private string[]? _characters;
    private IOcrRecognizer? _tesseract;
    private bool _disposed;

    public FrlgTextReader(string? modelDirectory = null) => _modelDirectory = modelDirectory
        ?? Path.Combine(AppContext.BaseDirectory, "models", "frlg");

    public FrlgReadResult Read(Mat image, string scene, string? debugDirectory = null)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Stopwatch timer = Stopwatch.StartNew();
            List<FrlgTextAttempt> attempts = [];
            FrlgReadResult Result(string text, string failure, int quality = 0) =>
                new(scene, text, failure, quality, timer.Elapsed.TotalMilliseconds, []) { TextAttempts = attempts.ToArray() };
            bool nature = FrlgScenes.Find(scene)!.Kind == "nature";
            string[] targets = FrlgScenes.Targets(scene);
            if (targets.Length > 0 && (nature || !FrlgJapaneseLexicon.ValidTargets(targets)))
                return Result("", "invalid-target-set");
            if (TouchesInk(image)) return Result("", "clipped-text");

            using Mat resized = new();
            Cv2.Resize(image, resized, new Size(Math.Max(1, image.Width * 69 / image.Height), 69));
            // White padding keeps blur from turning a complete top diacritic into an apparent clipped glyph.
            using Mat padded = PadWhite(resized, 6);
            using Mat blur = new();
            Cv2.GaussianBlur(padded, blur, new Size(5, 5), 1.5);
            Cv2.GaussianBlur(blur, blur, new Size(5, 5), 1.5);
            List<(int Threshold, Mat Image)> variants = [];
            try
            {
                foreach (int threshold in new[] { 160, 184, 208, 128, 96 })
                {
                    Mat? variant = BinarizeAndCrop(blur, threshold);
                    if (variant == null) continue;
                    variants.Add((threshold, variant));
                    if (debugDirectory != null)
                    {
                        Directory.CreateDirectory(debugDirectory);
                        File.WriteAllBytes(Path.Combine(debugDirectory, $"text-{threshold}.png"), variant.ToBytes());
                    }
                    ReadVariant("PaddleOCR", threshold, variant);
                    FrlgTextAttempt[] strong = attempts.Where(a => a.Accepted && a.Distance == 0 && a.Confidence >= .80).ToArray();
                    if (strong.Length >= 2 && attempts.Where(a => a.Accepted).All(a => a.Candidate == strong[0].Candidate))
                        return Result(strong[0].Candidate, "", (int)(strong.Average(a => a.Confidence) * 100));
                }
                if (variants.Count == 0) return Result("", "no-text");
                FrlgTextAttempt[] paddle = attempts.Where(a => a.Accepted).ToArray();
                string[] primary = paddle.Select(a => a.Candidate).Distinct().ToArray();
                // Require two strong, complete, consistent primary reads; otherwise obtain a second opinion.
                if (primary.Length == 1 && paddle.Count(a => a.Confidence >= .80 && a.Distance == 0) >= 2)
                    return Result(primary[0], "", (int)(paddle.Average(a => a.Confidence) * 100));
                foreach ((int threshold, Mat variant) in variants)
                    ReadVariant("Tesseract", threshold, variant);
                FrlgTextAttempt[] secondary = attempts.Where(a => a.Backend == "Tesseract" && a.Accepted).ToArray();
                string[] confirmed = secondary.GroupBy(a => a.Candidate).Where(g => g.Count() >= 2
                    && (primary.Contains(g.Key) || primary.Length == 0 && g.Count(a => a.Distance == 0 && a.Confidence >= .70) >= 2))
                    .Select(g => g.Key).ToArray();
                if (confirmed.Length == 1 && secondary.All(a => a.Candidate == confirmed[0]))
                    return Result(confirmed[0], "", (int)(secondary.Average(a => a.Confidence) * 100));
                return Result("", attempts.All(a => a.Failure.Length > 0) ? "text-backends-unavailable"
                    : primary.Length > 1 || confirmed.Length > 1 ? "text-candidate-conflict" : "insufficient-text-agreement");
            }
            finally { foreach ((int _, Mat variant) in variants) variant.Dispose(); }

            void ReadVariant(string backend, int threshold, Mat variant)
            {
                try
                {
                    OcrRecognizeResult raw = backend == "PaddleOCR" ? Paddle(variant) : Tesseract(variant);
                    FrlgWordMatch match = FrlgJapaneseLexicon.Match(raw.Text, nature, targets);
                    attempts.Add(new(backend, threshold, raw.Text, raw.Confidence, match.Text, match.Distance,
                        match.Accepted && raw.Confidence >= (match.Distance == 0 ? .55 : .75), ""));
                }
                catch (Exception ex) { attempts.Add(new(backend, threshold, "", 0, "", 99, false, ex.Message)); }
            }
        }
    }

    private OcrRecognizeResult Tesseract(Mat image)
    {
        _tesseract ??= new TesseractEngineFactory().CreateRecognizer("jpn", Path.Combine(_modelDirectory, "tessdata"), "LSTM_ONLY", "SINGLE_LINE");
        return _tesseract.Recognize(image.ToBytes());
    }

    private OcrRecognizeResult Paddle(Mat image)
    {
        if (_paddle == null)
        {
            using SessionOptions options = new() { IntraOpNumThreads = 2, InterOpNumThreads = 1, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
            _characters = File.ReadAllLines(Path.Combine(_modelDirectory, "chinese", "dict.txt"));
            _paddle = new InferenceSession(Path.Combine(_modelDirectory, "chinese", "rec.onnx"), options);
        }
        int width = Math.Clamp((int)Math.Round(48.0 * image.Width / image.Height), 24, 2048);
        using Mat resized = new();
        Cv2.Resize(image, resized, new Size(width, 48));
        byte[] pixels = FrlgDigitReader.ReadPixels(resized);
        DenseTensor<float> input = new(new[] { 1, 3, 48, width });
        for (int y = 0; y < 48; y++)
            for (int x = 0; x < width; x++)
                for (int c = 0; c < 3; c++)
                    input[0, c, y, x] = pixels[(y * width + x) * 3 + 2 - c] / 255f;
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> output = _paddle.Run(
            new[] { NamedOnnxValue.CreateFromTensor(_paddle.InputMetadata.Keys.First(), input) });
        Tensor<float> tensor = output.First().AsTensor<float>();
        if (tensor.Rank != 3 || tensor.Dimensions[0] != 1 || tensor.Dimensions[2] < _characters!.Length + 1
            || tensor.Dimensions[2] > _characters.Length + 2)
            throw new InvalidDataException("Paddle model and character dictionary shapes disagree.");
        StringBuilder text = new();
        List<double> confidence = [];
        float[] scores = tensor.ToArray();
        int classes = tensor.Dimensions[2];
        int previous = 0;
        for (int t = 0; t < tensor.Dimensions[1]; t++)
        {
            int offset = t * classes;
            int best = 0;
            for (int c = 1; c < classes; c++)
                if (scores[offset + c] > scores[offset + best]) best = c;
            if (best != 0 && best != previous)
            {
                text.Append(best <= _characters.Length ? _characters[best - 1] : " ");
                confidence.Add(scores[offset + best]);
            }
            previous = best;
        }
        return new(text.ToString(), confidence.Count == 0 ? 0 : (float)confidence.Average());
    }

    private static Mat? BinarizeAndCrop(Mat image, int threshold)
    {
        byte[] pixels = FrlgDigitReader.ReadPixels(image);
        int minX = image.Width, minY = image.Height, maxX = -1, maxY = -1, count = 0;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                int i = (y * image.Width + x) * 3;
                bool dark = pixels[i] <= threshold && pixels[i + 1] <= threshold && pixels[i + 2] <= threshold;
                if (dark) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); count++; }
                pixels[i] = pixels[i + 1] = pixels[i + 2] = dark ? (byte)0 : (byte)255;
            }
        double ratio = count / (double)(image.Width * image.Height);
        if (ratio < .01 || ratio > .50 || maxY - minY < 12) return null;
        // Reject touching text instead of accepting a plausible word from a clipped region.
        if (minX == 0 || minY == 0 || maxX == image.Width - 1 || maxY == image.Height - 1) return null;
        int padX = Math.Max(4, (maxX - minX + 1) / 20), padY = Math.Max(2, (maxY - minY + 1) / 20);
        int left = Math.Max(0, minX - padX), top = Math.Max(0, minY - padY);
        using Mat binary = image.Clone();
        FrlgDigitReader.CopyPixels(binary, pixels, 3);
        using Mat crop = new(binary, new Rect(left, top, Math.Min(image.Width, maxX + padX + 1) - left,
            Math.Min(image.Height, maxY + padY + 1) - top));
        return crop.Clone();
    }

    private static Mat PadWhite(Mat image, int padding)
    {
        Mat result = new();
        Cv2.Resize(image, result, new Size(image.Width + 2 * padding, image.Height + 2 * padding));
        byte[] pixels = Enumerable.Repeat((byte)255, result.Width * result.Height * 3).ToArray();
        byte[] source = FrlgDigitReader.ReadPixels(image);
        for (int row = 0; row < image.Height; row++)
            Array.Copy(source, row * image.Width * 3, pixels, ((row + padding) * result.Width + padding) * 3, image.Width * 3);
        FrlgDigitReader.CopyPixels(result, pixels, 3);
        return result;
    }

    private static bool TouchesInk(Mat image)
    {
        byte[] pixels = FrlgDigitReader.ReadPixels(image);
        int count = 0;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                if (x != 0 && y != 0 && x != image.Width - 1 && y != image.Height - 1) continue;
                int i = (y * image.Width + x) * 3;
                if (pixels[i] < 96 && pixels[i + 1] < 96 && pixels[i + 2] < 96 && ++count >= 2) return true;
            }
        return false;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _tesseract?.Dispose();
            _paddle?.Dispose();
        }
    }
}