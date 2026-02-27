using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Lensly.Controls;

public class SliderValueToWidthConverter : IValueConverter
{
    public static readonly SliderValueToWidthConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double val && parameter is Slider slider)
        {
            var range = slider.Maximum - slider.Minimum;
            if (range <= 0) return 0.0;
            var ratio = (val - slider.Minimum) / range;
            return slider.Bounds.Width * ratio;
        }
        return 0.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class SliderValueToPositionConverter : IValueConverter
{
    public static readonly SliderValueToPositionConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double val && parameter is Slider slider)
        {
            var range = slider.Maximum - slider.Minimum;
            if (range <= 0) return 0.0;
            var ratio = (val - slider.Minimum) / range;
            return (slider.Bounds.Width - 16) * ratio;
        }
        return 0.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StringToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var str = value as string;
        var geometryStr = str switch
        {
            "Library" => "M18.5,12A3.5,3.5 0 0,0 22,8.5A6.5,6.5 0 0,0 15.5,2A3.5,3.5 0 0,0 12,5.5A3.5,3.5 0 0,0 8.5,2A6.5,6.5 0 0,0 2,8.5A3.5,3.5 0 0,0 5.5,12A3.5,3.5 0 0,0 2,15.5A6.5,6.5 0 0,0 8.5,22A3.5,3.5 0 0,0 12,18.5A3.5,3.5 0 0,0 15.5,22A6.5,6.5 0 0,0 22,15.5A3.5,3.5 0 0,0 18.5,12M12,16A4,4 0 0,1 8,12A4,4 0 0,1 12,8A4,4 0 0,1 16,12A4,4 0 0,1 12,16M14.5,12A2.5,2.5 0 0,1 12,14.5A2.5,2.5 0 0,1 9.5,12A2.5,2.5 0 0,1 12,9.5A2.5,2.5 0 0,1 14.5,12Z",
            "Collections" => "M3,3H9V9H3V3M15,3H21V9H15V3M3,15H9V21H3V15M15,15H21V21H15V15Z",
            "Favorites" => "M12,21.35L10.55,20.03C5.4,15.36 2,12.28 2,8.5C2,5.42 4.42,3 7.5,3C9.24,3 10.91,3.81 12,5.09C13.09,3.81 14.76,3 16.5,3C19.58,3 22,5.42 22,8.5C22,12.28 18.6,15.36 13.45,20.04L12,21.35Z",
            "Recently Saved" => "M13,3A9,9 0 0,0 4,12H1L4.89,15.89L8.96,12H6A7,7 0 0,1 13,5A7,7 0 0,1 20,12A7,7 0 0,1 13,19C11.07,19 9.32,18.21 8.06,16.94L6.64,18.36C8.27,20 10.5,21 13,21A9,9 0 0,0 22,12A9,9 0 0,0 13,3Z",
            "Map" => "M15,2.1C10.5,2.1 7.2,5.8 7.2,10.2C7.2,15.5 14.2,21.5 14.6,21.8C14.8,22 15.2,22 15.4,21.8C15.8,21.5 22.8,15.5 22.8,10.2C22.8,5.8 19.5,2.1 15,2.1M15,13.1C13.4,13.1 12.1,11.8 12.1,10.2C12.1,8.6 13.4,7.3 15,7.3C16.6,7.3 17.9,8.6 17.9,10.2C17.9,11.8 16.6,13.1 15,13.1Z",
            "Videos" => "M17,10.5V7A1,1 0 0,0 16,6H4A1,1 0 0,0 3,7V17A1,1 0 0,0 4,18H16A1,1 0 0,0 17,17V13.5L21,17.5V6.5L17,10.5Z",
            "Screenshots" => "M4,4H20A2,2 0 0,1 22,6V18A2,2 0 0,1 20,20H4A2,2 0 0,1 2,18V6A2,2 0 0,1 4,4M4,6V18H20V6H4M6,8H8V10H6V8M10,8H12V10H10V8M14,8H16V10H14V8M6,12H18V16H6V12Z",
            "People" => "M12,4A4,4 0 0,1 16,8A4,4 0 0,1 12,12A4,4 0 0,1 8,8A4,4 0 0,1 12,4M12,14C16.42,14 20,15.79 20,18V20H4V18C4,15.79 7.58,14 12,14Z",
            "Recently Deleted" => "M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z",
            "All Albums" => "M4,6H2V20A2,2 0 0,0 4,22H18V20H4V6M20,2H8A2,2 0 0,0 6,4V16A2,2 0 0,0 8,18H20A2,2 0 0,0 22,16V4A2,2 0 0,0 20,2M20,16H8V4H20V16Z",
            "Instagram" => "M7.8,2H16.2C19.4,2 22,4.6 22,7.8V16.2A5.8,5.8 0 0,1 16.2,22H7.8C4.6,22 2,19.4 2,16.2V7.8A5.8,5.8 0 0,1 7.8,2M7.6,4A3.6,3.6 0 0,0 4,7.6V16.4C4,18.39 5.61,20 7.6,20H16.4A3.6,3.6 0 0,0 20,16.4V7.6C20,5.61 18.39,4 16.4,4H7.6M17.25,5.5A1.25,1.25 0 0,1 18.5,6.75A1.25,1.25 0 0,1 17.25,8A1.25,1.25 0 0,1 16,6.75A1.25,1.25 0 0,1 17.25,5.5M12,7A5,5 0 0,1 17,12A5,5 0 0,1 12,17A5,5 0 0,1 7,12A5,5 0 0,1 12,7M12,9A3,3 0 0,0 9,12A3,3 0 0,0 12,15A3,3 0 0,0 15,12A3,3 0 0,0 12,9Z",
            "WhatsApp" => "M12.04,2C6.58,2 2.13,6.45 2.13,11.91C2.13,13.66 2.59,15.36 3.45,16.86L2.05,22L7.3,20.62C8.75,21.41 10.38,21.83 12.04,21.83C17.5,21.83 21.95,17.38 21.95,11.92C21.95,9.27 20.92,6.78 19.05,4.91C17.18,3.03 14.69,2 12.04,2M12.05,3.67C14.25,3.67 16.31,4.53 17.87,6.09C19.42,7.65 20.28,9.72 20.28,11.92C20.28,16.46 16.58,20.15 12.04,20.15C10.56,20.15 9.11,19.76 7.85,19L7.55,18.83L4.43,19.65L5.26,16.61L5.06,16.29C4.24,15 3.8,13.47 3.8,11.91C3.81,7.37 7.5,3.67 12.05,3.67M8.53,7.33C8.37,7.33 8.1,7.39 7.87,7.64C7.65,7.89 7,8.5 7,9.75C7,11 7.89,12.21 8.06,12.44C8.22,12.67 9.88,15.22 12.39,16.31C13,16.57 13.46,16.72 13.82,16.83C14.42,17.02 14.96,16.97 15.39,16.9C15.86,16.81 16.87,16.32 17.08,15.76C17.3,15.21 17.3,14.73 17.22,14.63C17.14,14.52 16.95,14.47 16.64,14.31C16.33,14.16 14.83,13.42 14.55,13.32C14.27,13.22 14.07,13.17 13.87,13.47C13.67,13.77 13.11,14.47 12.95,14.68C12.78,14.89 12.6,14.92 12.3,14.76C12,14.61 10.97,14.17 9.75,13.08C8.8,12.24 8.16,11.19 8,10.88C7.83,10.57 7.97,10.42 8.13,10.27C8.27,10.13 8.44,9.93 8.59,9.76C8.75,9.59 8.8,9.46 8.91,9.25C9.02,9.04 8.96,8.86 8.88,8.71C8.8,8.55 8.17,7.02 7.9,6.4C7.64,5.79 7.37,5.88 7.18,5.88C7,5.87 6.8,5.87 6.6,5.87C6.4,5.87 6.1,5.92 5.86,6.17Z",
            _ => "M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z"
        };
        return Geometry.Parse(geometryStr);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public class NullToOpacityConverter : IValueConverter
{
    public static readonly NullToOpacityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value == null ? 0.0 : 1.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class FavoriteColorConverter : IValueConverter
{
    public static readonly FavoriteColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isFavorite && isFavorite)
            return Brush.Parse("#FF3B30"); // iOS Red
        return Brushes.White;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StringToBrushConverter : IValueConverter
{
    public static readonly StringToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is string paramStr && !string.IsNullOrEmpty(paramStr))
        {
            var colors = paramStr.Split('|');
            if (colors.Length == 2 && value is bool b)
            {
                return Brush.Parse(b ? colors[0] : colors[1]);
            }

            try
            {
                // Fallback to existing behavior if not a bool or no pipe
                return Brush.Parse(colors[0]);
            }
            catch { }
        }
        
        if (value is string colorStr && !string.IsNullOrEmpty(colorStr))
        {
            try
            {
                return Brush.Parse(colorStr);
            }
            catch { }
        }
        return Brushes.White;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class EqualityMultiConverter : IMultiValueConverter
{
    public static readonly EqualityMultiConverter Instance = new();

    public object? Convert(System.Collections.Generic.IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 && values[0] != null && values[1] != null)
        {
            return values[0]!.Equals(values[1]);
        }
        return false;
    }
}
