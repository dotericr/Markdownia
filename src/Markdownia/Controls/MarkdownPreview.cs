using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Markdownia.Rendering;

namespace Markdownia.Controls;

/// <summary>
/// Hosts the rendered document: a scrollable "page" whose text column is limited to a comfortable reading
/// measure and centered in the available space.
/// </summary>
public sealed class MarkdownPreview : UserControl
{
    private readonly ScrollViewer _scroll;
    private readonly Border _page;
    private RenderResult? _result;
    private double _maxWidthEm = 36;
    private double _fontSize = 18;

    public MarkdownPreview()
    {
        _page = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _scroll = new ScrollViewer
        {
            Content = _page,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        Content = _scroll;
        Focusable = true;
        SizeChanged += (_, _) => UpdatePageWidth();
        // Tunnel so Ctrl+wheel zooms before the ScrollViewer consumes the wheel event.
        AddHandler(PointerWheelChangedEvent, OnWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>Raised on Ctrl+mouse wheel with +1 / -1.</summary>
    public event Action<int>? ZoomRequested;

    public RenderResult? Result => _result;

    public ScrollViewer ScrollViewer => _scroll;

    /// <summary>Width of the text column in ems (0 = fill the pane).</summary>
    public void SetMeasure(double maxWidthEm, double fontSize)
    {
        _maxWidthEm = maxWidthEm;
        _fontSize = fontSize;
        UpdatePageWidth();
    }

    private void UpdatePageWidth()
    {
        var available = Bounds.Width;
        if (available <= 0)
            return;
        var gutter = Math.Clamp(available * 0.06, 20, 72);
        var column = _maxWidthEm <= 0 ? available - 2 * gutter : Math.Min(_maxWidthEm * _fontSize, available - 2 * gutter);
        column = Math.Max(120, column);
        _page.Width = column + 2 * gutter;
        _page.Padding = new Thickness(gutter, Math.Round(_fontSize * 2.4), gutter, Math.Round(_fontSize * 5));
    }

    public void SetTextRendering(string mode)
    {
        var (rendering, hinting) = mode switch
        {
            "Sharp" => (TextRenderingMode.Antialias, TextHintingMode.Strong),
            "Subpixel" => (TextRenderingMode.SubpixelAntialias, TextHintingMode.Light),
            _ => (TextRenderingMode.Antialias, TextHintingMode.Light),
        };
        TextOptions.SetTextRenderingMode(_scroll, rendering);
        TextOptions.SetTextHintingMode(_scroll, hinting);
        TextOptions.SetBaselinePixelAlignment(_scroll, BaselinePixelAlignment.Aligned);
    }

    public void Show(RenderResult result)
    {
        var offset = _scroll.Offset;
        _result = result;
        _page.Child = result.Root;
        // Keep the reader's place when the document is re-rendered while typing.
        Dispatcher.UIThread.Post(() => _scroll.Offset = offset, DispatcherPriority.Loaded);
    }

    public void ScrollToAnchor(string id)
    {
        if (_result is null || !_result.Anchors.TryGetValue(id, out var target))
            return;
        if (target.TranslatePoint(default, _page) is { } point)
        {
            var y = Math.Max(0, point.Y - _fontSize * 1.5);
            _scroll.Offset = new Vector(_scroll.Offset.X, y);
        }
    }

    /// <summary>
    /// Scrolls so the content generated from <paramref name="line"/> (fractional, 0-based source line) is at the top.
    /// Interpolates between the nearest rendered blocks.
    /// </summary>
    public void SyncToSourceLine(double line, bool atEnd)
    {
        if (_result is null || _result.LineAnchors.Count == 0)
            return;
        double target;
        if (atEnd)
        {
            target = _scroll.Extent.Height - _scroll.Viewport.Height;
        }
        else if (line <= 0.01)
        {
            target = 0;
        }
        else
        {
            var anchors = _result.LineAnchors;
            var index = -1;
            for (var i = 0; i < anchors.Count; i++)
            {
                if (anchors[i].Line <= line)
                    index = i;
                else
                    break;
            }
            if (index < 0)
            {
                target = 0;
            }
            else
            {
                var a = anchors[index];
                var yA = TopOf(a.Control);
                var nextIndex = index + 1;
                while (nextIndex < anchors.Count && anchors[nextIndex].Line == a.Line)
                    nextIndex++;
                if (yA is null)
                    return;
                if (nextIndex < anchors.Count && TopOf(anchors[nextIndex].Control) is { } yB)
                {
                    var span = anchors[nextIndex].Line - a.Line;
                    var fraction = span <= 0 ? 0 : Math.Clamp((line - a.Line) / span, 0, 1);
                    target = yA.Value + fraction * (yB - yA.Value);
                }
                else
                {
                    var height = a.Control.Bounds.Height;
                    target = yA.Value + Math.Clamp(line - a.Line, 0, 1) * height * 0.5;
                }
                target -= _page.Padding.Top * 0.5;
            }
        }
        target = Math.Clamp(target, 0, Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height));
        _scroll.Offset = new Vector(_scroll.Offset.X, target);
    }

    /// <summary>
    /// The inverse of <see cref="SyncToSourceLine"/>: the fractional 0-based source line shown at the top of the
    /// viewport, interpolated between the nearest rendered blocks above and below. Null when nothing is rendered.
    /// </summary>
    public double? SourceLineAtTop()
    {
        if (_result is null || _result.LineAnchors.Count == 0)
            return null;
        var y = _scroll.Offset.Y + _page.Padding.Top * 0.5;
        // Anchors are ordered by source line, but not always by position (footnotes render at the end),
        // so take the closest block above and the closest below rather than assuming order.
        (int Line, double Top)? above = null, below = null;
        foreach (var (line, control) in _result.LineAnchors)
        {
            if (TopOf(control) is not { } top)
                continue;
            if (top <= y)
            {
                if (above is null || top >= above.Value.Top)
                    above = (line, top);
            }
            else if (below is null || top < below.Value.Top)
            {
                below = (line, top);
            }
        }
        if (above is not { } a)
            return 0;
        if (below is { } b && b.Line > a.Line && b.Top > a.Top)
            return a.Line + (y - a.Top) / (b.Top - a.Top) * (b.Line - a.Line);
        return a.Line;
    }

    public bool IsScrolledToEnd =>
        _scroll.Offset.Y > 0 && _scroll.Offset.Y >= _scroll.Extent.Height - _scroll.Viewport.Height - 2;

    private double? TopOf(Control control) => control.TranslatePoint(default, _page)?.Y;

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Delta.Y != 0)
        {
            ZoomRequested?.Invoke(e.Delta.Y > 0 ? 1 : -1);
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.Handled)
            Focus();
    }

    public void SetBackground(IBrush brush) => _scroll.Background = brush;
}
