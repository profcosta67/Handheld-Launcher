using System;
using System.Globalization;
using System.Net;
using Avalonia.Data.Converters;

namespace HandheldLauncher.App.Converters;

public class HtmlDecodeConverter : IValueConverter
{
    public static readonly HtmlDecodeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s)
            return WebUtility.HtmlDecode(s);
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}