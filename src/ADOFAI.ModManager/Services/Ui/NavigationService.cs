using ADOFAI.ModManager.Core.Services;

namespace ADOFAI.ModManager.Services.Ui;

/// <summary>页面导航服务实现：持有主窗口引用，通过 DispatcherQueue 切换 NavigationView 选中项。</summary>
public sealed class NavigationService : INavigationService
{
    private MainWindow? _window;

    public void Attach(MainWindow window) => _window = window;

    public void NavigateTo(string tag)
    {
        if (_window is null)
        {
            return;
        }

        _window.DispatcherQueue.TryEnqueue(() => _window.NavigateToTag(tag));
    }
}
