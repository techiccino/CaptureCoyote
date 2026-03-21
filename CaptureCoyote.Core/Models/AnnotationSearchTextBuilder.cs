namespace CaptureCoyote.Core.Models;

public static class AnnotationSearchTextBuilder
{
    public static string Build(IEnumerable<AnnotationObject> annotations)
    {
        var parts = annotations
            .OrderBy(annotation => annotation.ZIndex)
            .Select(BuildAnnotationText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return parts.Count == 0 ? string.Empty : string.Join(Environment.NewLine, parts);
    }

    private static string? BuildAnnotationText(AnnotationObject annotation)
    {
        var content = annotation switch
        {
            TextAnnotation text => text.Text,
            CalloutAnnotation callout => callout.Text,
            StepAnnotation step => $"Step {step.Number}",
            _ => null
        };

        if (annotation.HasCustomLayerName && !string.IsNullOrWhiteSpace(content))
        {
            return $"{annotation.LayerName}{Environment.NewLine}{content}";
        }

        return annotation.HasCustomLayerName ? annotation.LayerName : content;
    }
}
