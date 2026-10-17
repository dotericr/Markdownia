using Avalonia.Media;

namespace Markdownia.Services;

/// <summary>
/// Fonts bundled with the application (all SIL Open Font License), plus access to installed system fonts.
/// Bundled fonts guarantee consistent, high-quality typography regardless of the host OS.
/// </summary>
public static class FontCatalog
{
    private const string FontRoot = "avares://Markdownia/Assets/Fonts";

    public sealed record FontChoice(string Name, string Description, FontFamily Family);

    public static readonly IReadOnlyList<FontChoice> ReadingFonts =
    [
        new("Literata", "Serif designed for long-form screen reading", new FontFamily($"{FontRoot}/Literata#Literata")),
        new("Source Serif 4", "Crisp transitional serif", new FontFamily($"{FontRoot}/SourceSerif4#Source Serif 4")),
        new("Inter", "Clean, neutral sans-serif", new FontFamily("fonts:Inter#Inter")),
        new("Atkinson Hyperlegible", "Sans-serif built for maximum legibility", new FontFamily($"{FontRoot}/AtkinsonHyperlegible#Atkinson Hyperlegible")),
    ];

    public static readonly IReadOnlyList<FontChoice> CodeFonts =
    [
        new("JetBrains Mono", "Monospace with tall x-height", new FontFamily($"{FontRoot}/JetBrainsMono#JetBrains Mono")),
    ];

    public static FontFamily UiFont { get; } = new("fonts:Inter#Inter");

    /// <summary>Names offered in font pickers: bundled fonts first, then installed families.</summary>
    public static IReadOnlyList<string> ReadingFontNames()
    {
        var names = ReadingFonts.Select(f => f.Name).ToList();
        names.AddRange(SystemFontNames().Where(n => !names.Contains(n, StringComparer.OrdinalIgnoreCase)));
        return names;
    }

    public static IReadOnlyList<string> CodeFontNames()
    {
        var names = CodeFonts.Select(f => f.Name).ToList();
        names.AddRange(SystemFontNames().Where(n =>
            !names.Contains(n, StringComparer.OrdinalIgnoreCase) &&
            (n.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
             n.Contains("Code", StringComparison.OrdinalIgnoreCase) ||
             n.Contains("Consol", StringComparison.OrdinalIgnoreCase) ||
             n.Contains("Courier", StringComparison.OrdinalIgnoreCase))));
        return names;
    }

    private static IReadOnlyList<string>? _systemFonts;

    private static IReadOnlyList<string> SystemFontNames()
    {
        if (_systemFonts is null)
        {
            try
            {
                _systemFonts = FontManager.Current.SystemFonts
                    .Select(f => f.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception)
            {
                _systemFonts = [];
            }
        }
        return _systemFonts;
    }

    public static FontFamily ResolveReading(string name) =>
        ReadingFonts.FirstOrDefault(f => f.Name == name)?.Family ?? Resolve(name, ReadingFonts[0].Family);

    public static FontFamily ResolveCode(string name) =>
        CodeFonts.FirstOrDefault(f => f.Name == name)?.Family ?? Resolve(name, CodeFonts[0].Family);

    private static FontFamily Resolve(string name, FontFamily fallback)
    {
        if (string.IsNullOrWhiteSpace(name))
            return fallback;
        return SystemFontNames().Contains(name, StringComparer.OrdinalIgnoreCase) ? new FontFamily(name) : fallback;
    }
}
