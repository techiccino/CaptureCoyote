using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Primitives;

namespace CaptureCoyote.Core.Models;

public sealed class BlurAnnotation : AnnotationObject
{
    private PixelRect _bounds;
    private double _strength = 12;

    public BlurAnnotation() : base(AnnotationKind.Blur)
    {
        FillColor = new ArgbColor(48, 255, 255, 255);
    }

    public PixelRect Bounds
    {
        get => _bounds;
        set => SetProperty(ref _bounds, value.Normalize());
    }

    public double Strength
    {
        get => _strength;
        set => SetProperty(ref _strength, Math.Max(2, value));
    }

    public override PixelRect GetBounds() => Bounds;

    public override void Move(double deltaX, double deltaY) => Bounds = Bounds.Offset(deltaX, deltaY);

    public override void Resize(PixelRect newBounds) => Bounds = newBounds;

    public override AnnotationObject Clone() =>
        new BlurAnnotation
        {
            Id = Id,
            ZIndex = ZIndex,
            LayerName = LayerName,
            StrokeColor = StrokeColor,
            FillColor = FillColor,
            StrokeThickness = StrokeThickness,
            Opacity = Opacity,
            Bounds = Bounds,
            Strength = Strength
        };
}
