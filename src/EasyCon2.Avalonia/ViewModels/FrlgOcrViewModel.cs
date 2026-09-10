using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Capture.Ocr.Frlg;
using EzCv;
using System.Text.Json;
using UiRect = Avalonia.Rect;

namespace EasyCon2.Avalonia.ViewModels;

public sealed record FrlgSceneChoice(string Label, string Key);
public sealed record FrlgRegionProfile(string Scene, int FrameWidth, int FrameHeight, int X, int Y, int Width, int Height);

public sealed partial class FrlgOcrViewModel : ObservableObject, IDisposable
{
    private readonly Func<byte[]?> _capture;
    private Mat? _frame;
    private readonly FrlgTextReader _textReader = new();
    private bool _disposed;
    [ObservableProperty] private Bitmap? _frameImage;
    [ObservableProperty] private string _sourceDescription = "先冻结采集画面，或打开一张截图。";
    [ObservableProperty] private string _status = "选择场景，再框住需要读取的文字或数字，边缘留少量空白。";
    [ObservableProperty] private string _resultText = "等待识别";
    [ObservableProperty] private string _details = "支持日文名称、性格、等级、六项能力值及日英 TID。名称和性格首次读取需要加载模型。";
    [ObservableProperty] private string _targetNames = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _x;
    [ObservableProperty] private int _y;
    [ObservableProperty] private int _regionWidth;
    [ObservableProperty] private int _regionHeight;
    [ObservableProperty] private FrlgSceneChoice _selectedScene;

    public FrlgSceneChoice[] Scenes { get; } = FrlgScenes.All.Select(s => new FrlgSceneChoice(s.Label, s.Key)).ToArray();

    public FrlgOcrViewModel(Func<byte[]?>? capture = null)
    {
        _capture = capture ?? (() => null);
        _selectedScene = Scenes[0];
    }

    public UiRect Selection
    {
        get => new(X, Y, Math.Max(0, RegionWidth), Math.Max(0, RegionHeight));
        set
        {
            X = (int)value.X;
            Y = (int)value.Y;
            RegionWidth = (int)value.Width;
            RegionHeight = (int)value.Height;
        }
    }

    public bool IsNameScene => FrlgScenes.Find(SelectedScene.Key)!.Kind == "name";
    private string SceneCall => SelectedScene.Key + (IsNameScene && !string.IsNullOrWhiteSpace(TargetNames) ? ":" + TargetNames.Trim() : "");
    public string OcrCall => $"OCR({X}, {Y}, {RegionWidth}, {RegionHeight}, \"{SceneCall}\")";
    public int FrameWidth => _frame?.Width ?? 0;
    public int FrameHeight => _frame?.Height ?? 0;

