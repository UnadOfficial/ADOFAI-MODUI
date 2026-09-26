using ADOFAI.ModManager.Core.Helpers;
using ADOFAI.ModManager.Core.Models.Local;
using ADOFAI.ModManager.Core.Models.Tuforums;
using ADOFAI.ModManager.Core.Models.Transfer;
using ADOFAI.ModManager.Core.Models;
using ADOFAI.ModManager.Core.Models.App;
using Microsoft.Extensions.Logging;

namespace ADOFAI.ModManager.Core.Services;

/// <summary>
/// Mod 生命周期门面：把"解析直链 → 下载 → 安装/更新 → 记录状态 → 重扫描"编排成单次调用，
/// 并统一处理游戏进程检查。供 ViewModel 直接使用。
/// </summary>
public interface IModLifecycleService
{
    /// <summary>安装远端 Mod 的最新版本（已安装且非降级时自动转为更新路径）。</summary>
    Task<OperationResult> InstallLatestAsync(TufMod remote, bool forceWhenGameRunning = false,
        IProgress<InstallProgress>? progress = null, bool forceReinstall = false, CancellationToken ct = default);

    /// <summary>更新已安装的本地 Mod 到远端最新版本。</summary>
    Task<OperationResult> UpdateAsync(LocalMod local, TufMod remote, bool forceWhenGameRunning = false,
        IProgress<InstallProgress>? progress = null, CancellationToken ct = default);

    /// <summary>卸载本地 Mod。</summary>
    Task<OperationResult> UninstallAsync(LocalMod local, bool forceWhenGameRunning = false,
        CancellationToken ct = default);

    /// <summary>切换启用/禁用。</summary>
    Task<OperationResult> ToggleEnabledAsync(LocalMod local, CancellationToken ct = default);

    /// <summary>导入本地 zip 安装（跳过下载阶段）。</summary>
    Task<OperationResult> InstallZipAsync(string zipPath, IProgress<InstallProgress>? progress = null,
        CancellationToken ct = default);
}

public sealed class ModLifecycleService : IModLifecycleService
{
    private readonly IModDownloadService _downloads;
    private readonly IModInstallerService _installer;
    private readonly ILocalModService _localMods;
    private readonly IDownloadUrlResolver _resolver;
    private readonly IGameProcessGuard _gameGuard;
    private readonly ISettingsService _settings;
    private readonly ILogger<ModLifecycleService> _logger;

    public ModLifecycleService(
        IModDownloadService downloads,
        IModInstallerService installer,
        ILocalModService localMods,
        IDownloadUrlResolver resolver,
        IGameProcessGuard gameGuard,
        ISettingsService settings,
        ILogger<ModLifecycleService> logger)
    {
        _downloads = downloads;
        _installer = installer;
        _localMods = localMods;
        _resolver = resolver;
        _gameGuard = gameGuard;
        _settings = settings;
        _logger = logger;
    }

