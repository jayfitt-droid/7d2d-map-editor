using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MapEditor.App;

/// <summary>Turns a packed 0xRRGGBB colour (as in biomes.xml) into a brush for swatches.</summary>
public sealed class RgbToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        uint rgb = value is uint u ? u : 0;
        var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
