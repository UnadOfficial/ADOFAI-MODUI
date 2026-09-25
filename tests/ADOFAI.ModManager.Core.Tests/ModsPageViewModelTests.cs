using ADOFAI.ModManager.Core.Helpers;
using ADOFAI.ModManager.Core.Models.App;
using ADOFAI.ModManager.Core.Models.Local;
using ADOFAI.ModManager.Core.Models.Tuforums;
using ADOFAI.ModManager.Core.Models.Transfer;
using ADOFAI.ModManager.Core.Services;
using ADOFAI.ModManager.Core.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ADOFAI.ModManager.Core.Tests;

public class ModsPageViewModelTests
{
    private static (ModsPageViewModel Vm, FakeLocal Local, FakeTuf Tuf) Create(LocalMod[] locals, TufMod[] remote)
    {
        var settings = TestHost.CreateSettings(@"C:\Games\ADOFAI");
        var local = new FakeLocal(locals);
        var tuf = new FakeTuf(remote);
        var vm = new ModsPageViewModel(
            tuf, local, new FakeLifecycle(), new FakeCache(),
            new FakeDialogs(), settings, NullLogger<ModsPageViewModel>.Instance);
        return (vm, local, tuf);
    }

    [Fact]
    public async Task Initialize_MergesRemoteAndLocal_AndMarksUpdateAvailable()
    {
        var local = new LocalModBuilder("adofaitweaks", "2.8.0").Build();
        var (vm, _, _) = Create(
            new[] { local },
            new[]
            {
                new TufMod { Slug = "adofaitweaks", Name = "AdofaiTweaks", Version = "2.9.3" },
                new TufMod { Slug = "tufhelper", Name = "TUFHelper", Version = "3.1.1" },
            });

        await vm.InitializeAsync();

        Assert.Equal(2, vm.BrowseCards.Count);
        Assert.Single(vm.InstalledCards);

        var tweaks = vm.BrowseCards.First(c => c.Slug == "adofaitweaks");
        Assert.Equal(UiStrings.StatusUpdateAvailable, tweaks.StatusBadge);
        Assert.Equal("2.8.0 → 2.9.3", tweaks.VersionText);

        var tufhelper = vm.BrowseCards.First(c => c.Slug == "tufhelper");
        Assert.Equal(UiStrings.StatusNotInstalled, tufhelper.StatusBadge);
    }

    [Fact]
    public async Task SearchFilter_NarrowsBothLists()
    {
        var (vm, _, _) = Create(
            Array.Empty<LocalMod>(),
            new[]
            {
                new TufMod { Slug = "adofaitweaks", Name = "AdofaiTweaks" },
                new TufMod { Slug = "tufhelper", Name = "TUFHelper" },
            });

        await vm.InitializeAsync();

        vm.SearchText = "tufhelper";
        vm.ApplyFilters();
        var card = Assert.Single(vm.BrowseCards);
        Assert.Equal("tufhelper", card.Slug);
        Assert.Empty(vm.InstalledCards);

        vm.SearchText = "";
        vm.ApplyFilters();
        Assert.Equal(2, vm.BrowseCards.Count);
    }

    [Fact]
    public async Task StatusFilter_NotInstalled_OnlyRemoteWithoutLocal()
    {
        var local = new LocalModBuilder("adofaitweaks", "2.9.3").Build();
        var (vm, _, _) = Create(
            new[] { local },
            new[]
            {
                new TufMod { Slug = "adofaitweaks", Name = "AdofaiTweaks", Version = "2.9.3" },
                new TufMod { Slug = "tufhelper", Name = "TUFHelper", Version = "3.1.1" },
            });

        await vm.InitializeAsync();
        vm.SelectedStatus = StatusFilterItem.NotInstalled;
        vm.ApplyFilters();

        var card = Assert.Single(vm.BrowseCards);
        Assert.Equal("tufhelper", card.Slug);
    }

    [Fact]
    public async Task UnlistedLocalMods_AppearInInstalledList()
    {
        var local = new LocalModBuilder("someLocalOnlyMod", "0.1.0").Build();
        var (vm, _, _) = Create(
            new[] { local },
            new[] { new TufMod { Slug = "tufhelper", Name = "TUFHelper", Version = "3.1.1" } });

        await vm.InitializeAsync();

        var card = Assert.Single(vm.InstalledCards);
        Assert.Null(card.Remote);
        Assert.Equal(UiStrings.StatusUnlisted, card.StatusBadge);
        Assert.Single(vm.BrowseCards); // 远端目录仍然展示
    }

