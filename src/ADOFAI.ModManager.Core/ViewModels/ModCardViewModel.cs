using System.Collections.ObjectModel;
using ADOFAI.ModManager.Core.Helpers;
using ADOFAI.ModManager.Core.Models;
using ADOFAI.ModManager.Core.Models.Local;
using ADOFAI.ModManager.Core.Models.Tuforums;
using ADOFAI.ModManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ADOFAI.ModManager.Core.ViewModels;

/// <summary>标签筛选条目。</summary>
public sealed record TagFilterItem(string Name, int Count)
{
    public override string ToString() => Name;
}

/// <summary>
/// Mod 卡片视图模型：聚合远端（TUF）与本地（扫描）信息，
/// 驱动版本徽章、操作按钮与行内下载进度条。
/// </summary>
public partial class ModCardViewModel : ObservableObject, IDisposable
{
    private readonly ModsPageViewModel _owner;
    private CancellationTokenSource? _operationCts;

    public ModCardViewModel(ModsPageViewModel owner, TufMod? remote, LocalMod? local)
    {
        _owner = owner;
        Remote = remote;
        Local = local;
        RefreshFrom(local);
    }

    public TufMod? Remote { get; internal set; }

    public LocalMod? Local { get; private set; }

    public string Slug => Remote?.Slug ?? Local?.DisplayId ?? string.Empty;

    public string Title => Remote?.Name ?? Local?.DisplayName ?? "(未知 Mod)";

    public string Author => Remote?.CreatorUsername ?? Local?.Author ?? string.Empty;

    public string Description => TextFormatting.Summarize(Remote?.Description, 170);

    public string TufPageUrl => Remote is null ? string.Empty : $"https://tuforums.com/mods/{Remote.Slug}";

    // ───────── 绑定属性 ─────────

    [ObservableProperty]
    private string _statusBadge = UiStrings.StatusNotInstalled;

    [ObservableProperty]
    private string _statusBadgeColor = "#8A8A8A";

    [ObservableProperty]
    private string _versionText = string.Empty;

    [ObservableProperty]
    private string _statsText = string.Empty;

    [ObservableProperty]
    private string _updatedText = string.Empty;

    /// <summary>图标路径（本地缓存文件路径或远程 URL；空表示无图标）。UI 转换为 ImageSource。</summary>
    [ObservableProperty]
    private string? _iconPath;

    [ObservableProperty]
    private ObservableCollection<TufTag> _tags = new();

    [ObservableProperty]
    private ModOpState _operationState = ModOpState.Idle;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _primaryButtonText = UiStrings.ActionInstall;

    [ObservableProperty]
    private string _secondaryButtonText = UiStrings.ActionDisable;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>主按钮是否可用（Idle 且有可执行动作时）。</summary>
    [ObservableProperty]
    private bool _canPrimary = true;

    [ObservableProperty]
    private bool _canSecondary;

    /// <summary>是否为纯本地卡片（未收录到 TUF 目录）。</summary>
    [ObservableProperty]
    private bool _isLocalOnly;

    /// <summary>是否关联了远端目录条目。</summary>
    [ObservableProperty]
    private bool _isRemote;

    // ───────── 派生状态 ─────────

    public bool IsDownloading => OperationState is ModOpState.Downloading or ModOpState.Resolving or ModOpState.Extracting or ModOpState.Finishing or ModOpState.Queued;

    public bool ShowProgress => IsDownloading || OperationState == ModOpState.Error;

    public bool ProgressIndeterminate => IsDownloading && ProgressPercent <= 0;

    public void AttachLocal(LocalMod? local)
    {
        Local = local;
        OnPropertyChanged(nameof(Local));
        RefreshFrom(local);
    }

