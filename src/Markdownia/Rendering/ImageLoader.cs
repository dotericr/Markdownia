using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Markdownia.Rendering;

/// <summary>Loads and caches bitmaps for preview images (local files, http(s) URLs and data URIs).</summary>
public static class ImageLoader
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Task<Bitmap?>> Pending = new(StringComparer.Ordinal);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Markdownia/0.1");
        return client;
    }

    /// <summary>Resolves an image reference relative to the document directory. Returns null for unsupported sources.</summary>
    public static string? Resolve(string source, string? baseDirectory)
    {
        source = source.Trim();
        if (source.Length == 0)
            return null;
        if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return source;
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https")
                return uri.AbsoluteUri;
            if (uri.IsFile)
                return uri.LocalPath;
            if (!OperatingSystem.IsWindows() || uri.Scheme.Length > 1)
                return null;
        }
        var path = Uri.UnescapeDataString(source.Split('?', '#')[0]);
        if (!Path.IsPathRooted(path))
        {
            if (baseDirectory is null)
                return null;
            path = Path.GetFullPath(Path.Combine(baseDirectory, path));
        }
        return path;
    }

    public static bool IsSupported(string resolved) =>
        !resolved.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) &&
        !resolved.StartsWith("data:image/svg", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets a cached bitmap synchronously, if one is already available.</summary>
    public static bool TryGetCached(string resolved, out Bitmap? bitmap)
    {
        var key = CacheKey(resolved);
        return Cache.TryGetValue(key, out bitmap);
    }

    public static Task<Bitmap?> LoadAsync(string resolved)
    {
        Dispatcher.UIThread.VerifyAccess();
        var key = CacheKey(resolved);
        if (Cache.TryGetValue(key, out var cached))
            return Task.FromResult(cached);
        if (Pending.TryGetValue(key, out var pending))
            return pending;

        var task = LoadCoreAsync(resolved).ContinueWith(t =>
        {
            var bitmap = t.IsCompletedSuccessfully ? t.Result : null;
            Cache[key] = bitmap;
            Pending.Remove(key);
            return bitmap;
        }, TaskScheduler.FromCurrentSynchronizationContext());
        Pending[key] = task;
        return task;
    }

    private static string CacheKey(string resolved)
    {
        if (resolved.StartsWith("http", StringComparison.OrdinalIgnoreCase) || resolved.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return resolved.Length > 512 ? resolved.GetHashCode().ToString() + resolved.Length : resolved;
        try
        {
            return resolved + "|" + File.GetLastWriteTimeUtc(resolved).Ticks;
        }
        catch (Exception)
        {
            return resolved;
        }
    }

    private static async Task<Bitmap?> LoadCoreAsync(string resolved)
    {
        try
        {
            byte[] bytes;
            if (resolved.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = resolved.IndexOf(',');
                if (comma < 0 || !resolved[..comma].Contains(";base64", StringComparison.OrdinalIgnoreCase))
                    return null;
                bytes = Convert.FromBase64String(resolved[(comma + 1)..]);
            }
            else if (resolved.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                bytes = await Http.GetByteArrayAsync(resolved).ConfigureAwait(false);
            }
            else
            {
                if (!File.Exists(resolved))
                    return null;
                bytes = await File.ReadAllBytesAsync(resolved).ConfigureAwait(false);
            }
            return new Bitmap(new MemoryStream(bytes));
        }
        catch (Exception)
        {
            return null;
        }
    }
}
