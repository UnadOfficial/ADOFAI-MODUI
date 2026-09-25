using ADOFAI.ModManager.Core.Models.Tuforums;

namespace ADOFAI.ModManager.Core.ViewModels;

/// <summary>设计时（XAML 预览器）假数据。</summary>
public static class DesignCatalog
{
    public static TufMod Sample(int id, string name, string slug, string version, params string[] tags) => new()
    {
        Id = id,
        Slug = slug,
        Name = name,
        CreatorUsername = "示例作者",
        Version = version,
        Description = "这是一个用于 XAML 设计器的示例 Mod 描述。包含 **Markdown** 标记与较长的正文，用于测试摘要截断与排版效果。",
        DownloadUrl = $"https://github.com/example/{slug}/releases/tag/v{version}",
        ImageUrl = null,
        SourceUploadedAt = DateTimeOffset.Now.AddDays(-3),
        IsPinned = id % 3 == 0,
        Likes = id * 7,
        DownloadCount = id * 137,
        Tags = tags.Select((t, i) => new TufTag { Id = i, Name = t, Color = "#708CFF" }).ToList(),
        Assignees = new List<TufActor>(),
        Versions = new List<TufVersion>(),
    };

    public static IReadOnlyList<TufMod> Samples { get; } = new[]
    {
        Sample(161, "TUFHelper", "tufhelper", "3.1.1", "Gameplay", "Quality Of Life", "Overlay"),
        Sample(90, "Jipper Key Viewer", "jipperkeyviewer", "1.7.2", "Overlay"),
        Sample(12, "AdofaiTweaks", "adofaitweaks", "2.9.3", "Gameplay", "Quality Of Life"),
        Sample(33, "KeyViewer", "keyviewer", "1.4.0", "Overlay"),
        Sample(48, "Richter Mapping", "richter-mapping", "0.9.1", "Tools"),
    };
}
