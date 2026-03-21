using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Primitives;

namespace CaptureCoyote.Core.Models;

public sealed class ArrowAnnotation : AnnotationObject
{
    private PixelPoint _start;
    private PixelPoint _end;
    private double _headSize = 16;

    public ArrowAnnotation() : base(AnnotationKind.Arrow)
    {
    }

    public PixelPoint Start
    {
        get => _start;
        set => SetProperty(ref _start, value);
    }

    public PixelPoint End
    {
        get => _end;
        set => SetProperty(ref _end, value);
    }

    public double HeadSize
    {
        get => _headSize;
        set => SetProperty(ref _headSize, Math.Max(6, value));
    }

    public override PixelRect GetBounds() => PixelRect.FromPoints(Start, End).Inflate(Math.Max(HeadSize, StrokeThickness));

    public override void Move(double deltaX, double deltaY)
    {
        Start = Start + new PixelPoint(deltaX, deltaY);
        End = End + new PixelPoint(deltaX, deltaY);
    }

    public override void Resize(PixelRect newBounds)
    {
        Start = new PixelPoint(newBounds.X, newBounds.Y);
        End = new PixelPoint(newBounds.Right, newBounds.Bottom);
    }

    public override AnnotationObject Clone() =>
        new ArrowAnnotation
        {
            Id = Id,
            ZIndex = ZIndex,
            LayerName = LayerName,
            StrokeColor = StrokeColor,
            FillColor = FillColor,
            StrokeThickness = StrokeThickness,
            Opacity = Opacity,
            Start = Start,
            End = End,
            HeadSize = HeadSize
        };
}
