using System.Collections.ObjectModel;
using System.Diagnostics;
using ADOFAI.ModManager.Core.Helpers;
using ADOFAI.ModManager.Core.Models;
using ADOFAI.ModManager.Core.Models.App;
using ADOFAI.ModManager.Core.Models.Local;
using ADOFAI.ModManager.Core.Models.Tuforums;
using ADOFAI.ModManager.Core.Models.Transfer;
using ADOFAI.ModManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace ADOFAI.ModManager.Core.ViewModels;

/// <summary>状态筛选条目。</summary>
public sealed record StatusFilterItem(string Label, LocalModStatus? Value)
{
    public override string ToString() => Label;

    public static StatusFilterItem All { get; } = new("全部状态", null);
    public static StatusFilterItem Installed { get; } = new(UiStrings.StatusInstalled, LocalModStatus.Installed);
    public static StatusFilterItem Update { get; } = new(UiStrings.StatusUpdateAvailable, LocalModStatus.UpdateAvailable);
    public static StatusFilterItem NotInstalled { get; } = new(UiStrings.StatusNotInstalled, null);
    public static StatusFilterItem Disabled { get; } = new(UiStrings.StatusDisabled, LocalModStatus.Disabled);
    public static StatusFilterItem Unlisted { get; } = new(UiStrings.StatusUnlisted, LocalModStatus.Unlisted);
}

/// <summary>
/// 主 ViewModel：TUF 远端目录 + 本地 Mods 扫描的合并视图。
/// 同时为“浏览 Mod”与“我的 Mod”两个页面提供数据与操作命令。
/// </summary>
public partial class ModsPageViewModel : ObservableObject, IDisposable
{
    private readonly ITuforumsClient _tuf;
    private readonly ILocalModService _local;
    private readonly IModLifecycleService _lifecycle;
    private readonly IDiskCacheService _cache;
    private readonly IDialogService _dialogs;
    private readonly ISettingsService _settings;
    private readonly ISteamLocatorService _steam;
    private readonly INavigationService _nav;
    private readonly IDispatcherService _dispatcher;
    private readonly ILogger<ModsPageViewModel> _logger;

    private readonly SemaphoreSlim _downloadSlots;
    private readonly Dictionary<string, SemaphoreSlim> _modLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ModCardViewModel> _allCards = new();
    private readonly LocalModsSnapshot?[] _lastScan = new LocalModsSnapshot?[1];
    private int _filterVersion;

    public ModsPageViewModel(
        ITuforumsClient tuf,
        ILocalModService local,
        IModLifecycleService lifecycle,
        IDiskCacheService cache,
        IDialogService dialogs,
        ISettingsService settings,
        ISteamLocatorService steam,
        INavigationService nav,
        ILogger<ModsPageViewModel> logger,
        IDispatcherService? dispatcher = null)
    {
        _tuf = tuf;
        _local = local;
        _lifecycle = lifecycle;
        _cache = cache;
        _dialogs = dialogs;
        _settings = settings;
        _steam = steam;
        _nav = nav;
        _logger = logger;
        _dispatcher = dispatcher ?? ImmediateDispatcherService.Instance;

        _settings.SettingsSaved += OnSettingsSaved;

        var parallel = Math.Clamp(settings.State.Settings.MaxParallelDownloads, 1, 6);
        _downloadSlots = new SemaphoreSlim(parallel, parallel);

        StatusOptions.Add(StatusFilterItem.All);
        StatusOptions.Add(StatusFilterItem.Installed);
        StatusOptions.Add(StatusFilterItem.Update);
        StatusOptions.Add(StatusFilterItem.NotInstalled);
        StatusOptions.Add(StatusFilterItem.Disabled);
        StatusOptions.Add(StatusFilterItem.Unlisted);
        SelectedStatus = StatusFilterItem.All;
    }

    // ───────── 绑定状态 ─────────

