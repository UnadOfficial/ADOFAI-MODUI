using System.Net;
using ADOFAI.ModManager.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ADOFAI.ModManager.Core.Tests;

public class DownloadUrlResolverTests
{
    private static (DownloadUrlResolver Resolver, FakeHandler Handler) Create(params (string Url, string Body, string ContentType, HttpStatusCode Status)[] responses)
    {
        var handler = new FakeHandler(responses);
        var httpFactory = new FakeHttpClientFactory(handler);
        var cache = new FakeDiskCache();
        var resolver = new DownloadUrlResolver(httpFactory, cache, NullLogger<DownloadUrlResolver>.Instance);
        return (resolver, handler);
    }

    [Fact]
    public async Task Direct_GitHubAsset_PassesThrough()
    {
        var (resolver, handler) = Create();
        var result = await resolver.ResolveAsync("https://github.com/adofaiex/JipperKeyViewer/releases/download/1.7.2/JipperKeyViewer.zip");

        Assert.True(result.IsDirectUrl);
        Assert.Equal("https://github.com/adofaiex/JipperKeyViewer/releases/download/1.7.2/JipperKeyViewer.zip", result.DirectUrl);
        Assert.Equal(0, handler.RequestCount); // 不发起网络请求
    }

    [Fact]
    public async Task NonGitHub_PassesThrough()
    {
        var (resolver, handler) = Create();
        var result = await resolver.ResolveAsync("https://api.tuforums.com/cdn/mods/thing.zip");

        Assert.True(result.IsDirectUrl);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task GitHubTagPage_ResolvesViaApi()
    {
        const string apiResponse = """
            {
              "tag_name": "v3.1.1",
              "html_url": "https://github.com/example/TUFHelper/releases/tag/v3.1.1",
              "assets": [
                { "name": "source.zip", "size": 100, "browser_download_url": "https://github.com/example/TUFHelper/releases/download/v3.1.1/source.zip" },
                { "name": "TUFHelper.zip", "size": 900, "browser_download_url": "https://github.com/example/TUFHelper/releases/download/v3.1.1/TUFHelper.zip" }
              ]
            }
            """;
        var (resolver, _) = Create(
            ("https://api.github.com/repos/example/TUFHelper/releases/tags/v3.1.1", apiResponse, "application/json", HttpStatusCode.OK));

        var result = await resolver.ResolveAsync("https://github.com/example/TUFHelper/releases/tag/v3.1.1");

        Assert.True(result.IsDirectUrl);
        Assert.EndsWith("TUFHelper.zip", result.DirectUrl, StringComparison.Ordinal); // 选择最大的 zip 资产
    }

    [Fact]
    public async Task GitHubRateLimited_FallsBackToExpandedAssetsHtml()
    {
        const string html = """<a href="/example/TUFHelper/releases/download/v3.1.1/TUFHelper.zip">TUFHelper.zip</a>""";
        var (resolver, handler) = Create(
            ("https://api.github.com/repos/example/TUFHelper/releases/tags/v3.1.1", "rate limited", "text/plain", HttpStatusCode.Forbidden),
            ("https://github.com/example/TUFHelper/releases/expanded_assets/v3.1.1", html, "text/html", HttpStatusCode.OK));

        var result = await resolver.ResolveAsync("https://github.com/example/TUFHelper/releases/tag/v3.1.1");

        Assert.True(result.IsDirectUrl);
        Assert.Equal("https://github.com/example/TUFHelper/releases/download/v3.1.1/TUFHelper.zip", result.DirectUrl);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task Unresolvable_FallsBackToBrowser()
    {
        var (resolver, _) = Create();
        var result = await resolver.ResolveAsync("https://github.com/example/some-repo");

        Assert.False(result.IsDirectUrl);
        Assert.NotNull(result.FallbackUrl);
    }

    [Fact]
    public async Task NullUrl_FallsBackToBrowser()
    {
        var (resolver, _) = Create();
        var result = await resolver.ResolveAsync(null);

        Assert.False(result.IsDirectUrl);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (string Body, string ContentType, HttpStatusCode Status)> _responses;

        public FakeHandler((string Url, string Body, string ContentType, HttpStatusCode Status)[] responses)
        {
            _responses = responses.ToDictionary(
                r => r.Url, r => (r.Body, r.ContentType, r.Status), StringComparer.Ordinal);
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var url = request.RequestUri!.ToString();
            if (!_responses.TryGetValue(url, out var hit))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(hit.Status)
            {
                Content = new StringContent(hit.Body, System.Text.Encoding.UTF8, hit.ContentType),
            });
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly FakeHandler _handler;

        public FakeHttpClientFactory(FakeHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name)
        {
            // 与生产 DI 注册保持一致：具名客户端带 BaseAddress（解析器用相对路径请求 GitHub API）
            var client = new HttpClient(_handler, disposeHandler: false);
            client.BaseAddress = name switch
            {
                "github-api" => new Uri("https://api.github.com/"),
                "tuforums" => new Uri("https://api.tuforums.com/"),
                _ => null,
            };
            return client;
        }
    }

    private sealed class FakeDiskCache : IDiskCacheService
    {
        public string CacheDirectory => Path.Combine(Path.GetTempPath(), "adofai-modui-tests", "fake-cache");

        public Dictionary<string, object> Store { get; } = new();

        public Task ClearAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string> FetchToCacheAsync(string category, string key, string url, CancellationToken ct = default)
            => Task.FromResult(Path.Combine(CacheDirectory, key));

        public string? GetCachedFilePath(string category, string key) => null;

        public Task<T?> GetJsonAsync<T>(string key, TimeSpan ttl, CancellationToken ct = default) where T : class
            => Task.FromResult(Store.TryGetValue(key, out var value) ? value as T : null);

        public Task SetJsonAsync<T>(string key, T value, CancellationToken ct = default)
        {
            Store[key] = value!;
            return Task.CompletedTask;
        }
    }
}
