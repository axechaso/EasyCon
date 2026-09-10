using EasyCon.Capture;
using EasyCon.Capture.Ocr.Frlg;
using EasyCon.Core;
using EzCv;
using System.Runtime.InteropServices;

namespace EasyCon.Tests;

[TestFixture]
public sealed class FrlgJapaneseTests
{
    private OcrEngineCache _cache = null!;
    private static Mat Fixture(string name) => Mat.FromImageData(File.ReadAllBytes(
        Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "Frlg", name)));

    [OneTimeSetUp]
    public void Initialize() => _cache = new OcrEngineCache();
    [OneTimeTearDown]
    public void Cleanup() => _cache.Dispose();

    [TestCase("Page1/bulbasaur_1_jpn.png", "FRLG_JPN_SUMMARY_NAME", "フシギダネ")]
    [TestCase("Page1/bulbasaur_1_jpn.png", "FRLG_JPN_NATURE", "のうてんき")]
    [TestCase("Page1/bulbasaur_1_jpn.png", "FRLG_JPN_LEVEL", "6")]
    [TestCase("Page1/deoxys_1_jpn.png", "FRLG_JPN_SUMMARY_NAME", "デオキシス")]
    [TestCase("Page1/deoxys_1_jpn.png", "FRLG_JPN_NATURE", "しんちょう")]
    [TestCase("Page1/deoxys_1_jpn.png", "FRLG_JPN_LEVEL", "30")]
    [TestCase("Page2/deoxys_1_jpn.png", "FRLG_JPN_HP", "70")]
    [TestCase("Page2/deoxys_1_jpn.png", "FRLG_JPN_ATTACK", "119")]
    [TestCase("Page2/deoxys_1_jpn.png", "FRLG_JPN_DEFENSE", "21")]
    [TestCase("Page2/deoxys_1_jpn.png", "FRLG_JPN_SP_ATTACK", "119")]
    [TestCase("Page2/deoxys_1_jpn.png", "FRLG_JPN_SP_DEFENSE", "23")]
    [TestCase("Page2/deoxys_1_jpn.png", "FRLG_JPN_SPEED", "107")]
    public void JapaneseSummaryReadsThroughEcs(string file, string scene, string expected)
    {
        using Mat frame = Fixture(file);
        EasyScript.OcrDelegate read = OcrDelegateFactory.Create((Func<Mat>)(() => frame.Clone()), _cache);
        Rect roi = FrlgOcr.DefaultRegion(scene, frame.Width, frame.Height);
        Assert.That(read(roi.X, roi.Y, roi.Width, roi.Height, scene), Is.EqualTo(expected),
            () => System.Text.Json.JsonSerializer.Serialize(_cache.LastFrlgResult));
    }

    [TestCase(720)]
    [TestCase(2160)]
    public void SummaryDigitsScaleWithFrame(int height)
    {
        using Mat original = Fixture("Page2/deoxys_1_jpn.png");
        using Mat frame = new();
        Cv2.Resize(original, frame, new Size(original.Width * height / original.Height, height));
        string scene = "FRLG_JPN_ATTACK";
        Assert.That(FrlgOcr.ReadFrame(frame, FrlgOcr.DefaultRegion(scene, frame.Width, frame.Height), scene).Text, Is.EqualTo("119"));
    }

    [Test]
    public void TargetSetCannotForceUnrelatedExactSpecies()
    {
        using Mat frame = Fixture("Page1/deoxys_1_jpn.png");
        Rect roi = FrlgOcr.DefaultRegion("FRLG_JPN_SUMMARY_NAME", frame.Width, frame.Height);
        EasyScript.OcrDelegate read = OcrDelegateFactory.Create((Func<Mat>)(() => frame.Clone()), _cache);
        Assert.That(read(roi.X, roi.Y, roi.Width, roi.Height, "FRLG_JPN_SUMMARY_NAME:デオキシス|ミュウ"), Is.EqualTo("デオキシス"));
        Assert.That(read(roi.X, roi.Y, roi.Width, roi.Height, "FRLG_JPN_SUMMARY_NAME:ハクリュー"), Is.Empty);
        Assert.That(_cache.LastFrlgResult!.TextAttempts.Any(a => a.Backend == "Tesseract" && a.Failure.Length == 0), Is.True,
            "The rejected primary result must obtain a real Tesseract second opinion.");
        Assert.That(read(roi.X, roi.Y, roi.Width, roi.Height, "FRLG_JPN_SUMMARY_NAME:not-a-species"), Is.Empty);
        Assert.That(_cache.LastFrlgResult!.Failure, Is.EqualTo("invalid-target-set"));
    }

    [Test]
    public void DictionariesCoverNamesAndAllNaturesWithoutDragonairDragoniteMixup()
    {
        Assert.That(FrlgJapaneseLexicon.NameCount, Is.GreaterThan(1000));
        Assert.That(FrlgJapaneseLexicon.NatureCount, Is.EqualTo(25));
        Assert.That(FrlgJapaneseLexicon.Match("ハクリュー", false).Text, Is.EqualTo("ハクリュー"));
        Assert.That(FrlgJapaneseLexicon.Match("カイリュー", false).Text, Is.EqualTo("カイリュー"));
        Assert.That(FrlgJapaneseLexicon.Match("ハクリュー", false, ["dragonite"]).Accepted, Is.False);
        Assert.That(FrlgJapaneseLexicon.Match("ハクリュー", false, ["dragonair"]).Accepted, Is.True);
        Assert.That(FrlgJapaneseLexicon.Match("フシギタネ", false).Text, Is.EqualTo("フシギダネ"));
        Assert.That(FrlgJapaneseLexicon.Match("", false).Accepted, Is.False);
        Assert.That(FrlgJapaneseLexicon.Match("意図しない全く別の文章", false).Accepted, Is.False);
        string[] natures = ["いじっぱり", "てれや", "ずぶとい", "ゆうかん", "おだやか", "しんちょう", "すなお", "おとなしい",
            "がんばりや", "せっかち", "わんぱく", "ようき", "のうてんき", "さみしがり", "おっとり", "ひかえめ",
            "むじゃき", "やんちゃ", "れいせい", "きまぐれ", "うっかりや", "のんき", "なまいき", "まじめ", "おくびょう"];
        foreach (string nature in natures)
        {
            FrlgWordMatch result = FrlgJapaneseLexicon.Match(nature + "なせいかく", true);
            Assert.That(result.Accepted, Is.True, nature);
            Assert.That(result.Text, Is.EqualTo(nature));
        }
    }

    [Test]
    public void WildLevelReadsDigitsWithoutTextEngine()
    {
        // Public battle fixture is English; the digit crop is shared. Japanese capture remains a hardware check.
        using Mat frame = Fixture("Wild/eng_dragonair.jpg");
        string scene = "FRLG_JPN_WILD_LEVEL";
        Assert.That(FrlgOcr.ReadFrame(frame, FrlgOcr.DefaultRegion(scene, frame.Width, frame.Height), scene).Text, Is.EqualTo("28"));
    }

    [Test]
    public void FaintedHpRetainsFullMaximumAndDoesNotConcatenatePair()
    {
        using Mat frame = Fixture("Page2/deoxys_1_jpn.png");
        // Remove the leading 7 from current HP: real glyphs now display 0/70.
        for (int y = 144; y < 210; y++)
        {
            byte[] background = new byte[3];
            int stride = checked((int)frame.Step());
            Marshal.Copy(IntPtr.Add(frame.Data, y * stride + 1405 * 3), background, 0, 3);
            for (int x = 1430; x < 1480; x++) Marshal.Copy(background, 0, IntPtr.Add(frame.Data, y * stride + x * 3), 3);
        }
        string scene = "FRLG_JPN_HP";
        Assert.That(FrlgOcr.ReadFrame(frame, FrlgOcr.DefaultRegion(scene, frame.Width, frame.Height), scene).Text, Is.EqualTo("70"));
    }

    [Test]
    public void MissingModelsAndBlankRegionFailClearly()
    {
        using Mat frame = Fixture("Page1/deoxys_1_jpn.png");
        using FrlgTextReader missing = new(Path.Combine(TestContext.CurrentContext.WorkDirectory, "absent-models"));
        string scene = "FRLG_JPN_SUMMARY_NAME";
        FrlgReadResult result = FrlgOcr.ReadFrame(frame, FrlgOcr.DefaultRegion(scene, frame.Width, frame.Height), scene, textReader: missing);
        Assert.That(result.Text, Is.Empty);
        Assert.That(result.Failure, Is.EqualTo("text-backends-unavailable"));
        Assert.That(result.TextAttempts.Select(a => a.Backend).Distinct(), Is.EquivalentTo(new[] { "PaddleOCR", "Tesseract" }));
        Assert.That(FrlgOcr.ReadFrame(frame, new Rect(0, 200, 120, 75), scene, textReader: missing).Text, Is.Empty);
    }
}