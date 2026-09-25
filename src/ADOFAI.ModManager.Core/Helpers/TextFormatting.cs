using System.Text.RegularExpressions;
using System.Globalization;

namespace ADOFAI.ModManager.Core.Helpers;

/// <summary>界面文案辅助（中文为主；描述摘要与统计格式化）。</summary>
public static partial class TextFormatting
{
    /// <summary>把 Markdown 描述压缩为纯文本摘要（去标记、折叠空白、截断）。</summary>
    public static string Summarize(string? markdown, int maxChars = 180)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var text = LinkRegex().Replace(markdown, "$1"); // [text](url) → text
        text = CodeBlockRegex().Replace(text, " ");
        text = MarkdownNoiseRegex().Replace(text, " ");
        text = DiscordEmojiRegex().Replace(text, string.Empty);
        text = WhitespaceRegex().Replace(text, " ").Trim();

        if (text.Length <= maxChars)
        {
            return text;
        }

        return text[..maxChars].TrimEnd() + "…";
    }

    /// <summary>统计行："下载 1.2k · 点赞 34"。</summary>
    public static string FormatStats(int downloadCount, int likes)
        => $"下载 {FormatCount(downloadCount)} · 点赞 {FormatCount(likes)}";

    public static string FormatCount(int count) =>
        count >= 10000 ? (count / 10000.0).ToString("0.#", CultureInfo.InvariantCulture) + "w" :
        count >= 1000 ? (count / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k" :
        count.ToString(CultureInfo.InvariantCulture);

    public static string FormatBytes(long bytes) =>
        bytes >= 1024L * 1024 * 1024 ? (bytes / 1024.0 / 1024 / 1024).ToString("F2", CultureInfo.InvariantCulture) + " GB" :
        bytes >= 1024 * 1024 ? (bytes / 1024.0 / 1024).ToString("F1", CultureInfo.InvariantCulture) + " MB" :
        bytes >= 1024 ? (bytes / 1024.0).ToString("F0", CultureInfo.InvariantCulture) + " KB" :
        bytes.ToString(CultureInfo.InvariantCulture) + " B";

    [GeneratedRegex(@"```.*?```", RegexOptions.Singleline)]
    private static partial Regex CodeBlockRegex();

    [GeneratedRegex(@"`[^`]*`|!\[[^\]]*\]\([^)]*\)|https?://\S+|[#*_~>|`]+")]
    private static partial Regex MarkdownNoiseRegex();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkRegex();

    // Discord 自定义表情 <:name:123456> / <a:name:123>
    [GeneratedRegex("<a?:[a-zA-Z0-9_]+:[0-9]+>")]
    private static partial Regex DiscordEmojiRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
