using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace Markdownia.Rendering;

/// <summary>A CSS-like length from a Pandoc <c>width=</c>/<c>height=</c> attribute or an HTML attribute.</summary>
public readonly record struct ImageLength(double Value, bool IsPercent)
{
    /// <summary>Parses <c>300</c>, <c>300px</c>, <c>50%</c>, <c>3in</c>, <c>8cm</c>, <c>80mm</c>, <c>12em</c> or <c>200pt</c>.</summary>
    public static ImageLength? Parse(string? text, double em)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var s = text.Trim().Trim('"', '\'').ToLowerInvariant();
        var unitStart = s.Length;
        while (unitStart > 0 && !char.IsDigit(s[unitStart - 1]) && s[unitStart - 1] != '.')
            unitStart--;
        if (!double.TryParse(s[..unitStart], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
            return null;
        return s[unitStart..].Trim() switch
        {
            "" or "px" => new ImageLength(value, false),
            "%" => new ImageLength(value, true),
            "in" => new ImageLength(value * 96, false),
            "cm" => new ImageLength(value * 96 / 2.54, false),
            "mm" => new ImageLength(value * 96 / 25.4, false),
            "pt" => new ImageLength(value * 96 / 72, false),
            "em" or "rem" => new ImageLength(value * em, false),
            _ => null,
        };
    }

    public double Resolve(double reference) =>
        IsPercent ? (double.IsFinite(reference) ? reference * Value / 100 : double.NaN) : Value;
}

/// <summary>
/// A preview image that honors width/height attributes, never overflows its column, and can be resized by
/// dragging the handle in its bottom-right corner. Releasing the handle reports the new width so the
/// Markdown source can be updated; double-clicking the handle resets the image to its natural size.
/// </summary>
public sealed class PreviewImage : Control
{
    private const double HandleSize = 11;
    private const double MinimumWidth = 24;

    private static readonly Cursor ResizeCursor = new(StandardCursorType.BottomRightCorner);

    private readonly Bitmap _bitmap;
    private readonly IBrush _accent;
    private readonly IBrush _outline;
    private bool _hovered;
    private bool _overHandle;
    private double? _dragWidth;
    private double? _committedWidth;
    private object? _tip;
    private double _availableWidth = double.PositiveInfinity;
    private double _dragStartX;
    private double _dragStartWidth;
    private TextBlock? _hostText;

    public PreviewImage(Bitmap bitmap, Color accent)
    {
        _bitmap = bitmap;
        _accent = new SolidColorBrush(accent);
        _outline = new SolidColorBrush(accent, 0.55);
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        // Never stretch: the image keeps its aspect ratio at the measured size.
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
    }

    public ImageLength? WidthSpec { get; init; }

    public ImageLength? HeightSpec { get; init; }

    /// <summary>Called with the new width in pixels when a resize drag ends, or with null to reset the size.</summary>
    public Action<int?>? Resized { get; init; }

    private Size NaturalSize => _bitmap.Size;

    private double Aspect => NaturalSize.Height > 0 ? NaturalSize.Width / NaturalSize.Height : 1;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Inline images are measured by their text block with unlimited width, so track the text block's width
        // to keep them inside the column.
        _hostText = this.FindAncestorOfType<TextBlock>();
        if (_hostText is not null)
            _hostText.SizeChanged += OnHostSizeChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_hostText is not null)
            _hostText.SizeChanged -= OnHostSizeChanged;
        _hostText = null;
    }

    private void OnHostSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5)
            InvalidateMeasure();
    }

    private double ColumnWidth(double available)
    {
        if (double.IsFinite(available))
            return available;
        if (_hostText is { Bounds.Width: > 0 } host)
            return host.Bounds.Width - host.Padding.Left - host.Padding.Right;
        return double.PositiveInfinity;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _availableWidth = availableSize.Width;
        var column = ColumnWidth(availableSize.Width);
        double width;
        if ((_dragWidth ?? _committedWidth) is { } pinned)
            width = pinned;
        else if (WidthSpec?.Resolve(column) is { } w && double.IsFinite(w))
            width = w;
        else if (HeightSpec?.Resolve(double.NaN) is { } h && double.IsFinite(h))
            width = h * Aspect;
        else
            width = NaturalSize.Width;
        width = Math.Max(1, Math.Min(width, column));
        return new Size(width, width / Aspect);
    }

    protected override Size ArrangeOverride(Size finalSize) => finalSize;

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.DrawImage(_bitmap, new Rect(_bitmap.Size), bounds);
        if (Resized is null || !(_hovered || _dragWidth is not null))
            return;

        context.DrawRectangle(null, new Pen(_outline, 1.5), bounds.Deflate(0.75));
        var handle = HandleRect();
        // White with an accent border so the handle stands out on both light and dark (or accent-colored) images.
        context.DrawRectangle(Brushes.White, new Pen(_accent, 2), handle, 2, 2);

        if (_dragWidth is not null)
        {
            var label = new FormattedText(
                $"{Math.Round(Bounds.Width)} × {Math.Round(Bounds.Height)}",
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 11, Brushes.White);
            var box = new Rect(bounds.Right - label.Width - 14 - HandleSize, bounds.Bottom - label.Height - 10, label.Width + 8, label.Height + 4);
            if (box.X > 0 && box.Y > 0)
            {
                context.DrawRectangle(new SolidColorBrush(Colors.Black, 0.65), null, box, 3, 3);
                context.DrawText(label, new Point(box.X + 4, box.Y + 2));
            }
        }
    }

    // Measured against the window, because this control's own origin moves as a centered image is resized.
    private double PointerX(PointerEventArgs e) => e.GetPosition(TopLevel.GetTopLevel(this) ?? (Visual)this).X;

    private Rect HandleRect() =>
        new(Bounds.Width - HandleSize - 3, Bounds.Height - HandleSize - 3, HandleSize, HandleSize);

    private bool IsOverHandle(Point p) => Resized is not null && HandleRect().Inflate(5).Contains(p);

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hovered = true;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = false;
        _overHandle = false;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragWidth is not null)
        {
            // A centered image grows on both sides, so it must grow twice as fast to follow the pointer.
            var centered = HorizontalAlignment == HorizontalAlignment.Center ||
                           Parent is Layoutable { HorizontalAlignment: HorizontalAlignment.Center };
            var column = ColumnWidth(_availableWidth);
            var max = double.IsFinite(column) ? column : 10000;
            var width = _dragStartWidth + (PointerX(e) - _dragStartX) * (centered ? 2 : 1);
            _dragWidth = Math.Clamp(width, Math.Min(MinimumWidth, max), max);
            InvalidateMeasure();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        var over = IsOverHandle(e.GetPosition(this));
        if (over == _overHandle)
            return;
        _overHandle = over;
        Cursor = over ? ResizeCursor : null;
        ToolTip.SetTip(this, over ? "Drag to resize · double-click to reset" : _tip);
    }

    /// <summary>The image tooltip (its title), restored when the pointer leaves the resize handle.</summary>
    public object? Tip
    {
        get => _tip;
        init
        {
            _tip = value;
            ToolTip.SetTip(this, value);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !IsOverHandle(e.GetPosition(this)))
            return;
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            Resized?.Invoke(null);
            return;
        }
        _dragStartX = PointerX(e);
        _dragStartWidth = Bounds.Width;
        _dragWidth = Bounds.Width;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragWidth is not { } width)
            return;
        e.Handled = true;
        e.Pointer.Capture(null);
        var changed = Math.Abs(width - _dragStartWidth) >= 1;
        _dragWidth = null;
        if (changed)
        {
            // Keep the dragged size until the re-rendered preview replaces this control.
            _committedWidth = width;
            Resized?.Invoke((int)Math.Round(width));
        }
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_dragWidth is null)
            return;
        _dragWidth = null;
        InvalidateMeasure();
        InvalidateVisual();
    }
}
