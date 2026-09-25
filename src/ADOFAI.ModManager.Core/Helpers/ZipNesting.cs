namespace ADOFAI.ModManager.Core.Helpers;

/// <summary>
/// 判断 zip 包是否把所有文件都套在同一层（或多层）顶层目录里，并给出需要剥掉的层数。
/// 规则：仅当**所有**文件条目共享同一第 N 层段且每个条目在该层之下还有内容时，才继续剥下一层。
/// 例：["Foo/Info.json", "Foo/Adofai.dll"] → 剥 1 层；["Foo/Foo/Info.json"] → 剥 2 层；
/// ["Info.json", "Foo/x.dll"] → 剥 0 层（根目录已有实质文件）。
/// </summary>
public static class ZipNesting
{
    public const int MaxStripDepth = 4;

    /// <summary>entryPaths：已归一化（'/' 分隔）的**文件**条目相对路径集合（不含纯目录条目）。</summary>
    public static int ComputeStripDepth(IEnumerable<string> entryPaths)
    {
        var paths = entryPaths as ICollection<string> ?? entryPaths.ToList();
        if (paths.Count == 0)
        {
            return 0;
        }

        var depth = 0;
        while (depth < MaxStripDepth)
        {
            if (!AllShareSegmentAt(paths, depth))
            {
                break;
            }

            depth++;
        }

        return depth;
    }

    private static bool AllShareSegmentAt(ICollection<string> paths, int depth)
    {
        string? head = null;

        foreach (var path in paths)
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            // 每个条目在当前层之后必须仍有内容，否则该层不可剥
            if (segments.Length < depth + 2)
            {
                return false;
            }

            var segment = segments[depth];
            if (head is null)
            {
                head = segment;
            }
            else if (!string.Equals(segment, head, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return head is not null;
    }
}
