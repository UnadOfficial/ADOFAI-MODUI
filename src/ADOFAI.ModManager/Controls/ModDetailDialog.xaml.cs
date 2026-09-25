using ADOFAI.ModManager.Core.Services;
using ADOFAI.ModManager.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ADOFAI.ModManager.Controls;

/// <summary>Mod 详情对话框（ContentDialog 子类，x:Bind 到 ModDetailViewModel）。</summary>
public sealed partial class ModDetailDialog : ContentDialog
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(ModDetailViewModel),
            typeof(ModDetailDialog),
            new PropertyMetadata(null));

    public ModDetailDialog()
    {
        InitializeComponent();
    }

    public ModDetailViewModel? ViewModel
    {
        get => (ModDetailViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private void VersionExpander_Expanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        ViewModel?.LoadVersionsCommand.Execute(null);
    }

    private void OpenTufPage_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.TufPageUrl is { Length: > 0 } url)
        {
            Browser.Open(url);
        }
    }
}
