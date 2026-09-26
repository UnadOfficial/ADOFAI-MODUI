using ADOFAI.ModManager.Core.ViewModels;
using ADOFAI.ModManager.Services.Ui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ADOFAI.ModManager.Core.Services;

namespace ADOFAI.ModManager.Views;

/// <summary>我的 Mod 页：本地 Mods 目录扫描结果（含未收录 Mod）。</summary>
public sealed partial class InstalledModsPage : Page
{
    private ModsPageViewModel ViewModel { get; }

    public InstalledModsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<ModsPageViewModel>();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.RefreshGameRootState();
        _ = ViewModel.RescanCommand.ExecuteAsync(null);
    }

    private void Notice_CloseClick(InfoBar sender, object args)
    {
        ViewModel.NoticeOpen = false;
    }

    private async void ImportZip_Click(object sender, RoutedEventArgs e)
    {
        var picker = App.Services.GetRequiredService<IFilePickerService>();
        var zipPath = await picker.PickFileAsync(".zip");
        if (!string.IsNullOrEmpty(zipPath))
        {
            await ViewModel.InstallZipAsync(zipPath);
        }
    }
}
