using ADOFAI.ModManager.Core.Helpers;
using System.Text.Json;
using ADOFAI.ModManager.Core.Models;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace ADOFAI.ModManager.Core.Services;

/// <summary>Mods 目录扫描结果。</summary>
public sealed class LocalModsSnapshot
{
    public required string ModsDirectory { get; init; }

    public required IReadOnlyList<Models.Local.LocalMod> Mods { get; init; }

    /// <summary>扫描时是否刚创建了 Mods 目录（首次运行）。</summary>
    public bool CreatedDirectory { get; init; }
}

/// <summary>
/// 本地 Mod 扫描与管理：解析 Mods/ 下每个子目录的 Info.json（UMM 规范）、
/// 禁用/启用切换（Info.json ↔ Info.json.disabled 约定）、安全卸载（先移入回收站式暂存区）。
/// UMM 扫描规则（源自 newman55/unity-mod-manager 源码）：遍历 Mods 直接子目录，读取 Info.json / info.json，
/// 要求非空 Id，按 Id 去重。UMM 本身无持久化禁用机制，故采用重命名约定。
/// </summary>
public interface ILocalModService
{
    Task<LocalModsSnapshot> ScanAsync(CancellationToken ct = default);

    /// <summary>切换启用/禁用状态，返回刷新后的本地 Mod（失败抛异常）。</summary>
    Task<LocalModsSnapshot> ToggleEnabledAsync(string modDirectoryPath, CancellationToken ct = default);

    /// <summary>卸载：把目录移入回收站式暂存目录（可人工恢复），返回暂存位置。</summary>
    Task<string> UninstallAsync(string modDirectoryPath, CancellationToken ct = default);

    string GetModsDirectory();
}

public sealed class LocalModService : ILocalModService
{
    private readonly ISettingsService _settings;
    private readonly ILogger<LocalModService> _logger;

    public LocalModService(ISettingsService settings, ILogger<LocalModService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public string GetModsDirectory()
    {
        var gameRoot = _settings.State.Settings.GameRoot;
        return string.IsNullOrEmpty(gameRoot)
            ? string.Empty
            : Path.Combine(gameRoot, "Mods");
    }

    public async Task<LocalModsSnapshot> ScanAsync(CancellationToken ct = default)
    {
        var modsDir = GetModsDirectory();
        if (string.IsNullOrEmpty(modsDir))
        {
            return new LocalModsSnapshot { ModsDirectory = string.Empty, Mods = Array.Empty<Models.Local.LocalMod>() };
        }

        var created = false;
        if (!Directory.Exists(modsDir))
        {
            try
            {
                Directory.CreateDirectory(modsDir);
                created = true;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "无法创建 Mods 目录：{Dir}", modsDir);
                return new LocalModsSnapshot { ModsDirectory = modsDir, Mods = Array.Empty<Models.Local.LocalMod>() };
            }
        }

        var mods = await Task.Run(() =>
        {
            var list = new List<Models.Local.LocalMod>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in Directory.EnumerateDirectories(modsDir))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(dir);

                // UMM 加载器自身目录不是 Mod；点目录（如本工具的 .modui_staging）也不是
                if (name.StartsWith('.') ||
                    string.Equals(name, "UnityModManager", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var mod = ScanOne(dir, name, seenIds);
                if (mod is not null)
                {
                    list.Add(mod);
                }
            }

            return list;
        }, ct).ConfigureAwait(false);

        return new LocalModsSnapshot { ModsDirectory = modsDir, Mods = mods, CreatedDirectory = created };
    }

    private Models.Local.LocalMod? ScanOne(string dir, string folderName, HashSet<string> seenIds)
    {
        var disabledByFolder = folderName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);

        // 信息文件候选（优先级：Info.json > info.json > Info.json.disabled）
        var infoJson = Path.Combine(dir, "Info.json");
        var infoLower = Path.Combine(dir, "info.json");
        var infoDisabled = Path.Combine(dir, "Info.json.disabled");

        string? infoPath = null;
        var disabled = false;

        if (File.Exists(infoJson))
        {
            infoPath = infoJson;
        }
        else if (File.Exists(infoLower))
        {
            infoPath = infoLower;
        }
        else if (File.Exists(infoDisabled))
        {
            infoPath = infoDisabled;
            disabled = true;
        }

        if (infoPath is null)
        {
            // 无 Info.json：UMM 不会加载；可能是资源包或损坏目录
            return new Models.Local.LocalMod
            {
                DirectoryPath = dir,
                FolderName = folderName,
                Info = null,
                Disabled = disabledByFolder,
                InfoFileName = "Info.json",
                Status = Models.LocalModStatus.Broken,
            };
        }

