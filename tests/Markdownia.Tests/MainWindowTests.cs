using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Markdownia.Controls;
using Markdownia.Rendering;
using Markdownia.Services;
using Markdownia.Views;

namespace Markdownia.Tests;

public sealed class MainWindowTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("markdownia-tests").FullName;

    public MainWindowTests() => Environment.SetEnvironmentVariable("MARKDOWNIA_CONFIG_DIR", Path.Combine(_dir, "config"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static void Pump(int milliseconds)
    {
        // Run the real dispatcher loop so DispatcherTimers (the debounced preview render) fire.
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < until)
        {
            using var slice = new CancellationTokenSource(20);
            Dispatcher.UIThread.MainLoop(slice.Token);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    private (MainWindow Window, TextEditor Editor, MarkdownPreview Preview) Open(string markdown)
    {
        var path = Path.Combine(_dir, "doc.md");
        File.WriteAllText(path, markdown);
        var window = new MainWindow(new AppSettings { RecentFiles = [], SyncScroll = true }) { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFileOnStartup(path);
        Pump(600);
        return (window, window.FindControl<TextEditor>("Editor")!, window.FindControl<MarkdownPreview>("Preview")!);
    }

    [AvaloniaFact]
    public void Scrolling_the_preview_scrolls_the_editor()
    {
        var sections = Enumerable.Range(1, 40).Select(i => $"## Section {i}\n\n" + string.Join(' ', Enumerable.Repeat("Some words to fill a paragraph.", 12)));
        var (window, editor, preview) = Open(string.Join("\n\n", sections) + "\n");

        // Pointing at the preview makes it the pane that leads scroll sync.
        var center = preview.TranslatePoint(new Point(preview.Bounds.Width / 2, 200), window)!.Value;
        window.MouseMove(center);
        var heading = preview.Result!.Anchors["section-20"];
        preview.ScrollViewer.Offset = new Vector(0, heading.TranslatePoint(default, (Visual)preview.ScrollViewer.Content!)!.Value.Y);
        Pump(300);

        var topLine = editor.TextArea.TextView.GetDocumentLineByVisualTop(editor.VerticalOffset + 1).LineNumber;
        var headingLine = editor.Document.Lines.First(l => editor.Document.GetText(l) == "## Section 20").LineNumber;
        Assert.InRange(topLine, headingLine - 2, headingLine + 1);
        window.Close();
    }

    [AvaloniaFact]
    public void Resizing_an_image_in_the_preview_updates_the_source()
    {
        var bitmap = new WriteableBitmap(new PixelSize(400, 200), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Premul);
#pragma warning disable CS0618
        bitmap.Save(Path.Combine(_dir, "pic.png"));
#pragma warning restore CS0618
        var (window, editor, preview) = Open("# Pictures\n\nBefore ![a picture](pic.png) after.\n");
        Pump(400);

        var image = preview.GetVisualDescendants().OfType<PreviewImage>().Single();
        Assert.Equal(400, image.Bounds.Width);
        image.Resized!(250);
        Assert.Equal("# Pictures\n\nBefore ![a picture](pic.png){width=250} after.\n", editor.Text);

        Pump(500);
        var resized = preview.GetVisualDescendants().OfType<PreviewImage>().Single();
        Assert.Equal(250, resized.Bounds.Width);
        Assert.Equal(125, resized.Bounds.Height);
        window.Close();
    }
}
