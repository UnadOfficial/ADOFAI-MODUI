using ADOFAI.ModManager.Core.Helpers;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace ADOFAI.ModManager.Core.Services;

/// <summary>解析结果：IsDirectUrl=true 时 DirectUrl 可直接下载；否则 FallbackUrl 需要浏览器手动下载。</summary>
public sealed record ResolvedDownload(bool IsDirectUrl, string DirectUrl, string? FallbackUrl, string? Note)
{
    public static ResolvedDownload Direct(string url) => new(true, url, null, null);

    public static ResolvedDownload BrowserOnly(string url, string note) => new(false, url, url, note);
}

/// <summary>
/// 下载地址解析器：把 TUF 上形态不一的 downloadUrl 归一为直链。
/// 处理三类：
///   1) GitHub Release 标签页/latest → 经 api.github.com 取最大 .zip 资产；限流(403)时回退解析 expanded_assets HTML；
///   2) GitHub /releases/download/ 直链、tuforums CDN、其他直链 → 原样返回；
///   3) 解析失败 → 浏览器兜底（用户手动下载后用"导入本地 zip"安装）。
/// </summary>
public interface IDownloadUrlResolver
{
    Task<ResolvedDownload> ResolveAsync(string? downloadUrl, CancellationToken ct = default);
}

