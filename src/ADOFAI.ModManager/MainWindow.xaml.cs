using ADOFAI.ModManager.Controls;
using ADOFAI.ModManager.Core.Services;
using ADOFAI.ModManager.Core.ViewModels;
using ADOFAI.ModManager.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ADOFAI.ModManager;

/// <summary>主窗口：Mica 背景 + NavigationView 导航壳。</summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Fluent：Mica 背景 + 标题栏融入
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        ConfigureWindow();

        ContentFrame.Navigate(typeof(ModsBrowsePage));

        // 绑定主题变更
        var settings = Services.GetRequiredService<ISettingsService>();
        ApplyTheme(settings.State.Settings.Theme);
        SettingsViewModel.ThemeChanged += ApplyTheme;

        // 动态徽章订阅
        var mods = Services.GetRequiredService<ModsPageViewModel>();
        mods.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(ModsPageViewModel.AvailableUpdatesCount) or nameof(ModsPageViewModel.HasUpdates))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (mods.AvailableUpdatesCount > 0)
                    {
                        InstalledUpdatesBadge.Value = mods.AvailableUpdatesCount;
                        InstalledUpdatesBadge.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        InstalledUpdatesBadge.Visibility = Visibility.Collapsed;
                    }
                });
            }
        };

        var downloads = Services.GetRequiredService<DownloadsViewModel>();
        downloads.ActiveItems.CollectionChanged += (s, e) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (downloads.ActiveItems.Count > 0)
                {
                    ActiveDownloadsBadge.Value = downloads.ActiveItems.Count;
                    ActiveDownloadsBadge.Visibility = Visibility.Visible;
                }
                else
                {
                    ActiveDownloadsBadge.Visibility = Visibility.Collapsed;
                }
            });
        };

        // 详情弹窗（全局订阅一次）
        mods.DetailsRequested += async card =>
        {
            try
            {
                var detail = new ModDetailViewModel(
                    card,
                    Services.GetRequiredService<ITuforumsClient>(),
                    Services.GetRequiredService<ITranslationService>());
                var dialog = new ModDetailDialog { ViewModel = detail };
                dialog.XamlRoot = Content.XamlRoot;
                await dialog.ShowAsync();
            }
            catch (Exception)
            {
                // 弹窗失败（窗口关闭中等）不致崩溃
            }
        };
    }

    private void ConfigureWindow()
    {
        var appWindow = AppWindow;
        if (appWindow != null)
        {
            appWindow.Title = "ADOFAI Mod 管理器";
            var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(appWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                var width = 1150;
                var height = 760;
                var x = Math.Max(0, (displayArea.WorkArea.Width - width) / 2);
                var y = Math.Max(0, (displayArea.WorkArea.Height - height) / 2);
                appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
            }
        }
    }

    public void ApplyTheme(string theme)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Content is FrameworkElement root)
            {
                root.RequestedTheme = theme switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default,
                };
            }
        });
    }

    private static IServiceProvider Services => App.Services;

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        var targetPage = tag switch
        {
            "browse" => typeof(ModsBrowsePage),
            "installed" => typeof(InstalledModsPage),
            "downloads" => typeof(DownloadsPage),
            "settings" => typeof(SettingsPage),
            _ => null,
        };

        if (targetPage is not null && ContentFrame.CurrentSourcePageType != targetPage)
        {
            ContentFrame.Navigate(targetPage);
        }
    }

    /// <summary>按 tag 切换选中的导航项。</summary>
    public void NavigateToTag(string tag)
    {
        var allItems = NavView.MenuItems.Concat(NavView.FooterMenuItems);
        foreach (var item in allItems)
        {
            if (item is NavigationViewItem navItem && navItem.Tag as string == tag)
            {
                NavView.SelectedItem = navItem;
                break;
            }
        }
    }
}
