using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Core.Primitives;
using CaptureCoyote.Editor.ViewModels;

namespace CaptureCoyote.Editor.Controls;

public partial class AnnotationCanvasControl : UserControl
{
    private const double HandleSize = 10;
    private const double EndpointHandleSize = 12;
    private const double SnapThreshold = 6;
    private enum InteractionMode
    {
        None,
        Drawing,
        Moving,
        Resizing,
        Cropping
    }

    private enum ResizeHandle
    {
        None,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        LineStart,
        LineEnd
    }

    private const double HitPadding = 10;
    private const double MinZoomFactor = 0.35;
    private const double MaxZoomFactor = 4.0;
    private const double ZoomStep = 0.15;
    private readonly record struct SnapGuide(bool IsVertical, double Coordinate);

    private InteractionMode _interactionMode;
    private ResizeHandle _resizeHandle;
    private PixelPoint _dragStartPoint;
    private PixelRect _originalBounds;
    private AnnotationObject? _workingAnnotation;
    private PixelRect? _previewCropRect;
    private AnnotationObject? _editingTextAnnotation;
    private readonly List<SnapGuide> _activeSnapGuides = [];
    private readonly TextBox _inlineTextEditor;
    private double _zoomFactor = 1;
    private bool _fitToViewport = true;

    public AnnotationCanvasControl()
    {
        InitializeComponent();

        _inlineTextEditor = new TextBox
        {
            Visibility = Visibility.Collapsed,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Background = new SolidColorBrush(Color.FromArgb(220, 18, 24, 32)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(24, 132, 196)),
            BorderThickness = new Thickness(2),
            Padding = new Thickness(8),
            FontSize = 24
        };

        _inlineTextEditor.LostFocus += (_, _) => CommitInlineText();
        _inlineTextEditor.PreviewKeyDown += InlineTextEditorOnPreviewKeyDown;

        SurfaceCanvas.MouseLeftButtonDown += SurfaceCanvasOnMouseLeftButtonDown;
        SurfaceCanvas.MouseMove += SurfaceCanvasOnMouseMove;
        SurfaceCanvas.MouseLeftButtonUp += SurfaceCanvasOnMouseLeftButtonUp;
        PreviewMouseWheel += AnnotationCanvasControlOnPreviewMouseWheel;
        PreviewKeyDown += AnnotationCanvasControlOnPreviewKeyDown;
        Loaded += AnnotationCanvasControlOnLoaded;
        SizeChanged += AnnotationCanvasControlOnSizeChanged;
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(EditorViewModel),
        typeof(AnnotationCanvasControl),
        new PropertyMetadata(null, OnViewModelChanged));

    public EditorViewModel? ViewModel
    {
        get => (EditorViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (AnnotationCanvasControl)d;

        if (e.OldValue is EditorViewModel oldViewModel)
        {
            oldViewModel.SurfaceInvalidated -= control.ViewModelOnSurfaceInvalidated;
        }

        if (e.NewValue is EditorViewModel newViewModel)
        {
            newViewModel.SurfaceInvalidated += control.ViewModelOnSurfaceInvalidated;
        }

        control.RefreshSurface();
    }

    private void ViewModelOnSurfaceInvalidated(object? sender, EventArgs e) => RefreshSurface();

    private void AnnotationCanvasControlOnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshSurface();
        ApplyFitZoom();
    }

    private void AnnotationCanvasControlOnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_fitToViewport && IsLoaded)
        {
            ApplyFitZoom();
        }
    }

    private void SurfaceCanvasOnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        Keyboard.Focus(this);

        if (ViewModel is null)
        {
            return;
        }

        var imagePoint = ToImagePoint(e.GetPosition(SurfaceCanvas));
        var localPoint = e.GetPosition(SurfaceCanvas);
        CommitInlineText();

        switch (ViewModel.SelectedTool)
        {
            case EditorTool.Select:
                HandleSelectMouseDown(imagePoint, localPoint, e.ClickCount);
                break;
            case EditorTool.Text:
                ViewModel.CaptureUndoState();
                var text = new TextAnnotation
                {
                    Bounds = new PixelRect(imagePoint.X, imagePoint.Y, 280, 88),
                    Text = string.Empty
                };
                ViewModel.AddAnnotation(text);
                BeginInlineTextEdit(text);
                break;
            case EditorTool.Callout:
                ViewModel.CaptureUndoState();
                var callout = new CalloutAnnotation
                {
                    Anchor = imagePoint,
                    Bounds = new PixelRect(imagePoint.X + 28, imagePoint.Y - 20, 300, 112),
                    Text = string.Empty
                };
                ViewModel.AddAnnotation(callout);
                BeginInlineTextEdit(callout);
                break;
            case EditorTool.Step:
                ViewModel.CaptureUndoState();
                var step = new StepAnnotation
                {
                    Bounds = new PixelRect(imagePoint.X - 26, imagePoint.Y - 26, 52, 52),
                    Number = ViewModel.GetNextStepNumber()
                };
                ViewModel.AddAnnotation(step);
                break;
            case EditorTool.Crop:
                ViewModel.CaptureUndoState();
                _interactionMode = InteractionMode.Cropping;
                _dragStartPoint = imagePoint;
                _previewCropRect = new PixelRect(imagePoint.X, imagePoint.Y, 0, 0);
                Mouse.Capture(SurfaceCanvas);
                ShowHint("Drag to define a new crop area");
                break;
            default:
                ViewModel.CaptureUndoState();
                _workingAnnotation = CreateAnnotationForTool(ViewModel.SelectedTool, imagePoint);
                if (_workingAnnotation is null)
                {
                    return;
                }

                ViewModel.AddAnnotation(_workingAnnotation, select: false);
                _interactionMode = InteractionMode.Drawing;
                _dragStartPoint = imagePoint;
                Mouse.Capture(SurfaceCanvas);
                ShowHint("Release to place the annotation");
                break;
        }

        RefreshSurface();
        e.Handled = true;
    }

    private void HandleSelectMouseDown(PixelPoint imagePoint, Point localPoint, int clickCount)
    {
        if (ViewModel is null)
        {
            return;
        }

        if (ViewModel.SelectedAnnotation is not null)
        {
            var handle = HitTestResizeHandle(localPoint, ViewModel.SelectedAnnotation);
            if (handle != ResizeHandle.None)
            {
                ViewModel.CaptureUndoState();
                _interactionMode = InteractionMode.Resizing;
                _resizeHandle = handle;
                _dragStartPoint = imagePoint;
                _originalBounds = ViewModel.SelectedAnnotation.GetBounds();
                Mouse.Capture(SurfaceCanvas);
                ShowHint(IsEndpointHandle(handle) ? "Drag an endpoint to reshape the line" : "Resize the selected annotation");
                return;
            }
        }

        var hit = HitTestAnnotation(imagePoint);
        ViewModel.SelectedAnnotation = hit;
        if (hit is null)
        {
            HideHint();
            return;
        }

        if (clickCount >= 2 && hit is TextAnnotation or CalloutAnnotation)
        {
            BeginInlineTextEdit(hit);
            HideHint();
            return;
        }

        ViewModel.CaptureUndoState();
        _interactionMode = InteractionMode.Moving;
        _dragStartPoint = imagePoint;
        _workingAnnotation = hit;
        Mouse.Capture(SurfaceCanvas);
        ShowHint("Drag to move the selected annotation");
    }

    private void SurfaceCanvasOnMouseMove(object sender, MouseEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var imagePoint = ToImagePoint(e.GetPosition(SurfaceCanvas));
        switch (_interactionMode)
        {
            case InteractionMode.Drawing:
                UpdateWorkingAnnotation(imagePoint);
                break;
            case InteractionMode.Moving:
                MoveSelectedAnnotation(imagePoint);
                break;
            case InteractionMode.Resizing:
                ResizeSelectedAnnotation(imagePoint);
                break;
            case InteractionMode.Cropping:
                _previewCropRect = PixelRect.FromPoints(_dragStartPoint, imagePoint);
                RefreshSurface();
                break;
            default:
                UpdateCursor(e.GetPosition(SurfaceCanvas));
                break;
        }
    }

    private void SurfaceCanvasOnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        Mouse.Capture(null);

        switch (_interactionMode)
        {
            case InteractionMode.Drawing:
                if (_workingAnnotation is not null && IsTooSmall(_workingAnnotation))
                {
                    ViewModel.RemoveAnnotation(_workingAnnotation);
                }
                else
                {
                    ViewModel.SelectedAnnotation = _workingAnnotation;
                    ViewModel.NotifyAnnotationChanged("Annotation created.");
                }

                _workingAnnotation = null;
                break;
            case InteractionMode.Moving:
                ViewModel.NotifyAnnotationChanged("Annotation moved.");
                _workingAnnotation = null;
                _activeSnapGuides.Clear();
                break;
            case InteractionMode.Resizing:
                ViewModel.NotifyAnnotationChanged("Annotation resized.");
                _activeSnapGuides.Clear();
                break;
            case InteractionMode.Cropping:
                if (_previewCropRect is { } crop && crop.Width > 6 && crop.Height > 6)
                {
                    ViewModel.SetCrop(crop);
                }
                else
                {
                    ViewModel.ClearCrop();
                }

                _previewCropRect = null;
                _activeSnapGuides.Clear();
                break;
        }

        _interactionMode = InteractionMode.None;
        _resizeHandle = ResizeHandle.None;
        HideHint();
        RefreshSurface();
    }

    private void UpdateWorkingAnnotation(PixelPoint currentPoint)
    {
        if (_workingAnnotation is null)
        {
            return;
        }

        var rect = PixelRect.FromPoints(_dragStartPoint, currentPoint);
        switch (_workingAnnotation)
        {
            case ShapeAnnotation shape:
                shape.Bounds = rect;
                break;
            case BlurAnnotation blur:
                blur.Bounds = rect;
                break;
            case LineAnnotation line:
                line.Start = _dragStartPoint;
                line.End = currentPoint;
                break;
            case ArrowAnnotation arrow:
                arrow.Start = _dragStartPoint;
                arrow.End = currentPoint;
                break;
        }

        RefreshSurface();
    }

    private void MoveSelectedAnnotation(PixelPoint currentPoint)
    {
        if (ViewModel?.SelectedAnnotation is null)
        {
            return;
        }

        var delta = currentPoint - _dragStartPoint;
        _dragStartPoint = currentPoint;
        _activeSnapGuides.Clear();
        ViewModel.SelectedAnnotation.Move(delta.X, delta.Y);
        RefreshSurface();
    }

    private void ResizeSelectedAnnotation(PixelPoint currentPoint)
    {
        if (ViewModel?.SelectedAnnotation is null)
        {
            return;
        }

        if (TryResizeLineEndpoint(ViewModel.SelectedAnnotation, currentPoint))
        {
            _activeSnapGuides.Clear();
            RefreshSurface();
            return;
        }

        if (TryResizeCalloutAnchor(ViewModel.SelectedAnnotation, currentPoint))
        {
            _activeSnapGuides.Clear();
            RefreshSurface();
            return;
        }

        var resized = _resizeHandle switch
        {
            ResizeHandle.TopLeft => PixelRect.FromPoints(currentPoint, new PixelPoint(_originalBounds.Right, _originalBounds.Bottom)),
            ResizeHandle.TopRight => PixelRect.FromPoints(new PixelPoint(_originalBounds.X, _originalBounds.Bottom), currentPoint),
            ResizeHandle.BottomLeft => PixelRect.FromPoints(new PixelPoint(_originalBounds.Right, _originalBounds.Y), currentPoint),
            ResizeHandle.BottomRight => PixelRect.FromPoints(new PixelPoint(_originalBounds.X, _originalBounds.Y), currentPoint),
            _ => _originalBounds
        };

        _activeSnapGuides.Clear();
        ViewModel.SelectedAnnotation.Resize(resized);
        RefreshSurface();
    }

    private void UpdateCursor(Point localPoint)
    {
        if (ViewModel?.SelectedAnnotation is null)
        {
            Cursor = Cursors.Arrow;
            return;
        }

        var handle = HitTestResizeHandle(localPoint, ViewModel.SelectedAnnotation);
        Cursor = handle switch
        {
            ResizeHandle.TopLeft or ResizeHandle.BottomRight => Cursors.SizeNWSE,
            ResizeHandle.TopRight or ResizeHandle.BottomLeft => Cursors.SizeNESW,
            ResizeHandle.LineStart or ResizeHandle.LineEnd => Cursors.Hand,
            _ => HitTestAnnotation(ToImagePoint(localPoint)) is not null ? Cursors.SizeAll : Cursors.Arrow
        };
    }

    private AnnotationObject? HitTestAnnotation(PixelPoint point)
    {
        if (ViewModel is null)
        {
            return null;
        }

        foreach (var annotation in ViewModel.Annotations.OrderByDescending(annotation => annotation.ZIndex))
        {
            switch (annotation)
            {
                case LineAnnotation line when DistanceToSegment(point, line.Start, line.End) <= HitPadding:
                    return annotation;
                case ArrowAnnotation arrow when DistanceToSegment(point, arrow.Start, arrow.End) <= HitPadding:
                    return annotation;
                case CalloutAnnotation callout when
                    callout.Bounds.Inflate(HitPadding / 2).Contains(point) ||
                    DistanceToSegment(point, callout.Anchor, GetCalloutConnectionPoint(callout.Bounds, callout.Anchor)) <= HitPadding:
                    return annotation;
                default:
                    if (annotation.GetBounds().Inflate(HitPadding / 2).Contains(point))
                    {
                        return annotation;
                    }

                    break;
            }
        }

        return null;
    }

    private ResizeHandle HitTestResizeHandle(Point localPoint, AnnotationObject annotation)
    {
        if (annotation is LineAnnotation line)
        {
            return HitTestLineEndpoints(localPoint, line.Start, line.End);
        }

        if (annotation is ArrowAnnotation arrow)
        {
            return HitTestLineEndpoints(localPoint, arrow.Start, arrow.End);
        }

        if (annotation is CalloutAnnotation callout)
        {
            var anchorPoint = ToLocalPoint(callout.Anchor);
            if (Distance(localPoint, anchorPoint) <= EndpointHandleSize)
            {
                return ResizeHandle.LineStart;
            }
        }

        var bounds = annotation.GetBounds();
        var localBounds = ToLocalRect(bounds);
        var corners = new Dictionary<ResizeHandle, Point>
        {
            [ResizeHandle.TopLeft] = new(localBounds.X, localBounds.Y),
            [ResizeHandle.TopRight] = new(localBounds.Right, localBounds.Y),
            [ResizeHandle.BottomLeft] = new(localBounds.X, localBounds.Bottom),
            [ResizeHandle.BottomRight] = new(localBounds.Right, localBounds.Bottom)
        };

        foreach (var pair in corners)
        {
            if (Math.Abs(localPoint.X - pair.Value.X) <= HandleSize &&
                Math.Abs(localPoint.Y - pair.Value.Y) <= HandleSize)
            {
                return pair.Key;
            }
        }

        return ResizeHandle.None;
    }

    private ResizeHandle HitTestLineEndpoints(Point localPoint, PixelPoint start, PixelPoint end)
    {
        var localStart = ToLocalPoint(start);
        var localEnd = ToLocalPoint(end);

        if (Distance(localPoint, localStart) <= EndpointHandleSize)
        {
            return ResizeHandle.LineStart;
        }

        if (Distance(localPoint, localEnd) <= EndpointHandleSize)
        {
            return ResizeHandle.LineEnd;
        }

        return ResizeHandle.None;
    }

    private AnnotationObject? CreateAnnotationForTool(EditorTool tool, PixelPoint imagePoint)
    {
        return tool switch
        {
            EditorTool.Arrow => new ArrowAnnotation { Start = imagePoint, End = imagePoint },
            EditorTool.Callout => null,
            EditorTool.Rectangle => new ShapeAnnotation { ShapeKind = ShapeKind.Rectangle, Kind = AnnotationKind.Rectangle, Bounds = new PixelRect(imagePoint.X, imagePoint.Y, 0, 0) },
            EditorTool.Ellipse => new ShapeAnnotation { ShapeKind = ShapeKind.Ellipse, Kind = AnnotationKind.Ellipse, Bounds = new PixelRect(imagePoint.X, imagePoint.Y, 0, 0) },
            EditorTool.Line => new LineAnnotation { Start = imagePoint, End = imagePoint },
            EditorTool.Highlight => new ShapeAnnotation { ShapeKind = ShapeKind.Highlight, Kind = AnnotationKind.Highlight, Bounds = new PixelRect(imagePoint.X, imagePoint.Y, 0, 0) },
            EditorTool.Blur => new BlurAnnotation { Bounds = new PixelRect(imagePoint.X, imagePoint.Y, 0, 0) },
            _ => null
        };
    }

    private static bool IsTooSmall(AnnotationObject annotation)
    {
        if (annotation is LineAnnotation line)
        {
            return Distance(line.Start, line.End) < 8;
        }

        if (annotation is ArrowAnnotation arrow)
        {
            return Distance(arrow.Start, arrow.End) < 8;
        }

        var bounds = annotation.GetBounds();
        return bounds.Width < 6 || bounds.Height < 6;
    }

    private void BeginInlineTextEdit(AnnotationObject annotation)
    {
        _editingTextAnnotation = annotation;
        _inlineTextEditor.Text = GetEditableText(annotation);
        _inlineTextEditor.FontSize = Math.Max(12, GetEditableFontSize(annotation));
        _inlineTextEditor.FontFamily = new FontFamily(GetEditableFontFamily(annotation));
        _inlineTextEditor.Foreground = new SolidColorBrush(ToColor(annotation.StrokeColor));
        _inlineTextEditor.CaretBrush = _inlineTextEditor.Foreground;
        _inlineTextEditor.Background = new SolidColorBrush(GetInlineEditorBackground(annotation));
        _inlineTextEditor.Visibility = Visibility.Visible;
        RefreshSurface();
        _inlineTextEditor.Focus();
        _inlineTextEditor.SelectAll();
    }

    private void CommitInlineText()
    {
        if (_editingTextAnnotation is null || ViewModel is null)
        {
            return;
        }

        SetEditableText(_editingTextAnnotation, _inlineTextEditor.Text);
        if (string.IsNullOrWhiteSpace(GetEditableText(_editingTextAnnotation)))
        {
            ViewModel.RemoveAnnotation(_editingTextAnnotation);
        }
        else
        {
            ViewModel.NotifyAnnotationChanged("Text updated.");
        }

        _editingTextAnnotation = null;
        _inlineTextEditor.Visibility = Visibility.Collapsed;
        RefreshSurface();
    }

    private void InlineTextEditorOnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _editingTextAnnotation = null;
            _inlineTextEditor.Visibility = Visibility.Collapsed;
            RefreshSurface();
            e.Handled = true;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            CommitInlineText();
            e.Handled = true;
        }
    }

    private void RefreshSurface()
    {
        if (!IsLoaded || ViewModel is null)
        {
            return;
        }

        SurfaceCanvas.Children.Clear();
        var visibleBounds = ViewModel.GetVisibleBounds();
        SurfaceCanvas.Width = Math.Max(1, visibleBounds.Width);
        SurfaceCanvas.Height = Math.Max(1, visibleBounds.Height);

        var background = new Image
        {
            Source = CreateVisibleBitmap(visibleBounds),
            Width = visibleBounds.Width,
            Height = visibleBounds.Height,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };

        SurfaceCanvas.Children.Add(background);

        foreach (var annotation in ViewModel.Annotations.OrderBy(annotation => annotation.ZIndex))
        {
            var element = CreateAnnotationElement(annotation);
            if (element is not null)
            {
                SurfaceCanvas.Children.Add(element);
            }
        }

        if (_previewCropRect is { } cropPreview)
        {
            AddBoundsSelectionOverlay(cropPreview, Colors.White, [4d, 4d]);
        }
        else if (ViewModel.SelectedAnnotation is not null)
        {
            AddSelectionOverlay(ViewModel.SelectedAnnotation, Color.FromRgb(24, 132, 196), [3d, 3d]);
        }

        AddSnapGuides();

        if (_editingTextAnnotation is not null)
        {
            var localBounds = ToLocalRect(GetEditableBounds(_editingTextAnnotation));
            Canvas.SetLeft(_inlineTextEditor, localBounds.X);
            Canvas.SetTop(_inlineTextEditor, localBounds.Y);
            _inlineTextEditor.Width = Math.Max(120, localBounds.Width);
            _inlineTextEditor.Height = Math.Max(48, localBounds.Height);
            if (!SurfaceCanvas.Children.Contains(_inlineTextEditor))
            {
                SurfaceCanvas.Children.Add(_inlineTextEditor);
            }
        }

        if (_fitToViewport)
        {
            ApplyFitZoom();
        }
    }

    private UIElement? CreateAnnotationElement(AnnotationObject annotation)
    {
        return annotation switch
        {
            ShapeAnnotation shape => CreateShapeElement(shape),
            BlurAnnotation blur => CreateBlurElement(blur),
            TextAnnotation text => CreateTextElement(text),
            CalloutAnnotation callout => CreateCalloutElement(callout),
            StepAnnotation step => CreateStepElement(step),
            LineAnnotation line => CreateLineElement(line),
            ArrowAnnotation arrow => CreateArrowElement(arrow),
            _ => null
        };
    }

    private UIElement? CreateShapeElement(ShapeAnnotation annotation)
    {
        var bounds = ToLocalRect(annotation.Bounds);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        FrameworkElement element = annotation.ShapeKind switch
        {
            ShapeKind.Ellipse => new Ellipse
            {
                Width = bounds.Width,
                Height = bounds.Height,
                Stroke = CreateBrush(annotation.StrokeColor, annotation.Opacity),
                StrokeThickness = annotation.StrokeThickness,
                Fill = CreateBrush(annotation.FillColor, annotation.Opacity)
            },
            ShapeKind.Highlight => new Border
            {
                Width = bounds.Width,
                Height = bounds.Height,
                Background = CreateBrush(annotation.FillColor == ArgbColor.Transparent ? ArgbColor.Highlight : annotation.FillColor, 0.35),
                CornerRadius = new CornerRadius(10)
            },
            _ => new Border
            {
                Width = bounds.Width,
                Height = bounds.Height,
                BorderBrush = CreateBrush(annotation.StrokeColor, annotation.Opacity),
                BorderThickness = new Thickness(Math.Max(1, annotation.StrokeThickness)),
                Background = CreateBrush(annotation.FillColor, annotation.Opacity),
                CornerRadius = new CornerRadius(10)
            }
        };

        element.IsHitTestVisible = false;
        Canvas.SetLeft(element, bounds.X);
        Canvas.SetTop(element, bounds.Y);
        return element;
    }

    private UIElement? CreateBlurElement(BlurAnnotation annotation)
    {
        var bounds = ToLocalRect(annotation.Bounds);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        var grid = new Grid
        {
            Width = bounds.Width,
            Height = bounds.Height,
            IsHitTestVisible = false
        };

        grid.Children.Add(new Image
        {
            Source = CreatePixelatedPreview(annotation.Bounds, annotation.Strength),
            Stretch = Stretch.Fill
        });

        grid.Children.Add(new Border
        {
            BorderBrush = CreateBrush(annotation.StrokeColor, Math.Min(1, annotation.Opacity)),
            BorderThickness = new Thickness(Math.Max(1, annotation.StrokeThickness)),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255))
        });

        Canvas.SetLeft(grid, bounds.X);
        Canvas.SetTop(grid, bounds.Y);
        return grid;
    }

    private UIElement? CreateTextElement(TextAnnotation annotation)
    {
        if (ReferenceEquals(annotation, _editingTextAnnotation))
        {
            return null;
        }

        var bounds = ToLocalRect(annotation.Bounds);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        var border = new Border
        {
            Width = bounds.Width,
            Height = bounds.Height,
            Background = CreateBrush(annotation.FillColor, Math.Min(annotation.Opacity, 0.6)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = annotation.Text,
                Foreground = CreateBrush(annotation.StrokeColor, annotation.Opacity),
                FontSize = Math.Max(12, annotation.FontSize),
                FontFamily = new FontFamily(annotation.FontFamily),
                TextWrapping = TextWrapping.Wrap
            }
        };

        Canvas.SetLeft(border, bounds.X);
        Canvas.SetTop(border, bounds.Y);
        return border;
    }

    private UIElement? CreateCalloutElement(CalloutAnnotation annotation)
    {
        if (ReferenceEquals(annotation, _editingTextAnnotation))
        {
            return null;
        }

        var bounds = ToLocalRect(annotation.Bounds);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        var anchor = ToLocalPoint(annotation.Anchor);
        var connection = ToLocalPoint(GetCalloutConnectionPoint(annotation.Bounds, annotation.Anchor));
        var stroke = CreateBrush(annotation.StrokeColor, annotation.Opacity);

        var canvas = new Canvas
        {
            Width = SurfaceCanvas.Width,
            Height = SurfaceCanvas.Height,
            IsHitTestVisible = false
        };

        canvas.Children.Add(new Line
        {
            X1 = anchor.X,
            Y1 = anchor.Y,
            X2 = connection.X,
            Y2 = connection.Y,
            Stroke = stroke,
            StrokeThickness = Math.Max(1.4, annotation.StrokeThickness)
        });

        canvas.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = stroke,
            Stroke = Brushes.White,
            StrokeThickness = 1
        });
        Canvas.SetLeft(canvas.Children[^1], anchor.X - 4);
        Canvas.SetTop(canvas.Children[^1], anchor.Y - 4);

        var balloon = new Border
        {
            Width = bounds.Width,
            Height = bounds.Height,
            Background = CreateBrush(annotation.FillColor, Math.Min(annotation.Opacity, 0.92)),
            BorderBrush = stroke,
            BorderThickness = new Thickness(Math.Max(1, annotation.StrokeThickness)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10),
            Child = new TextBlock
            {
                Text = annotation.Text,
                Foreground = CreateBrush(annotation.StrokeColor, annotation.Opacity),
                FontSize = Math.Max(12, annotation.FontSize),
                FontFamily = new FontFamily(annotation.FontFamily),
                TextWrapping = TextWrapping.Wrap
            }
        };

        canvas.Children.Add(balloon);
        Canvas.SetLeft(balloon, bounds.X);
        Canvas.SetTop(balloon, bounds.Y);
        return canvas;
    }

    private UIElement? CreateStepElement(StepAnnotation annotation)
    {
        var bounds = ToLocalRect(annotation.Bounds);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        var grid = new Grid
        {
            Width = bounds.Width,
            Height = bounds.Height,
            IsHitTestVisible = false
        };

        grid.Children.Add(new Ellipse
        {
            Fill = CreateBrush(annotation.FillColor, annotation.Opacity),
            Stroke = CreateBrush(annotation.StrokeColor, annotation.Opacity),
            StrokeThickness = annotation.StrokeThickness
        });

        grid.Children.Add(new TextBlock
        {
            Text = annotation.Number.ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = CreateBrush(annotation.StrokeColor, annotation.Opacity),
            FontWeight = FontWeights.Bold,
            FontSize = Math.Max(12, annotation.FontSize)
        });

        Canvas.SetLeft(grid, bounds.X);
        Canvas.SetTop(grid, bounds.Y);
        return grid;
    }

    private UIElement CreateLineElement(LineAnnotation annotation)
    {
        return new Path
        {
            Stroke = CreateBrush(annotation.StrokeColor, annotation.Opacity),
            StrokeThickness = Math.Max(1, annotation.StrokeThickness),
            Data = new LineGeometry(ToLocalPoint(annotation.Start), ToLocalPoint(annotation.End)),
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        };
    }

    private UIElement CreateArrowElement(ArrowAnnotation annotation)
    {
        var start = ToLocalPoint(annotation.Start);
        var end = ToLocalPoint(annotation.End);
        var direction = start - end;
        if (direction.Length < 0.5)
        {
            return new Path
            {
                Stroke = CreateBrush(annotation.StrokeColor, annotation.Opacity),
                StrokeThickness = Math.Max(1, annotation.StrokeThickness),
                Data = new LineGeometry(start, end),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false
            };
        }

        direction.Normalize();
        var perpendicular = new Vector(-direction.Y, direction.X);
        var headSize = Math.Max(10, annotation.HeadSize);
        var point1 = end + (direction * headSize) + (perpendicular * (headSize * 0.45));
        var point2 = end + (direction * headSize) - (perpendicular * (headSize * 0.45));

        var geometryGroup = new GeometryGroup();
        geometryGroup.Children.Add(new LineGeometry(start, end));
        geometryGroup.Children.Add(new LineGeometry(end, point1));
        geometryGroup.Children.Add(new LineGeometry(end, point2));

        return new Path
        {
            Stroke = CreateBrush(annotation.StrokeColor, annotation.Opacity),
            StrokeThickness = Math.Max(1, annotation.StrokeThickness),
            Data = geometryGroup,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        };
    }

    private BitmapSource CreateVisibleBitmap(PixelRect visibleBounds)
    {
        var rect = new Int32Rect(
            (int)Math.Round(visibleBounds.X),
            (int)Math.Round(visibleBounds.Y),
            Math.Max(1, (int)Math.Round(visibleBounds.Width)),
            Math.Max(1, (int)Math.Round(visibleBounds.Height)));

        var cropped = new CroppedBitmap(ViewModel!.BaseImage, rect);
        cropped.Freeze();
        return cropped;
    }

    private BitmapSource CreatePixelatedPreview(PixelRect originalBounds, double strength)
    {
        var safe = originalBounds.Normalize().ClampWithin(new PixelRect(0, 0, ViewModel!.BaseImage.PixelWidth, ViewModel.BaseImage.PixelHeight));
        var rect = new Int32Rect(
            (int)Math.Round(safe.X),
            (int)Math.Round(safe.Y),
            Math.Max(1, (int)Math.Round(safe.Width)),
            Math.Max(1, (int)Math.Round(safe.Height)));

        var cropped = new CroppedBitmap(ViewModel.BaseImage, rect);
        BitmapSource converted = cropped;
        if (cropped.Format != PixelFormats.Bgra32)
        {
            converted = new FormatConvertedBitmap(cropped, PixelFormats.Bgra32, null, 0);
        }

        var stride = rect.Width * 4;
        var pixels = new byte[stride * rect.Height];
        converted.CopyPixels(pixels, stride, 0);
        var blockSize = Math.Max(2, (int)Math.Round(strength));

        for (var y = 0; y < rect.Height; y += blockSize)
        {
            for (var x = 0; x < rect.Width; x += blockSize)
            {
                var sampleIndex = (y * stride) + (x * 4);
                var b = pixels[sampleIndex];
                var g = pixels[sampleIndex + 1];
                var r = pixels[sampleIndex + 2];
                var a = pixels[sampleIndex + 3];

                for (var yy = y; yy < Math.Min(rect.Height, y + blockSize); yy++)
                {
                    for (var xx = x; xx < Math.Min(rect.Width, x + blockSize); xx++)
                    {
                        var index = (yy * stride) + (xx * 4);
                        pixels[index] = b;
                        pixels[index + 1] = g;
                        pixels[index + 2] = r;
                        pixels[index + 3] = a;
                    }
                }
            }
        }

        var bitmap = new WriteableBitmap(rect.Width, rect.Height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, rect.Width, rect.Height), pixels, stride, 0);
        bitmap.Freeze();
        return bitmap;
    }

    private void AddSelectionOverlay(AnnotationObject annotation, Color borderColor, DoubleCollection dashArray)
    {
        if (annotation is LineAnnotation line)
        {
            AddLineSelectionOverlay(line.Start, line.End, borderColor, dashArray);
            return;
        }

        if (annotation is ArrowAnnotation arrow)
        {
            AddLineSelectionOverlay(arrow.Start, arrow.End, borderColor, dashArray);
            return;
        }

        if (annotation is CalloutAnnotation callout)
        {
            AddCalloutSelectionOverlay(callout, borderColor, dashArray);
            return;
        }

        AddBoundsSelectionOverlay(annotation.GetBounds(), borderColor, dashArray);
    }

    private void AddBoundsSelectionOverlay(PixelRect originalBounds, Color borderColor, DoubleCollection dashArray)
    {
        var bounds = ToLocalRect(originalBounds);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var rectangle = new Rectangle
        {
            Width = bounds.Width,
            Height = bounds.Height,
            Stroke = new SolidColorBrush(borderColor),
            StrokeDashArray = dashArray,
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };

        Canvas.SetLeft(rectangle, bounds.X);
        Canvas.SetTop(rectangle, bounds.Y);
        SurfaceCanvas.Children.Add(rectangle);

        foreach (var point in new[]
                 {
                     new Point(bounds.X, bounds.Y),
                     new Point(bounds.Right, bounds.Y),
                     new Point(bounds.X, bounds.Bottom),
                     new Point(bounds.Right, bounds.Bottom)
                 })
        {
            var handle = new Rectangle
            {
                Width = HandleSize,
                Height = HandleSize,
                RadiusX = 2,
                RadiusY = 2,
                Fill = new SolidColorBrush(borderColor),
                Stroke = Brushes.White,
                StrokeThickness = 1,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(handle, point.X - (HandleSize / 2));
            Canvas.SetTop(handle, point.Y - (HandleSize / 2));
            SurfaceCanvas.Children.Add(handle);
        }
    }

    private void AddLineSelectionOverlay(PixelPoint startPoint, PixelPoint endPoint, Color borderColor, DoubleCollection dashArray)
    {
        var start = ToLocalPoint(startPoint);
        var end = ToLocalPoint(endPoint);

        var line = new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = new SolidColorBrush(borderColor),
            StrokeThickness = 2,
            StrokeDashArray = dashArray,
            IsHitTestVisible = false
        };

        SurfaceCanvas.Children.Add(line);

        foreach (var point in new[] { start, end })
        {
            var handle = new Ellipse
            {
                Width = EndpointHandleSize,
                Height = EndpointHandleSize,
                Fill = new SolidColorBrush(borderColor),
                Stroke = Brushes.White,
                StrokeThickness = 1.4,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(handle, point.X - (EndpointHandleSize / 2));
            Canvas.SetTop(handle, point.Y - (EndpointHandleSize / 2));
            SurfaceCanvas.Children.Add(handle);
        }
    }

    private void AddCalloutSelectionOverlay(CalloutAnnotation annotation, Color borderColor, DoubleCollection dashArray)
    {
        AddBoundsSelectionOverlay(annotation.Bounds, borderColor, dashArray);

        var anchor = ToLocalPoint(annotation.Anchor);
        var connection = ToLocalPoint(GetCalloutConnectionPoint(annotation.Bounds, annotation.Anchor));

        var line = new Line
        {
            X1 = anchor.X,
            Y1 = anchor.Y,
            X2 = connection.X,
            Y2 = connection.Y,
            Stroke = new SolidColorBrush(borderColor),
            StrokeThickness = 2,
            StrokeDashArray = dashArray,
            IsHitTestVisible = false
        };

        SurfaceCanvas.Children.Add(line);

        var handle = new Ellipse
        {
            Width = EndpointHandleSize,
            Height = EndpointHandleSize,
            Fill = new SolidColorBrush(borderColor),
            Stroke = Brushes.White,
            StrokeThickness = 1.4,
            IsHitTestVisible = false
        };

        Canvas.SetLeft(handle, anchor.X - (EndpointHandleSize / 2));
        Canvas.SetTop(handle, anchor.Y - (EndpointHandleSize / 2));
        SurfaceCanvas.Children.Add(handle);
    }

    private void AddSnapGuides()
    {
        if (_activeSnapGuides.Count == 0)
        {
            return;
        }

        foreach (var guide in _activeSnapGuides.Distinct())
        {
            var shape = guide.IsVertical
                ? new Line
                {
                    X1 = guide.Coordinate,
                    X2 = guide.Coordinate,
                    Y1 = 0,
                    Y2 = SurfaceCanvas.Height,
                    Stroke = new SolidColorBrush(Color.FromArgb(210, 125, 211, 252)),
                    StrokeThickness = 1.5,
                    StrokeDashArray = [3d, 3d],
                    IsHitTestVisible = false
                }
                : new Line
                {
                    X1 = 0,
                    X2 = SurfaceCanvas.Width,
                    Y1 = guide.Coordinate,
                    Y2 = guide.Coordinate,
                    Stroke = new SolidColorBrush(Color.FromArgb(210, 125, 211, 252)),
                    StrokeThickness = 1.5,
                    StrokeDashArray = [3d, 3d],
                    IsHitTestVisible = false
                };

            SurfaceCanvas.Children.Add(shape);
        }
    }

    private PixelPoint ApplyMoveSnap(AnnotationObject annotation, PixelPoint proposedDelta)
    {
        _activeSnapGuides.Clear();
        if (ViewModel is null || (Math.Abs(proposedDelta.X) < double.Epsilon && Math.Abs(proposedDelta.Y) < double.Epsilon))
        {
            return proposedDelta;
        }

        if (annotation is LineAnnotation or ArrowAnnotation)
        {
            return proposedDelta;
        }

        var bounds = GetSnapBounds(annotation);
        if (bounds.IsEmpty)
        {
            return proposedDelta;
        }

        var proposedBounds = bounds.Offset(proposedDelta.X, proposedDelta.Y);
        var visibleBounds = ViewModel.GetVisibleBounds();
        var targets = BuildSnapTargets(annotation, visibleBounds);

        var xAdjustment = FindBestSnapAdjustment(
            [proposedBounds.X, proposedBounds.X + (proposedBounds.Width / 2), proposedBounds.Right],
            targets.VerticalTargets,
            out var snappedVertical);

        var yAdjustment = FindBestSnapAdjustment(
            [proposedBounds.Y, proposedBounds.Y + (proposedBounds.Height / 2), proposedBounds.Bottom],
            targets.HorizontalTargets,
            out var snappedHorizontal);

        if (xAdjustment is not null)
        {
            _activeSnapGuides.Add(new SnapGuide(true, snappedVertical - visibleBounds.X));
        }

        if (yAdjustment is not null)
        {
            _activeSnapGuides.Add(new SnapGuide(false, snappedHorizontal - visibleBounds.Y));
        }

        return new PixelPoint(proposedDelta.X + (xAdjustment ?? 0), proposedDelta.Y + (yAdjustment ?? 0));
    }

    private PixelRect ApplyResizeSnap(AnnotationObject annotation, PixelRect proposedBounds)
    {
        _activeSnapGuides.Clear();
        if (ViewModel is null || proposedBounds.IsEmpty)
        {
            return proposedBounds;
        }

        if (annotation is LineAnnotation or ArrowAnnotation)
        {
            return proposedBounds;
        }

        var visibleBounds = ViewModel.GetVisibleBounds();
        var targets = BuildSnapTargets(annotation, visibleBounds);
        var adjusted = proposedBounds;

        var verticalPoints = _resizeHandle switch
        {
            ResizeHandle.TopLeft or ResizeHandle.BottomLeft => new[] { proposedBounds.X },
            ResizeHandle.TopRight or ResizeHandle.BottomRight => new[] { proposedBounds.Right },
            _ => Array.Empty<double>()
        };

        var horizontalPoints = _resizeHandle switch
        {
            ResizeHandle.TopLeft or ResizeHandle.TopRight => new[] { proposedBounds.Y },
            ResizeHandle.BottomLeft or ResizeHandle.BottomRight => new[] { proposedBounds.Bottom },
            _ => Array.Empty<double>()
        };

        var xAdjustment = FindBestSnapAdjustment(verticalPoints, targets.VerticalTargets, out var snappedVertical);
        if (xAdjustment is { } deltaX)
        {
            adjusted = _resizeHandle switch
            {
                ResizeHandle.TopLeft or ResizeHandle.BottomLeft => PixelRect.FromPoints(
                    new PixelPoint(adjusted.X + deltaX, adjusted.Y),
                    new PixelPoint(adjusted.Right, adjusted.Bottom)),
                ResizeHandle.TopRight or ResizeHandle.BottomRight => PixelRect.FromPoints(
                    new PixelPoint(adjusted.X, adjusted.Y),
                    new PixelPoint(adjusted.Right + deltaX, adjusted.Bottom)),
                _ => adjusted
            };

            _activeSnapGuides.Add(new SnapGuide(true, snappedVertical - visibleBounds.X));
        }

        var yAdjustment = FindBestSnapAdjustment(horizontalPoints, targets.HorizontalTargets, out var snappedHorizontal);
        if (yAdjustment is { } deltaY)
        {
            adjusted = _resizeHandle switch
            {
                ResizeHandle.TopLeft or ResizeHandle.TopRight => PixelRect.FromPoints(
                    new PixelPoint(adjusted.X, adjusted.Y + deltaY),
                    new PixelPoint(adjusted.Right, adjusted.Bottom)),
                ResizeHandle.BottomLeft or ResizeHandle.BottomRight => PixelRect.FromPoints(
                    new PixelPoint(adjusted.X, adjusted.Y),
                    new PixelPoint(adjusted.Right, adjusted.Bottom + deltaY)),
                _ => adjusted
            };

            _activeSnapGuides.Add(new SnapGuide(false, snappedHorizontal - visibleBounds.Y));
        }

        return adjusted;
    }

    private (List<double> VerticalTargets, List<double> HorizontalTargets) BuildSnapTargets(AnnotationObject movingAnnotation, PixelRect visibleBounds)
    {
        var verticalTargets = new List<double>
        {
            visibleBounds.X,
            visibleBounds.X + (visibleBounds.Width / 2),
            visibleBounds.Right
        };

        var horizontalTargets = new List<double>
        {
            visibleBounds.Y,
            visibleBounds.Y + (visibleBounds.Height / 2),
            visibleBounds.Bottom
        };

        foreach (var annotation in ViewModel!.Annotations)
        {
            if (ReferenceEquals(annotation, movingAnnotation))
            {
                continue;
            }

            var bounds = GetSnapBounds(annotation);
            if (bounds.IsEmpty)
            {
                continue;
            }

            verticalTargets.Add(bounds.X);
            verticalTargets.Add(bounds.X + (bounds.Width / 2));
            verticalTargets.Add(bounds.Right);

            horizontalTargets.Add(bounds.Y);
            horizontalTargets.Add(bounds.Y + (bounds.Height / 2));
            horizontalTargets.Add(bounds.Bottom);
        }

        return (verticalTargets, horizontalTargets);
    }

    private static double? FindBestSnapAdjustment(IEnumerable<double> movingPoints, IEnumerable<double> targetPoints, out double snappedTarget)
    {
        snappedTarget = 0;
        double? bestAdjustment = null;
        var bestDistance = SnapThreshold + 0.1;

        foreach (var movingPoint in movingPoints)
        {
            foreach (var targetPoint in targetPoints)
            {
                var delta = targetPoint - movingPoint;
                var distance = Math.Abs(delta);
                if (distance <= SnapThreshold && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestAdjustment = delta;
                    snappedTarget = targetPoint;
                }
            }
        }

        return bestAdjustment;
    }

    private static PixelRect GetSnapBounds(AnnotationObject annotation)
    {
        return annotation switch
        {
            TextAnnotation text => text.Bounds.Normalize(),
            CalloutAnnotation callout => callout.Bounds.Normalize(),
            StepAnnotation step => step.Bounds.Normalize(),
            ShapeAnnotation shape => shape.Bounds.Normalize(),
            BlurAnnotation blur => blur.Bounds.Normalize(),
            LineAnnotation line => PixelRect.FromPoints(line.Start, line.End),
            ArrowAnnotation arrow => PixelRect.FromPoints(arrow.Start, arrow.End),
            _ => annotation.GetBounds().Normalize()
        };
    }

    private bool TryResizeLineEndpoint(AnnotationObject annotation, PixelPoint currentPoint)
    {
        switch (annotation)
        {
            case LineAnnotation line when _resizeHandle == ResizeHandle.LineStart:
                line.Start = currentPoint;
                return true;
            case LineAnnotation line when _resizeHandle == ResizeHandle.LineEnd:
                line.End = currentPoint;
                return true;
            case ArrowAnnotation arrow when _resizeHandle == ResizeHandle.LineStart:
                arrow.Start = currentPoint;
                return true;
            case ArrowAnnotation arrow when _resizeHandle == ResizeHandle.LineEnd:
                arrow.End = currentPoint;
                return true;
            default:
                return false;
        }
    }

    private bool TryResizeCalloutAnchor(AnnotationObject annotation, PixelPoint currentPoint)
    {
        if (annotation is CalloutAnnotation callout && _resizeHandle == ResizeHandle.LineStart)
        {
            callout.Anchor = currentPoint;
            return true;
        }

        return false;
    }

    private bool IsEndpointHandle(ResizeHandle handle) => handle is ResizeHandle.LineStart or ResizeHandle.LineEnd;

    private void AnnotationCanvasControlOnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        var delta = e.Delta > 0 ? ZoomStep : -ZoomStep;
        ApplyZoom(_zoomFactor + delta, fitToViewport: false, viewportFocus: e.GetPosition(SurfaceScrollViewer));
        e.Handled = true;
    }

    private void AnnotationCanvasControlOnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_inlineTextEditor.Visibility == Visibility.Visible)
        {
            return;
        }

        if (ViewModel?.SelectedAnnotation is not null && Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D)
        {
            ViewModel.DuplicateSelectionCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ViewModel?.SelectedAnnotation is not null &&
            (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift))
        {
            var step = Keyboard.Modifiers == ModifierKeys.Shift ? 10 : 1;
            switch (e.Key)
            {
                case Key.Left:
                    ViewModel.NudgeSelectedAnnotation(-step, 0);
                    e.Handled = true;
                    return;
                case Key.Right:
                    ViewModel.NudgeSelectedAnnotation(step, 0);
                    e.Handled = true;
                    return;
                case Key.Up:
                    ViewModel.NudgeSelectedAnnotation(0, -step);
                    e.Handled = true;
                    return;
                case Key.Down:
                    ViewModel.NudgeSelectedAnnotation(0, step);
                    e.Handled = true;
                    return;
            }
        }

        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        if (e.Key is Key.Add or Key.OemPlus)
        {
            ApplyZoom(_zoomFactor + ZoomStep, fitToViewport: false);
            e.Handled = true;
        }
        else if (e.Key is Key.Subtract or Key.OemMinus)
        {
            ApplyZoom(_zoomFactor - ZoomStep, fitToViewport: false);
            e.Handled = true;
        }
        else if (e.Key == Key.D0)
        {
            ApplyZoom(1, fitToViewport: false);
            e.Handled = true;
        }
        else if (e.Key == Key.D9)
        {
            ApplyFitZoom();
            e.Handled = true;
        }
    }

    private void ZoomInButtonOnClick(object sender, RoutedEventArgs e) => ApplyZoom(_zoomFactor + ZoomStep, fitToViewport: false);

    private void ZoomOutButtonOnClick(object sender, RoutedEventArgs e) => ApplyZoom(_zoomFactor - ZoomStep, fitToViewport: false);

    private void FitZoomButtonOnClick(object sender, RoutedEventArgs e) => ApplyFitZoom();

    private void ApplyFitZoom()
    {
        if (!IsLoaded)
        {
            return;
        }

        var viewportWidth = Math.Max(1, SurfaceScrollViewer.ViewportWidth > 0 ? SurfaceScrollViewer.ViewportWidth : SurfaceScrollViewer.ActualWidth);
        var viewportHeight = Math.Max(1, SurfaceScrollViewer.ViewportHeight > 0 ? SurfaceScrollViewer.ViewportHeight : SurfaceScrollViewer.ActualHeight);
        var contentWidth = Math.Max(1, SurfaceCanvas.Width);
        var contentHeight = Math.Max(1, SurfaceCanvas.Height);
        var fitZoom = Math.Min(viewportWidth / contentWidth, viewportHeight / contentHeight);
        ApplyZoom(fitZoom, fitToViewport: true);
    }

    private void ApplyZoom(double targetZoom, bool fitToViewport, Point? viewportFocus = null)
    {
        var clamped = Math.Clamp(targetZoom, MinZoomFactor, MaxZoomFactor);
        var previousZoom = _zoomFactor <= 0 ? 1 : _zoomFactor;
        var focus = viewportFocus ?? new Point(SurfaceScrollViewer.ViewportWidth / 2, SurfaceScrollViewer.ViewportHeight / 2);
        var previousHorizontalOffset = SurfaceScrollViewer.HorizontalOffset;
        var previousVerticalOffset = SurfaceScrollViewer.VerticalOffset;

        _zoomFactor = clamped;
        _fitToViewport = fitToViewport;
        SurfaceScaleTransform.ScaleX = _zoomFactor;
        SurfaceScaleTransform.ScaleY = _zoomFactor;
        ZoomText.Text = $"{_zoomFactor * 100:0}%";
        SurfaceScrollViewer.UpdateLayout();

        if (_fitToViewport)
        {
            SurfaceScrollViewer.ScrollToHome();
            return;
        }

        var ratio = _zoomFactor / previousZoom;
        SurfaceScrollViewer.ScrollToHorizontalOffset(((previousHorizontalOffset + focus.X) * ratio) - focus.X);
        SurfaceScrollViewer.ScrollToVerticalOffset(((previousVerticalOffset + focus.Y) * ratio) - focus.Y);
    }

    private PixelPoint ToImagePoint(Point localPoint)
    {
        var visible = ViewModel?.GetVisibleBounds() ?? PixelRect.Empty;
        return new PixelPoint(localPoint.X + visible.X, localPoint.Y + visible.Y);
    }

    private Point ToLocalPoint(PixelPoint imagePoint)
    {
        var visible = ViewModel?.GetVisibleBounds() ?? PixelRect.Empty;
        return new Point(imagePoint.X - visible.X, imagePoint.Y - visible.Y);
    }

    private Rect ToLocalRect(PixelRect imageRect)
    {
        var visible = ViewModel?.GetVisibleBounds() ?? PixelRect.Empty;
        return new Rect(imageRect.X - visible.X, imageRect.Y - visible.Y, imageRect.Width, imageRect.Height);
    }

    private static Brush CreateBrush(ArgbColor color, double opacity)
    {
        var brush = new SolidColorBrush(ToColor(color))
        {
            Opacity = Math.Clamp(opacity, 0.05, 1)
        };

        brush.Freeze();
        return brush;
    }

    private static Color ToColor(ArgbColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    private static string GetEditableText(AnnotationObject annotation) => annotation switch
    {
        TextAnnotation text => text.Text,
        CalloutAnnotation callout => callout.Text,
        _ => string.Empty
    };

    private static void SetEditableText(AnnotationObject annotation, string text)
    {
        switch (annotation)
        {
            case TextAnnotation textAnnotation:
                textAnnotation.Text = text;
                break;
            case CalloutAnnotation calloutAnnotation:
                calloutAnnotation.Text = text;
                break;
        }
    }

    private static PixelRect GetEditableBounds(AnnotationObject annotation) => annotation switch
    {
        TextAnnotation text => text.Bounds,
        CalloutAnnotation callout => callout.Bounds,
        _ => PixelRect.Empty
    };

    private static double GetEditableFontSize(AnnotationObject annotation) => annotation switch
    {
        TextAnnotation text => text.FontSize,
        CalloutAnnotation callout => callout.FontSize,
        _ => 24
    };

    private static string GetEditableFontFamily(AnnotationObject annotation) => annotation switch
    {
        TextAnnotation text => text.FontFamily,
        CalloutAnnotation callout => callout.FontFamily,
        _ => "Segoe UI"
    };

    private static Color GetInlineEditorBackground(AnnotationObject annotation)
    {
        var fill = annotation switch
        {
            TextAnnotation text => ToColor(text.FillColor),
            CalloutAnnotation callout => ToColor(callout.FillColor),
            _ => Color.FromArgb(235, 16, 20, 28)
        };
        if (fill.A < 220)
        {
            return Color.FromArgb(235, 16, 20, 28);
        }

        return fill;
    }

    private static PixelPoint GetCalloutConnectionPoint(PixelRect bounds, PixelPoint anchor)
    {
        var center = new PixelPoint(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
        var deltaX = anchor.X - center.X;
        var deltaY = anchor.Y - center.Y;

        if (Math.Abs(deltaX) < double.Epsilon && Math.Abs(deltaY) < double.Epsilon)
        {
            return center;
        }

        var halfWidth = Math.Max(1, bounds.Width / 2);
        var halfHeight = Math.Max(1, bounds.Height / 2);
        var scaleX = Math.Abs(deltaX) < double.Epsilon ? double.PositiveInfinity : halfWidth / Math.Abs(deltaX);
        var scaleY = Math.Abs(deltaY) < double.Epsilon ? double.PositiveInfinity : halfHeight / Math.Abs(deltaY);
        var scale = Math.Min(scaleX, scaleY);

        return new PixelPoint(center.X + (deltaX * scale), center.Y + (deltaY * scale));
    }

    private static double DistanceToSegment(PixelPoint point, PixelPoint start, PixelPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;

        if (Math.Abs(dx) < double.Epsilon && Math.Abs(dy) < double.Epsilon)
        {
            return Math.Sqrt(Math.Pow(point.X - start.X, 2) + Math.Pow(point.Y - start.Y, 2));
        }

        var t = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / ((dx * dx) + (dy * dy));
        t = Math.Clamp(t, 0, 1);
        var projectionX = start.X + (t * dx);
        var projectionY = start.Y + (t * dy);
        return Math.Sqrt(Math.Pow(point.X - projectionX, 2) + Math.Pow(point.Y - projectionY, 2));
    }

    private static double Distance(PixelPoint first, PixelPoint second)
    {
        return Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));
    }

    private static double Distance(Point first, Point second)
    {
        return Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));
    }

    private void ShowHint(string message)
    {
        HintText.Text = message;
        HintBadge.Visibility = Visibility.Visible;
    }

    private void HideHint() => HintBadge.Visibility = Visibility.Collapsed;
}
