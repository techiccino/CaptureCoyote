using System.Globalization;
using System.Windows.Data;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Editor.ViewModels;

namespace CaptureCoyote.Editor.Converters;

public sealed class LayerDisplayNameConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 ||
            values[0] is not AnnotationObject annotation ||
            values[1] is not EditorViewModel viewModel)
        {
            return string.Empty;
        }

        return viewModel.GetLayerDisplayName(annotation);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