    /// <summary>根据本地状态重算徽章 / 按钮 / 版本文本。</summary>
    public void RefreshFrom(LocalMod? local)
    {
        Local = local ?? Local;

        var remoteVersion = ParseVersion(Remote?.EffectiveVersion);
        var localVersion = Local?.LocalVersion;

        IsLocalOnly = Remote is null;
        IsRemote = Remote is not null;
        Tags = new ObservableCollection<TufTag>(Remote?.Tags ?? new List<TufTag>());
        StatsText = Remote is null ? string.Empty : TextFormatting.FormatStats(Remote.DownloadCount, Remote.Likes);
        UpdatedText = Remote?.SourceUploadedAt is { } t ? $"更新于 {t.ToLocalTime():yyyy-MM-dd}" : string.Empty;

        if (Local?.Disabled == true)
        {
            StatusBadge = UiStrings.StatusDisabled;
            StatusBadgeColor = "#9E9E9E";
        }
        else if (Local is null)
        {
            StatusBadge = Remote is null ? UiStrings.StatusBroken : UiStrings.StatusNotInstalled;
            StatusBadgeColor = Remote is null ? "#C4482B" : "#707070";
        }
        else if (Remote is not null && remoteVersion is not null && localVersion is not null && remoteVersion > localVersion)
        {
            StatusBadge = UiStrings.StatusUpdateAvailable;
            StatusBadgeColor = "#E8862B";
        }
        else if (Remote is not null)
        {
            StatusBadge = UiStrings.StatusInstalled;
            StatusBadgeColor = "#3BA55C";
        }
        else
        {
            StatusBadge = UiStrings.StatusUnlisted;
            StatusBadgeColor = "#4B7BD1";
        }

        // 展示作者声明的原始版本号（"2.8.0" 而不是规范化的 "2.8"）
        var localVersionText = Local?.DeclaredVersion is { Length: > 0 } declared
            ? declared
            : localVersion?.ToString();

        VersionText = (Remote?.EffectiveVersion, localVersionText) switch
        {
            (null, null) => string.Empty,
            (null, { } lv) => $"本地 {lv}",
            ({ } rv, null) => $"最新 {rv}",
            ({ } rv, { } lv) when remoteVersion is not null && localVersion is not null && remoteVersion > localVersion
                => $"{lv} → {rv}",
            ({ } rv, { } lv) => $"本地 {lv}" + (remoteVersion is null ? $"（远端 {rv}）" : string.Empty),
        };

        // 按钮：优先级 禁用态 > 纯本地 > 未安装 > 已安装
        if (Local is not null && Local.Disabled)
        {
            PrimaryButtonText = UiStrings.ActionEnable;
            SecondaryButtonText = UiStrings.ActionUninstall;
            CanPrimary = true;
            CanSecondary = true;
        }
        else if (Remote is null)
        {
            // 未收录的本地 Mod：主按钮为打开目录，次按钮禁用/启用
            PrimaryButtonText = UiStrings.ActionOpenFolder;
            SecondaryButtonText = Local is not null ? UiStrings.ActionDisable : string.Empty;
            CanPrimary = true;
            CanSecondary = Local is not null;
        }
        else if (Local is null)
        {
            PrimaryButtonText = UiStrings.ActionInstall;
            SecondaryButtonText = string.Empty;
            CanPrimary = true;
            CanSecondary = false;
        }
        else
        {
            PrimaryButtonText = StatusBadge == UiStrings.StatusUpdateAvailable ? UiStrings.ActionUpdate : UiStrings.ActionReinstall;
            SecondaryButtonText = UiStrings.ActionDisable;
            CanPrimary = true;
            CanSecondary = true;
        }

        if (OperationState != ModOpState.Idle)
        {
            CanPrimary = false;
            CanSecondary = false;
        }
    }

    private static UmVersion? ParseVersion(string? text) =>
        UmVersion.TryParse(text, out var v) ? v : null;

    // ───────── 命令（委托给页面 ViewModel，便于统一并发控制与对话框） ─────────

    [RelayCommand]
    private Task PrimaryActionAsync() => _owner.RunPrimaryAsync(this);

    [RelayCommand]
    private Task SecondaryActionAsync() => _owner.RunSecondaryAsync(this);

    [RelayCommand]
    private Task OpenTufPageAsync()
    {
        if (Remote is not null)
        {
            Browser.Open(TufPageUrl);
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private void OpenProjectPage()
    {
        var url = Remote?.ProjectUrl ?? Local?.Info?.HomePage;
        if (!string.IsNullOrWhiteSpace(url))
        {
            Browser.Open(url);
        }
    }

    [RelayCommand]
    private void OpenLocalFolder() => ModsPageViewModel.OpenModFolder(this);

    [RelayCommand]
    private void CancelOperation() => ModsPageViewModel.CancelOperation(this);

    /// <summary>展示详情（由页面 ViewModel 触发 DetailsRequested 事件）。</summary>
    [RelayCommand]
    private void ShowDetails() => _owner.RequestDetails(this);

    // ───────── 操作状态机（由 ModsPageViewModel 调用） ─────────

    internal CancellationTokenSource BeginOperation()
    {
        _operationCts = new CancellationTokenSource();
        SetState(ModOpState.Queued, 0, string.Empty);
        return _operationCts;
    }

    internal void EndOperation()
    {
        _operationCts?.Dispose();
        _operationCts = null;
        SetState(ModOpState.Idle, 0, string.Empty);
    }

    internal void SetState(ModOpState state, double percent, string text)
    {
        OperationState = state;
        ProgressPercent = percent;
        ProgressText = text;
        IsBusy = state != ModOpState.Idle;
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(ShowProgress));
        OnPropertyChanged(nameof(ProgressIndeterminate));
        RefreshFrom(null);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>卡片状态变化（下载页订阅）。</summary>
    public event EventHandler? StateChanged;

    /// <summary>触发取消（内部 CTS）。</summary>
    internal void RequestCancel() => _operationCts?.Cancel();

    public void Dispose()
    {
        _operationCts?.Dispose();
        _operationCts = null;
        GC.SuppressFinalize(this);
    }
}
