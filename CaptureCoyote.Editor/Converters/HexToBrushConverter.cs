using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using CaptureCoyote.Core.Primitives;

namespace CaptureCoyote.Editor.Converters;

public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hex = value as string;
        var color = ArgbColor.FromHex(hex, ArgbColor.Transparent);
        return new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
