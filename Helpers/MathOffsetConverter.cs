using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Lensly.Helpers;

public class MathOffsetConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double baseVal && double.TryParse(parameter?.ToString(), out double offset))
            return baseVal + offset;
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public class ThumbnailSliderConverter : IValueConverter
{
    public static readonly ThumbnailSliderConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double size)
        {
            var normalized = (size - 80) / (400 - 80);
            return 100 * normalized;
        }
        return 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}