    public ObservableCollection<ModCardViewModel> BrowseCards { get; } = new();

    public ObservableCollection<ModCardViewModel> InstalledCards { get; } = new();

    public ObservableCollection<TagFilterItem> AvailableTags { get; } = new();

    public ObservableCollection<StatusFilterItem> StatusOptions { get; } = new();

    public ObservableCollection<InstallRecord> History { get; } = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private TagFilterItem? _selectedTag;

    [ObservableProperty]
    private StatusFilterItem? _selectedStatus;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private bool _isOffline;

    [ObservableProperty]
    private string? _notice;

    [ObservableProperty]
    private bool _noticeOpen;

    [ObservableProperty]
    private bool _noticeIsError;

    [ObservableProperty]
    private string _gameRootText = string.Empty;

    [ObservableProperty]
    private bool _hasGameRoot;

    [ObservableProperty]
    private bool _isGameRootMissing = true;

    [RelayCommand]
    public void NavigateToSettings() => _nav.NavigateTo("settings");

    partial void OnSearchTextChanged(string value) => ScheduleFilter();

    partial void OnSelectedTagChanged(TagFilterItem? value) => ScheduleFilter();

    partial void OnSelectedStatusChanged(StatusFilterItem? value) => ScheduleFilter();

    /// <summary>详情弹窗请求（由页面订阅并展示 ContentDialog）。</summary>
    public event Action<ModCardViewModel>? DetailsRequested;

    /// <summary>全部卡片的内部视图（下载页复用）。</summary>
    public IReadOnlyList<ModCardViewModel> AllCardsInternal => _allCards;

    // ───────── 初始化 / 刷新 ─────────

    [RelayCommand]
    public async Task InitializeAsync()
    {
        await _settings.LoadAsync().ConfigureAwait(true);
        RefreshGameRootState();
        await RescanLocalAsync().ConfigureAwait(true);
        await LoadCatalogAsync(forceRefresh: false).ConfigureAwait(true);
        LoadHistory();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await RescanLocalAsync().ConfigureAwait(true);
        await LoadCatalogAsync(forceRefresh: true).ConfigureAwait(true);
    }

    /// <summary>重新扫描本地 Mods 目录并刷新卡片状态与筛选。</summary>
    [RelayCommand]
    public async Task RescanAsync()
    {
        IsRefreshing = true;
        try
        {
            if (!HasGameRoot)
            {
                _lastScan[0] = null;
                InstalledCards.Clear();
                _allCards.RemoveAll(c => c.Remote is null);
                foreach (var card in _allCards)
                {
                    card.AttachLocal(null);
                }

                RebuildTagFilter();
                ApplyFilters();
                return;
            }

            await RescanLocalAsync().ConfigureAwait(true);

            IReadOnlyList<TufMod> remotes;
            try
            {
                var snapshot = await _tuf.GetCatalogAsync(forceRefresh: false).ConfigureAwait(true);
                remotes = snapshot.Mods;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "重新扫描时获取远端目录失败，回退到当前已加载的卡片");
                remotes = _allCards.Where(c => c.Remote is not null).Select(c => c.Remote!).ToList();
            }

            if (remotes.Count == 0 && _allCards.Any(c => c.Remote is not null))
            {
                remotes = _allCards.Where(c => c.Remote is not null).Select(c => c.Remote!).ToList();
            }

            MergeCards(remotes);
            RebuildTagFilter();
            ApplyFilters();
            LoadIcons();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task RescanLocalAsync()
    {
        if (!HasGameRoot)
        {
            _lastScan[0] = null;
            return;
        }

        try
        {
            _lastScan[0] = await _local.ScanAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "本地扫描失败");
            _lastScan[0] = null;
        }
    }

