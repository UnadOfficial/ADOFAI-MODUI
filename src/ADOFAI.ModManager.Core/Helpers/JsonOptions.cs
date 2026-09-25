using System.Text.Json;
using System.Text.Json.Serialization;

namespace ADOFAI.ModManager.Core.Helpers;

/// <summary>全局共享的 System.Text.Json 配置。</summary>
public static class JsonOptions
{
    /// <summary>网络 DTO 反序列化：宽容（大小写不敏感、允许字符串数字、忽略注释与尾逗号）。</summary>
    public static readonly JsonSerializerOptions Web = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>本地 Info.json：同样宽容，并保留可读格式化输出。</summary>
    public static readonly JsonSerializerOptions LocalInfo = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    /// <summary>应用状态文件：严格驼峰命名，格式化输出。</summary>
    public static readonly JsonSerializerOptions State = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
