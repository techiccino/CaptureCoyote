using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Primitives;

namespace CaptureCoyote.Core.Models;

public sealed class StepAnnotation : AnnotationObject
{
    private PixelRect _bounds;
    private int _number = 1;
    private double _fontSize = 24;

    public StepAnnotation() : base(AnnotationKind.Step)
    {
        FillColor = ArgbColor.Accent;
        StrokeColor = ArgbColor.White;
        StrokeThickness = 3;
    }

    public PixelRect Bounds
    {
        get => _bounds;
        set => SetProperty(ref _bounds, value.Normalize());
    }

    public int Number
    {
        get => _number;
        set => SetProperty(ref _number, Math.Max(1, value));
    }

    public double FontSize
    {
        get => _fontSize;
        set => SetProperty(ref _fontSize, Math.Max(10, value));
    }

    public override PixelRect GetBounds() => Bounds;

    public override void Move(double deltaX, double deltaY) => Bounds = Bounds.Offset(deltaX, deltaY);

    public override void Resize(PixelRect newBounds) => Bounds = newBounds;

    public override AnnotationObject Clone() =>
        new StepAnnotation
        {
            Id = Id,
            ZIndex = ZIndex,
            LayerName = LayerName,
            StrokeColor = StrokeColor,
            FillColor = FillColor,
            StrokeThickness = StrokeThickness,
            Opacity = Opacity,
            Bounds = Bounds,
            Number = Number,
            FontSize = FontSize
        };
}
