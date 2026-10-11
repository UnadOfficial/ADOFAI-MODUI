using System.Globalization;
namespace ADOFAI.ModManager.Core.Models.App;

/// <summary>应用设置（随 app-state.json 持久化）。</summary>
public sealed class AppSettings
{
    /// <summary>游戏根目录。</summary>
    public string GameRoot { get; set; } = string.Empty;

    public GameSource GameSource { get; set; } = GameSource.None;

    /// <summary>最大并行下载数。</summary>
    public int MaxParallelDownloads { get; set; } = 2;

    /// <summary>操作前检查游戏进程（建议保持开启）。</summary>
    public bool CheckGameProcess { get; set; } = true;

    /// <summary>自动检查更新（拉取远端目录时顺带比对）。</summary>
    public bool AutoCheckUpdates { get; set; } = true;

    /// <summary>目录缓存有效期（分钟）。</summary>
    public int CatalogCacheTtlMinutes { get; set; } = 30;

    /// <summary>界面主题：Default / Light / Dark。</summary>
    public string Theme { get; set; } = "Default";

    /// <summary>生效的 GitHub 镜像源前缀或模板（空字符串表示直连）。例如："https://ghproxy.net/"</summary>
    public string GitHubMirror { get; set; } = string.Empty;

    /// <summary>用户输入的自定义 GitHub 镜像地址，切换预设时保留记录。</summary>
    public string CustomGitHubMirror { get; set; } = string.Empty;

    /// <summary>详情页翻译提供商：Auto（自动）/ Bing（微软必应）/ Google（谷歌）。</summary>
    public string TranslationProvider { get; set; } = "Auto";
}

/// <summary>本地 Mod ↔ Tuforums slug 的持久化关联。</summary>
public sealed class ModLinkRecord
{
    public string Slug { get; set; } = string.Empty;

    public string LocalId { get; set; } = string.Empty;
}

/// <summary>一次安装/更新的元数据记录。</summary>
public sealed class InstallRecord
{
    public string Slug { get; set; } = string.Empty;

    public string LocalId { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public DateTimeOffset InstalledAt { get; set; }

    public string? SourceUrl { get; set; }

    /// <summary>历史列表展示用摘要。</summary>
    public string Summary
    {
        get
        {
            var when = InstalledAt.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(Version) ? when : $"{Version} · {when}";
        }
    }
}

/// <summary>应用持久化状态根对象。</summary>
public sealed class AppState
{
    public AppSettings Settings { get; set; } = new();

    public List<ModLinkRecord> Links { get; set; } = new();

    public List<InstallRecord> Installs { get; set; } = new();
}