    partial void OnXChanged(int value) => RegionChanged();
    partial void OnYChanged(int value) => RegionChanged();
    partial void OnRegionWidthChanged(int value) => RegionChanged();
    partial void OnRegionHeightChanged(int value) => RegionChanged();
    partial void OnSelectedSceneChanged(FrlgSceneChoice value) => RegionChanged();
    partial void OnTargetNamesChanged(string value) => RegionChanged();
    private void RegionChanged()
    {
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(OcrCall));
        OnPropertyChanged(nameof(IsNameScene));
        ResultText = "等待识别";
        Details = "区域或场景已修改，请重新读取选区。";
    }

    public void LoadImage(byte[] bytes, string description)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Mat next = Mat.FromImageData(bytes);
        Bitmap bitmap;
        try
        {
            using MemoryStream stream = new(bytes);
            bitmap = new Bitmap(stream);
        }
        catch
        {
            next.Dispose();
            throw;
        }
        bool sameSize = _frame != null && _frame.Width == next.Width && _frame.Height == next.Height;
        Bitmap? oldBitmap = FrameImage;
        _frame?.Dispose();
        _frame = next;
        FrameImage = bitmap;
        oldBitmap?.Dispose();
        if (!sameSize) Selection = default;
        SourceDescription = $"{description} · {next.Width} × {next.Height} 原始像素";
        Status = "已冻结画面。拖动框选，或使用默认区域后微调；识别读取当前这张画面。";
        ResultText = "等待识别";
        Details = "等待对当前快照进行识别。";
        OnPropertyChanged(nameof(FrameWidth));
        OnPropertyChanged(nameof(FrameHeight));
    }

    [RelayCommand]
    private void Capture()
    {
        try
        {
            byte[]? bytes = _capture();
            if (bytes == null)
            {
                Status = "暂无采集画面。请先在主窗口连接采集卡，或打开截图。";
                return;
            }
            LoadImage(bytes, "采集卡快照");
        }
        catch (Exception ex) { Status = $"读取画面失败：{ex.Message}"; }
    }

    [RelayCommand]
    private void DefaultRegion()
    {
        if (_frame == null) { Status = "请先加载画面。"; return; }
        Rect region = FrlgOcr.DefaultRegion(SelectedScene.Key, _frame.Width, _frame.Height);
        Selection = new UiRect(region.X, region.Y, region.Width, region.Height);
        Status = "已应用 Switch 默认区域。可继续拖动重新框选，或修改坐标。";
    }

    [RelayCommand]
    private async Task RecognizeAsync()
    {
        if (_frame == null) { Status = "请先加载画面。"; return; }
        if (RegionWidth <= 0 || RegionHeight <= 0) { Status = "请先框选当前场景的文字或数字。"; return; }
        using Mat snapshot = _frame.Clone();
        Rect region = new(X, Y, RegionWidth, RegionHeight);
        string scene = SceneCall;
        IsBusy = true;
        ResultText = "识别中";
        Details = "";
        try
        {
            FrlgReadResult result = await Task.Run(() => FrlgOcr.ReadFrame(snapshot, region, scene, textReader: _textReader));
            if (_disposed) return;
            ResultText = result.Success ? result.Text : "未识别";
            Status = result.Success
                ? $"{SelectedScene.Label} · {result.ElapsedMilliseconds:F1} ms · 请刷新快照后再次确认"
                : $"读取失败（{result.Failure}）。请检查区域、遮挡或画面清晰度。";
            Details = result.TextAttempts.Length > 0 ? string.Join(Environment.NewLine, result.TextAttempts.Select(a =>
                $"{a.Backend} / {a.Threshold}：[{a.Raw.Trim()}] → {(a.Accepted ? a.Candidate : "未确认")} · {a.Confidence:P0} / 距离 {a.Distance} {a.Failure}"))
                : string.Join(Environment.NewLine, result.Attempts.Select(a =>
                $"阈值 {a.Threshold}：{(a.Failure.Length == 0 ? a.Text : a.Failure)}" + Environment.NewLine
                + string.Join("  |  ", a.Digits.Select(d => $"{d.Digit}：误差 {d.Rmsd:F1} / 差距 {d.RunnerUpRmsd - d.Rmsd:F1}"))));
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                ResultText = "未识别";
                Status = $"识别失败：{ex.Message}";
            }
        }
        finally { if (!_disposed) IsBusy = false; }
    }

    public string ExportRegion()
    {
        if (_frame == null) throw new InvalidOperationException("请先加载画面。");
        if (IsNameScene && !FrlgJapaneseLexicon.ValidTargets(FrlgScenes.Targets(SceneCall)))
            throw new InvalidOperationException("候选名称须为词典中的日文种族名或英文 slug，用 | 分隔。");
        if (X < 0 || Y < 0 || RegionWidth <= 0 || RegionHeight <= 0
            || (long)X + RegionWidth > FrameWidth || (long)Y + RegionHeight > FrameHeight)
            throw new InvalidOperationException("请选择画面内的有效区域。");
        return JsonSerializer.Serialize(new FrlgRegionProfile(SceneCall, FrameWidth, FrameHeight,
            X, Y, RegionWidth, RegionHeight), new JsonSerializerOptions { WriteIndented = true });
    }

    public void ImportRegion(string json)
    {
        FrlgRegionProfile profile = JsonSerializer.Deserialize<FrlgRegionProfile>(json)
            ?? throw new InvalidDataException("区域文件为空。");
        if (_frame == null || profile.FrameWidth != FrameWidth || profile.FrameHeight != FrameHeight)
            throw new InvalidDataException("请先加载与保存区域相同分辨率的画面。");
        FrlgSceneChoice scene = Scenes.FirstOrDefault(s => s.Key == FrlgScenes.BaseKey(profile.Scene))
            ?? throw new InvalidDataException("不支持此识别场景。");
        string[] targets = FrlgScenes.Targets(profile.Scene);
        if (targets.Length > 0 && (FrlgScenes.Find(scene.Key)!.Kind != "name" || !FrlgJapaneseLexicon.ValidTargets(targets)))
            throw new InvalidDataException("区域文件的名称候选无效。");
        if (profile.X < 0 || profile.Y < 0 || profile.Width <= 0 || profile.Height <= 0
            || (long)profile.X + profile.Width > FrameWidth || (long)profile.Y + profile.Height > FrameHeight)
            throw new InvalidDataException("区域超出画面。");
        SelectedScene = scene;
        TargetNames = string.Join('|', FrlgScenes.Targets(profile.Scene));
        Selection = new UiRect(profile.X, profile.Y, profile.Width, profile.Height);
        Status = "已恢复保存的区域。";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Bitmap? bitmap = FrameImage;
        FrameImage = null;
        bitmap?.Dispose();
        _frame?.Dispose();
        _frame = null;
        _textReader.Dispose();
    }
}