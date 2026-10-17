using Avalonia.Media;
using Markdownia.Theming;

namespace Markdownia.Rendering;

/// <summary>Typography and color settings for one render of the preview.</summary>
public sealed record PreviewStyle
{
    public required MarkdownTheme Theme { get; init; }
    public required FontFamily BodyFont { get; init; }
    public required FontFamily MonoFont { get; init; }
    public double FontSize { get; init; } = 18;
    public double LineSpacing { get; init; } = 1.6;
    public bool Justify { get; init; }
    public string? BaseDirectory { get; init; }

    /// <summary>Body line height snapped to whole pixels, which keeps baselines crisp.</summary>
    public double LineHeight => Math.Round(FontSize * LineSpacing);
}
