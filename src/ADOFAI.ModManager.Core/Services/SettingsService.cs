using ADOFAI.ModManager.Core.Helpers;
using ADOFAI.ModManager.Core.Models.App;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ADOFAI.ModManager.Core.Services;

/// <summary>应用状态（设置 + 关联映射 + 安装历史）的持久化。</summary>
public interface ISettingsService
{
    /// <summary>当前状态（启动时加载，修改后调用 Save）。</summary>
    AppState State { get; }

    /// <summary>加载（不存在文件时返回默认值）。</summary>
    Task LoadAsync(CancellationToken ct = default);

    Task SaveAsync(CancellationToken ct = default);

    /// <summary>数据目录（%LOCALAPPDATA%\ADOFAI.ModManager）。</summary>
    string DataDirectory { get; }
}

/// <summary>设置持久化实现：写临时文件再原子替换，损坏时自动回退默认值。</summary>
public sealed class SettingsService : ISettingsService, IDisposable
{
    private readonly ILogger<SettingsService> _logger;
    private readonly string _stateFilePath;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
        DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ADOFAI.ModManager");
        _stateFilePath = Path.Combine(DataDirectory, "app-state.json");
        State = new AppState();
    }

    public string DataDirectory { get; }

    public void Dispose()
    {
        _ioLock.Dispose();
        GC.SuppressFinalize(this);
    }

    public AppState State { get; private set; }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                State = new AppState();
                return;
            }

            await _ioLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await using var stream = File.OpenRead(_stateFilePath);
                var loaded = await JsonSerializer.DeserializeAsync<AppState>(stream, JsonOptions.State, ct)
                    .ConfigureAwait(false);
                State = loaded ?? new AppState();
                State.Settings ??= new AppSettings();
                State.Links ??= new List<ModLinkRecord>();
                State.Installs ??= new List<InstallRecord>();
            }
            finally
            {
                _ioLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "状态文件读取失败，回退默认设置：{File}", _stateFilePath);
            State = new AppState();
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(DataDirectory);
        var tmp = _stateFilePath + ".tmp";

        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, State, JsonOptions.State, ct).ConfigureAwait(false);
            }

            File.Move(tmp, _stateFilePath, overwrite: true);
        }
        finally
        {
            _ioLock.Release();
        }
    }
}
