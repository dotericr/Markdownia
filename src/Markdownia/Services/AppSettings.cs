using System.Text.Json;
using System.Text.Json.Serialization;

namespace Markdownia.Services;

/// <summary>User preferences, persisted as JSON in the per-user config directory.</summary>
public sealed class AppSettings
{
    public string ThemeId { get; set; } = "paper";

    // Preview typography
    public string PreviewFont { get; set; } = "Literata";
    public string CodeFont { get; set; } = "JetBrains Mono";
    public double PreviewFontSize { get; set; } = 18;
    public double LineSpacing { get; set; } = 1.6;
    public string ContentWidth { get; set; } = "Medium";
    public bool Justify { get; set; }
    public bool SmartTypography { get; set; } = true;
    public string TextRendering { get; set; } = "Smooth";

    // Editor
    public string EditorFont { get; set; } = "JetBrains Mono";
    public double EditorFontSize { get; set; } = 14;
    public bool WordWrap { get; set; } = true;
    public bool ShowLineNumbers { get; set; } = true;
    public bool SyncScroll { get; set; } = true;

    // Layout
    public bool ShowEditor { get; set; } = true;
    public bool ShowPreview { get; set; } = true;
    public double EditorPaneRatio { get; set; } = 0.5;
    public bool RibbonCollapsed { get; set; }
    public double WindowWidth { get; set; } = 1400;
    public double WindowHeight { get; set; } = 900;
    public bool WindowMaximized { get; set; }

    public List<string> RecentFiles { get; set; } = [];
}

[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

public static class SettingsStore
{
    private static string Directory
    {
        get
        {
            var overrideDir = Environment.GetEnvironmentVariable("MARKDOWNIA_CONFIG_DIR");
            if (!string.IsNullOrEmpty(overrideDir))
                return overrideDir;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Markdownia");
        }
    }

    private static string FilePath => Path.Combine(Directory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                using var stream = File.OpenRead(FilePath);
                return JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings: fall back to defaults.
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var tmp = FilePath + ".tmp";
            using (var stream = File.Create(tmp))
                JsonSerializer.Serialize(stream, settings, SettingsJsonContext.Default.AppSettings);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; never crash because they could not be written.
        }
    }
}
