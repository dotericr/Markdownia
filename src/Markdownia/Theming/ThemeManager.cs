using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Markdownia.Theming;

/// <summary>
/// Publishes the active <see cref="MarkdownTheme"/> as application resources so that chrome defined in XAML
/// (via DynamicResource) follows the theme, and switches the Fluent light/dark variant to match.
/// </summary>
public static class ThemeManager
{
    public static MarkdownTheme Current { get; private set; } = Themes.Paper;

    public static event EventHandler? ThemeChanged;

    public static void Apply(MarkdownTheme theme)
    {
        Current = theme;
        if (Application.Current is not { } app)
            return;

        app.RequestedThemeVariant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;

        var r = app.Resources;
        void Brush(string key, Color color) => r[key] = new SolidColorBrush(color);

        Brush("Md.ChromeBrush", theme.Chrome);
        Brush("Md.ChromeBorderBrush", theme.ChromeBorder);
        Brush("Md.ChromeForegroundBrush", theme.ChromeForeground);
        Brush("Md.ChromeMutedBrush", Blend(theme.ChromeForeground, theme.Chrome, 0.62));
        Brush("Md.ChromeHoverBrush", Blend(theme.ChromeForeground, theme.Chrome, 0.08));
        Brush("Md.ChromePressedBrush", Blend(theme.ChromeForeground, theme.Chrome, 0.14));
        Brush("Md.ChromeCheckedBrush", Blend(theme.Accent, theme.Chrome, 0.20));
        Brush("Md.RibbonBrush", Blend(theme.Background, theme.Chrome, 0.55));
        Brush("Md.AccentBrush", theme.Accent);
        Brush("Md.AccentForegroundBrush", Luminance(theme.Accent) > 0.55 ? Color.Parse("#1A1A1A") : Colors.White);
        Brush("Md.PreviewBackgroundBrush", theme.Background);
        Brush("Md.PreviewForegroundBrush", theme.Foreground);
        Brush("Md.EditorBackgroundBrush", theme.EditorBackground);
        Brush("Md.EditorForegroundBrush", theme.EditorForeground);
        Brush("Md.SelectionBrush", theme.Selection);

        // Tint Fluent's accent so focus rings, sliders, checkboxes etc. match the theme.
        r["SystemAccentColor"] = theme.Accent;
        r["SystemAccentColorLight1"] = Blend(Colors.White, theme.Accent, 0.15);
        r["SystemAccentColorLight2"] = Blend(Colors.White, theme.Accent, 0.30);
        r["SystemAccentColorLight3"] = Blend(Colors.White, theme.Accent, 0.45);
        r["SystemAccentColorDark1"] = Blend(Colors.Black, theme.Accent, 0.15);
        r["SystemAccentColorDark2"] = Blend(Colors.Black, theme.Accent, 0.30);
        r["SystemAccentColorDark3"] = Blend(Colors.Black, theme.Accent, 0.45);

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Mixes <paramref name="top"/> over <paramref name="bottom"/> with the given opacity.</summary>
    public static Color Blend(Color top, Color bottom, double amount)
    {
        byte Mix(byte a, byte b) => (byte)Math.Round(a * amount + b * (1 - amount));
        return Color.FromRgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    public static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
}
