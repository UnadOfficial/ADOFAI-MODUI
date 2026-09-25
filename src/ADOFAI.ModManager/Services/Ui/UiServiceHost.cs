using ADOFAI.ModManager.Core.Services;
using Microsoft.UI.Xaml;

namespace ADOFAI.ModManager.Services.Ui;

/// <summary>UI 服务宿主：持有主窗口，为对话框/文件选择器提供 XamlRoot 与 HWND。</summary>
public sealed class UiServiceHost : IDialogService, IFilePickerService
{
    private Window? _window;

    public void Attach(Window window) => _window = window;

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "确定", string cancelText = "取消")
    {
        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = confirmText,
            CloseButtonText = cancelText,
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
        };

        return await ShowAsync(dialog) == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary;
    }

    public async Task AlertAsync(string title, string message)
    {
        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "关闭",
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
        };

        await ShowAsync(dialog);
    }

    public async Task<string?> PickFolderAsync()
    {
        if (_window is null)
        {
            return null;
        }

        var picker = new Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder,
            FileTypeFilter = { "*" },
        };

        // 非打包应用必须以 HWND 初始化选择器
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public async Task<string?> PickFileAsync(params string[] extensions)
    {
        if (_window is null)
        {
            return null;
        }

        var picker = new Windows.Storage.Pickers.FileOpenPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads,
            ViewMode = Windows.Storage.Pickers.PickerViewMode.List,
        };

        foreach (var ext in extensions)
        {
            picker.FileTypeFilter.Add(ext);
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private async Task<Microsoft.UI.Xaml.Controls.ContentDialogResult> ShowAsync(Microsoft.UI.Xaml.Controls.ContentDialog dialog)
    {
        if (_window?.Content is not FrameworkElement root)
        {
            throw new InvalidOperationException("主窗口尚未就绪。");
        }

        dialog.XamlRoot = root.XamlRoot;
        return await dialog.ShowAsync();
    }
}
