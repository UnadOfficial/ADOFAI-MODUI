namespace ADOFAI.ModManager.Core.Models;

/// <summary>本地 Mod 的综合状态（由扫描结果 + 远端目录比对得出）。</summary>
public enum LocalModStatus
{
    /// <summary>已安装且为最新。</summary>
    Installed,

    /// <summary>已安装，远端存在更新版本。</summary>
    UpdateAvailable,

    /// <summary>已禁用（Info.json 被重命名 / 目录带 .disabled 后缀）。</summary>
    Disabled,

    /// <summary>本地存在但未收录到 Tuforums 目录。</summary>
    Unlisted,

    /// <summary>Info.json 缺失或无法解析。</summary>
    Broken,
}

/// <summary>卡片当前操作状态（驱动按钮与进度条）。</summary>
public enum ModOpState
{
    Idle,
    Queued,
    Resolving,
    Downloading,
    Extracting,
    Finishing,
    Success,
    Error,
}

/// <summary>安装管线的阶段（用于进度通知）。</summary>
public enum InstallPhase
{
    Preparing,
    ResolvingDownloadUrl,
    Downloading,
    Extracting,
    PreservingConfigs,
    Swapping,
    Done,
}

/// <summary>游戏目录的发现来源。</summary>
public enum GameSource
{
    /// <summary>未找到。</summary>
    None,

    /// <summary>通过注册表 SteamPath 直接定位。</summary>
    Registry,

    /// <summary>通过 libraryfolders.vdf 枚举 Steam 库定位。</summary>
    LibraryVdf,

    /// <summary>用户手动选择。</summary>
    Manual,
}

/// <summary>结果种类。</summary>
public enum ResultStatus
{
    Ok,
    Warning,
    Error,
}

/// <summary>浏览页排序方式。</summary>
public enum CatalogSortMode
{
    PinnedThenRecent,
    Recent,
    Downloads,
    Likes,
    Name,
}
