using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Primitives;

namespace CaptureCoyote.Core.Models;

public sealed class TextAnnotation : AnnotationObject
{
    private PixelRect _bounds;
    private string _text = string.Empty;
    private double _fontSize = 26;
    private string _fontFamily = "Segoe UI";

    public TextAnnotation() : base(AnnotationKind.Text)
    {
        StrokeColor = ArgbColor.White;
        FillColor = new ArgbColor(44, 0, 0, 0);
        StrokeThickness = 0;
    }

    public PixelRect Bounds
    {
        get => _bounds;
        set => SetProperty(ref _bounds, value.Normalize());
    }

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public double FontSize
    {
        get => _fontSize;
        set => SetProperty(ref _fontSize, Math.Max(10, value));
    }

    public string FontFamily
    {
        get => _fontFamily;
        set => SetProperty(ref _fontFamily, string.IsNullOrWhiteSpace(value) ? "Segoe UI" : value);
    }

    public override PixelRect GetBounds() => Bounds;

    public override void Move(double deltaX, double deltaY) => Bounds = Bounds.Offset(deltaX, deltaY);

    public override void Resize(PixelRect newBounds) => Bounds = newBounds;

    public override AnnotationObject Clone() =>
        new TextAnnotation
        {
            Id = Id,
            ZIndex = ZIndex,
            LayerName = LayerName,
            StrokeColor = StrokeColor,
            FillColor = FillColor,
            StrokeThickness = StrokeThickness,
            Opacity = Opacity,
            Bounds = Bounds,
            Text = Text,
            FontSize = FontSize,
            FontFamily = FontFamily
        };
}
