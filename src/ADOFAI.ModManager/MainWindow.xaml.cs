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

        ContentFrame.Navigate(typeof(ModsBrowsePage));

        // 详情弹窗（全局订阅一次）
        var mods = Services.GetRequiredService<ModsPageViewModel>();
        mods.DetailsRequested += async card =>
        {
            try
            {
                var detail = new ModDetailViewModel(card, Services.GetRequiredService<ITuforumsClient>());
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
        foreach (var item in NavView.MenuItems)
        {
            if (item is NavigationViewItem navItem && navItem.Tag as string == tag)
            {
                NavView.SelectedItem = navItem;
                break;
            }
        }
    }
}
