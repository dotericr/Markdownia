using Avalonia;

namespace Markdownia;

internal static class Program
{
    // Don't use Avalonia, third-party APIs or SynchronizationContext-reliant code before AppMain is called.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Matches StartupWMClass in the Linux desktop entry, so docks group the window with its launcher.
            .With(new X11PlatformOptions { WmClass = "Markdownia" })
            .WithInterFont()
            .LogToTrace();
}
