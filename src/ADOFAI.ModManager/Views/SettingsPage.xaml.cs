using ADOFAI.ModManager.Core.ViewModels;
using ADOFAI.ModManager.Services.Ui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ADOFAI.ModManager.Views;

/// <summary>设置页：游戏目录、下载选项、缓存维护。</summary>
public sealed partial class SettingsPage : Page
{
    private SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
    }

    private async void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.PickFolderAsync();
    }
}
