using System.Text.Json.Serialization;
using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Mvvm;
using CaptureCoyote.Core.Primitives;

namespace CaptureCoyote.Core.Models;

[JsonDerivedType(typeof(ArrowAnnotation), nameof(ArrowAnnotation))]
[JsonDerivedType(typeof(CalloutAnnotation), nameof(CalloutAnnotation))]
[JsonDerivedType(typeof(ShapeAnnotation), nameof(ShapeAnnotation))]
[JsonDerivedType(typeof(LineAnnotation), nameof(LineAnnotation))]
[JsonDerivedType(typeof(TextAnnotation), nameof(TextAnnotation))]
[JsonDerivedType(typeof(StepAnnotation), nameof(StepAnnotation))]
[JsonDerivedType(typeof(BlurAnnotation), nameof(BlurAnnotation))]
public abstract class AnnotationObject : ObservableObject
{
    private int _zIndex;
    private ArgbColor _strokeColor = ArgbColor.Accent;
    private ArgbColor _fillColor = ArgbColor.Transparent;
    private double _strokeThickness = 4;
    private double _opacity = 1;
    private string _layerName = string.Empty;

    protected AnnotationObject(AnnotationKind kind)
    {
        Kind = kind;
    }

    public Guid Id { get; set; } = Guid.NewGuid();

    public AnnotationKind Kind { get; init; }

    public int ZIndex
    {
        get => _zIndex;
        set => SetProperty(ref _zIndex, value);
    }

    public ArgbColor StrokeColor
    {
        get => _strokeColor;
        set => SetProperty(ref _strokeColor, value);
    }

    public ArgbColor FillColor
    {
        get => _fillColor;
        set => SetProperty(ref _fillColor, value);
    }

    public double StrokeThickness
    {
        get => _strokeThickness;
        set => SetProperty(ref _strokeThickness, value);
    }

    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, value);
    }

    public string LayerName
    {
        get => _layerName;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            if (SetProperty(ref _layerName, normalized))
            {
                RaisePropertyChanged(nameof(HasCustomLayerName));
                RaisePropertyChanged(nameof(LayerDisplayName));
            }
        }
    }

    [JsonIgnore]
    public bool HasCustomLayerName => !string.IsNullOrWhiteSpace(LayerName);

    [JsonIgnore]
    public string LayerDisplayName => HasCustomLayerName ? LayerName : BuildDefaultLayerName();

    public abstract PixelRect GetBounds();

    public abstract void Move(double deltaX, double deltaY);

    public abstract void Resize(PixelRect newBounds);

    public abstract AnnotationObject Clone();

    private string BuildDefaultLayerName()
    {
        return this switch
        {
            ShapeAnnotation { ShapeKind: ShapeKind.Highlight } => "Highlight",
            ShapeAnnotation { ShapeKind: ShapeKind.Ellipse } => "Ellipse",
            ShapeAnnotation => "Rectangle",
            BlurAnnotation => "Pixelate",
            ArrowAnnotation => "Arrow",
            LineAnnotation => "Line",
            CalloutAnnotation => "Callout",
            TextAnnotation => "Text",
            StepAnnotation => "Step",
            _ => Kind.ToString()
        };
    }
}
