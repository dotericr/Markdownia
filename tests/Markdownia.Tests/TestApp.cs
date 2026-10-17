using Avalonia;
using Avalonia.Headless;
using Markdownia;
using Markdownia.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace Markdownia.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
}
