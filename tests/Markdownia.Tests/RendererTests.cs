using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.LogicalTree;
using Markdownia.Rendering;
using Markdownia.Services;
using Markdownia.Theming;

namespace Markdownia.Tests;

public class RendererTests
{
    private static RenderResult Render(string markdown)
    {
        var style = new PreviewStyle
        {
            Theme = Themes.Paper,
            BodyFont = FontCatalog.ReadingFonts[0].Family,
            MonoFont = FontCatalog.CodeFonts[0].Family,
        };
        return new MarkdownRenderer(style, _ => { }).Render(MarkdownService.Parse(markdown, true));
    }

    [AvaloniaFact]
    public void Guide_renders_with_anchors_for_headings_and_footnotes()
    {
        using var stream = AssetLoader.Open(new Uri("avares://Markdownia/Assets/Welcome.md"));
        var result = Render(new StreamReader(stream).ReadToEnd());

        Assert.Contains("tables", result.Anchors.Keys);
        Assert.Contains("fn:1", result.Anchors.Keys);
        Assert.Contains("fnref:1", result.Anchors.Keys);
        Assert.True(result.LineAnchors.Count > 20);
    }

    [AvaloniaFact]
    public void Links_are_tracked_as_interactive_ranges()
    {
        var result = Render("See [the docs](https://example.com) now.");
        var block = Assert.IsType<RichTextBlock>(result.Root.GetLogicalDescendants().OfType<RichTextBlock>().Single());
        var range = Assert.Single(block.Ranges);
        Assert.Equal("https://example.com", range.Link);
        Assert.Equal(4, range.Start);
        Assert.Equal(12, range.End);
    }

    [AvaloniaFact]
    public void Invalid_math_reports_an_error_instead_of_throwing()
    {
        var result = Render("$$\n\\frac{1}{\n$$");
        var math = result.Root.GetLogicalDescendants().OfType<MathElement>().Single();
        Assert.True(math.HasError);
    }

    [AvaloniaFact]
    public void Image_alone_in_a_paragraph_becomes_a_captioned_figure()
    {
        var result = Render("![A caption](missing.png)");
        var texts = result.Root.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Inlines?.Count > 0 ? string.Concat(t.Inlines.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)) : t.Text).ToList();
        Assert.Contains("A caption", texts);
    }
}
