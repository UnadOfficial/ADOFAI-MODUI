using System.IO.Compression;
using ADOFAI.ModManager.Core.Helpers;
using ADOFAI.ModManager.Core.Models.Local;
using ADOFAI.ModManager.Core.Models.Transfer;
using ADOFAI.ModManager.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ADOFAI.ModManager.Core.Services;

/// <summary>安装进度通知。</summary>
public sealed record InstallProgress(InstallPhase Phase, double Percent, string? Detail = null);

/// <summary>安装结果。</summary>
public sealed record InstallOutcome(string ModId, string ModDirectory, ModInfoJson Info, IReadOnlyList<string> PreservedFiles);

/// <summary>
/// Mod 安装器：负责把下载好的 zip 安全地安装到 Mods/ 目录。
/// 流程：Zip-Slip 校验 → 嵌套剥层 → 解压到 staging → 校验 Info.json → 旧目录备份 →
/// 回拷用户配置（新包中不存在的）→ 原子替换 → 失败自动回滚。
/// </summary>
public interface IModInstallerService
{
    Task<InstallOutcome> InstallFromZipAsync(string zipFilePath, IProgress<InstallProgress>? progress = null, CancellationToken ct = default);
}

public sealed class ModInstallerService : IModInstallerService
{
    private readonly ILocalModService _localMods;
    private readonly ILogger<ModInstallerService> _logger;

    public ModInstallerService(ILocalModService localMods, ILogger<ModInstallerService> logger)
    {
        _localMods = localMods;
        _logger = logger;
    }

