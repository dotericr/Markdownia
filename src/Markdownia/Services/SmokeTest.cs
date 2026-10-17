using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Markdownia.Rendering;
using Markdownia.Theming;
using Markdownia.Views;

namespace Markdownia.Services;

/// <summary>
/// <c>Markdownia --smoke-test out.png [document.md]</c>: renders the main window chrome and the full document
/// preview offscreen to PNG files, then exits. Used to verify Native AOT builds end to end (XAML, editor, Markdown,
/// math, fonts) without user interaction. Writes <c>out.png</c> (window) and <c>out-preview.png</c> (document).
/// </summary>
internal static class SmokeTest
{
    public static void Run(IClassicDesktopStyleApplicationLifetime desktop, AppSettings settings, string output, string? document)
    {
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new MainWindow(settings) { Width = 1400, Height = 900 };
        var size = new Size(1400, 900);

        DispatcherTimer.RunOnce(() =>
        {
            var exitCode = 0;
            try
            {
                window.Measure(size);
                window.Arrange(new Rect(size));
                SaveWindow(window, size, output);

                var markdown = document is not null ? File.ReadAllText(document) : ReadGuide();
                var previewPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, Path.GetFileNameWithoutExtension(output) + "-preview.png");
                SavePreview(markdown, settings, document, previewPath);
                Console.WriteLine($"Smoke test rendered {output} and {previewPath}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Smoke test failed: " + ex);
                exitCode = 1;
            }
            desktop.Shutdown(exitCode);
        }, TimeSpan.FromMilliseconds(1500));
    }

    private static string ReadGuide()
    {
        using var stream = AssetLoader.Open(new Uri("avares://Markdownia/Assets/Welcome.md"));
        return new StreamReader(stream).ReadToEnd();
    }

    private static void SaveWindow(Window window, Size size, string path)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height));
        if (window.Content is Visual content)
        {
            using (var ctx = bitmap.CreateDrawingContext())
                ctx.FillRectangle(window.Background ?? Brushes.White, new Rect(size));
            bitmap.Render(content);
        }
        Save(bitmap, path);
    }

    private static void SavePreview(string markdown, AppSettings settings, string? document, string path)
    {
        var theme = ThemeManager.Current;
        var style = new PreviewStyle
        {
            Theme = theme,
            BodyFont = FontCatalog.ResolveReading(settings.PreviewFont),
            MonoFont = FontCatalog.ResolveCode(settings.CodeFont),
            FontSize = settings.PreviewFontSize,
            LineSpacing = settings.LineSpacing,
            BaseDirectory = document is null ? null : Path.GetDirectoryName(Path.GetFullPath(document)),
        };
        var parsed = MarkdownService.Parse(markdown, settings.SmartTypography);
        var result = new MarkdownRenderer(style, _ => { }).Render(parsed);
        var page = new Border
        {
            Background = new SolidColorBrush(theme.Background),
            Padding = new Thickness(48),
            Width = 760,
            Child = result.Root,
        };
        // Host the page in a layout root so templated children (ScrollViewers around code, math and tables)
        // get their templates applied; a few passes let presenters pick up their content.
        var host = new Window { Content = page, SizeToContent = SizeToContent.Height, Width = 760 };
        for (var pass = 0; pass < 4; pass++)
        {
            host.Measure(new Size(760, double.PositiveInfinity));
            host.Arrange(new Rect(host.DesiredSize));
            Dispatcher.UIThread.RunJobs();
            foreach (var control in page.GetVisualDescendants().OfType<Layoutable>())
                control.InvalidateMeasure();
        }
        // The host window is clamped to the screen; measure the page itself at its full height.
        page.Measure(new Size(760, double.PositiveInfinity));
        page.Arrange(new Rect(page.DesiredSize));
        var height = (int)Math.Ceiling(Math.Min(page.DesiredSize.Height, 12000));
        using var bitmap = new RenderTargetBitmap(new PixelSize(760, height));
        bitmap.Render(page);
        Save(bitmap, path);
    }

    private static void Save(Bitmap bitmap, string path)
    {
#pragma warning disable CS0618 // Save(string) is fine for a diagnostic PNG.
        bitmap.Save(path);
#pragma warning restore CS0618
    }
}
