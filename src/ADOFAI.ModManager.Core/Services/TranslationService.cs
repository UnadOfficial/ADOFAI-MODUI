using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ADOFAI.ModManager.Core.Helpers;
using Microsoft.Extensions.Logging;

namespace ADOFAI.ModManager.Core.Services;

/// <summary>翻译结果。</summary>
public sealed record TranslationResult(
    string TranslatedText,
    string? DetectedSourceLanguage,
    string ProviderName,
    bool Success,
    string? ErrorMessage = null);

/// <summary>在线翻译服务抽象。</summary>
public interface ITranslationService
{
    /// <summary>将指定文本翻译为目标语言（默认 zh-CN）。</summary>
    Task<TranslationResult> TranslateAsync(
        string text,
        string targetLanguage = "zh-CN",
        string sourceLanguage = "auto",
        CancellationToken ct = default);
}

public sealed partial class TranslationService : ITranslationService, IDisposable
{
    private static readonly TimeSpan DiskCacheTtl = TimeSpan.FromDays(30);
    private static readonly TimeSpan BingSessionTtl = TimeSpan.FromMinutes(50);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISettingsService _settings;
    private readonly IDiskCacheService _cache;
    private readonly ILogger<TranslationService> _logger;

    private readonly ConcurrentDictionary<string, string> _memoryCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _bingSessionLock = new(1, 1);
    private BingSession? _bingSession;

    public TranslationService(
        IHttpClientFactory httpClientFactory,
        ISettingsService settings,
        IDiskCacheService cache,
        ILogger<TranslationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string targetLanguage = "zh-CN",
        string sourceLanguage = "auto",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new TranslationResult(string.Empty, null, "None", true);
        }

        var normalizedTarget = NormalizeTargetLanguage(targetLanguage);
        var cacheKey = ComputeCacheKey(text, normalizedTarget);

        // 1. 内存缓存
        if (_memoryCache.TryGetValue(cacheKey, out var memCached))
        {
            return new TranslationResult(memCached, null, "Cache", true);
        }

        // 2. 磁盘缓存
        try
        {
            var diskCached = await _cache.GetJsonAsync<CachedTranslationItem>(cacheKey, DiskCacheTtl, ct)
                .ConfigureAwait(false);
            if (diskCached is { Text.Length: > 0 })
            {
                _memoryCache[cacheKey] = diskCached.Text;
                return new TranslationResult(diskCached.Text, diskCached.SourceLanguage, "DiskCache", true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取翻译磁盘缓存失败，忽略并继续发起网络请求");
        }

        // 3. 网络请求分发
        var preferredProvider = _settings.State.Settings.TranslationProvider?.Trim() ?? "Auto";
        TranslationResult result;

        if (string.Equals(preferredProvider, "Bing", StringComparison.OrdinalIgnoreCase))
        {
            result = await TranslateViaBingAsync(text, normalizedTarget, sourceLanguage, ct).ConfigureAwait(false);
        }
        else if (string.Equals(preferredProvider, "Google", StringComparison.OrdinalIgnoreCase))
        {
            result = await TranslateViaGoogleAsync(text, normalizedTarget, sourceLanguage, ct).ConfigureAwait(false);
        }
        else
        {
            // 自动模式：优先必应，失败时回退谷歌
            try
            {
                result = await TranslateViaBingAsync(text, normalizedTarget, sourceLanguage, ct).ConfigureAwait(false);
                if (!result.Success)
                {
                    _logger.LogWarning("必应翻译返回失败，准备回退至谷歌翻译：{Error}", result.ErrorMessage);
                    result = await TranslateViaGoogleAsync(text, normalizedTarget, sourceLanguage, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "必应翻译异常，自动回退至谷歌翻译");
                result = await TranslateViaGoogleAsync(text, normalizedTarget, sourceLanguage, ct).ConfigureAwait(false);
            }
        }

        // 4. 写入缓存
        if (result.Success && !string.IsNullOrWhiteSpace(result.TranslatedText))
        {
            _memoryCache[cacheKey] = result.TranslatedText;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _cache.SetJsonAsync(cacheKey, new CachedTranslationItem
                    {
                        Text = result.TranslatedText,
                        SourceLanguage = result.DetectedSourceLanguage,
                    }, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "写入翻译磁盘缓存失败");
                }
            }, CancellationToken.None);
        }

