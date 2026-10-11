using System.Collections.ObjectModel;
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
    private readonly IGitHubMirrorService _mirrorService;

    public SettingsViewModel(
        ISteamLocatorService steam,
        ISettingsService settings,
        IDiskCacheService cache,
        IDialogService dialogs,
        IFilePickerService picker,
        IGameProcessGuard gameGuard,
        IGitHubMirrorService mirrorService)
    {
        _steam = steam;
        _settings = settings;
        _cache = cache;
        _dialogs = dialogs;
        _picker = picker;
        _gameGuard = gameGuard;
        _mirrorService = mirrorService;

        var s = settings.State.Settings;
        _gameRoot = s.GameRoot;
        _checkGameProcess = s.CheckGameProcess;
        _maxParallelDownloads = s.MaxParallelDownloads;
        _gamePathStatus = string.IsNullOrEmpty(s.GameRoot) ? "尚未设置游戏目录" : "已设置";
        _selectedTheme = s.Theme switch
        {
            "Light" => "浅色模式",
            "Dark" => "深色模式",
            _ => "跟随系统",
        };
        _selectedTranslationProvider = s.TranslationProvider switch
        {
            "Bing" => "微软必应翻译",
            "Google" => "谷歌翻译",
            _ => "自动",
        };

        foreach (var option in _mirrorService.Presets)
        {
            GitHubMirrorOptions.Add(option);
        }

        _customGitHubMirrorUrl = !string.IsNullOrWhiteSpace(s.CustomGitHubMirror)
            ? s.CustomGitHubMirror
            : (!string.IsNullOrWhiteSpace(s.GitHubMirror) && !GitHubMirrorOptions.Any(o => !o.IsCustom && string.Equals(o.Url, s.GitHubMirror, StringComparison.OrdinalIgnoreCase))
                ? s.GitHubMirror
                : "https://ghproxy.net/");

        if (string.IsNullOrWhiteSpace(s.GitHubMirror))
        {
            _selectedGitHubMirrorOption = GitHubMirrorOptions.FirstOrDefault(o => o.IsDirect) ?? GitHubMirrorOptions[0];
            _isCustomMirror = false;
        }
        else
        {
            var matched = GitHubMirrorOptions.FirstOrDefault(o => !o.IsCustom && string.Equals(o.Url, s.GitHubMirror, StringComparison.OrdinalIgnoreCase));
            if (matched is not null)
            {
                _selectedGitHubMirrorOption = matched;
                _isCustomMirror = false;
            }
            else
            {
                _selectedGitHubMirrorOption = GitHubMirrorOptions.FirstOrDefault(o => o.IsCustom) ?? GitHubMirrorOptions[^1];
                _isCustomMirror = true;
                _customGitHubMirrorUrl = s.GitHubMirror;
            }
        }
    }

    public SettingsViewModel(
        ISteamLocatorService steam,
        ISettingsService settings,
        IDiskCacheService cache,
        IDialogService dialogs,
        IFilePickerService picker,
        IGameProcessGuard gameGuard)
        : this(steam, settings, cache, dialogs, picker, gameGuard, new GitHubMirrorService(settings, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<GitHubMirrorService>.Instance))
    {
    }

    public ObservableCollection<string> ThemeOptions { get; } = new()
    {
        "跟随系统",
        "浅色模式",
        "深色模式"
    };

    [ObservableProperty]
    private string _selectedTheme;

    partial void OnSelectedThemeChanged(string value)
    {
        var themeKey = value switch
        {
            "浅色模式" => "Light",
            "深色模式" => "Dark",
            _ => "Default",
        };
        _settings.State.Settings.Theme = themeKey;
        _ = _settings.SaveAsync();
        ThemeChanged?.Invoke(themeKey);
    }

    public static event Action<string>? ThemeChanged;

    public ObservableCollection<string> TranslationProviderOptions { get; } = new()
    {
        "自动",
        "微软必应翻译",
        "谷歌翻译"
    };

    [ObservableProperty]
    private string _selectedTranslationProvider;

    partial void OnSelectedTranslationProviderChanged(string value)
    {
        var providerKey = value switch
        {
            "微软必应翻译（Bing）" => "Bing",
            "谷歌翻译（Google）" => "Google",
            _ => "Auto",
        };
        _settings.State.Settings.TranslationProvider = providerKey;
        _ = _settings.SaveAsync();
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

    public ObservableCollection<GitHubMirrorOption> GitHubMirrorOptions { get; } = new();

    [ObservableProperty]
    private GitHubMirrorOption _selectedGitHubMirrorOption = null!;

    [ObservableProperty]
    private string _customGitHubMirrorUrl = string.Empty;

    [ObservableProperty]
    private bool _isCustomMirror;

    [ObservableProperty]
    private bool _isTestingMirror;

    [ObservableProperty]
    private string? _mirrorTestStatus;

    [ObservableProperty]
    private string _mirrorTestStatusColor = "#707070";

    partial void OnSelectedGitHubMirrorOptionChanged(GitHubMirrorOption value)
    {
        if (value is null)
        {
            return;
        }

        MirrorTestStatus = null;
        if (value.IsCustom)
        {
            IsCustomMirror = true;
            var custom = CustomGitHubMirrorUrl?.Trim() ?? string.Empty;
            _settings.State.Settings.GitHubMirror = custom;
            _ = _settings.SaveAsync();
        }
        else
        {
            IsCustomMirror = false;
            _settings.State.Settings.GitHubMirror = value.Url ?? string.Empty;
            _ = _settings.SaveAsync();
        }
    }

    partial void OnCustomGitHubMirrorUrlChanged(string value)
    {
        if (IsCustomMirror)
        {
            var trimmed = value?.Trim() ?? string.Empty;
            _settings.State.Settings.CustomGitHubMirror = trimmed;
            _settings.State.Settings.GitHubMirror = trimmed;
            _ = _settings.SaveAsync();
        }
    }

    [RelayCommand]
    public async Task TestMirrorAsync()
    {
        if (IsTestingMirror)
        {
            return;
        }

        IsTestingMirror = true;
        MirrorTestStatus = "正在测试连接…";
        MirrorTestStatusColor = "#707070";

        try
        {
            var target = IsCustomMirror
                ? CustomGitHubMirrorUrl?.Trim()
                : SelectedGitHubMirrorOption?.Url;

            var result = await _mirrorService.TestLatencyAsync(target).ConfigureAwait(true);
            MirrorTestStatus = result.Message;
            if (result.Success)
            {
                MirrorTestStatusColor = result.LatencyMs > 1500 ? "#E8862B" : "#3BA55C";
            }
            else
            {
                MirrorTestStatusColor = "#C4482B";
            }
        }
        finally
        {
            IsTestingMirror = false;
        }
    }

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

    [RelayCommand]
    public void OpenDataFolder()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ADOFAI.ModManager");
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    public void OpenLogsFolder()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ADOFAI.ModManager", "logs");
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    public void OpenTrashFolder()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ADOFAI.ModManager", "trash");
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    public void OpenGitHub() => Browser.Open("https://github.com/UnadOfficial/ADOFAI-MODUI");

    [RelayCommand]
    public void OpenTuforums() => Browser.Open("https://tuforums.com/mods");

    public string AppVersion => "1.0.0";
}
