using System.Diagnostics;
namespace ADOFAI.ModManager.Core.Services;

/// <summary>消息与确认对话框抽象（UI 层实现，保证 ViewModel 可测试）。</summary>
public interface IDialogService
{
    /// <summary>确认对话框；返回是否点击了确认。</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "确定", string cancelText = "取消");

    /// <summary>提示对话框（仅一个确认按钮）。</summary>
    Task AlertAsync(string title, string message);
}

/// <summary>文件选择器抽象。</summary>
public interface IFilePickerService
{
    /// <summary>选择文件夹；取消返回 null。</summary>
    Task<string?> PickFolderAsync();

    /// <summary>选择单个文件（extensions 形如 ".zip"）；取消返回 null。</summary>
    Task<string?> PickFileAsync(params string[] extensions);
}

/// <summary>用系统浏览器打开链接。</summary>
public static class Browser
{
    public static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // 打开失败不影响主流程（例如没有默认浏览器）
        }
    }
}
