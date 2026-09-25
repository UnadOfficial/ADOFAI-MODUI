using ADOFAI.ModManager.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace ADOFAI.ModManager.Views;

/// <summary>下载与任务页：进行中的下载/安装任务 + 历史记录。</summary>
public sealed partial class DownloadsPage : Page
{
    private DownloadsViewModel ViewModel { get; }

    public DownloadsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<DownloadsViewModel>();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.RefreshActive();
    }
}
