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
            foreach (string suffix in new[] { "なせ", "なせい", "なせいか" })
            {
                FrlgWordMatch partialDescriptor = FrlgJapaneseLexicon.Match(nature + suffix, true);
                Assert.That(partialDescriptor.Accepted, Is.True, nature + suffix);
                Assert.That(partialDescriptor.Text, Is.EqualTo(nature));
                Assert.That(partialDescriptor.Distance, Is.Zero);
            }
        }
    }

    [TestCase(748)] // Ends inside the following L: r2 rejected the entire region as clipped-text.
    [TestCase(1104)] // Includes the entire Lv30のとき clause.
    [TestCase(474)] // Complete nature and なせ; the fixed descriptor need not be complete.
    public void NatureReadsWithFollowingLevelOrPartialDescriptor(int width)
    {
        using Mat frame = Fixture("Page1/deoxys_1_jpn.png");
        EasyScript.OcrDelegate read = OcrDelegateFactory.Create((Func<Mat>)(() => frame.Clone()), _cache);
        Assert.That(read(256, 787, width, 67, "FRLG_JPN_NATURE"), Is.EqualTo("しんちょう"),
            () => System.Text.Json.JsonSerializer.Serialize(_cache.LastFrlgResult));
    }

    [Test]
    public void DefaultRegionHandlesShortNatureWithTrailingLevel()
    {
        using Mat frame = Fixture("Page1/bulbasaur_1_jpn.png");
        // Compose のんき from the public のうてんき glyphs by removing うて.
        // Keep the following descriptor and Lv6; the default region now ends inside the level.
        int stride = checked((int)frame.Step());
        byte[] row = new byte[(1360 - 480) * 3];
        for (int y = 787; y < 854; y++)
        {
            Marshal.Copy(IntPtr.Add(frame.Data, y * stride + 480 * 3), row, 0, row.Length);
            Marshal.Copy(row, 0, IntPtr.Add(frame.Data, y * stride + 350 * 3), row.Length);
        }
        string scene = "FRLG_JPN_NATURE";
        FrlgReadResult result = FrlgOcr.ReadFrame(frame, FrlgOcr.DefaultRegion(scene, frame.Width, frame.Height), scene,
            textReader: _cache.FrlgText);
        Assert.That(result.Text, Is.EqualTo("のんき"), () => System.Text.Json.JsonSerializer.Serialize(result));
    }

    [TestCase("FRLG_JPN_NATURE", 305, 787, 699, 67)] // Cropped first nature glyph, with trailing L.
    [TestCase("FRLG_JPN_NATURE", 256, 805, 748, 49)] // Cropped top, with trailing L.
    [TestCase("FRLG_JPN_NATURE", 256, 787, 325, 67)] // Cropped last nature glyph.
    [TestCase("FRLG_JPN_SUMMARY_NAME", 1300, 224, 362, 72)]
    public void ClippedNatureAndNameStillFail(string scene, int x, int y, int width, int height)
    {
        using Mat frame = Fixture("Page1/deoxys_1_jpn.png");
        FrlgReadResult result = FrlgOcr.ReadFrame(frame, new Rect(x, y, width, height), scene, textReader: _cache.FrlgText);
        Assert.That(result.Text, Is.Empty);
        Assert.That(result.Failure, Is.EqualTo("clipped-text"));
    }

    [TestCase("しんちょうなせ", "しんちょう", 0)]
    [TestCase("きまぐれなせい", "きまぐれ", 0)]
    [TestCase("きまくれなせい", "きまぐれ", 1)]
    [TestCase("きまべれなせい", "きまぐれ", 1)]
    [TestCase("き ま ぐ れ 志 せ い", "きまぐれ", 1)]
    [TestCase("者 ま ぐ れ 思 せ い", "きまぐれ", 2)]
    public void NatureLogsMatchNameSeparatelyFromDescriptor(string raw, string expected, int distance)
    {
        FrlgWordMatch result = FrlgJapaneseLexicon.Match(raw, true);
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Text, Is.EqualTo(expected));
        Assert.That(result.Distance, Is.EqualTo(distance));
    }

    [TestCase("しんちょなせ")]
    [TestCase("しんちょ")]
    [TestCase("しんちょうなせいかくLv30のとき")]
    [TestCase("しんちょう無関係")]
    public void NatureCannotMatchTruncatedNameOrArbitrarySuffix(string raw)
    {
        Assert.That(FrlgJapaneseLexicon.Match(raw, true).Accepted, Is.False);
    }

    [Test]
    public void SpatiallySplitNatureRequiresDescriptor()
    {
        Assert.That(FrlgJapaneseLexicon.Match("しんちょう", true, requireNatureDescriptor: true).Accepted, Is.False);
        Assert.That(FrlgJapaneseLexicon.Match("しんちょうなせいかく", true, requireNatureDescriptor: true).Accepted, Is.True);
    }

    [Test]
    public void NatureConfirmationRequiresIndependentExactReadsAndNoConflict()
    {
        FrlgTextAttempt Attempt(string backend, string raw, double confidence)
        {
            FrlgWordMatch match = FrlgJapaneseLexicon.Match(raw, true);
            return new(backend, 184, raw, confidence, match.Text, match.Distance, match.Accepted, "");
        }
        FrlgTextAttempt exact = Attempt("PaddleOCR", "きまぐれなせいかく", .96);
        FrlgTextAttempt supporting = Attempt("PaddleOCR", "きまくれなせいかく", .88);
        FrlgTextAttempt confirmation = Attempt("Tesseract", "きまぐれなせいかく", .79);
        Assert.That(FrlgTextReader.NatureConfirmedByBothBackends([exact, supporting], [confirmation]), Is.True);
        Assert.That(FrlgTextReader.NatureConfirmedByBothBackends([exact], [confirmation]), Is.False);
        Assert.That(FrlgTextReader.NatureConfirmedByBothBackends([exact, supporting], []), Is.False);
        Assert.That(FrlgTextReader.NatureConfirmedByBothBackends([exact, supporting], [confirmation with { Distance = 1 }]), Is.False);
        Assert.That(FrlgTextReader.NatureConfirmedByBothBackends([exact with { Confidence = .70 }, supporting], [confirmation]), Is.False);
        Assert.That(FrlgTextReader.NatureConfirmedByBothBackends([exact, supporting], [confirmation with { Confidence = .60 }]), Is.False);
        Assert.That(FrlgTextReader.NatureConfirmedByBothBackends([exact, supporting],
            [confirmation, Attempt("Tesseract", "おっとりなせいかく", .95)]), Is.False);
    }

    [Test]
    public void KanaVoicingCorrectionPreservesRealSpeciesAndTargetRestrictions()
    {
        foreach ((string raw, string expected) in new[] { ("サンター", "サンダー"), ("ラブラス", "ラプラス") })
        {
            FrlgWordMatch match = FrlgJapaneseLexicon.Match(raw, false);
            Assert.That(match.Accepted, Is.True);
            Assert.That(match.Text, Is.EqualTo(expected));
            Assert.That(match.Distance, Is.Zero, "Distance is measured after kana voicing normalization.");
        }
        Assert.That(FrlgJapaneseLexicon.Match("ラブカス", false).Text, Is.EqualTo("ラブカス"));
        Assert.That(FrlgJapaneseLexicon.Match("ラブカス", false, ["lapras"]).Accepted, Is.False);
        Assert.That(FrlgJapaneseLexicon.Match("ラブラス", false, ["luvdisc"]).Accepted, Is.False);
        Assert.That(FrlgJapaneseLexicon.Match("ラブラス", false, ["lapras"]).Accepted, Is.True);
        Assert.That(FrlgJapaneseLexicon.Match("サンター", false, ["mew"]).Accepted, Is.False);
    }

    [TestCase(720)]
    [TestCase(1080)]
    [TestCase(2160)]
    public void DefaultNatureRegionIncludesRaisedDakuten(int height)
    {
        Rect region = FrlgOcr.DefaultRegion("FRLG_JPN_NATURE", height * 16 / 9, height);
        Assert.That(region.Y, Is.GreaterThan(778 * height / 1080.0));
        Assert.That(region.Y, Is.LessThan(785 * height / 1080.0));
        Assert.That(region.Y + region.Height, Is.GreaterThan(851 * height / 1080.0));
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