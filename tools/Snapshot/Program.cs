using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Markdownia;
using Markdownia.Services;
using Markdownia.Theming;
using Markdownia.Views;

// Usage: Snapshot <out.png> [--theme id] [--file path] [--layout split|reader|editor] [--size WxH]
//                 [--tab Home|Insert|Design|View] [--scroll px] [--font name] [--fontsize n]
var argsList = args.ToList();
string Opt(string name, string fallback)
{
    var i = argsList.IndexOf(name);
    return i >= 0 && i + 1 < argsList.Count ? argsList[i + 1] : fallback;
}

var output = Path.GetFullPath(args[0]);
AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

var settings = new AppSettings
{
    ThemeId = Opt("--theme", "paper"),
    RecentFiles = [],
};
var layout = Opt("--layout", "split");
settings.ShowEditor = layout != "reader";
settings.ShowPreview = layout != "editor";
settings.PreviewFont = Opt("--font", settings.PreviewFont);
settings.PreviewFontSize = double.Parse(Opt("--fontsize", settings.PreviewFontSize.ToString()));
ThemeManager.Apply(Themes.Find(settings.ThemeId));

var size = Opt("--size", "1400x900").Split('x').Select(double.Parse).ToArray();
var window = new MainWindow(settings) { Width = size[0], Height = size[1] };
window.Show();

var file = Opt("--file", "");
if (file.Length > 0)
    window.OpenFileOnStartup(file);

void Pump(int ms)
{
    var until = DateTime.UtcNow.AddMilliseconds(ms);
    while (DateTime.UtcNow < until)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(15);
    }
}

Pump(1500);
var tab = Opt("--tab", "");
if (tab.Length > 0 && window.FindControl<RadioButton>("Tab" + tab) is { } radio)
    radio.IsChecked = true;
var scroll = double.Parse(Opt("--scroll", "0"));
if (scroll > 0 && window.FindControl<Markdownia.Controls.MarkdownPreview>("Preview") is { } preview)
    preview.ScrollViewer.Offset = new Vector(0, scroll);
var editorScroll = double.Parse(Opt("--editor-scroll", "0"));
if (editorScroll > 0 && window.FindControl<AvaloniaEdit.TextEditor>("Editor") is { } editor)
    editor.GetVisualDescendants().OfType<ScrollViewer>().First().Offset = new Vector(0, editorScroll);
Pump(800);

var frame = window.CaptureRenderedFrame();
#pragma warning disable CS0618
frame?.Save(output);
#pragma warning restore CS0618
Console.WriteLine(frame is null ? "no frame" : $"saved {output}");
window.Close();
