using System.Net;
using ADOFAI.ModManager.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ADOFAI.ModManager.Core.Tests;

public class TranslationServiceTests
{
    private const string BingHtmlMock = """
        <html>
        <head><script>
        var params_AbusePreventionHelper = [1790480393423,"testToken123",3600000];
        var _G = { IG: "301B57E5A7DF47CA82417B12E47FE007" };
        </script></head>
        <body data-iid="translator.5025"></body>
        </html>
        """;

    private const string BingResponseMock = """
        [{"translations":[{"text":"你好，世界","to":"zh-Hans"}],"detectedLanguage":{"language":"en"}}]
        """;

    private const string GoogleResponseMock = """
        [[["你好世界","Hello world",null,null,10]],null,"en"]
        """;

    private static (TranslationService Service, DynamicFakeHandler Handler, TestHost.FakeSettingsService Settings) CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new DynamicFakeHandler(responder);
        var httpFactory = new TestHttpClientFactory(handler);
        var settings = TestHost.CreateSettings();
        var cache = new InMemoryDiskCache();
        var service = new TranslationService(httpFactory, settings, cache, NullLogger<TranslationService>.Instance);
        return (service, handler, settings);
    }

    [Fact]
    public async Task TranslateViaBing_Success()
    {
        var (service, handler, settings) = CreateService(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("/translator", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BingHtmlMock, System.Text.Encoding.UTF8, "text/html"),
                };
            }

            if (url.Contains("/ttranslatev3", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BingResponseMock, System.Text.Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        settings.State.Settings.TranslationProvider = "Bing";
        var result = await service.TranslateAsync("Hello world", "zh-CN");

        Assert.True(result.Success);
        Assert.Equal("你好，世界", result.TranslatedText);
        Assert.Equal("Bing", result.ProviderName);
        Assert.Equal("en", result.DetectedSourceLanguage);
    }

    [Fact]
    public async Task TranslateViaGoogle_Success()
    {
        var (service, handler, settings) = CreateService(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("translate.googleapis.com", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(GoogleResponseMock, System.Text.Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        settings.State.Settings.TranslationProvider = "Google";
        var result = await service.TranslateAsync("Hello world", "zh-CN");

        Assert.True(result.Success);
        Assert.Equal("你好世界", result.TranslatedText);
        Assert.Equal("Google", result.ProviderName);
    }

    [Fact]
    public async Task AutoMode_BingFails_FallsBackToGoogle()
    {
        var (service, handler, settings) = CreateService(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("bing.com", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            if (url.Contains("translate.googleapis.com", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(GoogleResponseMock, System.Text.Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        settings.State.Settings.TranslationProvider = "Auto";
        var result = await service.TranslateAsync("Hello world", "zh-CN");

        Assert.True(result.Success);
        Assert.Equal("你好世界", result.TranslatedText);
        Assert.Equal("Google", result.ProviderName);
    }

    [Fact]
    public async Task EmptyText_ReturnsImmediately()
    {
        var (service, handler, _) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await service.TranslateAsync("   ");

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.TranslatedText);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task SubsequentCalls_HitCache()
    {
        var (service, handler, settings) = CreateService(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("translate.googleapis.com", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(GoogleResponseMock, System.Text.Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        settings.State.Settings.TranslationProvider = "Google";

        var first = await service.TranslateAsync("Hello world", "zh-CN");
        Assert.True(first.Success);
        var initialRequestCount = handler.RequestCount;
        Assert.True(initialRequestCount > 0);

        var second = await service.TranslateAsync("Hello world", "zh-CN");
        Assert.True(second.Success);
        Assert.Equal(first.TranslatedText, second.TranslatedText);
        Assert.Equal(initialRequestCount, handler.RequestCount); // 没有发起新的 HTTP 请求
    }

    private sealed class DynamicFakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public DynamicFakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public TestHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class InMemoryDiskCache : IDiskCacheService
    {
        private readonly Dictionary<string, object> _store = new();

        public string CacheDirectory => Path.Combine(Path.GetTempPath(), "test-cache");

        public Task ClearAsync(CancellationToken ct = default)
        {
            _store.Clear();
            return Task.CompletedTask;
        }

        public Task<string> FetchToCacheAsync(string category, string key, string url, CancellationToken ct = default)
            => Task.FromResult(string.Empty);

        public string? GetCachedFilePath(string category, string key) => null;

        public Task<T?> GetJsonAsync<T>(string key, TimeSpan ttl, CancellationToken ct = default) where T : class
            => Task.FromResult(_store.TryGetValue(key, out var val) ? val as T : null);

        public Task SetJsonAsync<T>(string key, T value, CancellationToken ct = default)
        {
            _store[key] = value!;
            return Task.CompletedTask;
        }
    }
}
