namespace ADOFAI.ModManager.Core.Services;

/// <summary>GitHub 镜像源预设选项。</summary>
public sealed record GitHubMirrorOption(string Key, string DisplayName, string? Url, string Description)
{
    public override string ToString() => DisplayName;

    public bool IsCustom => string.Equals(Key, "Custom", StringComparison.OrdinalIgnoreCase);

    public bool IsDirect => string.Equals(Key, "Direct", StringComparison.OrdinalIgnoreCase);
}

/// <summary>镜像连通性与延迟测试结果。</summary>
public sealed record MirrorLatencyResult(bool Success, long LatencyMs, string? Message);
