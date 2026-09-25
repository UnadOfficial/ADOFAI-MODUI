namespace ADOFAI.ModManager.Core.Helpers;

/// <summary>压缩包条目路径不安全（Zip-Slip 等）时抛出。</summary>
public sealed class UnsafeArchiveEntryException : Exception
{
    public UnsafeArchiveEntryException(string entryName)
        : base($"压缩包内发现不安全的路径条目，已拒绝解压：{entryName}")
    {
        EntryName = entryName;
    }

    public string EntryName { get; }
}

/// <summary>
/// 归档路径安全防护（防 Zip-Slip）与归一化。
/// </summary>
public static class PathGuard
{
    /// <summary>
    /// 校验并归一化 zip 条目路径为以 '/' 分隔的相对路径。
    /// 拒绝：绝对路径（/ 或 盘符）、包含 ".."、包含 ':' 等非法字符的条目。
    /// </summary>
    /// <exception cref="UnsafeArchiveEntryException">条目不安全。</exception>
    public static string GetSafeRelativePath(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName))
        {
            throw new UnsafeArchiveEntryException(entryName ?? string.Empty);
        }

        var normalized = entryName.Replace('\\', '/').TrimStart('/');

        if (normalized.Contains("//"))
        {
            normalized = CollapseDuplicateSlashes(normalized);
        }

        if (normalized.Contains(':'))
        {
            throw new UnsafeArchiveEntryException(entryName);
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment == "." || segment == "..")
            {
                throw new UnsafeArchiveEntryException(entryName);
            }
        }

        if (segments.Length == 0)
        {
            throw new UnsafeArchiveEntryException(entryName);
        }

        return string.Join('/', segments);
    }

    /// <summary>把 OS 相对路径转换为归档风格的 '/' 分隔路径（用于比较）。</summary>
    public static string ToArchiveStyle(string relativeOsPath)
        => relativeOsPath.Replace('\\', '/');

    private static string CollapseDuplicateSlashes(string input)
    {
        var sb = new System.Text.StringBuilder(input.Length);
        var lastWasSlash = false;
        foreach (var c in input)
        {
            if (c == '/')
            {
                if (lastWasSlash)
                {
                    continue;
                }

                lastWasSlash = true;
            }
            else
            {
                lastWasSlash = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
