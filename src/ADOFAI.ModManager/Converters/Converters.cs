using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ADOFAI.ModManager.Converters;

/// <summary>字符串（本地文件路径或 URL）→ ImageSource；null/空 → null（触发占位符）。</summary>
public sealed class StringToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, string language)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var uri = Path.IsPathRooted(path) && File.Exists(path)
                ? new Uri(path)
                : new Uri(path, UriKind.Absolute);
            return new BitmapImage(uri);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>十六进制颜色（#RRGGBB）→ 半透明 SolidColorBrush（用于徽章底色）。</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, string language)
    {
        if (value is not string hex || hex.Length < 7 || hex[0] != '#')
        {
            return null;
        }

        try
        {
            var color = new Windows.UI.Color
            {
                A = 0xFF,
                R = System.Convert.ToByte(hex[1..3], 16),
                G = System.Convert.ToByte(hex[3..5], 16),
                B = System.Convert.ToByte(hex[5..7], 16),
            };
            return new SolidColorBrush(color) { Opacity = 0.20 };
        }
        catch (Exception)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>null → Collapsed，非 null → Visible。</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>null → Visible，非 null → Collapsed。</summary>
public sealed class InverseNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language) =>
        value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>true → Visible，false → Collapsed。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>true → Collapsed，false → Visible。</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>空字符串 → Visible（用于 SecondaryButtonText 为空时隐藏按钮）。</summary>
public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language) =>
        value is string s && s.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>bool → InfoBarSeverity（true → Error，false → Success）。</summary>
public sealed class BoolToSeverityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language) =>
        value is true ? InfoBarSeverity.Error : InfoBarSeverity.Success;

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>bool 取反。</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language) =>
        value is true ? false : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>非空字符串 → true（用于 InfoBar.IsOpen 等 bool 属性）。</summary>
public sealed class StringNotEmptyToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language) =>
        value is string s && s.Length > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotSupportedException();
}
