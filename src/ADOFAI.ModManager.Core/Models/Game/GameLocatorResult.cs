namespace ADOFAI.ModManager.Core.Models.Game;

/// <summary>游戏目录定位结果。</summary>
public sealed class GameLocatorResult
{
    public bool Found { get; init; }

    /// <summary>游戏根目录（包含 exe 与 _Data 目录）。</summary>
    public string GameRoot { get; init; } = string.Empty;

    public GameSource Source { get; init; } = GameSource.None;

    /// <summary>命中的 Steam 库目录。</summary>
    public string? LibraryPath { get; init; }

    /// <summary>appmanifest_977950.acf 中的 StateFlags（可检测"安装中/已完整安装"）。</summary>
    public int? ManifestStateFlags { get; init; }

    /// <summary>人类可读的失败原因（Found=false 时）。</summary>
    public string? FailureReason { get; init; }

    public static GameLocatorResult Fail(string reason) => new() { Found = false, FailureReason = reason };
}
