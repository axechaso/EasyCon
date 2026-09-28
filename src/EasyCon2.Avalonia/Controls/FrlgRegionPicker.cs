using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace EasyCon2.Avalonia.Controls;

/// <summary>Selection is always in source pixels, independent of window size and display DPI.</summary>
public sealed class FrlgRegionPicker : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<FrlgRegionPicker, Bitmap?>(nameof(Source));
    public static readonly StyledProperty<Rect> SelectionProperty =
        AvaloniaProperty.Register<FrlgRegionPicker, Rect>(nameof(Selection), defaultBindingMode: BindingMode.TwoWay);
    private Point? _start;

    static FrlgRegionPicker()
    {
        AffectsRender<FrlgRegionPicker>(SourceProperty, SelectionProperty);
    }

    public FrlgRegionPicker() => Cursor = new Cursor(StandardCursorType.Cross);
    public Bitmap? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public Rect Selection { get => GetValue(SelectionProperty); set => SetValue(SelectionProperty, value); }

    public static Rect ImageBounds(Size available, PixelSize pixels)
    {
        if (pixels.Width <= 0 || pixels.Height <= 0) return default;
        double scale = Math.Min(available.Width / pixels.Width, available.Height / pixels.Height);
        Size size = new(pixels.Width * scale, pixels.Height * scale);
        return new Rect((available.Width - size.Width) / 2, (available.Height - size.Height) / 2, size.Width, size.Height);
    }

    public static Point ToPixel(Point point, Rect display, PixelSize pixels) => new(
        Math.Clamp((point.X - display.X) / display.Width * pixels.Width, 0, pixels.Width),
        Math.Clamp((point.Y - display.Y) / display.Height * pixels.Height, 0, pixels.Height));

    public static Rect PixelSelection(Point start, Point end)
    {
        double x = Math.Floor(Math.Min(start.X, end.X));
        double y = Math.Floor(Math.Min(start.Y, end.Y));
        return new Rect(x, y, Math.Ceiling(Math.Max(start.X, end.X)) - x, Math.Ceiling(Math.Max(start.Y, end.Y)) - y);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#141A22")), new Rect(Bounds.Size));
        if (Source == null) return;
        Rect display = ImageBounds(Bounds.Size, Source.PixelSize);
        context.DrawImage(Source, new Rect(Source.Size), display);
        if (Selection.Width <= 0 || Selection.Height <= 0) return;
        Rect selection = new(display.X + Selection.X / Source.PixelSize.Width * display.Width,
            display.Y + Selection.Y / Source.PixelSize.Height * display.Height,
            Selection.Width / Source.PixelSize.Width * display.Width,
            Selection.Height / Source.PixelSize.Height * display.Height);
        using DrawingContext.PushedState clip = context.PushClip(display);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#2000E5C0")), new Pen(Brushes.SpringGreen, 2), selection);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Source == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Rect display = ImageBounds(Bounds.Size, Source.PixelSize);
        Point point = e.GetPosition(this);
        if (!display.Contains(point)) return;
        _start = ToPixel(point, display, Source.PixelSize);
        SetCurrentValue(SelectionProperty, new Rect(_start.Value, default(Size)));
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        UpdateSelection(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_start == null) return;
        UpdateSelection(e);
        _start = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _start = null;
        base.OnPointerCaptureLost(e);
    }

    private void UpdateSelection(PointerEventArgs e)
    {
        if (_start == null || Source == null) return;
        Rect display = ImageBounds(Bounds.Size, Source.PixelSize);
        if (display.Width == 0 || display.Height == 0) return;
        SetCurrentValue(SelectionProperty, PixelSelection(_start.Value, ToPixel(e.GetPosition(this), display, Source.PixelSize)));
    }
}