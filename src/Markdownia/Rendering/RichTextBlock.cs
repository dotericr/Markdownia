using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Markdownia.Rendering;

/// <summary>A character range inside a <see cref="RichTextBlock"/> that carries a link target and/or a tooltip.</summary>
public sealed record InteractiveRange(int Start, int End, string? Link, string? ToolTip);

/// <summary>
/// Selectable rich text that also supports clickable links and hover tooltips (for links and abbreviations).
/// Avalonia has no hyperlink inline, so link ranges are tracked by character offset and resolved by hit testing.
/// </summary>
public sealed class RichTextBlock : SelectableTextBlock
{
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor TextCursor = new(StandardCursorType.Ibeam);

    private InteractiveRange? _hovered;
    private Point _pressPoint;

    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    public List<InteractiveRange> Ranges { get; } = [];

    public Action<string>? LinkActivated { get; set; }

    private InteractiveRange? RangeAt(Point point)
    {
        if (Ranges.Count == 0)
            return null;
        var layoutPoint = new Point(point.X - Padding.Left, point.Y - Padding.Top);
        var hit = TextLayout.HitTestPoint(layoutPoint);
        if (!hit.IsInside)
            return null;
        var index = hit.TextPosition;
        foreach (var range in Ranges)
        {
            if (index >= range.Start && index < range.End)
                return range;
        }
        return null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var range = RangeAt(e.GetPosition(this));
        if (ReferenceEquals(range, _hovered))
            return;
        _hovered = range;
        Cursor = range?.Link is not null ? HandCursor : TextCursor;
        var tip = range?.ToolTip;
        if (tip is null)
        {
            ToolTip.SetIsOpen(this, false);
            ToolTip.SetTip(this, null);
        }
        else
        {
            ToolTip.SetTip(this, tip);
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = null;
        ToolTip.SetTip(this, null);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _pressPoint = e.GetPosition(this);
        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;
        var point = e.GetPosition(this);
        var moved = Math.Abs(point.X - _pressPoint.X) > 3 || Math.Abs(point.Y - _pressPoint.Y) > 3;
        if (moved)
            return;
        if (RangeAt(point) is { Link: { } link })
        {
            ClearSelection();
            LinkActivated?.Invoke(link);
            e.Handled = true;
        }
    }
}
