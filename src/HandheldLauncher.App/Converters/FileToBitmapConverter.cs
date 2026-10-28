using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace HandheldLauncher.App.Converters;

/// <summary>
/// Binds a file-path string (GameEntry.GridImagePath etc.) straight to an
/// Image.Source. Missing/invalid paths just produce no image instead of
/// throwing — cover art is optional until SteamGridDB is wired in.
/// </summary>
public sealed class FileToBitmapConverter : IValueConverter
{
    public static readonly FileToBitmapConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return new Bitmap(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
