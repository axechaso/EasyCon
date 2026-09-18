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
    [TestCase("Page2/nidorino_hp_85_live_jpn.png", "FRLG_JPN_HP", "85")]
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
    public void StatDigitsUseLowErrorVotesAndIgnoreOneConflictingThreshold()
    {
        Assert.That(FrlgDigitReader.HasEnoughSeparation("stat", 60.0, 63.2), Is.True,
            "A clean stat-font 8 may have a small runner-up margin.");
        Assert.That(FrlgDigitReader.HasEnoughSeparation("stat", 71.0, 74.2), Is.False);
        Assert.That(FrlgDigitReader.HasEnoughSeparation("tid", 60.0, 63.2), Is.False);

        static FrlgReadAttempt Vote(int threshold, string text) => new(threshold, text, "",
            [new(text[^1] - '0', new Rect(1, 1, 10, 20), 50, 54)]);
        FrlgReadAttempt[] attempts = [Vote(175, "46"), Vote(190, "48"), Vote(205, "48")];
        Assert.That(FrlgOcr.ConfirmDigitAttempts(attempts, "stat"), Is.EqualTo("48"));
        Assert.That(FrlgOcr.ConfirmDigitAttempts(attempts, "level"), Is.Null);
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
    [TestCase("おとなしいせいかく", "おとなしい", 0)]
    [TestCase("おとなしいせいか", "おとなしい", 0)]
    [TestCase("おとなしいせいかご", "おとなしい", 1)]
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
        Assert.That(FrlgJapaneseLexicon.Match("おとなしいせいかく", true, requireNatureDescriptor: true).Accepted, Is.True);
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
    public void NameConfirmationAcceptsExactPlusIndependentConsistentVote()
    {
        FrlgTextAttempt exact = new("PaddleOCR", 184, "クラブ", .77, "クラブ", 0, true, "");
        FrlgTextAttempt supporting = new("PaddleOCR", 160, "ワラブ", .90, "クラブ", 1, true, "");
        Assert.That(FrlgTextReader.NameConfirmedByPrimaryVariants([exact, supporting]), Is.True);
        Assert.That(FrlgTextReader.NameConfirmedByPrimaryVariants(
            [exact with { Confidence = .70 }, supporting with { Confidence = .68 }]), Is.True);
        Assert.That(FrlgTextReader.NameConfirmedByPrimaryVariants([exact with { Confidence = .59 }, supporting]), Is.False);
        Assert.That(FrlgTextReader.NameConfirmedByPrimaryVariants([exact, supporting with { Confidence = .59 }]), Is.False);
        Assert.That(FrlgTextReader.NameConfirmedByPrimaryVariants([exact, supporting with { Threshold = 184 }]), Is.False);
        Assert.That(FrlgTextReader.NameConfirmedByPrimaryVariants(
            [exact, supporting with { Candidate = "ラブカス" }]), Is.False);
        Assert.That(FrlgTextReader.NameConfirmedByPrimaryVariants(
            [exact with { LexiconAccepted = false }, supporting with { LexiconAccepted = false }]), Is.False);
        Assert.That(FrlgTextReader.NameConfirmedByPrimaryVariants(
            [exact with { Distance = 1 }, supporting]), Is.False);
        FrlgTextAttempt kinglerExact = new("PaddleOCR", 184, "キングラー", .90, "キングラー", 0, true, "");
        FrlgTextAttempt kinglerSupport = new("PaddleOCR", 160, "キンウラー", .72, "キングラー", 1, false, "");
        Assert.That(FrlgTextReader.NameConfirmedByPrimaryVariants([kinglerExact, kinglerSupport]), Is.True);
        Assert.That(FrlgTextReader.NameConfirmedByPrimaryVariants(
            [kinglerExact, kinglerSupport with { Candidate = "キングドラ" }]), Is.False);
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
    public void DefaultWildRegionsKeepCompleteGlyphMargins()
    {
        Assert.That(FrlgOcr.DefaultRegion("FRLG_JPN_NAME", 1920, 1080),
            Is.EqualTo(new Rect(300, 122, 350, 72)));
        Assert.That(FrlgOcr.DefaultRegion("FRLG_JPN_WILD_LEVEL", 1920, 1080),
            Is.EqualTo(new Rect(720, 130, 115, 68)));
    }

    [Test]
    public void WildLevelReadsDigitsWithoutDigitTemplates()
    {
        // Public fixture is English, whose level sits farther right than the Japanese layout.
        using Mat frame = Fixture("Wild/eng_dragonair.jpg");
        string scene = "FRLG_JPN_WILD_LEVEL";
        Assert.That(FrlgOcr.ReadFrame(frame, new Rect(755, 129, 70, 66), scene).Text,
            Is.EqualTo("28"));
    }

    [TestCase("Wild/eng_dragonair.jpg", '♂')]
    [TestCase("Wild/eng_chansey.jpg", '♀')]
    public void BattleGenderMarkerIsReadFromItsGlyph(string file, char expected)
    {
        using Mat frame = Fixture(file);
        using Mat region = new(frame, new Rect(300, 122, 350, 72));
        Assert.That(FrlgTextReader.DetectGenderMarker(region), Is.EqualTo(expected));
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