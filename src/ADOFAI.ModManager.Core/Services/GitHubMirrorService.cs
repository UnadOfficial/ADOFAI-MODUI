using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ADOFAI.ModManager.Core.Services;

/// <summary>GitHub 镜像服务抽象：预设管理、链接转换与测速。</summary>
public interface IGitHubMirrorService
{
    /// <summary>预设镜像源列表（含直连与自定义）。</summary>
    IReadOnlyList<GitHubMirrorOption> Presets { get; }

    /// <summary>当前生效的镜像源（空字符串表示直连）。</summary>
    string CurrentMirror { get; }

    /// <summary>
    /// 将目标 URL 转换为镜像加速后的 URL。
    /// 仅对 GitHub 相关地址生效；非 GitHub 地址或已含镜像前缀时原样返回。
    /// </summary>
    string ApplyMirror(string url);

    /// <summary>
    /// 测试指定镜像地址的连通性与往返延迟。
    /// </summary>
    Task<MirrorLatencyResult> TestLatencyAsync(string? mirrorUrl, CancellationToken ct = default);
}

public sealed class GitHubMirrorService : IGitHubMirrorService
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(6);
    private const string HealthCheckTarget = "https://github.com/robots.txt";

    private readonly ISettingsService _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GitHubMirrorService> _logger;

    public static readonly IReadOnlyList<GitHubMirrorOption> DefaultPresets = new List<GitHubMirrorOption>
    {
        new("Direct", "直连（GitHub 官方）", string.Empty, "直接连接 GitHub 官方服务器，无中间代理"),
        new("ghproxy.net", "ghproxy.net", "https://ghproxy.net/", ""),
        new("ghfast.top", "ghfast.top", "https://ghfast.top/", ""),
        new("gh-proxy.org", "gh-proxy.org", "https://gh-proxy.org/", ""),
        new("gh.ddlc.top", "gh.ddlc.top", "https://gh.ddlc.top/", ""),
        new("gh.con.sh", "gh.con.sh", "https://gh.con.sh/", ""),
        new("ghps.cc", "ghps.cc", "https://ghps.cc/", ""),
        
        new("Custom", "自定义镜像源…", null, "支持 gh-proxy 前缀或包含 {url} 的链接模板"),
    };

    public GitHubMirrorService(
        ISettingsService settings,
        IHttpClientFactory httpClientFactory,
        ILogger<GitHubMirrorService> logger)
    {
        _settings = settings;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public IReadOnlyList<GitHubMirrorOption> Presets => DefaultPresets;

    public string CurrentMirror => _settings.State.Settings.GitHubMirror?.Trim() ?? string.Empty;

    public string ApplyMirror(string url)
    {
        return ApplyMirrorWithPrefix(url, CurrentMirror);
    }

    public static string ApplyMirrorWithPrefix(string url, string? mirror)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        if (string.IsNullOrWhiteSpace(mirror))
        {
            return url;
        }

        if (!IsGitHubUrl(url))
        {
            return url;
        }

        var trimmedMirror = mirror.Trim();
        var cleanPrefix = trimmedMirror.TrimEnd('/');

        // 避免重复叠加相同镜像前缀
        if (url.StartsWith(cleanPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        // 模板占位符支持：{url} 或 {0}
        if (trimmedMirror.Contains("{url}", StringComparison.OrdinalIgnoreCase))
        {
            return trimmedMirror.Replace("{url}", url, StringComparison.OrdinalIgnoreCase);
        }

        if (trimmedMirror.Contains("{0}"))
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, trimmedMirror, url);
        }

        return $"{cleanPrefix}/{url}";
    }

    public static bool IsGitHubUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host;
        return host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("codeload.github.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("gist.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<MirrorLatencyResult> TestLatencyAsync(string? mirrorUrl, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TestTimeout);

        var testUrl = string.IsNullOrWhiteSpace(mirrorUrl)
            ? HealthCheckTarget
            : ApplyMirrorWithPrefix(HealthCheckTarget, mirrorUrl);

        var sw = Stopwatch.StartNew();
        try
        {
            var client = _httpClientFactory.CreateClient("downloads");
            using var request = new HttpRequestMessage(HttpMethod.Get, testUrl);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1024);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);

            sw.Stop();

            if (response.IsSuccessStatusCode ||
                response.StatusCode == System.Net.HttpStatusCode.PartialContent ||
                response.StatusCode == System.Net.HttpStatusCode.Redirect ||
                response.StatusCode == System.Net.HttpStatusCode.MovedPermanently ||
                response.StatusCode == System.Net.HttpStatusCode.Found)
            {
                _logger.LogInformation("镜像源探测成功：{Url}，耗时 {Ms} ms", testUrl, sw.ElapsedMilliseconds);
                return new MirrorLatencyResult(true, sw.ElapsedMilliseconds, $"✓ 连通正常（{sw.ElapsedMilliseconds} ms）");
            }

            _logger.LogWarning("镜像源探测异常：{Url}，状态码 {Status}", testUrl, response.StatusCode);
            return new MirrorLatencyResult(false, sw.ElapsedMilliseconds, $"✕ 响应异常 (HTTP {(int)response.StatusCode})");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning("镜像源探测超时：{Url}", testUrl);
            return new MirrorLatencyResult(false, sw.ElapsedMilliseconds, "✕ 连接超时（超过 6 秒）");
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "镜像源探测 HTTP 异常：{Url}", testUrl);
            return new MirrorLatencyResult(false, sw.ElapsedMilliseconds, $"✕ 连接失败（{ex.Message}）");
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "镜像源探测未知异常：{Url}", testUrl);
            return new MirrorLatencyResult(false, sw.ElapsedMilliseconds, $"✕ 测试出错：{ex.Message}");
        }
    }
}
