using System.Net;
using ADOFAI.ModManager.Core.Models;
using ADOFAI.ModManager.Core.Models.Game;
using ADOFAI.ModManager.Core.Services;
using ADOFAI.ModManager.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ADOFAI.ModManager.Core.Tests;

public class SettingsViewModelTests
{
    private static SettingsViewModel CreateVm(TestHost.FakeSettingsService settings, HttpMessageHandler? handler = null)
    {
        var steam = new FakeSteamLocator();
        var cache = new FakeDiskCache();
        var dialogs = new FakeDialogService();
        var picker = new FakeFilePickerService();
        var guard = new FakeGameGuard();
        var http = new FakeHttpClientFactory(handler ?? new FakeHandler());
        var mirrorService = new GitHubMirrorService(settings, http, NullLogger<GitHubMirrorService>.Instance);

        return new SettingsViewModel(steam, settings, cache, dialogs, picker, guard, mirrorService);
    }

    [Fact]
    public void InitialState_DefaultSettings_SelectsDirect()
    {
        var settings = TestHost.CreateSettings();
        var vm = CreateVm(settings);

        Assert.NotNull(vm.SelectedGitHubMirrorOption);
        Assert.True(vm.SelectedGitHubMirrorOption.IsDirect);
        Assert.False(vm.IsCustomMirror);
    }

    [Fact]
    public void SelectPreset_UpdatesSettingsAndPersists()
    {
        var settings = TestHost.CreateSettings();
        var vm = CreateVm(settings);

        var ghfast = vm.GitHubMirrorOptions.First(o => o.Url == "https://ghfast.top/");
        vm.SelectedGitHubMirrorOption = ghfast;

        Assert.Equal("https://ghfast.top/", settings.State.Settings.GitHubMirror);
        Assert.False(vm.IsCustomMirror);
    }

    [Fact]
    public void SelectCustom_TogglesCustomInputAndPersists()
    {
        var settings = TestHost.CreateSettings();
        var vm = CreateVm(settings);

        var customOption = vm.GitHubMirrorOptions.First(o => o.IsCustom);
        vm.SelectedGitHubMirrorOption = customOption;

        Assert.True(vm.IsCustomMirror);

        vm.CustomGitHubMirrorUrl = "https://custom.example.com/";

        Assert.Equal("https://custom.example.com/", settings.State.Settings.GitHubMirror);
        Assert.Equal("https://custom.example.com/", settings.State.Settings.CustomGitHubMirror);
    }

    [Fact]
    public async Task TestMirrorCommand_UpdatesStatusText()
    {
        var settings = TestHost.CreateSettings();
        var handler = new FakeHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var vm = CreateVm(settings, handler);

        var ghproxy = vm.GitHubMirrorOptions.First(o => o.Url == "https://ghproxy.net/");
        vm.SelectedGitHubMirrorOption = ghproxy;

        await vm.TestMirrorCommand.ExecuteAsync(null);

        Assert.False(vm.IsTestingMirror);
        Assert.NotNull(vm.MirrorTestStatus);
        Assert.Contains("连通正常", vm.MirrorTestStatus);
        Assert.Equal("#3BA55C", vm.MirrorTestStatusColor);
    }

    private sealed class FakeSteamLocator : ISteamLocatorService
    {
        public Task<GameLocatorResult> LocateAsync(CancellationToken ct = default)
            => Task.FromResult(new GameLocatorResult { Found = true, GameRoot = "D:\\Game", Source = GameSource.Manual });

        public Task<GameLocatorResult> ValidateAsync(string? gameRoot, CancellationToken ct = default)
            => Task.FromResult(new GameLocatorResult { Found = true, GameRoot = gameRoot ?? string.Empty, Source = GameSource.Manual });

        public bool IsValidGameRoot(string? gameRoot) => !string.IsNullOrWhiteSpace(gameRoot);
    }

    private sealed class FakeDiskCache : IDiskCacheService
    {
        public string CacheDirectory => Path.Combine(Path.GetTempPath(), "adofai-modui-tests", "fake-cache");
        public Task ClearAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> FetchToCacheAsync(string category, string key, string url, CancellationToken ct = default)
            => Task.FromResult(Path.Combine(CacheDirectory, key));
        public Task<T?> GetJsonAsync<T>(string key, TimeSpan ttl, CancellationToken ct = default) where T : class => Task.FromResult<T?>(null);
        public Task SetJsonAsync<T>(string key, T data, CancellationToken ct = default) => Task.CompletedTask;
        public string? GetCachedFilePath(string category, string key) => null;
    }

    private sealed class FakeDialogService : IDialogService
    {
        public Task AlertAsync(string title, string message) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string confirmText = "确定", string cancelText = "取消") => Task.FromResult(true);
    }

    private sealed class FakeFilePickerService : IFilePickerService
    {
        public Task<string?> PickFileAsync(params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
    }

    private sealed class FakeGameGuard : IGameProcessGuard
    {
        public bool IsGameRunning() => false;
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _resp;
        public FakeHandler(HttpResponseMessage? resp = null) => _resp = resp ?? new HttpResponseMessage(HttpStatusCode.OK);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_resp.StatusCode));
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