    [Fact]
    public async Task TagFilter_Works()
    {
        var (vm, _, _) = Create(
            Array.Empty<LocalMod>(),
            new[]
            {
                new TufMod { Slug = "a", Name = "A", Tags = new List<TufTag> { new() { Name = "Overlay" } } },
                new TufMod { Slug = "b", Name = "B", Tags = new List<TufTag> { new() { Name = "Gameplay" } } },
            });

        await vm.InitializeAsync();
        Assert.Equal(3, vm.AvailableTags.Count); // 全部标签 + Overlay + Gameplay

        vm.SelectedTag = vm.AvailableTags.First(t => t.Name == "Overlay");
        vm.ApplyFilters();
        Assert.Equal("a", Assert.Single(vm.BrowseCards).Slug);
    }

    // ───────── 构造辅助 ─────────

    private sealed class LocalModBuilder
    {
        private readonly string _id;
        private readonly string _version;

        public LocalModBuilder(string id, string version)
        {
            _id = id;
            _version = version;
        }

        public LocalMod Build()
        {
            var dir = Path.Combine(Path.GetTempPath(), "adofai-modui-tests", "vm-" + _id);
            Directory.CreateDirectory(dir);
            var info = new ModInfoJson { Id = _id, Version = _version, DisplayName = _id };
            Assert.True(UmVersion.TryParse(_version, out var v));
            return new LocalMod
            {
                DirectoryPath = dir,
                FolderName = _id,
                Info = info,
                LocalVersion = v,
            };
        }
    }

    private sealed class FakeTuf : ITuforumsClient
    {
        private readonly TufMod[] _mods;

        public FakeTuf(TufMod[] mods) => _mods = mods;

        public Task<CatalogSnapshot> GetCatalogAsync(bool forceRefresh = false, CancellationToken ct = default)
            => Task.FromResult(new CatalogSnapshot(_mods, DateTimeOffset.UtcNow, FromCache: false));

        public Task<TufMod?> GetModAsync(string slug, CancellationToken ct = default)
            => Task.FromResult<TufMod?>(null);
    }

    private sealed class FakeLocal : ILocalModService
    {
        private readonly LocalMod[] _mods;

        public FakeLocal(LocalMod[] mods) => _mods = mods;

        public string GetModsDirectory() => @"C:\Games\ADOFAI\Mods";

        public Task<LocalModsSnapshot> ScanAsync(CancellationToken ct = default)
            => Task.FromResult(new LocalModsSnapshot { ModsDirectory = GetModsDirectory(), Mods = _mods });

        public Task<LocalModsSnapshot> ToggleEnabledAsync(string modDirectoryPath, CancellationToken ct = default)
            => ScanAsync(ct);

        public Task<string> UninstallAsync(string modDirectoryPath, CancellationToken ct = default)
            => Task.FromResult(Path.GetTempPath());
    }

    private sealed class FakeLifecycle : IModLifecycleService
    {
        public Task<OperationResult> InstallLatestAsync(TufMod remote, bool forceWhenGameRunning = false, IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
            => Task.FromResult(OperationResult.Ok());

        public Task<OperationResult> UpdateAsync(LocalMod local, TufMod remote, bool forceWhenGameRunning = false, IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
            => Task.FromResult(OperationResult.Ok());

        public Task<OperationResult> UninstallAsync(LocalMod local, bool forceWhenGameRunning = false, CancellationToken ct = default)
            => Task.FromResult(OperationResult.Ok());

        public Task<OperationResult> ToggleEnabledAsync(LocalMod local, CancellationToken ct = default)
            => Task.FromResult(OperationResult.Ok());

        public Task<OperationResult> InstallZipAsync(string zipPath, IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
            => Task.FromResult(OperationResult.Ok());
    }

    private sealed class FakeCache : IDiskCacheService
    {
        public string CacheDirectory => Path.GetTempPath();

        public Task ClearAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string> FetchToCacheAsync(string category, string key, string url, CancellationToken ct = default)
            => Task.FromResult(Path.Combine(CacheDirectory, key));

        public string? GetCachedFilePath(string category, string key) => null;

        public Task<T?> GetJsonAsync<T>(string key, TimeSpan ttl, CancellationToken ct = default) where T : class
            => Task.FromResult<T?>(null);

        public Task SetJsonAsync<T>(string key, T value, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeDialogs : IDialogService
    {
        public Task AlertAsync(string title, string message) => Task.CompletedTask;

        public Task<bool> ConfirmAsync(string title, string message, string confirmText = "确定", string cancelText = "取消")
            => Task.FromResult(true);
    }
}
