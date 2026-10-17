using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Markdownia.Services;
using Markdownia.Theming;
using Markdownia.Views;

namespace Markdownia;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = SettingsStore.Load();
            ThemeManager.Apply(Themes.Find(settings.ThemeId));

            var args = desktop.Args ?? [];
            var smokeIndex = Array.IndexOf(args, "--smoke-test");
            if (smokeIndex >= 0 && smokeIndex + 1 < args.Length)
            {
                SmokeTest.Run(desktop, settings, args[smokeIndex + 1], args.Skip(smokeIndex + 2).FirstOrDefault());
                base.OnFrameworkInitializationCompleted();
                return;
            }

            var window = new MainWindow(settings);
            desktop.MainWindow = window;
            var file = desktop.Args?.FirstOrDefault(a => !a.StartsWith('-'));
            if (file is not null)
                window.OpenFileOnStartup(file);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
