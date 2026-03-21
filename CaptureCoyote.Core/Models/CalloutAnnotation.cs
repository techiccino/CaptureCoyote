using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Primitives;

namespace CaptureCoyote.Core.Models;

public sealed class CalloutAnnotation : AnnotationObject
{
    private PixelRect _bounds;
    private PixelPoint _anchor;
    private string _text = string.Empty;
    private double _fontSize = 24;
    private string _fontFamily = "Segoe UI";

    public CalloutAnnotation() : base(AnnotationKind.Callout)
    {
        StrokeColor = ArgbColor.Accent;
        FillColor = new ArgbColor(224, 12, 17, 24);
        StrokeThickness = 2.5;
    }

    public PixelRect Bounds
    {
        get => _bounds;
        set => SetProperty(ref _bounds, value.Normalize());
    }

    public PixelPoint Anchor
    {
        get => _anchor;
        set => SetProperty(ref _anchor, value);
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

    public override PixelRect GetBounds()
    {
        var union = PixelRect.FromPoints(
            new PixelPoint(Math.Min(Bounds.X, Anchor.X), Math.Min(Bounds.Y, Anchor.Y)),
            new PixelPoint(Math.Max(Bounds.Right, Anchor.X), Math.Max(Bounds.Bottom, Anchor.Y)));
        return union.Inflate(Math.Max(8, StrokeThickness + 4));
    }

    public override void Move(double deltaX, double deltaY)
    {
        Bounds = Bounds.Offset(deltaX, deltaY);
        Anchor = Anchor + new PixelPoint(deltaX, deltaY);
    }

    public override void Resize(PixelRect newBounds) => Bounds = newBounds;

    public override AnnotationObject Clone() =>
        new CalloutAnnotation
        {
            Id = Id,
            ZIndex = ZIndex,
            LayerName = LayerName,
            StrokeColor = StrokeColor,
            FillColor = FillColor,
            StrokeThickness = StrokeThickness,
            Opacity = Opacity,
            Bounds = Bounds,
            Anchor = Anchor,
            Text = Text,
            FontSize = FontSize,
            FontFamily = FontFamily
        };
}