    public async Task<InstallOutcome> InstallFromZipAsync(
        string zipFilePath, IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
    {
        if (!File.Exists(zipFilePath))
        {
            throw new FileNotFoundException("找不到安装包文件。", zipFilePath);
        }

        var modsDir = _localMods.GetModsDirectory();
        if (string.IsNullOrEmpty(modsDir))
        {
            throw new InvalidOperationException("尚未设置游戏目录，无法确定 Mods 目录。");
        }

        progress?.Report(new InstallProgress(InstallPhase.Preparing, 0));
        Directory.CreateDirectory(modsDir);

        var stagingRoot = Path.Combine(modsDir, ".modui_staging");
        Directory.CreateDirectory(stagingRoot);
        var staging = Path.Combine(stagingRoot, "install-" + Guid.NewGuid().ToString("N")[..8]);

        string? targetDir = null;      // 目标目录（Move 之后由回滚逻辑使用）
        string? backupDir = null;      // 旧目录备份
        var movedOld = false;

        try
        {
            // ── 读取 zip 条目并做安全校验 + 嵌套剥层计算 ──
            progress?.Report(new InstallProgress(InstallPhase.Extracting, 5));
            var fileEntries = new List<string>();
            using (var inspect = ZipFile.OpenRead(zipFilePath))
            {
                foreach (var entry in inspect.Entries)
                {
                    if (entry.FullName.EndsWith('/'))
                    {
                        continue; // 纯目录条目
                    }

                    fileEntries.Add(PathGuard.GetSafeRelativePath(entry.FullName));
                }
            }

            if (fileEntries.Count == 0)
            {
                throw new InvalidDataException("压缩包为空或不含有效文件。");
            }

            var stripDepth = ZipNesting.ComputeStripDepth(fileEntries);
            _logger.LogInformation("安装包条目 {Count} 个，剥层深度 {Depth}", fileEntries.Count, stripDepth);

            // ── 解压到 staging ──
            await Task.Run(() => ExtractZip(zipFilePath, staging, stripDepth), ct).ConfigureAwait(false);
            progress?.Report(new InstallProgress(InstallPhase.Extracting, 70));

            // ── 校验 staging 中的 Info.json ──
            var infoPath = new[]
            {
                Path.Combine(staging, "Info.json"),
                Path.Combine(staging, "info.json"),
            }.FirstOrDefault(File.Exists) ?? throw new InvalidDataException(
                "解压后未在根目录找到 Info.json（已自动剥除嵌套目录；若仍失败请检查压缩包结构）。");

            var info = JsonSerializer.Deserialize<ModInfoJson>(
                await File.ReadAllTextAsync(infoPath, ct).ConfigureAwait(false), JsonOptions.LocalInfo)
                ?? throw new InvalidDataException("Info.json 解析失败。");

            if (string.IsNullOrWhiteSpace(info.Id))
            {
                throw new InvalidDataException("Info.json 缺少 Id 字段，UMM 无法加载该 Mod。");
            }

            // ── 确定目标目录：已装同 Id Mod → 原目录；否则 Mods/{Id} ──
            var existing = (await _localMods.ScanAsync(ct).ConfigureAwait(false)).Mods
                .FirstOrDefault(m => m.Info is not null &&
                    string.Equals(m.Info.Id, info.Id, StringComparison.OrdinalIgnoreCase));

            targetDir = existing?.DirectoryPath ?? Path.Combine(modsDir, SanitizeFolderName(info.Id));

            // ── 备份旧目录 ──
            if (Directory.Exists(targetDir))
            {
                backupDir = staging + ".old";
                Directory.Move(targetDir, backupDir);
                movedOld = true;
            }

            // ── staging → 目标目录（原子替换）──
            progress?.Report(new InstallProgress(InstallPhase.Swapping, 80));
            Directory.Move(staging, targetDir);

            // ── 回拷用户配置 ──
            progress?.Report(new InstallProgress(InstallPhase.PreservingConfigs, 90));
            IReadOnlyList<string> preserved = Array.Empty<string>();
            if (movedOld && backupDir is not null)
            {
                preserved = ConfigPreservation.PreserveConfigs(backupDir, targetDir);
                if (preserved.Count > 0)
                {
                    _logger.LogInformation("已保留 {Count} 个用户配置文件：{Files}",
                        preserved.Count, string.Join(", ", preserved));
                }
            }

            // ── 清理备份 ──
            if (movedOld && backupDir is not null)
            {
                TryDeleteDirectory(backupDir);
                backupDir = null;
            }

            progress?.Report(new InstallProgress(InstallPhase.Done, 100));
            return new InstallOutcome(info.Id, targetDir, info, preserved);
        }
        catch (Exception)
        {
            // ── 回滚：尽力恢复旧目录、清空 staging ──
            try
            {
                if (targetDir is not null && Directory.Exists(targetDir) && movedOld)
                {
                    // 新目录已就位但后续步骤失败 → 删除新目录并还原备份
                    TryDeleteDirectory(targetDir);
                }

                if (movedOld && backupDir is not null && Directory.Exists(backupDir) && targetDir is not null)
                {
                    Directory.Move(backupDir, targetDir);
                }
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(rollbackEx, "回滚失败，请检查 Mods 目录与 .modui_staging 目录");
            }

            TryDeleteDirectory(staging);
            throw;
        }
        finally
        {
            // 安装成功后 staging 子目录已被 Move 走，清理遗留的空根目录
            // （若回滚失败留下了 .old 备份则不删，便于人工恢复）
            TryDeleteEmptyDirectory(stagingRoot);
        }
    }

    /// <summary>仅在目录存在且为空时删除（用于清理 staging 根目录）。</summary>
    private void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path, recursive: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "清理空目录失败：{Path}", path);
        }
    }

    /// <summary>带剥层的解压（全部条目先经 PathGuard 校验）。</summary>
    internal static void ExtractZip(string zipFilePath, string destRoot, int stripDepth)
    {
        using var archive = ZipFile.OpenRead(zipFilePath);
        foreach (var entry in archive.Entries)
        {
            var fullName = entry.FullName;
            if (string.IsNullOrWhiteSpace(fullName) || fullName.EndsWith('/'))
            {
                continue; // 目录条目按需建目录
            }

            var relative = PathGuard.GetSafeRelativePath(fullName);
            var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length <= stripDepth)
            {
                continue; // 剥层后为空（目录条目）
            }

            var joined = string.Join(Path.DirectorySeparatorChar, segments.Skip(stripDepth));
            var target = Path.Combine(destRoot, joined);

            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            entry.ExtractToFile(target, overwrite: true);
        }
    }

    internal static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (cleaned.Length == 0)
        {
            cleaned = "UnknownMod";
        }

        return cleaned;
    }

    private void TryDeleteDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "清理目录失败（文件可能被占用）：{Dir}", directory);
        }
    }
}
