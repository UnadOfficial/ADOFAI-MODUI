namespace ADOFAI.ModManager.Core.ViewModels;

/// <summary>ViewModel 生成的界面文案（zh-CN；后续可迁移到 .resw 资源做本地化）。</summary>
public static class UiStrings
{
    public const string StatusNotInstalled = "未安装";
    public const string StatusInstalled = "已安装";
    public const string StatusUpdateAvailable = "可更新";
    public const string StatusUpToDate = "已是最新";
    public const string StatusDisabled = "已禁用";
    public const string StatusUnlisted = "未收录";
    public const string StatusBroken = "无法识别";

    public const string ActionInstall = "安装";
    public const string ActionUpdate = "更新";
    public const string ActionReinstall = "重装";
    public const string ActionUninstall = "卸载";
    public const string ActionEnable = "启用";
    public const string ActionDisable = "禁用";
    public const string ActionOpenFolder = "打开目录";
    public const string ActionTranslate = "翻译";
    public const string ActionShowOriginal = "查看原文";
    public const string ActionTranslating = "正在翻译…";
    public const string ActionTranslateChangelog = "翻译更新日志";
    public const string TranslationFailed = "翻译失败，请检查网络连接";

    public const string Installing = "安装中…";
    public const string Updating = "更新中…";
    public const string Uninstalling = "卸载中…";
    public const string Downloading = "下载中";
    public const string Extracting = "解压中…";
    public const string Finishing = "收尾中…";

    public const string GameRunningWarning = "检测到《冰与火之舞》正在运行。安装/更新/卸载 Mod 可能因 DLL 文件占用而失败，建议先退出游戏。";

    public const string ConfirmUninstallTitle = "卸载 Mod";
    public const string ConfirmUninstallFormat = "确定卸载《{0}》吗？\n\n该操作会删除 Mods 目录下对应的文件夹（会先移入备份区）。";

    public const string ConfirmOpenUrlTitle = "需要在浏览器中下载";
    public const string BrowserFallbackMessage = "该 Mod 的下载地址无法自动解析为直链。点击“确定”将在浏览器中打开页面，手动下载 zip 后回到本窗口使用“导入本地 zip”完成安装。";

    public const string UnknownVersion = "未知版本";
}
