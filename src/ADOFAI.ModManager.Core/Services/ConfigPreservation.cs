namespace ADOFAI.ModManager.Core.Services;

/// <summary>
/// 更新覆盖时的用户配置保护规则。
/// “保留” = 旧目录中匹配配置白名单、且新包中不存在同路径文件的用户文件。
/// 白名单（不区分大小写）：
///   - 精确文件名：Settings.xml / Config.xml / Config.json / Settings.json / Settings.ini / Config.ini
///   - 扩展名：.cfg / .ini
///   - 根目录下其它 *.json / *.xml（视为配置类数据；Info.json 除外）
///   - 顶层目录：config/ 与 settings/ 整个目录
/// Info.json 永远以新包为准（不回拷）。
/// </summary>
public static class ConfigPreservation
{
    private static readonly HashSet<string> ConfigFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "settings.xml", "config.xml", "config.json", "settings.json", "settings.ini", "config.ini",
    };

    private static readonly HashSet<string> ConfigExtensions = new(StringComparer.OrdinalIgnoreCase) { ".cfg", ".ini" };

    private static readonly HashSet<string> ConfigTopDirectories = new(StringComparer.OrdinalIgnoreCase) { "config", "settings" };

    public static bool IsConfigPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        if (segments.Length == 1)
        {
            var fileName = segments[0];
            if (fileName.Equals("Info.json", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("info.json", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (ConfigFileNames.Contains(fileName))
            {
                return true;
            }

            var ext = Path.GetExtension(fileName);
            if (ConfigExtensions.Contains(ext))
            {
                return true;
            }

            // 根目录下其余 json/xml 视为配置类数据
            return ext is ".json" or ".xml";
        }

        return ConfigTopDirectories.Contains(segments[0]);
    }

    /// <summary>
    /// 把 oldDir 中应保留且 newDir 中不存在的配置文件拷贝到 newDir。
    /// </summary>
    /// <returns>实际拷贝的相对路径列表（'/' 分隔）。</returns>
    public static IReadOnlyList<string> PreserveConfigs(string oldDir, string newDir)
    {
        var preserved = new List<string>();
        if (!Directory.Exists(oldDir))
        {
            return preserved;
        }

        var newFiles = CollectRelativeFiles(newDir);

        foreach (var oldFile in CollectRelativeFiles(oldDir))
        {
            if (!IsConfigPath(oldFile))
            {
                continue;
            }

            if (newFiles.Contains(oldFile))
            {
                continue; // 新包自带同名文件 → 以新包为准
            }

            var source = Path.Combine(oldDir, oldFile.Replace('/', Path.DirectorySeparatorChar));
            var target = Path.Combine(newDir, oldFile.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: false);
            preserved.Add(oldFile);
        }

        return preserved;
    }

    private static HashSet<string> CollectRelativeFiles(string rootDir)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(rootDir))
        {
            return set;
        }

        foreach (var file in Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(rootDir, file).Replace('\\', '/');
            set.Add(relative);
        }

        return set;
    }
}

/// <summary>本地 Mod ↔ 远端 TUF 条目的匹配（Id 归一化优先，DisplayName 次之，持久化映射最高优先）。</summary>
public static class ModMatching
{
    public static string NormalizeId(string id) => id.Trim().ToLowerInvariant();

    /// <summary>本地 Mod 与远端 Mod 是否为同一 Mod。</summary>
    public static bool Matches(Models.Local.LocalMod local, Models.Tuforums.TufMod remote, string? linkedSlug = null)
    {
        if (local.Info is null)
        {
            return false;
        }

        // 1) 用户显式关联（或状态文件中的持久化映射）
        if (!string.IsNullOrEmpty(linkedSlug) &&
            string.Equals(linkedSlug, remote.Slug, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 2) Info.Id == slug（社区约定常用）
        if (string.Equals(NormalizeId(local.Info.Id), remote.Slug, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 3) DisplayName == Name
        if (!string.IsNullOrWhiteSpace(local.Info.DisplayName) &&
            string.Equals(local.Info.DisplayName.Trim(), remote.Name?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
