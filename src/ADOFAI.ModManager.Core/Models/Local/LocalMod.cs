namespace ADOFAI.ModManager.Core.Models.Local;

/// <summary>
/// 本地 Mods 目录中的一个 Mod（对应 Mods/ 下的一个子目录）。
/// </summary>
public sealed class LocalMod
{
    /// <summary>Mod 目录绝对路径。</summary>
    public required string DirectoryPath { get; init; }

    /// <summary>目录名（可能带 .disabled 后缀）。</summary>
    public required string FolderName { get; init; }

    /// <summary>解析出的 Info.json；损坏时为 null。</summary>
    public ModInfoJson? Info { get; init; }

    /// <summary>禁用状态（Info.json 被重命名为 Info.json.disabled 或目录带 .disabled 后缀）。</summary>
    public bool Disabled { get; init; }

    /// <summary>Info.json 的实际文件名（用于启用/禁用切换）。</summary>
    public string InfoFileName { get; set; } = "Info.json";

    /// <summary>本地版本（宽松解析；无法解析为 null）。</summary>
    public Helpers.UmVersion? LocalVersion { get; init; }

    /// <summary>Info.json 中声明的原始版本文本（用于展示，保持作者写法；比较请用 <see cref="LocalVersion"/>）。</summary>
    public string DeclaredVersion =>
        string.IsNullOrWhiteSpace(Info?.Version) ? string.Empty : Info!.Version!.Trim();

    /// <summary>状态（结合远端目录比对后由 ViewModel 设置）。</summary>
    public LocalModStatus Status { get; set; } = LocalModStatus.Unlisted;

    /// <summary>关联的 Tuforums slug（来自持久化映射或自动匹配）。</summary>
    public string? LinkedSlug { get; set; }

    /// <summary>主程序集文件是否存在于目录中（仅提示用）。</summary>
    public bool DllMissing { get; init; }

    public string DisplayId => Info?.Id ?? FolderName;

    public string DisplayName => Info?.EffectiveDisplayName ?? FolderName;

    public string Author => Info?.EffectiveAuthor ?? string.Empty;

    public string VersionText => Info?.Version ?? "?";
}
