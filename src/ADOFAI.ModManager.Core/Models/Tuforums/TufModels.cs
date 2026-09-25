namespace ADOFAI.ModManager.Core.Models.Tuforums;

/// <summary>
/// Tuforums Mods API（https://api.tuforums.com/v2/mods）的分页响应。
/// 实测字段：{ total, mods[], offset, limit, hasMore }。
/// </summary>
public sealed class TufModsPage
{
    public int Total { get; set; }

    public List<TufMod>? Mods { get; set; }

    public int Offset { get; set; }

    public int Limit { get; set; }

    public bool HasMore { get; set; }
}

/// <summary>
/// 单个 Mod 条目。所有可能缺失/为 null 的字段均按可空建模，
/// 由 <see cref="TufMod.Normalize"/> 统一归一化为非空默认值。
/// </summary>
public sealed class TufMod
{
    public int Id { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string CreatorUsername { get; set; } = string.Empty;

    public string? CreatorDiscordId { get; set; }

    public string? Version { get; set; }

    public string? Description { get; set; }

    public string? DownloadUrl { get; set; }

    public string? ImageUrl { get; set; }

    public string? ProjectUrl { get; set; }

    public DateTimeOffset? DeprecatedAfter { get; set; }

    public DateTimeOffset? SourceUploadedAt { get; set; }

    public bool IsPinned { get; set; }

    public int Likes { get; set; }

    public int DownloadCount { get; set; }

    public List<TufTag>? Tags { get; set; }

    public List<TufActor>? Assignees { get; set; }

    public TufActor? PostedBy { get; set; }

    public List<TufVersion>? Versions { get; set; }

    public TufVersion? LatestVersion { get; set; }

    /// <summary>归一化：将所有可空集合/字符串收敛为非空值，便于下游安全使用。</summary>
    public void Normalize()
    {
        Slug ??= string.Empty;
        Name = string.IsNullOrWhiteSpace(Name) ? Slug : Name;
        CreatorUsername ??= string.Empty;
        Description ??= string.Empty;
        Tags ??= new List<TufTag>();
        Assignees ??= new List<TufActor>();
        Versions ??= new List<TufVersion>();

        foreach (var tag in Tags)
        {
            tag.Normalize();
        }
    }

    /// <summary>最新版本号（优先 LatestVersion.Version，其次顶层 Version）。</summary>
    public string EffectiveVersion =>
        LatestVersion?.Version is { Length: > 0 } v ? v
        : Version is { Length: > 0 } top ? top
        : string.Empty;
}

/// <summary>Mod 的某个历史版本。</summary>
public sealed class TufVersion
{
    public int Id { get; set; }

    public string Version { get; set; } = string.Empty;

    public string? DownloadUrl { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset? ReleasedAt { get; set; }

    public string? Source { get; set; }
}

/// <summary>标签（带展示颜色）。</summary>
public sealed class TufTag
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Color { get; set; }

    public int SortOrder { get; set; }

    public void Normalize() => Name ??= string.Empty;
}

/// <summary>作者 / 发布者信息。</summary>
public sealed class TufActor
{
    public string? UserId { get; set; }

    public int? PlayerId { get; set; }

    public string? Name { get; set; }

    public string? Username { get; set; }
}
