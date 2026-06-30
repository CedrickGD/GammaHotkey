using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GammaHotkey.Mvvm;

/// <summary>Visible when the bound int count is 0, otherwise Collapsed (empty-state hint).</summary>
public sealed class ZeroCountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int n && n == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