public sealed partial class DownloadUrlResolver : IDownloadUrlResolver
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDiskCacheService _cache;
    private readonly ILogger<DownloadUrlResolver> _logger;

    public DownloadUrlResolver(IHttpClientFactory httpClientFactory, IDiskCacheService cache, ILogger<DownloadUrlResolver> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ResolvedDownload> ResolveAsync(string? downloadUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return ResolvedDownload.BrowserOnly(string.Empty, "该 Mod 未提供下载地址。");
        }

        var url = downloadUrl.Trim();

        // 非 GitHub 直链：直接返回
        if (!url.Contains("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return ResolvedDownload.Direct(url);
        }

        // GitHub releases/download/.../*.zip 直链
        if (TryParseGitHubDownloadAsset(url, out var direct))
        {
            return ResolvedDownload.Direct(direct);
        }

        // GitHub 标签页 / latest 页 → 解析资产
        if (!TryParseGitHubTagPage(url, out var owner, out var repo, out var tag))
        {
            // 形如 /releases 或 /releases/latest 的整页链接 → 按 latest 解析
            if (TryParseOwnerRepo(url, out var owner2, out var repo2))
            {
                return await ResolveGitHubAsync(owner2, repo2, "latest", url, ct).ConfigureAwait(false);
            }

            return ResolvedDownload.BrowserOnly(url, "无法自动解析 GitHub 下载地址，请在浏览器中手动下载 zip 后使用“导入本地 zip”安装。");
        }

        return await ResolveGitHubAsync(owner!, repo!, tag!, url, ct).ConfigureAwait(false);
    }

    private async Task<ResolvedDownload> ResolveGitHubAsync(string owner, string repo, string tag, string pageUrl, CancellationToken ct)
    {
        // 磁盘缓存（按 页面URL 解析结果）
        var cacheKey = $"resolve/{owner}-{repo}-{tag}";
        var cached = await _cache.GetJsonAsync<CachedResolve>(cacheKey + ".json", TimeSpan.FromHours(12), ct).ConfigureAwait(false);
        if (cached?.DirectUrl is { Length: > 0 } cachedUrl)
        {
            return ResolvedDownload.Direct(cachedUrl);
        }

        // 1) api.github.com
        try
        {
            var client = _httpClientFactory.CreateClient("github-api");
            var tagPath = tag.Equals("latest", StringComparison.OrdinalIgnoreCase) ? "latest" : $"tags/{tag}";
            var release = await client.GetFromJsonAsync<GitHubRelease>(
                $"repos/{owner}/{repo}/releases/{tagPath}", JsonOptions.Web, ct).ConfigureAwait(false);

            var asset = release?.Assets
                ?.Where(a => a.Name is not null && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.Size)
                .FirstOrDefault();

            if (asset?.BrowserDownloadUrl is { Length: > 0 } direct)
            {
                await _cache.SetJsonAsync(cacheKey + ".json", new CachedResolve { DirectUrl = direct }, ct).ConfigureAwait(false);
                _logger.LogInformation("GitHub 资产解析成功：{Url} → {Direct}", pageUrl, direct);
                return ResolvedDownload.Direct(direct);
            }

            if (release?.Assets is { Count: > 0 })
            {
                // 有 release 但没有 zip 资产
                return ResolvedDownload.BrowserOnly(release.HtmlUrl ?? pageUrl, "该 Release 未附带 .zip 资产，请在浏览器中查看。");
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "GitHub API 解析失败（可能限流），回退 HTML 解析：{Page}", pageUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "GitHub API 解析异常，回退 HTML 解析：{Page}", pageUrl);
        }

        // 2) expanded_assets HTML 回退（GitHub 前端懒加载资产列表的接口）
        try
        {
            var client = _httpClientFactory.CreateClient("downloads");
            var assetsUrl = $"https://github.com/{owner}/{repo}/releases/expanded_assets/{tag}";
            using var resp = await client.GetAsync(assetsUrl, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                var html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                // 匹配 href 属性内的资产路径（用前后查找去掉 href=" 与尾引号）
                var match = AssetHrefRegex().Matches(html)
                    .Select(m => m.Value)
                    .Where(href => href.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();

                if (match is not null)
                {
                    var direct = "https://github.com" + match;
                    await _cache.SetJsonAsync(cacheKey + ".json", new CachedResolve { DirectUrl = direct }, ct).ConfigureAwait(false);
                    return ResolvedDownload.Direct(direct);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "expanded_assets 回退解析失败：{Page}", pageUrl);
        }

        return ResolvedDownload.BrowserOnly(pageUrl, "无法自动解析 GitHub 下载地址，请在浏览器中手动下载 zip 后使用“导入本地 zip”安装。");
    }

    private static bool TryParseGitHubTagPage(string url, out string owner, out string repo, out string tag)
    {
        owner = repo = tag = string.Empty;
        var match = TagPageRegex().Match(url);
        if (!match.Success)
        {
            return false;
        }

        owner = match.Groups["owner"].Value;
        repo = match.Groups["repo"].Value;
        tag = Uri.UnescapeDataString(match.Groups["tag"].Value);
        return true;
    }

    private static bool TryParseGitHubDownloadAsset(string url, out string direct)
    {
        direct = string.Empty;
        var match = DownloadAssetRegex().Match(url);
        if (!match.Success)
        {
            return false;
        }

        direct = $"https://github.com/{match.Groups["owner"].Value}/{match.Groups["repo"].Value}/releases/download/{match.Groups["rest"].Value}";
        return true;
    }

    private static bool TryParseOwnerRepo(string url, out string owner, out string repo)
    {
        owner = repo = string.Empty;
        var match = OwnerRepoRegex().Match(url);
        if (!match.Success)
        {
            return false;
        }

        owner = match.Groups["owner"].Value;
        repo = match.Groups["repo"].Value;
        return true;
    }

    [GeneratedRegex(@"github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/releases/tag/(?<tag>[^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex TagPageRegex();

    [GeneratedRegex(@"github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/releases/download/(?<rest>[^?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex DownloadAssetRegex();

    [GeneratedRegex(@"github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/", RegexOptions.IgnoreCase)]
    private static partial Regex OwnerRepoRegex();

    // expanded_assets HTML 中形如 href="/owner/repo/releases/download/1.0.0/file.zip"
    // 使用查找断言只捕获引号内的路径部分
    [GeneratedRegex(@"(?<=href="")[^""]*/releases/download/[^""]*?\.zip(?="")", RegexOptions.IgnoreCase)]
    private static partial Regex AssetHrefRegex();

    private sealed class CachedResolve
    {
        public string? DirectUrl { get; set; }
    }
}
