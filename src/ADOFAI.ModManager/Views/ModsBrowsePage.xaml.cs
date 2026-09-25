using ADOFAI.ModManager.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace ADOFAI.ModManager.Views;

/// <summary>浏览 Mod 页：TUF 远端目录（合并本地状态）。</summary>
public sealed partial class ModsBrowsePage : Page
{
    private ModsPageViewModel ViewModel { get; }

    public ModsBrowsePage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<ModsPageViewModel>();

        // x:Bind 的 TwoWay 在失焦时才回写；搜索框需要逐字符即时过滤
        SearchBox.TextChanged += (_, args) =>
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.ProgrammaticChange)
            {
                ViewModel.SearchText = SearchBox.Text;
            }
        };
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.RefreshGameRootState();
    }

    private void Notice_CloseClick(InfoBar sender, object args)
    {
        ViewModel.NoticeOpen = false;
    }
}
