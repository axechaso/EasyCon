using EasyCon.Capture;
using EasyCon.Capture.Ocr;
using EasyCon.Capture.Ocr.Frlg;
using EasyCon.Core;
using EzCv;
using System.Runtime.InteropServices;

namespace EasyCon.Tests;

[TestFixture]
public sealed class FrlgOcrTests
{
    private static Mat Fixture(string name) => Mat.FromImageData(File.ReadAllBytes(
        Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "Frlg", name)));

    private static void FillWhite(Mat image, Rect region)
    {
        Assert.That(image.Channels(), Is.EqualTo(3));
        byte[] row = Enumerable.Repeat((byte)255, region.Width * 3).ToArray();
        IntPtr data = image.Data;
        int stride = checked((int)image.Step());
        for (int y = region.Y; y < region.Y + region.Height; y++)
            Marshal.Copy(row, 0, IntPtr.Add(data, y * stride + region.X * 3), row.Length);
        GC.KeepAlive(image);
    }

    [TestCase("nyash_jpn_45345.png", FrlgOcr.JapaneseTid, "45345", 1080)]
    [TestCase("nyash_jpn_45345.png", FrlgOcr.JapaneseTid, "45345", 720)]
    [TestCase("nyash_jpn_45345.png", FrlgOcr.JapaneseTid, "45345", 2160)]
    [TestCase("tom_eng_60895.jpg", FrlgOcr.EnglishTid, "60895", 720)]
    [TestCase("tom_eng_60895.jpg", FrlgOcr.EnglishTid, "60895", 1080)]
    public void PublicScreenshotsReadThroughEcsDelegate(string name, string scene, string expected, int height)
    {
        using Mat original = Fixture(name);
        using Mat frame = new();
        Cv2.Resize(original, frame, new Size(original.Width * height / original.Height, height));
        using OcrEngineCache cache = new(new ForbiddenFactory());
        FrameStore store = new();
        store.Publish(frame.Clone());
        try
        {
            EasyScript.OcrDelegate read = OcrDelegateFactory.Create(store.AcquireLatest, cache);
            Rect roi = FrlgOcr.DefaultRegion(scene, frame.Width, frame.Height);
            Assert.That(read(roi.X, roi.Y, roi.Width, roi.Height, scene), Is.EqualTo(expected));
            Assert.That(cache.LastFrlgResult?.Success, Is.True);
            Assert.That(cache.LastConfidence, Is.GreaterThan(0));
            // Releasing a recognition lease must not release the producer's current frame.
            Assert.That(read(roi.X, roi.Y, roi.Width, roi.Height, scene), Is.EqualTo(expected));
        }
        finally { store.ReleaseCurrent(); }
    }

    [Test]
    public void ManualRegionAndOwnedMatDelegateUseSamePipeline()
    {
        using Mat frame = Fixture("nyash_jpn_45345.png");
        using OcrEngineCache cache = new(new ForbiddenFactory());
        EasyScript.OcrDelegate read = OcrDelegateFactory.Create((Func<Mat>)(() => frame.Clone()), cache);
        Assert.That(read(1300, 130, 310, 83, FrlgOcr.JapaneseTid), Is.EqualTo("45345"));
        Assert.That(read(1600, 130, 400, 83, FrlgOcr.JapaneseTid), Is.Empty);
        Assert.That(cache.LastFrlgResult?.Failure, Is.EqualTo("invalid-region"));
        Assert.That(cache.LastConfidence, Is.Zero);
    }

    [TestCase(0, 0, 1920, 1080, "region-size-out-of-range")]
    [TestCase(-1, 100, 330, 80, "invalid-region")]
    [TestCase(1290, 130, 0, 80, "invalid-region")]
    [TestCase(int.MaxValue, 130, int.MaxValue, 80, "invalid-region")]
    public void InvalidRegionsFailWithoutTruncation(int x, int y, int width, int height, string failure)
    {
        using Mat frame = Fixture("nyash_jpn_45345.png");
        FrlgReadResult result = FrlgOcr.ReadFrame(frame, new Rect(x, y, width, height), FrlgOcr.JapaneseTid);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Text, Is.Empty);
        Assert.That(result.Failure, Is.EqualTo(failure));
    }

    [TestCase(FrlgOcr.JapaneseTid)]
    [TestCase(FrlgOcr.EnglishTid)]
    public void EmptyAndMissingFramesFail(string scene)
    {
        using OcrEngineCache cache = new() { LastConfidence = 99 };
        EasyScript.OcrDelegate read = OcrDelegateFactory.Create((Func<FrameLease?>)(() => null), cache);
        Assert.That(read(0, 0, 330, 90, scene), Is.Empty);
        Assert.That(cache.LastConfidence, Is.Zero);
        // Decode to obtain the native OpenCV 5 type; do not assume OpenCV 4's channel bit shift.
        using Mat blank = Fixture("nyash_jpn_45345.png");
        FillWhite(blank, new Rect(0, 0, blank.Width, blank.Height));
        Rect roi = FrlgOcr.DefaultRegion(scene, 1920, 1080);
        Assert.That(FrlgOcr.ReadFrame(blank, roi, scene).Success, Is.False);
    }

    [Test]
    public void FourDigitsAndClippedGlyphAreRejected()
    {
        using Mat frame = Fixture("nyash_jpn_45345.png");
        FrlgReadResult four = FrlgOcr.ReadFrame(frame, new Rect(1300, 128, 255, 85), FrlgOcr.JapaneseTid);
        FrlgReadResult clipped = FrlgOcr.ReadFrame(frame, new Rect(1325, 128, 290, 85), FrlgOcr.JapaneseTid);
        Assert.That(four.Text, Is.Empty);
        Assert.That(clipped.Text, Is.Empty);
    }

    [TestCase("00895", true)]
    [TestCase("00000", true)]
    [TestCase("99999", false)]
    public void ComposedCapturedDigitsPreserveZerosAndRejectOverflow(string text, bool accepted)
    {
        // Composition tests the output contract, not accuracy on additional real captures.
        using Mat original = Fixture("tom_eng_60895.jpg");
        using Mat reference = new();
        Cv2.Resize(original, reference, new Size(1920, 1080));
        using Mat frame = reference.Clone();
        Rect roi = FrlgOcr.DefaultRegion(FrlgOcr.EnglishTid, 1920, 1080);
        FillWhite(frame, roi);
        const string referenceDigits = "60895";
        Assert.That(reference.Channels(), Is.EqualTo(3));
        for (int index = 0; index < text.Length; index++)
        {
            int sourceIndex = referenceDigits.IndexOf(text[index]);
            byte[] row = new byte[39 * 3];
            for (int y = 130; y < 200; y++)
            {
                IntPtr source = IntPtr.Add(reference.Data, checked((int)(y * reference.Step())) + (1341 + sourceIndex * 39) * 3);
                IntPtr destination = IntPtr.Add(frame.Data, checked((int)(y * frame.Step())) + (1341 + index * 39) * 3);
                Marshal.Copy(source, row, 0, row.Length);
                Marshal.Copy(row, 0, destination, row.Length);
            }
        }
        GC.KeepAlive(reference);
        FrlgReadResult result = FrlgOcr.ReadFrame(frame, roi, FrlgOcr.EnglishTid);
        Assert.That(result.Success, Is.EqualTo(accepted), result.Failure);
        Assert.That(result.Text, Is.EqualTo(accepted ? text : ""));
    }

    [TestCase("jpn")]
    [TestCase("eng")]
    [TestCase("FRLG_EN_ALL")]
    public void ExistingModelNamesStillUseOriginalEngine(string lang)
    {
        using Mat frame = Fixture("nyash_jpn_45345.png");
        RecordingFactory factory = new();
        using OcrEngineCache cache = new(factory);
        EasyScript.OcrDelegate read = OcrDelegateFactory.Create((Func<Mat>)(() => frame.Clone()), cache);
        Assert.That(read(1, 1, 100, 50, lang), Is.EqualTo("legacy-text"));
        Assert.That(factory.Language, Is.EqualTo(lang));
        Assert.That(cache.LastFrlgResult, Is.Null);
    }

    private sealed class ForbiddenFactory : IOcrEngineFactory
    {
        public IOcrRecognizer CreateRecognizer(string lang, string dataPath, string engineMode, string psmode)
            => throw new InvalidOperationException("FRLG digits must never initialize text OCR.");
        public IOcrEngine? CreateEngine(string lang, string dataPath, string engineMode, string psmode) => null;
    }

    private sealed class RecordingFactory : IOcrEngineFactory
    {
        public string? Language { get; private set; }
        public IOcrRecognizer CreateRecognizer(string lang, string dataPath, string engineMode, string psmode)
        {
            Language = lang;
            return new FakeRecognizer();
        }
        public IOcrEngine? CreateEngine(string lang, string dataPath, string engineMode, string psmode) => null;
    }

    private sealed class FakeRecognizer : IOcrRecognizer
    {
        public OcrRecognizeResult Recognize(byte[] image) => new("legacy-text", 0.9f);
        public void Dispose() { }
    }
}