        return result;
    }

    // ───────── 微软必应翻译实现 ─────────

    private async Task<TranslationResult> TranslateViaBingAsync(
        string text,
        string targetLanguage,
        string sourceLanguage,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("translation");
        var session = await GetOrCreateBingSessionAsync(client, forceRefresh: false, ct).ConfigureAwait(false);

        var bingTarget = targetLanguage is "zh-CN" or "zh-Hans" ? "zh-Hans" : targetLanguage;
        var bingSource = sourceLanguage is "auto" or "" ? "auto-detect" : sourceLanguage;

        var url = $"https://www.bing.com/ttranslatev3?isVertical=1&IG={Uri.EscapeDataString(session.Ig)}&IID={Uri.EscapeDataString(session.Iid)}";
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["fromLang"] = bingSource,
            ["text"] = text,
            ["to"] = bingTarget,
            ["key"] = session.Key,
            ["token"] = session.Token,
        });

        using var response = await client.PostAsync(url, content, ct).ConfigureAwait(false);

        // 如果凭据失效或 400，重新拉取凭据重试一次
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogInformation("必应翻译凭据可能过期（HTTP {Code}），重新拉取凭据重试", response.StatusCode);
            session = await GetOrCreateBingSessionAsync(client, forceRefresh: true, ct).ConfigureAwait(false);
            url = $"https://www.bing.com/ttranslatev3?isVertical=1&IG={Uri.EscapeDataString(session.Ig)}&IID={Uri.EscapeDataString(session.Iid)}";
            var retryContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["fromLang"] = bingSource,
                ["text"] = text,
                ["to"] = bingTarget,
                ["key"] = session.Key,
                ["token"] = session.Token,
            });

            using var retryResp = await client.PostAsync(url, retryContent, ct).ConfigureAwait(false);
            retryResp.EnsureSuccessStatusCode();
            return await ParseBingResponseAsync(retryResp, ct).ConfigureAwait(false);
        }

        return await ParseBingResponseAsync(response, ct).ConfigureAwait(false);
    }

    private static async Task<TranslationResult> ParseBingResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
        {
            var first = root[0];
            string? detectedLang = null;
            if (first.TryGetProperty("detectedLanguage", out var detected) &&
                detected.TryGetProperty("language", out var langProp))
            {
                detectedLang = langProp.GetString();
            }

            if (first.TryGetProperty("translations", out var transArr) &&
                transArr.ValueKind == JsonValueKind.Array &&
                transArr.GetArrayLength() > 0)
            {
                var transText = transArr[0].GetProperty("text").GetString();
                if (!string.IsNullOrEmpty(transText))
                {
                    return new TranslationResult(transText, detectedLang, "Bing", true);
                }
            }
        }

        return new TranslationResult(string.Empty, null, "Bing", false, "未能从必应翻译解析出译文");
    }

    private async Task<BingSession> GetOrCreateBingSessionAsync(HttpClient client, bool forceRefresh, CancellationToken ct)
    {
        await _bingSessionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _bingSession is { } existing && DateTimeOffset.UtcNow < existing.ExpiresAt)
            {
                return existing;
            }

            using var getReq = new HttpRequestMessage(HttpMethod.Get, "https://www.bing.com/translator");
            getReq.Headers.Referrer = new Uri("https://www.bing.com/");
            using var resp = await client.SendAsync(getReq, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            var igMatch = IgRegex().Match(html);
            var helperMatch = AbuseHelperRegex().Match(html);
            var iidMatch = IidRegex().Match(html);

            if (!igMatch.Success || !helperMatch.Success)
            {
                throw new InvalidOperationException("无法获取必应翻译鉴权凭据");
            }

            var ig = igMatch.Groups[1].Value;
            var key = helperMatch.Groups[1].Value;
            var token = helperMatch.Groups[2].Value;
            var iid = iidMatch.Success ? iidMatch.Groups[1].Value : "translator.5028";

            var session = new BingSession(ig, key, token, iid, DateTimeOffset.UtcNow.Add(BingSessionTtl));
            _bingSession = session;
            return session;
        }
        finally
        {
            _bingSessionLock.Release();
        }
    }

    // ───────── 谷歌翻译实现 ─────────

    private async Task<TranslationResult> TranslateViaGoogleAsync(
        string text,
        string targetLanguage,
        string sourceLanguage,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("translation");
        var googleTarget = targetLanguage is "zh-Hans" or "zh-CN" ? "zh-CN" : targetLanguage;
        var googleSource = sourceLanguage is "auto-detect" or "" ? "auto" : sourceLanguage;

        var url = "https://translate.googleapis.com/translate_a/single";
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client"] = "dict-chrome-ex",
            ["sl"] = googleSource,
            ["tl"] = googleTarget,
            ["dt"] = "t",
            ["q"] = text,
        });

        using var response = await client.PostAsync(url, content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
        {
            var sentences = root[0];
            if (sentences.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var seg in sentences.EnumerateArray())
                {
                    if (seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() > 0)
                    {
                        var part = seg[0].GetString();
                        if (!string.IsNullOrEmpty(part))
                        {
                            sb.Append(part);
                        }
                    }
                }

                var detectedLang = root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String
                    ? root[2].GetString()
                    : null;

                var resultText = sb.ToString();
                if (!string.IsNullOrEmpty(resultText))
                {
                    return new TranslationResult(resultText, detectedLang, "Google", true);
                }
            }
        }

        return new TranslationResult(string.Empty, null, "Google", false, "未能从谷歌翻译解析出译文");
    }

    private static string NormalizeTargetLanguage(string lang) =>
        lang.ToLowerInvariant() switch
        {
            "zh" or "zh-cn" or "zh-hans" => "zh-CN",
            "zh-tw" or "zh-hant" or "zh-hk" => "zh-TW",
            _ => lang,
        };

    private static string ComputeCacheKey(string text, string targetLanguage)
    {
        var raw = $"{targetLanguage}:{text}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return "trans_" + Convert.ToHexString(hash)[..24].ToLowerInvariant();
    }

    [GeneratedRegex(@"IG\s*[:=]\s*""([^""]+)""", RegexOptions.Compiled)]
    private static partial Regex IgRegex();

    [GeneratedRegex(@"params_AbusePreventionHelper\s*=\s*\[([^,]+),\s*""([^""]+)""", RegexOptions.Compiled)]
    private static partial Regex AbuseHelperRegex();

    [GeneratedRegex(@"data-iid\s*=\s*""([^""]+)""", RegexOptions.Compiled)]
    private static partial Regex IidRegex();

    private sealed record BingSession(string Ig, string Key, string Token, string Iid, DateTimeOffset ExpiresAt);

    public sealed class CachedTranslationItem
    {
        public string Text { get; set; } = string.Empty;

        public string? SourceLanguage { get; set; }
    }

    public void Dispose()
    {
        _bingSessionLock.Dispose();
    }
}
