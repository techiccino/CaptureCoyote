using CaptureCoyote.Core.Primitives;

namespace CaptureCoyote.Core.Models;

public sealed class StyleDefaults
{
    public ArgbColor StrokeColor { get; set; } = ArgbColor.Accent;

    public ArgbColor FillColor { get; set; } = ArgbColor.Transparent;

    public double StrokeThickness { get; set; } = 4;

    public double Opacity { get; set; } = 1;

    public double FontSize { get; set; } = 26;

    public double BlurStrength { get; set; } = 12;

    public static StyleDefaults CreateDefault() => new();

    public StyleDefaults Clone() => new()
    {
        StrokeColor = StrokeColor,
        FillColor = FillColor,
        StrokeThickness = StrokeThickness,
        Opacity = Opacity,
        FontSize = FontSize,
        BlurStrength = BlurStrength
    };
}
