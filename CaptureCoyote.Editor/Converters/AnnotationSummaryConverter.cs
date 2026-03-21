using System.Globalization;
using System.Windows.Data;
using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Models;

namespace CaptureCoyote.Editor.Converters;

public sealed class AnnotationSummaryConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not AnnotationObject annotation)
        {
            return string.Empty;
        }

        var mode = parameter as string;
        return string.Equals(mode, "Detail", StringComparison.OrdinalIgnoreCase)
            ? BuildDetail(annotation)
            : BuildTitle(annotation);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static string BuildTitle(AnnotationObject annotation)
    {
        return annotation switch
        {
            TextAnnotation text when !string.IsNullOrWhiteSpace(text.Text) => $"Text: {Trim(text.Text, 28)}",
            CalloutAnnotation callout when !string.IsNullOrWhiteSpace(callout.Text) => $"Callout: {Trim(callout.Text, 26)}",
            StepAnnotation step => $"Step {step.Number}",
            ShapeAnnotation shape => shape.ShapeKind switch
            {
                ShapeKind.Highlight => "Highlight",
                ShapeKind.Ellipse => "Ellipse",
                _ => "Rectangle"
            },
            BlurAnnotation => "Pixelate",
            ArrowAnnotation => "Arrow",
            LineAnnotation => "Line",
            CalloutAnnotation => "Callout",
            TextAnnotation => "Text",
            _ => annotation.Kind.ToString()
        };
    }

    private static string BuildDetail(AnnotationObject annotation)
    {
        var bounds = annotation.GetBounds().Normalize();
        return $"{Math.Round(bounds.Width)} x {Math.Round(bounds.Height)} at {Math.Round(bounds.X)}, {Math.Round(bounds.Y)}";
    }

    private static string Trim(string value, int maxLength)
    {
        var compact = string.Join(" ", value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return compact.Length <= maxLength
            ? compact
            : $"{compact[..maxLength].TrimEnd()}...";
    }
}
