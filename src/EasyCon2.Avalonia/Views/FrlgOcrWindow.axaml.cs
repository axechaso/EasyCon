using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EasyCon2.Avalonia.ViewModels;

namespace EasyCon2.Avalonia.Views;

public partial class FrlgOcrWindow : Window
{
    public FrlgOcrWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as FrlgOcrViewModel)?.Dispose();
    }

    private async void OpenImage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FrlgOcrViewModel vm) return;
        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择游戏截图",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("截图") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp"] }]
            });
            if (files.Count == 0) return;
            await using Stream stream = await files[0].OpenReadAsync();
            using MemoryStream bytes = new();
            await stream.CopyToAsync(bytes);
            vm.LoadImage(bytes.ToArray(), files[0].Name);
        }
        catch (Exception ex) { vm.Status = $"打开截图失败：{ex.Message}"; }
    }

    private async void SaveRegion_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FrlgOcrViewModel vm) return;
        try
        {
            string json = vm.ExportRegion();
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存 OCR 区域",
                SuggestedFileName = "frlg-tid-region.json",
                DefaultExtension = "json"
            });
            if (file == null) return;
            await using Stream stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using StreamWriter writer = new(stream);
            await writer.WriteAsync(json);
            vm.Status = "区域已保存，包含场景、分辨率和坐标。";
        }
        catch (Exception ex) { vm.Status = $"保存区域失败：{ex.Message}"; }
    }

    private async void LoadRegion_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FrlgOcrViewModel vm) return;
        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "载入 OCR 区域",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("区域配置") { Patterns = ["*.json"] }]
            });
            if (files.Count == 0) return;
            await using Stream stream = await files[0].OpenReadAsync();
            using StreamReader reader = new(stream);
            vm.ImportRegion(await reader.ReadToEndAsync());
        }
        catch (Exception ex) { vm.Status = $"载入区域失败：{ex.Message}"; }
    }

    private async void CopyCall_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FrlgOcrViewModel vm) return;
        try
        {
            _ = vm.ExportRegion();
            if (Clipboard == null) throw new InvalidOperationException("当前环境没有剪贴板。");
            DataTransfer transfer = new();
            transfer.Add(DataTransferItem.CreateText(vm.OcrCall));
            await Clipboard.SetDataAsync(transfer);
            vm.Status = "已复制 OCR 调用。识别失败返回空字符串，请保留多次确认。";
        }
        catch (Exception ex) { vm.Status = $"复制失败：{ex.Message}"; }
    }
}