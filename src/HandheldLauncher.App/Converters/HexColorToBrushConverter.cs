using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace HandheldLauncher.App.Converters;

/// <summary>
/// "#RRGGBB" string -&gt; SolidColorBrush. Usado pro badge de plataforma no
/// canto da capa (ver GameTileViewModel.PlatformBadgeColorHex) — a
/// ViewModel fica sem nenhuma dependência do Avalonia (nem IBrush nem
/// Color), igual o resto deste projeto; só essa conversão final mora aqui.
/// </summary>
public sealed class HexColorToBrushConverter : IValueConverter
{
    public static readonly HexColorToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex)) return null;

        try
        {
            return new SolidColorBrush(Color.Parse(hex));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