    private async Task LoadCatalogAsync(bool forceRefresh)
    {
        IsRefreshing = true;
        try
        {
            var snapshot = await _tuf.GetCatalogAsync(forceRefresh).ConfigureAwait(true);
            IsOffline = snapshot.FromCache;
            MergeCards(snapshot.Mods);
            RebuildTagFilter();
            ApplyFilters();
            LoadIcons();
            Notice = snapshot.FromCache ? "当前显示缓存的 Mod 目录（离线或刷新失败）" : null;
            NoticeOpen = snapshot.FromCache;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "拉取 TUF 目录失败");
            ShowNotice($"拉取 Mod 目录失败：{ex.Message}", isError: true);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>异步加载卡片图标（磁盘缓存；失败用占位符兜底）。结果回投到 UI 线程更新绑定。</summary>
    private void LoadIcons()
    {
        foreach (var card in _allCards)
        {
            var url = card.Remote?.ImageUrl;
            if (string.IsNullOrWhiteSpace(url) || card.IconPath is not null)
            {
                continue;
            }

            var slug = card.Remote!.Slug;
            _ = Task.Run(async () =>
            {
                try
                {
                    var path = await _cache.FetchToCacheAsync("icons", slug, url).ConfigureAwait(false);
                    _dispatcher.Enqueue(() => card.IconPath = path);
                }
                catch (Exception)
                {
                    // 图标加载失败 → 保持占位符
                }
            });
        }
    }

    /// <summary>刷新游戏目录状态显示（页面切换与设置变更时调用）。</summary>
    public void RefreshGameRootState()
    {
        var root = _settings.State.Settings.GameRoot;
        HasGameRoot = _steam.IsValidGameRoot(root);
        IsGameRootMissing = !HasGameRoot;
        GameRootText = HasGameRoot ? root : (string.IsNullOrWhiteSpace(root) ? "未设置游戏目录" : "游戏目录无效");

        foreach (var card in _allCards)
        {
            card.RefreshFrom(card.Local);
        }
    }

    private void OnSettingsSaved(object? sender, EventArgs e)
    {
        _dispatcher.Enqueue(() =>
        {
            RefreshGameRootState();
            _ = RescanAsync();
        });
    }

    // ───────── 合并远端 + 本地 ─────────

    private void MergeCards(IReadOnlyList<TufMod> remoteMods)
    {
        var scan = _lastScan[0];
        var locals = scan?.Mods ?? new List<LocalMod>();
        var linkMap = _settings.State.Links.ToDictionary(l => l.Slug, l => l.LocalId, StringComparer.OrdinalIgnoreCase);

        var newCards = new List<ModCardViewModel>();
        var usedLocals = new HashSet<string>(StringComparer.Ordinal);

        foreach (var remote in remoteMods)
        {
            var local = locals.FirstOrDefault(m => ModMatching.Matches(m, remote,
                linkMap.TryGetValue(remote.Slug, out var linked) ? linked : null));

            if (local is not null)
            {
                usedLocals.Add(local.DirectoryPath);
                local.LinkedSlug = remote.Slug;
                local.Status = local.Disabled
                    ? LocalModStatus.Disabled
                    : CompareStatus(local, remote);
            }

            var existing = FindByKey(SlugKey(remote.Slug));
            if (existing is not null)
            {
                existing.Remote = remote;
                existing.AttachLocal(local);
                newCards.Add(existing);
            }
            else
            {
                newCards.Add(new ModCardViewModel(this, remote, local));
            }
        }

        // 本地独有（未收录）Mod
        foreach (var local in locals.Where(m => !usedLocals.Contains(m.DirectoryPath)))
        {
            local.Status = local.Info is null ? LocalModStatus.Broken : LocalModStatus.Unlisted;
            var key = LocalKey(local.DirectoryPath);
            var existing = FindByKey(key);
            if (existing is not null)
            {
                existing.Remote = null;
                existing.AttachLocal(local);
                newCards.Add(existing);
            }
            else
            {
                newCards.Add(new ModCardViewModel(this, null, local));
            }
        }

        // 保留正在进行操作、但已从列表消失的卡片（避免下载中途 UI 断开）
        foreach (var card in _allCards)
        {
            if (card.IsBusy && !ReferenceExists(newCards, card))
            {
                newCards.Add(card);
            }
        }

        _allCards.Clear();
        _allCards.AddRange(newCards);
    }

    private ModCardViewModel? FindByKey(string key)
    {
        foreach (var card in _allCards)
        {
            if (KeyOf(card) == key)
            {
                return card;
            }
        }

        return null;
    }

    private static bool ReferenceExists(IEnumerable<ModCardViewModel> list, ModCardViewModel card)
        => list.Any(c => ReferenceEquals(c, card));

    private static string KeyOf(ModCardViewModel card)
        => card.Remote is not null ? SlugKey(card.Remote.Slug) : LocalKey(card.Local?.DirectoryPath ?? card.Slug);

    private static string SlugKey(string slug) => "slug:" + slug.ToLowerInvariant();

    private static string LocalKey(string path) => "local:" + path.ToLowerInvariant();

    private static LocalModStatus CompareStatus(LocalMod local, TufMod remote)
    {
        var remoteOk = UmVersion.TryParse(remote.EffectiveVersion, out var remoteVersion);
        var localOk = local.LocalVersion is not null;
        if (remoteOk && localOk && remoteVersion > local.LocalVersion!)
        {
            return LocalModStatus.UpdateAvailable;
        }

        return LocalModStatus.Installed;
    }

    private void RebuildTagFilter()
    {
        var counts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in _allCards)
        {
            foreach (var tag in card.Tags)
            {
                counts[tag.Name] = counts.GetValueOrDefault(tag.Name) + 1;
            }
        }

        var selected = SelectedTag?.Name;
        AvailableTags.Clear();
        AvailableTags.Add(new TagFilterItem("全部标签", _allCards.Count));
        foreach (var (name, count) in counts)
        {
            AvailableTags.Add(new TagFilterItem(name, count));
        }

        SelectedTag = selected is null ? null : AvailableTags.FirstOrDefault(t => t.Name == selected);
    }

