using System.Diagnostics;

namespace Markdownia.Services;

/// <summary>
/// Linux desktop integration for installed builds. The .deb registers Markdownia for Markdown files, which offers it
/// under "Open With"; making it the default handler is the user's choice, so it is done on request with xdg-mime.
/// </summary>
internal static class DesktopIntegration
{
    public const string DesktopFileId = "markdownia.desktop";

    private static readonly string[] MarkdownMimeTypes = ["text/markdown", "text/x-markdown"];

    /// <summary>True on Linux when Markdownia's desktop entry is installed and xdg-mime is available.</summary>
    public static bool CanSetDefault =>
        OperatingSystem.IsLinux() && FindDesktopFile() is not null && FindOnPath("xdg-mime") is not null;

    public static async Task<bool> IsDefaultForMarkdownAsync()
    {
        var (exitCode, output) = await RunAsync("xdg-mime", "query", "default", MarkdownMimeTypes[0]);
        return exitCode == 0 && output.Trim() == DesktopFileId;
    }

    /// <summary>Makes Markdownia the default application for Markdown files. Returns an error message on failure.</summary>
    public static async Task<string?> SetDefaultForMarkdownAsync()
    {
        var (exitCode, output) = await RunAsync("xdg-mime", ["default", DesktopFileId, .. MarkdownMimeTypes]);
        return exitCode == 0 ? null : (output.Trim() is { Length: > 0 } message ? message : $"xdg-mime exited with code {exitCode}.");
    }

    private static string? FindDesktopFile()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } dh ? dh : Path.Combine(home, ".local", "share");
        var dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS") is { Length: > 0 } dd ? dd : "/usr/local/share:/usr/share";
        return dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Prepend(dataHome)
            .Select(dir => Path.Combine(dir, "applications", DesktopFileId))
            .FirstOrDefault(File.Exists);
    }

    private static string? FindOnPath(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, program))
            .FirstOrDefault(File.Exists);

    private static async Task<(int ExitCode, string Output)> RunAsync(string program, params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in arguments)
                info.ArgumentList.Add(argument);
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, await stdout + await stderr);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (-1, ex.Message);
        }
    }
}
