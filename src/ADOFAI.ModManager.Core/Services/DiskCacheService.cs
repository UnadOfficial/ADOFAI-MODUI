using ADOFAI.ModManager.Core.Helpers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ADOFAI.ModManager.Core.Services;

/// <summary>磁盘缓存（目录 JSON、图标、下载包）。</summary>
public interface IDiskCacheService
{
    string CacheDirectory { get; }

    /// <summary>读取缓存的 JSON；过期或不存在返回 null。</summary>
    Task<T?> GetJsonAsync<T>(string key, TimeSpan ttl, CancellationToken ct = default) where T : class;

    Task SetJsonAsync<T>(string key, T value, CancellationToken ct = default);

    /// <summary>下载 url 到缓存目录（category 子目录），返回本地文件路径。</summary>
    Task<string> FetchToCacheAsync(string category, string key, string url, CancellationToken ct = default);

    /// <summary>取缓存文件路径（不存在时为 null）。</summary>
    string? GetCachedFilePath(string category, string key);

    Task ClearAsync(CancellationToken ct = default);
}

public sealed class DiskCacheService : IDiskCacheService, IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DiskCacheService> _logger;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public DiskCacheService(IHttpClientFactory httpClientFactory, ILogger<DiskCacheService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        CacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ADOFAI.ModManager", "cache");
        Directory.CreateDirectory(CacheDirectory);
    }

    public string CacheDirectory { get; }

    public void Dispose()
    {
        _ioLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private string PathOf(string key) => Path.Combine(CacheDirectory, Sanitize(key) + ".json");

    private string CategoryDirectory(string category)
    {
        var dir = Path.Combine(CacheDirectory, Sanitize(category));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<T?> GetJsonAsync<T>(string key, TimeSpan ttl, CancellationToken ct = default) where T : class
    {
        try
        {
            var path = PathOf(key);
            if (!File.Exists(path))
            {
                return null;
            }

            var meta = new FileInfo(path);
            if (DateTimeOffset.UtcNow - meta.LastWriteTimeUtc > ttl)
            {
                return null;
            }

            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions.Web, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogWarning(ex, "缓存读取失败：{Key}", key);
            return null;
        }
    }

    public async Task SetJsonAsync<T>(string key, T value, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var path = PathOf(key);
            var tmp = path + ".tmp";
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions.Web, ct).ConfigureAwait(false);
            }

            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException)
        {
            _logger.LogWarning(ex, "缓存写入失败：{Key}", key);
        }
    }

    public async Task<string> FetchToCacheAsync(string category, string key, string url, CancellationToken ct = default)
    {
        var dir = CategoryDirectory(category);
        var ext = Path.GetExtension(new Uri(url).AbsolutePath);
        if (string.IsNullOrEmpty(ext))
        {
            ext = ".bin";
        }

        var target = Path.Combine(dir, Sanitize(key) + ext);
        if (File.Exists(target))
        {
            return target;
        }

        var client = _httpClientFactory.CreateClient("downloads");
        var tmp = target + ".tmp";
        await using (var stream = File.Create(tmp))
        using (var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            await using var content = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await content.CopyToAsync(stream, ct).ConfigureAwait(false);
        }

        File.Move(tmp, target, overwrite: true);
        return target;
    }

    public string? GetCachedFilePath(string category, string key)
    {
        var dir = Path.Combine(CacheDirectory, Sanitize(category));
        if (!Directory.Exists(dir))
        {
            return null;
        }

        var file = Directory.EnumerateFiles(dir, Sanitize(key) + ".*").FirstOrDefault();
        return file;
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(CacheDirectory))
            {
                Directory.Delete(CacheDirectory, recursive: true);
            }

            Directory.CreateDirectory(CacheDirectory);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "清理缓存失败");
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private static string Sanitize(string key)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = key.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
