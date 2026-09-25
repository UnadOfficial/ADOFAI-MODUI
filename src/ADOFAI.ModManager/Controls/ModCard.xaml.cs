using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ADOFAI.ModManager.Controls;

/// <summary>Mod 卡片控件：绑定一个 ModCardViewModel 实例。</summary>
public sealed partial class ModCard : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(Core.ViewModels.ModCardViewModel),
            typeof(ModCard),
            new PropertyMetadata(null));

    public ModCard()
    {
        InitializeComponent();
    }

    public Core.ViewModels.ModCardViewModel? ViewModel
    {
        get => (Core.ViewModels.ModCardViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
}
