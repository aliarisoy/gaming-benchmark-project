using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RigSight.Agent.Converters;

/// <summary>Shows an element when the bound bool is true; pass "invert" to flip it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        if (Invert) flag = !flag;
        if (parameter is string text &&
            (text.Equals("invert", StringComparison.OrdinalIgnoreCase) ||
             text.Equals("inverse", StringComparison.OrdinalIgnoreCase)))
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>Shows the page control whose enum name matches ConverterParameter.</summary>
public sealed class PageVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is not string page) return Visibility.Collapsed;
        return string.Equals(value.ToString(), page, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Visible for non-empty strings.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasText = !string.IsNullOrWhiteSpace(value as string);
        if (Invert) hasText = !hasText;
        return hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
