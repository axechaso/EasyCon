using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using EasyCon2.Avalonia.Controls;
using EasyCon2.Avalonia.ViewModels;
using EasyCon2.Avalonia.Views;
using NUnit.Framework;

[assembly: AvaloniaTestApplication(typeof(FrlgOcr.Ui.Tests.TestAppBuilder))]

namespace FrlgOcr.Ui.Tests;

public sealed class TestApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApplication>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

[TestFixture]
public sealed class RegionPickerTests
{
    private static byte[] Fixture() => File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory,
        "TestData", "nyash_jpn_45345.png"));

    [AvaloniaTest]
    public async Task DragSelectionRecognizesAndRendersWindowAsync()
    {
        using FrlgOcrViewModel vm = new();
        vm.LoadImage(Fixture(), "上游日版测试截图");
        FrlgOcrWindow window = new() { DataContext = vm };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            FrlgRegionPicker picker = window.FindControl<FrlgRegionPicker>("RegionPicker")!;
            Rect display = FrlgRegionPicker.ImageBounds(picker.Bounds.Size, vm.FrameImage!.PixelSize);
            Point offset = picker.TranslatePoint(default, window)!.Value;
            Point NativePoint(int x, int y) => offset + new Point(display.X + x / 1920.0 * display.Width,
                display.Y + y / 1080.0 * display.Height);
            // Reverse drag is intentional, and the pointer coordinates include letterboxing.
            window.MouseDown(NativePoint(1614, 214), MouseButton.Left);
            window.MouseMove(NativePoint(1295, 126));
            window.MouseUp(NativePoint(1295, 126), MouseButton.Left);
            Assert.That(vm.X, Is.InRange(1294, 1295));
            Assert.That(vm.Y, Is.InRange(125, 126));
            Assert.That(vm.RegionWidth, Is.InRange(319, 321));
            Assert.That(vm.RegionHeight, Is.InRange(88, 90));
            await vm.RecognizeCommand.ExecuteAsync(null);
            Assert.That(vm.ResultText, Is.EqualTo("45345"));
            Assert.That(vm.OcrCall, Does.Contain("FRLG_JPN_TID"));
            Dispatcher.UIThread.RunJobs();
            using RenderTargetBitmap rendered = new(new PixelSize(1120, 820));
            rendered.Render(window);
            string output = Environment.GetEnvironmentVariable("FRLG_UI_SCREENSHOT")
                ?? Path.Combine(TestContext.CurrentContext.WorkDirectory, "frlg-ocr-window.png");
            rendered.Save(output);
            Assert.That(new FileInfo(output).Length, Is.GreaterThan(10000));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void RegionProfilesRoundTripAndRejectWrongResolution()
    {
        using FrlgOcrViewModel vm = new();
        vm.LoadImage(Fixture(), "fixture");
        vm.Selection = new Rect(1300, 130, 310, 83);
        string profile = vm.ExportRegion();
        vm.Selection = new Rect(1, 1, 10, 10);
        vm.ImportRegion(profile);
        Assert.That(vm.Selection, Is.EqualTo(new Rect(1300, 130, 310, 83)));
        Assert.Throws<InvalidDataException>(() => vm.ImportRegion(profile.Replace("1920", "1280")));
        Assert.That(vm.Selection, Is.EqualTo(new Rect(1300, 130, 310, 83)));
        // A fresh snapshot at the same resolution keeps a manually selected ROI.
        vm.LoadImage(Fixture(), "refreshed");
        Assert.That(vm.Selection, Is.EqualTo(new Rect(1300, 130, 310, 83)));
    }

    [AvaloniaTest]
    public void MissingCaptureAndUnselectedRegionAreClear()
    {
        using FrlgOcrViewModel vm = new();
        vm.CaptureCommand.Execute(null);
        Assert.That(vm.Status, Does.Contain("暂无采集画面"));
        vm.LoadImage(Fixture(), "fixture");
        Assert.Throws<InvalidOperationException>(() => vm.ExportRegion());
        Assert.That(vm.ResultText, Is.EqualTo("等待识别"));
    }

    [Test]
    public void DisplayScaleAndLetterboxDoNotChangeNativeCoordinates()
    {
        PixelSize pixels = new(1920, 1080);
        foreach (Size size in new[] { new Size(800, 600), new Size(1024, 350), new Size(1920, 1080) })
        {
            Rect display = FrlgRegionPicker.ImageBounds(size, pixels);
            Point point = new(display.X + display.Width * 0.75, display.Y + display.Height * 0.25);
            Point native = FrlgRegionPicker.ToPixel(point, display, pixels);
            Assert.That(native.X, Is.EqualTo(1440).Within(0.001));
            Assert.That(native.Y, Is.EqualTo(270).Within(0.001));
        }
    }

    [AvaloniaTest]
    public async Task JapaneseNameSelectionTargetsAndRenderingAsync()
    {
        using FrlgOcrViewModel vm = new();
        vm.LoadImage(File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "TestData", "Page1", "bulbasaur_1_jpn.png")), "上游日文摘要截图");
        vm.SelectedScene = vm.Scenes.Single(s => s.Key == "FRLG_JPN_SUMMARY_NAME");
        vm.TargetNames = "フシギダネ|ハクリュー";
        vm.DefaultRegionCommand.Execute(null);
        string profile = vm.ExportRegion();
        vm.TargetNames = "";
        vm.ImportRegion(profile);
        Assert.That(vm.OcrCall, Does.Contain("FRLG_JPN_SUMMARY_NAME:フシギダネ|ハクリュー"));
        FrlgOcrWindow window = new() { DataContext = vm };
        window.Show();
        try
        {
            await vm.RecognizeCommand.ExecuteAsync(null);
            Assert.That(vm.ResultText, Is.EqualTo("フシギダネ"), vm.Details);
            Assert.That(vm.Details, Does.Contain("PaddleOCR"));
            Dispatcher.UIThread.RunJobs();
            using RenderTargetBitmap rendered = new(new PixelSize(1120, 820));
            rendered.Render(window);
            string output = Environment.GetEnvironmentVariable("FRLG_JPN_UI_SCREENSHOT")
                ?? Path.Combine(TestContext.CurrentContext.WorkDirectory, "frlg-jpn-window.png");
            rendered.Save(output);
            vm.SelectedScene = vm.Scenes.Single(s => s.Key == "FRLG_JPN_NATURE");
            Assert.That(vm.ResultText, Is.EqualTo("等待识别"));
            Assert.That(vm.OcrCall, Does.Not.Contain("フシギダネ"));
            vm.DefaultRegionCommand.Execute(null);
            await vm.RecognizeCommand.ExecuteAsync(null);
            Assert.That(vm.ResultText, Is.EqualTo("のうてんき"), vm.Details);
        }
        finally { window.Close(); }
    }
}