    public async Task<OperationResult> InstallLatestAsync(
        TufMod remote, bool forceWhenGameRunning = false,
        IProgress<InstallProgress>? progress = null, bool forceReinstall = false, CancellationToken ct = default)
    {
        try
        {
            // 已安装的相同/更高版本且未要求强制重装 → 无需安装
            var snapshot = await _localMods.ScanAsync(ct).ConfigureAwait(false);
            var local = FindLinked(snapshot, remote);

            if (!forceReinstall && local?.Info is not null && !local.Disabled)
            {
                var remoteVersion = ParseVersion(remote.EffectiveVersion);
                var localVersion = local.LocalVersion;
                if (localVersion is not null && remoteVersion is not null && localVersion >= remoteVersion)
                {
                    return OperationResult.Ok($"《{remote.Name}》已安装（{localVersion}），无需重复安装。");
                }
            }

            return await InstallCoreAsync(remote, local, forceWhenGameRunning, progress, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Warn("操作已取消。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "安装 {Mod} 失败", remote.Name);
            return OperationResult.Error($"安装《{remote.Name}》失败：{ex.Message}", ex);
        }
    }

    public async Task<OperationResult> UpdateAsync(
        LocalMod local, TufMod remote, bool forceWhenGameRunning = false,
        IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
        => await GuardedAsync($"更新《{remote.Name}》", forceWhenGameRunning, async () =>
        {
            // 更新会覆盖 DLL：即使允许强制执行也先确保进程检查已完成
            return await InstallCoreAsync(remote, local, forceWhenGameRunning, progress, ct).ConfigureAwait(false);
        }).ConfigureAwait(false);

    public async Task<OperationResult> UninstallAsync(
        LocalMod local, bool forceWhenGameRunning = false, CancellationToken ct = default)
        => await GuardedAsync($"卸载《{local.DisplayName}》", forceWhenGameRunning, async () =>
        {
            var trash = await _localMods.UninstallAsync(local.DirectoryPath, ct).ConfigureAwait(false);
            ForgetInstallRecord(local);
            await _settings.SaveAsync(notifySettingsChanged: false, ct).ConfigureAwait(false);
            return OperationResult.Ok($"已卸载《{local.DisplayName}》。\n（备份位置：{trash}）");
        }).ConfigureAwait(false);

    public async Task<OperationResult> ToggleEnabledAsync(LocalMod local, CancellationToken ct = default)
    {
        try
        {
            var snapshot = await _localMods.ToggleEnabledAsync(local.DirectoryPath, ct).ConfigureAwait(false);
            var name = local.DisplayName;
            return OperationResult.Ok(local.Disabled ? $"已启用《{name}》。" : $"已禁用《{name}》。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换启用状态失败：{Dir}", local.DirectoryPath);
            return OperationResult.Error($"切换状态失败：{ex.Message}", ex);
        }
    }

    public async Task<OperationResult> InstallZipAsync(
        string zipPath, IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
    {
        try
        {
            var outcome = await _installer.InstallFromZipAsync(zipPath, progress, ct).ConfigureAwait(false);
            await RecordInstallAsync(slug: null, outcome, ct).ConfigureAwait(false);
            return OperationResult.Ok($"已安装《{outcome.Info.Id}》到 {outcome.ModDirectory}");
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Warn("操作已取消。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "本地导入安装失败：{Zip}", zipPath);
            return OperationResult.Error($"导入安装失败：{ex.Message}", ex);
        }
    }

    // ─────────────────────────── 内部实现 ───────────────────────────

    private async Task<OperationResult> GuardedAsync(
        string operationName, bool forceWhenGameRunning, Func<Task<OperationResult>> action)
    {
        if (_settings.State.Settings.CheckGameProcess && _gameGuard.IsGameRunning() && !forceWhenGameRunning)
        {
            return OperationResult.Warn(
                $"检测到《冰与火之舞》正在运行。\n{operationName}可能导致 DLL 文件占用错误，请先退出游戏。",
                payload: "GameRunning");
        }

        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Warn("操作已取消。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Operation} 失败", operationName);
            return OperationResult.Error($"{operationName}失败：{ex.Message}", ex);
        }
    }

    private async Task<OperationResult> InstallCoreAsync(
        TufMod remote, LocalMod? local, bool forceWhenGameRunning,
        IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        // 游戏运行检查（安装新 Mod 允许覆盖继续；更新同理）
        if (_settings.State.Settings.CheckGameProcess && _gameGuard.IsGameRunning() && !forceWhenGameRunning)
        {
            return OperationResult.Warn(
                $"检测到《冰与火之舞》正在运行。\n安装/更新《{remote.Name}》可能因 DLL 文件占用而失败，请先退出游戏。",
                payload: "GameRunning");
        }

        // 1) 解析直链
        progress?.Report(new InstallProgress(InstallPhase.ResolvingDownloadUrl, 2));
        var resolved = await _resolver.ResolveAsync(remote.DownloadUrl, ct).ConfigureAwait(false);
        if (!resolved.IsDirectUrl)
        {
            return OperationResult.Warn(resolved.Note ?? "需要浏览器手动下载。", resolved.FallbackUrl);
        }

        // 2) 下载
        progress?.Report(new InstallProgress(InstallPhase.Downloading, 5));
        var fileName = $"{remote.Slug}-{Sanitize(remote.EffectiveVersion)}.zip";
        var zipPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ADOFAI.ModManager", "cache", "downloads", fileName);

        await _downloads.DownloadAsync(new DownloadRequest(resolved.DirectUrl, zipPath, remote.Name), ToDownloadProgress(progress), ct)
            .ConfigureAwait(false);

        // 3) 安装
        var outcome = await _installer.InstallFromZipAsync(zipPath, progress, ct).ConfigureAwait(false);

        // 4) 记录状态（建立本地↔远端关联）
        await RecordInstallAsync(remote.Slug, outcome, ct).ConfigureAwait(false);

        var preservedNote = outcome.PreservedFiles.Count > 0
            ? $"\n已保留 {outcome.PreservedFiles.Count} 个用户配置文件。"
            : string.Empty;

        return OperationResult.Ok($"《{remote.Name}》{remote.EffectiveVersion} 安装完成。{preservedNote}");
    }

    private static Progress<DownloadProgress>? ToDownloadProgress(IProgress<InstallProgress>? progress)
    {
        if (progress is null)
        {
            return null;
        }

        // 下载阶段占用 5%..70% 的进度区间
        return new Progress<DownloadProgress>(p =>
        {
            var percent = p.Percent is { } pct ? 5 + pct * 0.65 : 20;
            progress.Report(new InstallProgress(
                InstallPhase.Downloading,
                percent,
                FormatSpeed(p.SpeedBytesPerSecond)));
        });
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return name;
    }

    private static string FormatSpeed(double bytesPerSecond) =>
        bytesPerSecond <= 0 ? string.Empty :
        bytesPerSecond > 1024 * 1024 ? $"{bytesPerSecond / 1024 / 1024:F1} MB/s" :
        $"{bytesPerSecond / 1024:F0} KB/s";

    private static LocalMod? FindLinked(LocalModsSnapshot snapshot, TufMod remote)
        => snapshot.Mods.FirstOrDefault(m => ModMatching.Matches(m, remote));

    private static UmVersion? ParseVersion(string? text)
        => UmVersion.TryParse(text, out var v) ? v : null;

    private async Task RecordInstallAsync(string? slug, InstallOutcome outcome, CancellationToken ct)
    {
        var state = _settings.State;
        if (!string.IsNullOrEmpty(slug))
        {
            if (!state.Links.Any(l => string.Equals(l.Slug, slug, StringComparison.OrdinalIgnoreCase)))
            {
                state.Links.Add(new ModLinkRecord { Slug = slug, LocalId = outcome.ModId });
            }

            state.Installs.RemoveAll(i => string.Equals(i.Slug, slug, StringComparison.OrdinalIgnoreCase));
            state.Installs.Add(new InstallRecord
            {
                Slug = slug,
                LocalId = outcome.ModId,
                Version = outcome.Info.Version ?? string.Empty,
                InstalledAt = DateTimeOffset.Now,
            });
        }

        await _settings.SaveAsync(notifySettingsChanged: false, ct).ConfigureAwait(false);
    }

    private void ForgetInstallRecord(LocalMod local)
    {
        var state = _settings.State;
        state.Installs.RemoveAll(i => string.Equals(i.LocalId, local.DisplayId, StringComparison.OrdinalIgnoreCase));
        state.Links.RemoveAll(l => string.Equals(l.LocalId, local.DisplayId, StringComparison.OrdinalIgnoreCase));
    }
}
