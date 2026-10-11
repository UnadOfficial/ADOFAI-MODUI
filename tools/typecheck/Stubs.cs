// ─────────────────────────────────────────────────────────────────────────────
// 手写桩：等价于 WinUI XAML 编译器为各 x:Class 生成的成员
//   * InitializeComponent()
//   * x:Name 元素对应的字段
//
// 仅用于 tools/typecheck 的类型检查工程；真实构建中这些成员由 XamlCompiler
// 生成，本文件不参与编译（该工程不在解决方案内）。
//
// 维护提示：XAML 中新增 x:Name 或新增 x:Class 文件时，请同步更新本文件。
// ─────────────────────────────────────────────────────────────────────────────
using Microsoft.UI.Xaml.Controls;

namespace ADOFAI.ModManager
{
    public partial class App
    {
        private void InitializeComponent() { }
    }

    public sealed partial class MainWindow
    {
        private void InitializeComponent() { }

        private Grid AppTitleBar = null!;
        private TextBlock AppTitleText = null!;
        private NavigationView NavView = null!;
        private NavigationViewItem NavInstalledItem = null!;
        private NavigationViewItem NavDownloadsItem = null!;
        private InfoBadge InstalledUpdatesBadge = null!;
        private InfoBadge ActiveDownloadsBadge = null!;
        private Frame ContentFrame = null!;
    }
}

namespace ADOFAI.ModManager.Controls
{
    public sealed partial class ModCard
    {
        private void InitializeComponent() { }
    }

    public sealed partial class ModDetailDialog
    {
        private void InitializeComponent() { }
    }
}

namespace ADOFAI.ModManager.Views
{
    public sealed partial class ModsBrowsePage
    {
        private void InitializeComponent() { }

        private AutoSuggestBox SearchBox = null!;
    }

    public sealed partial class InstalledModsPage
    {
        private void InitializeComponent() { }

        private TextBox SearchBox = null!;
    }

    public sealed partial class DownloadsPage
    {
        private void InitializeComponent() { }
    }

    public sealed partial class SettingsPage
    {
        private void InitializeComponent() { }
    }
}