    // ───────── 过滤 ─────────

    private async void ScheduleFilter()
    {
        var version = ++_filterVersion;
        await Task.Delay(150).ConfigureAwait(true);
        if (version != _filterVersion)
        {
            return;
        }

        ApplyFilters();
    }

    public void ApplyFilters()
    {
        var query = SearchText?.Trim() ?? string.Empty;
        var queryTerms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tag = SelectedTag?.Name;
        var status = SelectedStatus;

        bool TagMatch(ModCardViewModel c) =>
            string.IsNullOrEmpty(tag) || tag == "全部标签" || c.Tags.Any(t => string.Equals(t.Name, tag, StringComparison.OrdinalIgnoreCase));

        bool TextMatch(ModCardViewModel c)
        {
            if (queryTerms.Length == 0)
            {
                return true;
            }

            var haystack = string.Concat(c.Title, " ", c.Author, " ", c.Slug, " ", c.Description);
            foreach (var term in queryTerms)
            {
                if (!haystack.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        bool StatusMatch(ModCardViewModel c)
        {
            if (status is null || status == StatusFilterItem.All)
            {
                return true;
            }

            if (status == StatusFilterItem.NotInstalled)
            {
                return c.Local is null && c.Remote is not null;
            }

            return c.Local is not null &&
                   status.Value is { } value &&
                   (c.Local.Status == value ||
                    (value == LocalModStatus.Disabled && c.Local.Disabled));
        }

        var browse = _allCards.Where(c => c.Remote is not null && TextMatch(c) && TagMatch(c) && StatusMatch(c));
        var installed = _allCards.Where(c => c.Local is not null && TextMatch(c) && TagMatch(c) && StatusMatch(c));

        ReplaceAll(BrowseCards, browse);
        ReplaceAll(InstalledCards, installed);
    }

    private static void ReplaceAll<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    // ───────── 卡片操作（由 ModCardViewModel 委托） ─────────

    public async Task RunPrimaryAsync(ModCardViewModel card)
    {
        if (!HasGameRoot)
        {
            await _dialogs.AlertAsync("未设置游戏目录", "请先到“设置”页定位《冰与火之舞》的游戏根目录。").ConfigureAwait(true);
            return;
        }

        if (card.Local is not null && card.Local.Disabled)
        {
            await ToggleAsync(card).ConfigureAwait(true);
            return;
        }

        if (card.Remote is null)
        {
            OpenModFolder(card);
            return;
        }

        var forceReinstall = card.PrimaryButtonText == UiStrings.ActionReinstall;
        await InstallOrUpdateAsync(card, forceReinstall).ConfigureAwait(true);
    }

    public async Task RunSecondaryAsync(ModCardViewModel card)
    {
        if (!HasGameRoot)
        {
            await _dialogs.AlertAsync("未设置游戏目录", "请先到“设置”页定位《冰与火之舞》的游戏根目录。").ConfigureAwait(true);
            return;
        }

        if (card.Local is null)
        {
            return;
        }

        if (card.Local.Disabled)
        {
            await UninstallAsync(card).ConfigureAwait(true);
        }
        else
        {
            await ToggleAsync(card).ConfigureAwait(true);
        }
    }

    private async Task InstallOrUpdateAsync(ModCardViewModel card, bool forceReinstall = false)
    {
        if (card.Remote is null || card.IsBusy)
        {
            return;
        }

        if (!await EnsureGameRootAsync().ConfigureAwait(true))
        {
            return;
        }

        var cts = card.BeginOperation();
        var slotHeld = false;
        SemaphoreSlim modLock = LockFor(card.Slug);

        try
        {
            await modLock.WaitAsync(cts.Token).ConfigureAwait(true);
            await _downloadSlots.WaitAsync(cts.Token).ConfigureAwait(true);
            slotHeld = true;

            card.SetState(ModOpState.Resolving, 0, "准备中…");

            var progress = new Progress<InstallProgress>(p =>
            {
                card.SetState(PhaseToState(p.Phase), p.Percent, p.Detail ?? string.Empty);
            });

            var result = await _lifecycle.InstallLatestAsync(card.Remote, forceWhenGameRunning: false, progress, forceReinstall, cts.Token)
                .ConfigureAwait(true);

            if (result.Status == ResultStatus.Warning && result.Payload == "GameRunning")
            {
                card.SetState(ModOpState.Idle, 0, string.Empty);
                if (await _dialogs.ConfirmAsync("游戏正在运行", UiStrings.GameRunningWarning, "仍要继续", "取消")
                        .ConfigureAwait(true))
                {
                    await InstallOrUpdateForcedAsync(card, forceReinstall).ConfigureAwait(true);
                }

                return;
            }

            if (result.Status == ResultStatus.Warning)
            {
                // 浏览器兜底下载
                card.SetState(ModOpState.Idle, 0, string.Empty);
                if (!string.IsNullOrEmpty(result.Payload) &&
                    await _dialogs.ConfirmAsync(UiStrings.ConfirmOpenUrlTitle, UiStrings.BrowserFallbackMessage, "打开浏览器", "取消")
                        .ConfigureAwait(true))
                {
                    Browser.Open(result.Payload);
                }

                return;
            }

            if (result.Status == ResultStatus.Error)
            {
                card.SetState(ModOpState.Error, 0, result.Message);
                ShowNotice(result.Message, isError: true);
                return;
            }

            card.SetState(ModOpState.Success, 100, result.Message);
            await AfterMutatingOperationAsync().ConfigureAwait(true);
            ShowNotice(result.Message, isError: false);
        }
        catch (OperationCanceledException)
        {
            card.SetState(ModOpState.Idle, 0, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "安装/更新失败：{Mod}", card.Title);
            card.SetState(ModOpState.Error, 0, ex.Message);
            ShowNotice($"操作失败：{ex.Message}", isError: true);
        }
        finally
        {
            if (slotHeld)
            {
                _downloadSlots.Release();
            }

            modLock.Release();
            card.EndOperation();
            cts.Dispose();
        }
    }

    private async Task InstallOrUpdateForcedAsync(ModCardViewModel card, bool forceReinstall = false)
    {
        if (card.Remote is null)
        {
            return;
        }

        var cts = card.BeginOperation();
        var slotHeld = false;
        var modLock = LockFor(card.Slug);

        try
        {
            await modLock.WaitAsync(cts.Token).ConfigureAwait(true);
            await _downloadSlots.WaitAsync(cts.Token).ConfigureAwait(true);
            slotHeld = true;

            var progress = new Progress<InstallProgress>(p =>
                card.SetState(PhaseToState(p.Phase), p.Percent, p.Detail ?? string.Empty));

            var result = await _lifecycle.InstallLatestAsync(card.Remote, forceWhenGameRunning: true, progress, forceReinstall, cts.Token)
                .ConfigureAwait(true);

            if (result.Status == ResultStatus.Error)
            {
                card.SetState(ModOpState.Error, 0, result.Message);
                ShowNotice(result.Message, isError: true);
                return;
            }

            card.SetState(ModOpState.Success, 100, result.Message);
            await AfterMutatingOperationAsync().ConfigureAwait(true);
            ShowNotice(result.Message, isError: false);
        }
        catch (OperationCanceledException)
        {
            card.SetState(ModOpState.Idle, 0, string.Empty);
        }
        finally
        {
            if (slotHeld)
            {
                _downloadSlots.Release();
            }

            modLock.Release();
            card.EndOperation();
            cts.Dispose();
        }
    }

    private async Task UninstallAsync(ModCardViewModel card)
    {
        if (!HasGameRoot)
        {
            ShowNotice("未配置游戏路径，无法卸载 Mod。", isError: true);
            return;
        }

        if (card.Local is null || card.IsBusy)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            UiStrings.ConfirmUninstallTitle,
            string.Format(CultureInfo.CurrentCulture, UiStrings.ConfirmUninstallFormat, card.Title)).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        var result = await _lifecycle.UninstallAsync(card.Local, forceWhenGameRunning: false).ConfigureAwait(true);
        if (result.Status == ResultStatus.Warning && result.Payload == "GameRunning")
        {
            if (await _dialogs.ConfirmAsync("游戏正在运行", UiStrings.GameRunningWarning, "仍要继续", "取消")
                    .ConfigureAwait(true))
            {
                result = await _lifecycle.UninstallAsync(card.Local, forceWhenGameRunning: true).ConfigureAwait(true);
            }
            else
            {
                return;
            }
        }

        if (result.Status == ResultStatus.Error)
        {
            ShowNotice(result.Message, isError: true);
            return;
        }

        ShowNotice(result.Message, isError: false);
        await AfterMutatingOperationAsync().ConfigureAwait(true);
    }

    private async Task ToggleAsync(ModCardViewModel card)
    {
        if (!HasGameRoot)
        {
            ShowNotice("未配置游戏路径，无法管理 Mod 状态。", isError: true);
            return;
        }

        if (card.Local is null || card.IsBusy)
        {
            return;
        }

        var result = await _lifecycle.ToggleEnabledAsync(card.Local).ConfigureAwait(true);
        if (result.Status == ResultStatus.Error)
        {
            ShowNotice(result.Message, isError: true);
            return;
        }

        ShowNotice(result.Message, isError: false);
        await AfterMutatingOperationAsync().ConfigureAwait(true);
    }

    public static void CancelOperation(ModCardViewModel card) => card.RequestCancel();

    /// <summary>一键更新全部可更新 Mod（顺序执行，避免并发写目录）。</summary>
    [RelayCommand]
    public async Task UpdateAllAsync()
    {
        if (!HasGameRoot)
        {
            ShowNotice("未配置有效的游戏路径，无法更新 Mod。", isError: true);
            return;
        }

        var targets = _allCards
            .Where(c => c.Remote is not null && c.Local is not null && !c.Local.Disabled && !c.IsBusy &&
                        c.StatusBadge == UiStrings.StatusUpdateAvailable)
            .ToList();

        if (targets.Count == 0)
        {
            ShowNotice("没有可更新的 Mod。", isError: false);
            return;
        }

        foreach (var card in targets)
        {
            await InstallOrUpdateAsync(card).ConfigureAwait(true);
        }
    }

    /// <summary>导入本地 zip 安装（页面用文件选择器取路径后调用）。</summary>
    public async Task InstallZipAsync(string zipPath)
    {
        if (!await EnsureGameRootAsync().ConfigureAwait(true))
        {
            return;
        }

        var result = await _lifecycle.InstallZipAsync(zipPath).ConfigureAwait(true);
        ShowNotice(result.Message, isError: result.Status == ResultStatus.Error);
        if (result.Status != ResultStatus.Error)
        {
            await AfterMutatingOperationAsync().ConfigureAwait(true);
        }
    }

    public static void OpenModFolder(ModCardViewModel card)
    {
        if (card.Local?.DirectoryPath is { Length: > 0 } path && Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }

    [RelayCommand]
    public void OpenModsFolder()
    {
        if (!HasGameRoot)
        {
            return;
        }

        var mods = _local.GetModsDirectory();
        if (!string.IsNullOrEmpty(mods) && Directory.Exists(mods))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{mods}\"") { UseShellExecute = true });
        }
    }

