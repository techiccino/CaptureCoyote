using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Primitives;

namespace CaptureCoyote.Core.Models;

public sealed class ShapeAnnotation : AnnotationObject
{
    private PixelRect _bounds;
    private ShapeKind _shapeKind;

    public ShapeAnnotation() : base(AnnotationKind.Rectangle)
    {
    }

    public ShapeKind ShapeKind
    {
        get => _shapeKind;
        set => SetProperty(ref _shapeKind, value);
    }

    public PixelRect Bounds
    {
        get => _bounds;
        set => SetProperty(ref _bounds, value.Normalize());
    }

    public override PixelRect GetBounds() => Bounds;

    public override void Move(double deltaX, double deltaY) => Bounds = Bounds.Offset(deltaX, deltaY);

    public override void Resize(PixelRect newBounds) => Bounds = newBounds;

    public override AnnotationObject Clone() =>
        new ShapeAnnotation
        {
            Id = Id,
            Kind = ShapeKind switch
            {
                ShapeKind.Ellipse => AnnotationKind.Ellipse,
                ShapeKind.Highlight => AnnotationKind.Highlight,
                _ => AnnotationKind.Rectangle
            },
            ZIndex = ZIndex,
            LayerName = LayerName,
            StrokeColor = StrokeColor,
            FillColor = FillColor,
            StrokeThickness = StrokeThickness,
            Opacity = Opacity,
            Bounds = Bounds,
            ShapeKind = ShapeKind
        };
}