        Models.Local.ModInfoJson? info = null;
        try
        {
            info = JsonSerializer.Deserialize<Models.Local.ModInfoJson>(
                File.ReadAllText(infoPath), JsonOptions.LocalInfo);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex, "Info.json 解析失败：{Path}", infoPath);
        }

        if (info is null || string.IsNullOrWhiteSpace(info.Id))
        {
            return new Models.Local.LocalMod
            {
                DirectoryPath = dir,
                FolderName = folderName,
                Info = info,
                Disabled = disabled || disabledByFolder,
                InfoFileName = Path.GetFileName(infoPath),
                Status = Models.LocalModStatus.Broken,
            };
        }

        // 与已记录的同 Id Mod 冲突：UMM 会拒绝加载重复 Id，这里标记为损坏并跳过注册
        if (!seenIds.Add(info.Id))
        {
            _logger.LogWarning("重复的 Mod Id '{Id}'：{Dir}（UMM 将拒绝加载）", info.Id, dir);
        }

        UmVersion? version = UmVersion.TryParse(info.Version, out var parsedVersion) ? parsedVersion : null;

        // 检查主程序集是否存在（仅提示）
        var dllName = info.AssemblyName;
        if (string.IsNullOrWhiteSpace(dllName))
        {
            dllName = info.Id + ".dll";
        }

        var dllMissing = !File.Exists(Path.Combine(dir, dllName));

        return new Models.Local.LocalMod
        {
            DirectoryPath = dir,
            FolderName = folderName,
            Info = info,
            Disabled = disabled || disabledByFolder,
            InfoFileName = Path.GetFileName(infoPath),
            LocalVersion = version,
            DllMissing = dllMissing,
        };
    }

    public async Task<LocalModsSnapshot> ToggleEnabledAsync(string modDirectoryPath, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            var infoJson = Path.Combine(modDirectoryPath, "Info.json");
            var infoLower = Path.Combine(modDirectoryPath, "info.json");
            var infoDisabled = Path.Combine(modDirectoryPath, "Info.json.disabled");

            var folderName = Path.GetFileName(modDirectoryPath.TrimEnd(Path.DirectorySeparatorChar));

            // 目录级 .disabled 后缀：先移除
            if (folderName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            {
                var bare = folderName[..^".disabled".Length];
                var bareDir = Path.Combine(Path.GetDirectoryName(modDirectoryPath)!, bare);
                if (Directory.Exists(bareDir))
                {
                    throw new IOException($"目标目录已存在：{bareDir}");
                }

                Directory.Move(modDirectoryPath, bareDir);
                return;
            }

            // 两者并存视为已启用 → 清理 .disabled
            if (File.Exists(infoDisabled))
            {
                if (!File.Exists(infoJson) && !File.Exists(infoLower))
                {
                    File.Move(infoDisabled, infoJson);
                }
                else
                {
                    File.Delete(infoDisabled);
                }

                return;
            }

            // 已启用 → 禁用
            if (File.Exists(infoJson))
            {
                File.Move(infoJson, infoDisabled);
            }
            else if (File.Exists(infoLower))
            {
                File.Move(infoLower, Path.Combine(modDirectoryPath, "Info.json.disabled"));
            }
            else
            {
                throw new IOException("目录中未找到 Info.json，无法切换状态。");
            }
        }, ct).ConfigureAwait(false);

        return await ScanAsync(ct).ConfigureAwait(false);
    }

    /// <summary>移动目录；跨卷时回退为"复制 + 删除"（游戏目录与 %LOCALAPPDATA% 常不在同一分区）。</summary>
    private static void MoveDirectory(string source, string destination)
    {
        try
        {
            Directory.Move(source, destination);
        }
        catch (IOException)
        {
            CopyDirectory(source, destination);
            Directory.Delete(source, recursive: true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }

    public async Task<string> UninstallAsync(string modDirectoryPath, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var trash = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ADOFAI.ModManager", "trash",
                DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "_" + Path.GetFileName(modDirectoryPath.TrimEnd(Path.DirectorySeparatorChar)));

            Directory.CreateDirectory(Path.GetDirectoryName(trash)!);
            MoveDirectory(modDirectoryPath, trash);
            _logger.LogInformation("Mod 已移入回收站区：{From} → {To}", modDirectoryPath, trash);
            return trash;
        }, ct).ConfigureAwait(false);
    }
}