    public void RequestDetails(ModCardViewModel card) => DetailsRequested?.Invoke(card);

    // ───────── 辅助 ─────────

    private SemaphoreSlim LockFor(string slug)
    {
        lock (_modLocks)
        {
            if (!_modLocks.TryGetValue(slug, out var sem))
            {
                sem = new SemaphoreSlim(1, 1);
                _modLocks[slug] = sem;
            }

            return sem;
        }
    }

    private async Task<bool> EnsureGameRootAsync()
    {
        if (HasGameRoot)
        {
            return true;
        }

        await _dialogs.AlertAsync("未设置游戏目录", "请先到“设置”页定位《冰与火之舞》的游戏根目录。").ConfigureAwait(true);
        return false;
    }

    private async Task AfterMutatingOperationAsync()
    {
        await RescanAsync().ConfigureAwait(true);
        LoadHistory();
    }

    private void LoadHistory()
    {
        History.Clear();
        foreach (var record in _settings.State.Installs
                     .OrderByDescending(i => i.InstalledAt)
                     .Take(30))
        {
            History.Add(record);
        }
    }

    private void ShowNotice(string message, bool isError)
    {
        Notice = message;
        NoticeIsError = isError;
        NoticeOpen = !string.IsNullOrEmpty(message);
    }

    private static ModOpState PhaseToState(InstallPhase phase) => phase switch
    {
        InstallPhase.ResolvingDownloadUrl => ModOpState.Resolving,
        InstallPhase.Downloading => ModOpState.Downloading,
        InstallPhase.Extracting => ModOpState.Extracting,
        InstallPhase.PreservingConfigs or InstallPhase.Swapping => ModOpState.Finishing,
        InstallPhase.Done => ModOpState.Success,
        _ => ModOpState.Queued,
    };

    public void Dispose()
    {
        _settings.SettingsSaved -= OnSettingsSaved;
        _downloadSlots.Dispose();
        foreach (var gate in _modLocks.Values)
        {
            gate.Dispose();
        }

        _modLocks.Clear();
        GC.SuppressFinalize(this);
    }
}
