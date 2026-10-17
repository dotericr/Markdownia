using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CSharpMath.Atom;
using CSharpMath.Avalonia;

namespace Markdownia.Rendering;

/// <summary>
/// Renders a LaTeX formula with CSharpMath (Latin Modern Math) directly into the Avalonia drawing context.
/// Inline formulas report their baseline so they sit on the surrounding text's baseline.
/// </summary>
public sealed class MathElement : Control
{
    private readonly MathPainter _painter = new();
    private readonly double _ascent;
    private readonly double _descent;
    private readonly double _width;

    public MathElement(string latex, double fontSize, Color color, Color errorColor, bool display)
    {
        Latex = latex;
        _painter.LineStyle = display ? LineStyle.Display : LineStyle.Text;
        _painter.FontSize = (float)fontSize;
        _painter.TextColor = color;
        _painter.ErrorColor = errorColor;
        _painter.ErrorFontSize = (float)(fontSize * 0.8);
        _painter.DisplayErrorInline = true;
        _painter.LaTeX = latex;

        var bounds = _painter.Measure();
        _ascent = Math.Max(0, -bounds.Y);
        _descent = Math.Max(0, bounds.Height - _ascent);
        _width = Math.Max(1, bounds.Width);

        // Lets TextBlock align embedded inline formulas to the text baseline.
        TextBlock.SetBaselineOffset(this, _ascent);
        HasError = _painter.ErrorMessage is not null;
        if (HasError)
            ToolTip.SetTip(this, _painter.ErrorMessage);
    }

    public string Latex { get; }

    public bool HasError { get; }

    protected override Size MeasureOverride(Size availableSize) => new(_width, _ascent + _descent);

    public override void Render(DrawingContext context)
    {
        var canvas = new AvaloniaCanvas(context, Bounds.Size);
        _painter.Draw(canvas, 0, (float)_ascent);
    }
}
