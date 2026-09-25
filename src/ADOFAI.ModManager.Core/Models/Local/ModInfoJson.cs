using System.Text.Json.Serialization;

namespace ADOFAI.ModManager.Core.Models.Local;

/// <summary>
/// Unity Mod Manager（UMM）规范的 Info.json。
/// 字段集合来自 newman55/unity-mod-manager 的 ModInfo 定义，
/// 反序列化时容忍缺失字段、注释与尾逗号；未知字段忽略。
/// </summary>
public sealed class ModInfoJson
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("DisplayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("Author")]
    public string? Author { get; set; }

    [JsonPropertyName("Version")]
    public string? Version { get; set; }

    [JsonPropertyName("ManagerVersion")]
    public string? ManagerVersion { get; set; }

    [JsonPropertyName("GameVersion")]
    public string? GameVersion { get; set; }

    [JsonPropertyName("AssemblyName")]
    public string? AssemblyName { get; set; }

    [JsonPropertyName("EntryMethod")]
    public string? EntryMethod { get; set; }

    [JsonPropertyName("HomePage")]
    public string? HomePage { get; set; }

    [JsonPropertyName("Repository")]
    public string? Repository { get; set; }

    [JsonPropertyName("ContentType")]
    public string? ContentType { get; set; }

    [JsonPropertyName("Requirements")]
    public List<string>? Requirements { get; set; }

    [JsonPropertyName("LoadAfter")]
    public List<string>? LoadAfter { get; set; }

    public string EffectiveDisplayName =>
        string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName!;

    public string EffectiveAuthor => Author ?? string.Empty;

    /// <summary>解析 "OtherMod@1.2.0" 形式的依赖 id 部分。</summary>
    public IReadOnlyList<string> RequirementIds()
    {
        if (Requirements is not { Count: > 0 })
        {
            return Array.Empty<string>();
        }

        var result = new List<string>(Requirements.Count);
        foreach (var raw in Requirements)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var at = raw.IndexOf('@');
            result.Add(at > 0 ? raw[..at].Trim() : raw.Trim());
        }

        return result;
    }
}
