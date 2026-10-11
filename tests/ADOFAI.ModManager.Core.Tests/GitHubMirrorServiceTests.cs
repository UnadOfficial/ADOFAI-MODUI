using System.Net;
using ADOFAI.ModManager.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ADOFAI.ModManager.Core.Tests;

public class GitHubMirrorServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ApplyMirror_EmptyOrWhitespaceMirror_ReturnsOriginalUrl(string? mirror)
    {
        const string rawUrl = "https://github.com/owner/repo/releases/download/v1.0/mod.zip";
        var result = GitHubMirrorService.ApplyMirrorWithPrefix(rawUrl, mirror);
        Assert.Equal(rawUrl, result);
    }

    [Fact]
    public void ApplyMirror_NonGitHubUrl_ReturnsOriginalUrl()
    {
        const string cdnUrl = "https://api.tuforums.com/cdn/mods/JipperKeyViewer.zip";
        var result = GitHubMirrorService.ApplyMirrorWithPrefix(cdnUrl, "https://ghproxy.net/");
        Assert.Equal(cdnUrl, result);
    }

    [Theory]
    [InlineData("https://ghproxy.net/")]
    [InlineData("https://ghproxy.net")]
    public void ApplyMirror_GitHubReleaseDownload_AddsPrefixWithSingleSlash(string mirror)
    {
        const string rawUrl = "https://github.com/owner/repo/releases/download/v1.0/mod.zip";
        var result = GitHubMirrorService.ApplyMirrorWithPrefix(rawUrl, mirror);
        Assert.Equal("https://ghproxy.net/https://github.com/owner/repo/releases/download/v1.0/mod.zip", result);
    }

    [Fact]
    public void ApplyMirror_TemplatePlaceholder_ReplacesCorrectly()
    {
        const string rawUrl = "https://github.com/owner/repo/releases/download/v1.0/mod.zip";
        var template1 = "https://proxy.example.com/{url}";
        var result1 = GitHubMirrorService.ApplyMirrorWithPrefix(rawUrl, template1);
        Assert.Equal("https://proxy.example.com/https://github.com/owner/repo/releases/download/v1.0/mod.zip", result1);

        var template2 = "https://proxy.example.com/download?target={url}&auth=1";
        var result2 = GitHubMirrorService.ApplyMirrorWithPrefix(rawUrl, template2);
        Assert.Equal("https://proxy.example.com/download?target=https://github.com/owner/repo/releases/download/v1.0/mod.zip&auth=1", result2);

        var template3 = "https://proxy.example.com/{0}";
        var result3 = GitHubMirrorService.ApplyMirrorWithPrefix(rawUrl, template3);
        Assert.Equal("https://proxy.example.com/https://github.com/owner/repo/releases/download/v1.0/mod.zip", result3);
    }

    [Fact]
    public void ApplyMirror_AlreadyPrefixed_DoesNotDoublePrefix()
    {
        const string alreadyMirrored = "https://ghproxy.net/https://github.com/owner/repo/releases/download/v1.0/mod.zip";
        var result = GitHubMirrorService.ApplyMirrorWithPrefix(alreadyMirrored, "https://ghproxy.net/");
        Assert.Equal(alreadyMirrored, result);
    }

    [Theory]
    [InlineData("https://raw.githubusercontent.com/owner/repo/main/mod.json")]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset-2e65be/123")]
    [InlineData("https://codeload.github.com/owner/repo/zip/refs/heads/main")]
    public void ApplyMirror_VariousGitHubHosts_CorrectlyMirrored(string url)
    {
        var result = GitHubMirrorService.ApplyMirrorWithPrefix(url, "https://ghfast.top/");
        Assert.Equal($"https://ghfast.top/{url}", result);
    }

    [Fact]
    public void Presets_ContainsExpectedPublicMirrors()
    {
        var settings = TestHost.CreateSettings();
        var service = new GitHubMirrorService(settings, new FakeHttpClientFactory(new FakeHandler()), NullLogger<GitHubMirrorService>.Instance);

        Assert.Contains(service.Presets, p => p.IsDirect);
        Assert.Contains(service.Presets, p => p.IsCustom);
        Assert.Contains(service.Presets, p => p.Url == "https://ghproxy.net/");
        Assert.Contains(service.Presets, p => p.Url == "https://ghfast.top/");
        Assert.Contains(service.Presets, p => p.Url == "https://gh.ddlc.top/");
        Assert.Contains(service.Presets, p => p.Url == "https://gh.con.sh/");
        Assert.Contains(service.Presets, p => p.Url == "https://ghps.cc/");
        Assert.Contains(service.Presets, p => p.Url == "https://gh-proxy.com/");
    }

    [Fact]
    public async Task TestLatencyAsync_WhenServerReturnsOk_ReturnsSuccessWithLatency()
    {
        var handler = new FakeHandler(response: new HttpResponseMessage(HttpStatusCode.OK));
        var factory = new FakeHttpClientFactory(handler);
        var settings = TestHost.CreateSettings();
        var service = new GitHubMirrorService(settings, factory, NullLogger<GitHubMirrorService>.Instance);

        var result = await service.TestLatencyAsync("https://ghproxy.net/");

        Assert.True(result.Success);
        Assert.True(result.LatencyMs >= 0);
        Assert.Contains("连通正常", result.Message);
    }

    [Fact]
    public async Task TestLatencyAsync_WhenServerError_ReturnsFailureMessage()
    {
        var handler = new FakeHandler(response: new HttpResponseMessage(HttpStatusCode.BadGateway));
        var factory = new FakeHttpClientFactory(handler);
        var settings = TestHost.CreateSettings();
        var service = new GitHubMirrorService(settings, factory, NullLogger<GitHubMirrorService>.Instance);

        var result = await service.TestLatencyAsync("https://broken.example.com/");

        Assert.False(result.Success);
        Assert.Contains("502", result.Message);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public FakeHandler(HttpResponseMessage? response = null)
        {
            _response = response ?? new HttpResponseMessage(HttpStatusCode.OK);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_response.StatusCode));
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
