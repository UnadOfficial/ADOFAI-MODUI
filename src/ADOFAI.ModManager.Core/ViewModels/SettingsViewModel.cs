using System.Diagnostics;
using ADOFAI.ModManager.Core.Helpers;
using ADOFAI.ModManager.Core.Models.Game;
using ADOFAI.ModManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ADOFAI.ModManager.Core.Models;

namespace ADOFAI.ModManager.Core.ViewModels;

/// <summary>设置页 ViewModel：游戏目录定位/校验、缓存清理、并发设置。</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISteamLocatorService _steam;
    private readonly ISettingsService _settings;
    private readonly IDiskCacheService _cache;
    private readonly IDialogService _dialogs;
    private readonly IFilePickerService _picker;
    private readonly IGameProcessGuard _gameGuard;

    public SettingsViewModel(
        ISteamLocatorService steam,
        ISettingsService settings,
        IDiskCacheService cache,
        IDialogService dialogs,
        IFilePickerService picker,
        IGameProcessGuard gameGuard)
    {
        _steam = steam;
        _settings = settings;
        _cache = cache;
        _dialogs = dialogs;
        _picker = picker;
        _gameGuard = gameGuard;

        var s = settings.State.Settings;
        _gameRoot = s.GameRoot;
        _checkGameProcess = s.CheckGameProcess;
        _maxParallelDownloads = s.MaxParallelDownloads;
        _gamePathStatus = string.IsNullOrEmpty(s.GameRoot) ? "尚未设置游戏目录" : "已设置";
    }

    [ObservableProperty]
    private string _gameRoot;

    [ObservableProperty]
    private string _gamePathStatus;

    [ObservableProperty]
    private bool _isLocating;

    [ObservableProperty]
    private bool _checkGameProcess;

    /// <summary>NumberBox 绑定用（值域 1-6；保存时取整）。</summary>
    [ObservableProperty]
    private double _maxParallelDownloads;

    [ObservableProperty]
    private string? _notice;

    partial void OnCheckGameProcessChanged(bool value) => Persist();

    partial void OnMaxParallelDownloadsChanged(double value) => Persist();

    private void Persist()
    {
        var s = _settings.State.Settings;
        s.CheckGameProcess = CheckGameProcess;
        s.MaxParallelDownloads = Math.Clamp((int)Math.Round(MaxParallelDownloads), 1, 6);
        _ = _settings.SaveAsync();
    }

    /// <summary>自动定位（注册表 + libraryfolders.vdf）。</summary>
    [RelayCommand]
    public async Task LocateAsync()
    {
        IsLocating = true;
        try
        {
            var result = await _steam.LocateAsync().ConfigureAwait(true);
            Apply(result);
            if (!result.Found)
            {
                Notice = result.FailureReason;
            }
        }
        finally
        {
            IsLocating = false;
        }
    }

    /// <summary>手动选择目录（由页面调用文件选择器后传入路径）。</summary>
    public async Task PickFolderAsync()
    {
        var folder = await _picker.PickFolderAsync().ConfigureAwait(true);
        if (folder is null)
        {
            return;
        }

        var result = await _steam.ValidateAsync(folder).ConfigureAwait(true);
        Apply(result);
        if (!result.Found)
        {
            Notice = result.FailureReason ?? "所选目录不是有效的游戏根目录。";
        }
    }

    [RelayCommand]
    public async Task ValidateCurrentAsync()
    {
        var result = await _steam.ValidateAsync(GameRoot).ConfigureAwait(true);
        Apply(result);
    }

    [RelayCommand]
    public async Task ClearCacheAsync()
    {
        await _cache.ClearAsync().ConfigureAwait(true);
        Notice = "缓存已清理。";
    }

    [RelayCommand]
    public void OpenGameFolder()
    {
        if (!string.IsNullOrEmpty(GameRoot) && Directory.Exists(GameRoot))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{GameRoot}\"") { UseShellExecute = true });
        }
    }

    [RelayCommand]
    public void CheckGameRunning()
    {
        var running = _gameGuard.IsGameRunning();
        Notice = running ? "检测到游戏正在运行。" : "游戏未运行。";
    }

    private void Apply(GameLocatorResult result)
    {
        if (result.Found)
        {
            GameRoot = result.GameRoot;
            GamePathStatus = result.Source switch
            {
                GameSource.Registry => $"已定位（注册表）：{result.GameRoot}",
                GameSource.LibraryVdf => $"已定位（Steam 库）：{result.GameRoot}",
                GameSource.Manual => $"校验通过：{result.GameRoot}",
                _ => $"已定位：{result.GameRoot}",
            };

            var s = _settings.State.Settings;
            s.GameRoot = result.GameRoot;
            s.GameSource = result.Source;
            _ = _settings.SaveAsync();
            Notice = null;
        }
        else
        {
            GamePathStatus = "未找到有效游戏目录";
        }
    }
}
