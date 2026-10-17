using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Markdownia.Editing;
using Markdownia.Rendering;

namespace Markdownia.Tests;

public class ImageTests
{
    private static string Apply(string text, string image, bool isHtml, int? width)
    {
        var start = text.IndexOf(image, StringComparison.Ordinal);
        var edit = MarkdownEditing.ImageWidthEdit(text, start, start + image.Length, isHtml, width);
        return edit is { } e ? text[..e.Offset] + e.Text + text[(e.Offset + e.Length)..] : text;
    }

    [Fact]
    public void Width_attribute_is_added_after_a_markdown_image()
    {
        Assert.Equal("a ![x](p.png){width=320} b", Apply("a ![x](p.png) b", "![x](p.png)", false, 320));
    }

    [Fact]
    public void Existing_size_attributes_are_replaced_and_other_attributes_kept()
    {
        Assert.Equal("![x](p.png){#fig .wide width=200}", Apply("![x](p.png){#fig .wide height=3in width=50%}", "![x](p.png)", false, 200));
    }

    [Fact]
    public void Resetting_removes_the_attribute_block_when_it_becomes_empty()
    {
        Assert.Equal("![x](p.png) after", Apply("![x](p.png){width=200} after", "![x](p.png)", false, null));
        Assert.Equal("![x](p.png){.wide}", Apply("![x](p.png){.wide width=200}", "![x](p.png)", false, null));
    }

    [Fact]
    public void Html_images_get_a_width_attribute()
    {
        Assert.Equal("<img src=\"a.png\" alt=\"A\" width=\"150\">", Apply("<img src=\"a.png\" width=\"20\" alt=\"A\" height=40>", "<img src=\"a.png\" width=\"20\" alt=\"A\" height=40>", true, 150));
        Assert.Equal("<img src=\"a.png\" width=\"90\" />", Apply("<img src=\"a.png\"/>", "<img src=\"a.png\"/>", true, 90));
    }

    [Fact]
    public void Lengths_parse_common_units()
    {
        Assert.Equal(new ImageLength(300, false), ImageLength.Parse("300", 16));
        Assert.Equal(new ImageLength(300, false), ImageLength.Parse("300px", 16));
        Assert.Equal(new ImageLength(50, true), ImageLength.Parse("50%", 16));
        Assert.Equal(96, ImageLength.Parse("1in", 16)!.Value.Value, 3);
        Assert.Equal(160, ImageLength.Parse("10em", 16)!.Value.Value, 3);
        Assert.Null(ImageLength.Parse("wide", 16));
    }

    [AvaloniaFact]
    public void Dragging_the_handle_reports_the_new_width()
    {
        var bitmap = new WriteableBitmap(new PixelSize(200, 100), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Premul);
        int? reported = -1;
        var image = new PreviewImage(bitmap, Colors.SteelBlue) { Resized = w => reported = w };
        var window = new Window { Width = 600, Height = 400, Content = new StackPanel { Children = { image } } };
        window.Show();

        Assert.Equal(200, image.Bounds.Width);
        var handle = new Point(200 - 8, 100 - 8);
        window.MouseMove(handle);
        window.MouseDown(handle, MouseButton.Left);
        window.MouseMove(handle + new Vector(100, 0));
        Assert.Equal(300, image.Bounds.Width);
        Assert.Equal(150, image.Bounds.Height);
        window.MouseUp(handle + new Vector(100, 0), MouseButton.Left);
        Assert.Equal(300, reported);
    }

    [AvaloniaFact]
    public void Images_never_exceed_the_column()
    {
        var bitmap = new WriteableBitmap(new PixelSize(1000, 500), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Premul);
        var image = new PreviewImage(bitmap, Colors.SteelBlue) { WidthSpec = new ImageLength(1200, false) };
        var window = new Window { Width = 400, Height = 400, Content = new StackPanel { Children = { image } } };
        window.Show();
        Assert.Equal(400, image.Bounds.Width);
        Assert.Equal(200, image.Bounds.Height);
    }
}
