using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Primitives;

namespace CaptureCoyote.Core.Models;

public sealed class LineAnnotation : AnnotationObject
{
    private PixelPoint _start;
    private PixelPoint _end;

    public LineAnnotation() : base(AnnotationKind.Line)
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

    public override PixelRect GetBounds() => PixelRect.FromPoints(Start, End).Inflate(Math.Max(6, StrokeThickness));

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
        new LineAnnotation
        {
            Id = Id,
            ZIndex = ZIndex,
            LayerName = LayerName,
            StrokeColor = StrokeColor,
            FillColor = FillColor,
            StrokeThickness = StrokeThickness,
            Opacity = Opacity,
            Start = Start,
            End = End
        };
}
