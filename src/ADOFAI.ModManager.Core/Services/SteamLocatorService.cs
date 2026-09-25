using ADOFAI.ModManager.Core.Helpers;
using System.Text.RegularExpressions;
using ADOFAI.ModManager.Core.Models.Game;
using Microsoft.Win32;
using ADOFAI.ModManager.Core.Models;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace ADOFAI.ModManager.Core.Services;

/// <summary>
/// ADOFAI 游戏目录定位器：
///   1) 注册表 HKCU\Software\Valve\Steam\SteamPath（回退 HKLM 两处）；
///   2) 解析 libraryfolders.vdf 枚举全部 Steam 库；
///   3) 逐库校验 steamapps/common/A Dance of Fire and Ice/（exe + _Data 目录 + appmanifest_977950.acf）。
/// </summary>
public interface ISteamLocatorService
{
    Task<GameLocatorResult> LocateAsync(CancellationToken ct = default);

    /// <summary>校验用户手动选择的目录是否为有效游戏根目录。</summary>
    Task<GameLocatorResult> ValidateAsync(string? gameRoot, CancellationToken ct = default);
}

public sealed partial class SteamLocatorService : ISteamLocatorService
{
    public const string SteamAppId = "977950";
    public const string GameFolderName = "A Dance of Fire and Ice";
    public const string GameExeName = "A Dance of Fire and Ice.exe";
    public const string GameDataFolderSuffix = "_Data";
    public const string GameProcessName = "A Dance of Fire and Ice";

    private readonly ILogger<SteamLocatorService> _logger;

    public SteamLocatorService(ILogger<SteamLocatorService> logger) => _logger = logger;

    public async Task<GameLocatorResult> LocateAsync(CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            // 1) 找到 Steam 根目录
            var steamRoot = FindSteamRootFromRegistry();
            if (steamRoot is null)
            {
                _logger.LogInformation("注册表中未找到 Steam 安装路径");
                return GameLocatorResult.Fail("未在注册表中找到 Steam 安装路径，请手动选择游戏目录。");
            }

            // 2) Steam 根目录自身优先（最常见安装位置）
            var direct = ValidateGameRoot(Path.Combine(steamRoot, "steamapps", "common", GameFolderName), steamRoot, GameSource.Registry);
            if (direct.Found)
            {
                return direct;
            }

            // 3) 解析 libraryfolders.vdf 枚举所有库
            var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath))
            {
                return GameLocatorResult.Fail($"Steam 根目录 {steamRoot} 下未找到 libraryfolders.vdf。");
            }

            string vdfText;
            try
            {
                vdfText = File.ReadAllText(vdfPath);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "读取 libraryfolders.vdf 失败");
                return GameLocatorResult.Fail("读取 libraryfolders.vdf 失败。");
            }

            if (!VdfParser.TryParse(vdfText, out var root))
            {
                return GameLocatorResult.Fail("libraryfolders.vdf 解析失败。");
            }

            var libraryPaths = VdfParser.CollectStringValues(root, "path");
            foreach (var rawPath in libraryPaths)
            {
                ct.ThrowIfCancellationRequested();
                var library = NormalizeSteamPath(rawPath);
                if (string.IsNullOrEmpty(library) || !Directory.Exists(library))
                {
                    continue;
                }

                var candidate = Path.Combine(library, "steamapps", "common", GameFolderName);
                var result = ValidateGameRoot(candidate, library, GameSource.LibraryVdf);
                if (result.Found)
                {
                    return result;
                }
            }

            return GameLocatorResult.Fail("已在 Steam 库中搜索，但未找到《冰与火之舞》安装目录，请手动选择游戏根目录。");
        }, ct);
    }

    public async Task<GameLocatorResult> ValidateAsync(string? gameRoot, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
        {
            return GameLocatorResult.Fail("目录为空。");
        }

        return await Task.Run(() => ValidateGameRoot(
            gameRoot.Trim('"', ' '), libraryPath: null, GameSource.Manual), ct);
    }

    /// <summary>校验目录是否为有效游戏根目录（exe + _Data 目录，均不区分大小写）。</summary>
    private GameLocatorResult ValidateGameRoot(string gameRoot, string? libraryPath, GameSource source)
    {
        try
        {
            if (!Directory.Exists(gameRoot))
            {
                return GameLocatorResult.Fail("目录不存在。");
            }

            var exe = FindFileCaseInsensitive(gameRoot, GameExeName);
            if (exe is null)
            {
                return GameLocatorResult.Fail($"目录中未找到 {GameExeName}。");
            }

            var dataFolder = FindDirectoryEndingWith(gameRoot, GameDataFolderSuffix);
            if (dataFolder is null)
            {
                return GameLocatorResult.Fail("目录中未找到 Unity 数据文件夹（*_Data）。");
            }

            var stateFlags = TryReadManifestState(libraryPath ?? gameRoot);

            return new GameLocatorResult
            {
                Found = true,
                GameRoot = gameRoot,
                Source = source,
                LibraryPath = libraryPath,
                ManifestStateFlags = stateFlags,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "校验游戏目录失败：{Root}", gameRoot);
            return GameLocatorResult.Fail("访问目录失败（权限或 IO 错误）。");
        }
    }

    private string? FindSteamRootFromRegistry()
    {
        // SteamPath 使用正斜杠存储（如 C:/Program Files (x86)/Steam）
        var candidates = new[]
        {
            (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
        };

        foreach (var (hive, keyPath, valueName) in candidates)
        {
            try
            {
                using var key = hive.OpenSubKey(keyPath);
                var value = key?.GetValue(valueName) as string;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    var path = NormalizeSteamPath(value);
                    if (Directory.Exists(path))
                    {
                        _logger.LogInformation("Steam 根目录（{Key}\\{Value}）：{Path}", keyPath, valueName, path);
                        return path;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                _logger.LogWarning(ex, "读取注册表 {Key} 失败", keyPath);
            }
        }

        return null;
    }

    private static string NormalizeSteamPath(string path)
        => path.Trim().Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);

    internal static string? FindFileCaseInsensitive(string directory, string fileName)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.exe"))
            {
                if (string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 忽略，视为未找到
        }

        return null;
    }

    private static string? FindDirectoryEndingWith(string directory, string suffix)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(directory, "*" + suffix))
            {
                // Unity 数据目录形如 "<游戏名>_Data"
                var name = Path.GetFileName(dir);
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return dir;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 忽略
        }

        return null;
    }

    /// <summary>读取 appmanifest_977950.acf 的 StateFlags（找不到或解析失败返回 null）。</summary>
    private static int? TryReadManifestState(string libraryPath)
    {
        try
        {
            var manifest = Path.Combine(libraryPath, "steamapps", $"appmanifest_{SteamAppId}.acf");
            if (!File.Exists(manifest))
            {
                return null;
            }

            var text = File.ReadAllText(manifest);
            var match = StateFlagsRegex().Match(text);
            return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [GeneratedRegex("\"StateFlags\"\\s+\"(\\d+)\"")]
    private static partial Regex StateFlagsRegex();
}
