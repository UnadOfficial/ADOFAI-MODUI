using ADOFAI.ModManager.Core.Helpers;
using ADOFAI.ModManager.Core.Models.Tuforums;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace ADOFAI.ModManager.Core.Services;

/// <summary>远端目录快照（内存 + 磁盘缓存的结果）。</summary>
public sealed record CatalogSnapshot(IReadOnlyList<TufMod> Mods, DateTimeOffset FetchedAt, bool FromCache);

/// <summary>
/// Tuforums Mods API 客户端。
/// 实测接口：
///   GET /v2/mods?limit=N&amp;offset=N → { total, mods[], offset, limit, hasMore }
///   GET /v2/mods/{slug}            → { mod: { ..., latestVersion, versions[] } }
/// 列表接口无服务端搜索/排序参数，搜索过滤在客户端完成。
/// </summary>
public interface ITuforumsClient
{
    /// <summary>分页聚合拉取全量 Mod 列表（带磁盘缓存）。</summary>
    Task<CatalogSnapshot> GetCatalogAsync(bool forceRefresh = false, CancellationToken ct = default);

    /// <summary>按 slug 获取详情（含 versions 历史与 latestVersion）。</summary>
    Task<TufMod?> GetModAsync(string slug, CancellationToken ct = default);
}

public sealed class TuforumsClient : ITuforumsClient
{
    public const string BaseUrl = "https://api.tuforums.com/";
    private const int PageSize = 100;
    private const int MaxPages = 20; // 安全上限，防止接口异常时无限循环

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDiskCacheService _cache;
    private readonly ISettingsService _settings;
    private readonly ILogger<TuforumsClient> _logger;
    private CatalogSnapshot? _memory;

    public TuforumsClient(
        IHttpClientFactory httpClientFactory,
        IDiskCacheService cache,
        ISettingsService settings,
        ILogger<TuforumsClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _settings = settings;
        _logger = logger;
    }

    public async Task<CatalogSnapshot> GetCatalogAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        var ttl = TimeSpan.FromMinutes(Math.Clamp(_settings.State.Settings.CatalogCacheTtlMinutes, 1, 24 * 60));

        if (!forceRefresh)
        {
            if (_memory is { } mem && DateTimeOffset.UtcNow - mem.FetchedAt < ttl)
            {
                return mem;
            }

            var cached = await _cache.GetJsonAsync<CachedCatalog>("mods.json", ttl, ct).ConfigureAwait(false);
            if (cached is not null)
            {
                var snapshot = new CatalogSnapshot(cached.Mods ?? new List<TufMod>(), cached.FetchedAt, FromCache: true);
                _memory = snapshot;
                return snapshot;
            }
        }

        var mods = await FetchAllPagesAsync(ct).ConfigureAwait(false);
        var fresh = new CatalogSnapshot(mods, DateTimeOffset.UtcNow, FromCache: false);
        _memory = fresh;
        await _cache.SetJsonAsync("mods.json", new CachedCatalog
        {
            FetchedAt = fresh.FetchedAt,
            Mods = mods.ToList(),
        }, ct).ConfigureAwait(false);
        return fresh;
    }

    public async Task<TufMod?> GetModAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var client = _httpClientFactory.CreateClient("tuforums");
        var url = $"v2/mods/{Uri.EscapeDataString(slug.Trim())}";

        var response = await Retry.ExecuteAsync(
            async token =>
            {
                using var resp = await client.GetAsync(url, token).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<TufModDetailResponse>(JsonOptions.Web, token)
                    .ConfigureAwait(false);
            },
            ct: ct).ConfigureAwait(false);

        var mod = response?.Mod;
        if (mod is null)
        {
            return null;
        }

        mod.Normalize();
        return mod;
    }

    private async Task<List<TufMod>> FetchAllPagesAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("tuforums");
        var all = new List<TufMod>();
        var offset = 0;

        for (var page = 0; page < MaxPages; page++)
        {
            var url = $"v2/mods?limit={PageSize}&offset={offset}";
            var result = await Retry.ExecuteAsync(
                async token =>
                {
                    using var resp = await client.GetAsync(url, token).ConfigureAwait(false);
                    resp.EnsureSuccessStatusCode();
                    return await resp.Content.ReadFromJsonAsync<TufModsPage>(JsonOptions.Web, token)
                        .ConfigureAwait(false);
                },
                ct: ct,
                onRetry: (attempt, ex) => _logger.LogWarning(ex, "拉取 TUF 目录失败（第 {Attempt} 次），准备重试", attempt))
                .ConfigureAwait(false);

            var mods = result?.Mods;
            if (mods is not { Count: > 0 })
            {
                break;
            }

            foreach (var mod in mods)
            {
                mod.Normalize();
            }

            all.AddRange(mods);

            // 依据 hasMore 与 total 双重判断，避免接口字段异常时死循环
            var total = result?.Total ?? 0;
            offset += mods.Count;
            if (result?.HasMore != true || (total > 0 && all.Count >= total))
            {
                break;
            }
        }

        _logger.LogInformation("TUF 目录拉取完成：{Count} 个 Mod", all.Count);
        return all;
    }

    private sealed class CachedCatalog
    {
        public DateTimeOffset FetchedAt { get; set; }

        public List<TufMod>? Mods { get; set; }
    }

    private sealed class TufModDetailResponse
    {
        public TufMod? Mod { get; set; }
    }
}
