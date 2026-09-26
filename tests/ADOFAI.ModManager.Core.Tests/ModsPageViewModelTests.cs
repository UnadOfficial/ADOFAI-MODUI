using ADOFAI.ModManager.Core.Helpers;
using ADOFAI.ModManager.Core.Models.App;
using ADOFAI.ModManager.Core.Models.Game;
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
        var (vm, local, tuf, _, _) = CreateFull(locals, remote);
        return (vm, local, tuf);
    }

    private static (ModsPageViewModel Vm, FakeLocal Local, FakeTuf Tuf, TestHost.FakeSettingsService Settings, FakeNav Nav) CreateFull(
        LocalMod[] locals, TufMod[] remote, string? gameRoot = @"C:\Games\ADOFAI", bool isValidGameRoot = true)
    {
        var settings = TestHost.CreateSettings(gameRoot);
        var local = new FakeLocal(locals);
        var tuf = new FakeTuf(remote);
        var steam = new FakeSteam(isValidGameRoot);
        var nav = new FakeNav();
        var vm = new ModsPageViewModel(
            tuf, local, new FakeLifecycle(), new FakeCache(),
            new FakeDialogs(), settings, steam, nav, NullLogger<ModsPageViewModel>.Instance);
        return (vm, local, tuf, settings, nav);
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

    [Fact]
    public async Task WhenGameRootNotConfigured_CardsHaveCanPrimaryFalseAndWarningState()
    {
        var local = new LocalModBuilder("adofaitweaks", "2.8.0").Build();
        var (vm, _, _, _, _) = CreateFull(
            new[] { local },
            new[]
            {
                new TufMod { Slug = "adofaitweaks", Name = "AdofaiTweaks", Version = "2.9.3" },
                new TufMod { Slug = "tufhelper", Name = "TUFHelper", Version = "3.1.1" },
            },
            gameRoot: "",
            isValidGameRoot: false);

        await vm.InitializeAsync();

        Assert.False(vm.HasGameRoot);
        Assert.True(vm.IsGameRootMissing);
        Assert.Equal("未设置游戏目录", vm.GameRootText);

        // 远端卡片与本地卡片操作按钮均置灰禁用
        Assert.Equal(2, vm.BrowseCards.Count);
        foreach (var card in vm.BrowseCards)
        {
            Assert.False(card.CanPrimary);
            Assert.False(card.CanSecondary);
            Assert.Contains("未配置游戏路径", card.PrimaryButtonToolTip);
            // 详情与外部链接仍然可以查看
            Assert.True(card.ShowDetailsCommand.CanExecute(null));
            Assert.True(card.OpenTufPageCommand.CanExecute(null));
        }
    }

    [Fact]
    public async Task WhenGameRootConfigured_CardsHaveCanPrimaryTrue()
    {
        var (vm, _, _, _, _) = CreateFull(
            Array.Empty<LocalMod>(),
            new[] { new TufMod { Slug = "tufhelper", Name = "TUFHelper", Version = "3.1.1" } },
            gameRoot: @"C:\Games\ADOFAI",
            isValidGameRoot: true);

        await vm.InitializeAsync();

        Assert.True(vm.HasGameRoot);
        Assert.False(vm.IsGameRootMissing);

        var card = Assert.Single(vm.BrowseCards);
        Assert.True(card.CanPrimary);
    }

    [Fact]
    public async Task ToolbarAndActions_GuardedWhenGameRootNotConfigured()
    {
        var local = new LocalModBuilder("adofaitweaks", "2.8.0").Build();
        var (vm, _, _, _, _) = CreateFull(
            new[] { local },
            new[] { new TufMod { Slug = "adofaitweaks", Name = "AdofaiTweaks", Version = "2.9.3" } },
            gameRoot: "",
            isValidGameRoot: false);

        await vm.InitializeAsync();

        // 1) 一键更新被拦截并给出错误提示
        await vm.UpdateAllAsync();
        Assert.True(vm.NoticeIsError);
        Assert.Contains("未配置", vm.Notice);

        // 2) 导入 zip 被拦截
        await vm.InstallZipAsync("dummy.zip");

        // 3) 卡片主操作被拦截
        var card = Assert.Single(vm.BrowseCards);
        await card.PrimaryActionCommand.ExecuteAsync(null);

        // 4) 重新扫描在未配置时清空已安装列表
        await vm.RescanAsync();
        Assert.Empty(vm.InstalledCards);
    }

    [Fact]
    public void NavigateToSettings_CallsNavService()
    {
        var (vm, _, _, _, nav) = CreateFull(
            Array.Empty<LocalMod>(),
            Array.Empty<TufMod>(),
            gameRoot: "",
            isValidGameRoot: false);

        vm.NavigateToSettings();
        Assert.Equal("settings", nav.LastNavigatedTag);
    }

    [Fact]
    public void SteamLocatorService_IsValidGameRoot_StrictValidation()
    {
        var locator = new SteamLocatorService(NullLogger<SteamLocatorService>.Instance);

        // 1) 空或空白
        Assert.False(locator.IsValidGameRoot(null));
        Assert.False(locator.IsValidGameRoot(""));
        Assert.False(locator.IsValidGameRoot("   "));

        // 2) 不存在的目录
        var nonExistent = Path.Combine(Path.GetTempPath(), "non-existent-" + Guid.NewGuid().ToString("N"));
        Assert.False(locator.IsValidGameRoot(nonExistent));

        // 3) 存在目录但无 exe、无 _Data
        var emptyDir = TestHost.CreateTempDir();
        Assert.False(locator.IsValidGameRoot(emptyDir));

        // 4) 有 exe 但缺少 _Data
        File.WriteAllText(Path.Combine(emptyDir, "A Dance of Fire and Ice.exe"), "dummy");
        Assert.False(locator.IsValidGameRoot(emptyDir));

        // 5) 同时具备 exe 与 _Data 目录（大小写不敏感）
        Directory.CreateDirectory(Path.Combine(emptyDir, "A Dance of Fire and Ice_Data"));
        Assert.True(locator.IsValidGameRoot(emptyDir));
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
        public List<string> Alerts { get; } = new();

        public Task AlertAsync(string title, string message)
        {
            Alerts.Add($"{title}: {message}");
            return Task.CompletedTask;
        }

        public Task<bool> ConfirmAsync(string title, string message, string confirmText = "确定", string cancelText = "取消")
            => Task.FromResult(true);
    }

    private sealed class FakeSteam : ISteamLocatorService
    {
        private readonly bool _isValid;

        public FakeSteam(bool isValid = true) => _isValid = isValid;

        public Task<GameLocatorResult> LocateAsync(CancellationToken ct = default)
            => Task.FromResult(new GameLocatorResult { Found = _isValid, GameRoot = @"C:\Games\ADOFAI" });

        public Task<GameLocatorResult> ValidateAsync(string? gameRoot, CancellationToken ct = default)
            => Task.FromResult(new GameLocatorResult { Found = _isValid, GameRoot = gameRoot ?? string.Empty });

        public bool IsValidGameRoot(string? gameRoot) => _isValid && !string.IsNullOrWhiteSpace(gameRoot);
    }

    private sealed class FakeNav : INavigationService
    {
        public string? LastNavigatedTag { get; private set; }

        public void NavigateTo(string tag) => LastNavigatedTag = tag;
    }
}
