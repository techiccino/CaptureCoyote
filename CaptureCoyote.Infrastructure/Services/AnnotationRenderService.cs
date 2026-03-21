using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Core.Primitives;
using CaptureCoyote.Infrastructure.Helpers;
using CaptureCoyote.Services.Abstractions;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaFontFamily = System.Windows.Media.FontFamily;
using MediaPen = System.Windows.Media.Pen;
using WpfFlowDirection = System.Windows.FlowDirection;
using WpfPoint = System.Windows.Point;

namespace CaptureCoyote.Infrastructure.Services;

public sealed class AnnotationRenderService : IAnnotationRenderService
{
    public byte[] RenderToPng(ScreenshotProject project)
    {
        return Render(project, new ExportOptions
        {
            Format = ImageFileFormat.Png,
            IncludeCrop = true
        });
    }

    public byte[] Render(ScreenshotProject project, ExportOptions options)
    {
        var source = EnsureBgra32(ImageHelper.ToBitmapSource(project.OriginalImagePngBytes));
        var crop = GetRenderCrop(project, options, source.PixelWidth, source.PixelHeight);
        var pixelWidth = Math.Max(1, (int)Math.Round(crop.Width));
        var pixelHeight = Math.Max(1, (int)Math.Round(crop.Height));

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new TranslateTransform(-crop.X, -crop.Y));
            context.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));

            foreach (var annotation in project.Annotations.OrderBy(annotation => annotation.ZIndex))
            {
                DrawAnnotation(context, annotation, source, crop);
            }

            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        return ImageHelper.Encode(bitmap, options.Format, options.JpegQuality);
    }

    private static PixelRect GetRenderCrop(ScreenshotProject project, ExportOptions options, int width, int height)
    {
        if (!options.IncludeCrop || !project.CropState.IsActive || project.CropState.Bounds.IsEmpty)
        {
            return new PixelRect(0, 0, width, height);
        }

        return project.CropState.Bounds.Normalize().ClampWithin(new PixelRect(0, 0, width, height));
    }

    private static void DrawAnnotation(DrawingContext context, AnnotationObject annotation, BitmapSource source, PixelRect crop)
    {
        switch (annotation)
        {
            case ShapeAnnotation shape:
                DrawShape(context, shape);
                break;
            case CalloutAnnotation callout:
                DrawCallout(context, callout);
                break;
            case ArrowAnnotation arrow:
                DrawArrow(context, arrow);
                break;
            case LineAnnotation line:
                DrawLine(context, line);
                break;
            case TextAnnotation text:
                DrawText(context, text);
                break;
            case StepAnnotation step:
                DrawStep(context, step);
                break;
            case BlurAnnotation blur:
                DrawBlur(context, blur, source, crop);
                break;
        }
    }

    private static void DrawShape(DrawingContext context, ShapeAnnotation shape)
    {
        var rect = ToRect(shape.Bounds);
        var fill = CreateBrush(shape.FillColor, shape.Opacity);
        var pen = CreatePen(shape.StrokeColor, shape.StrokeThickness, shape.Opacity);

        switch (shape.ShapeKind)
        {
            case ShapeKind.Ellipse:
                context.DrawEllipse(fill, pen, new WpfPoint(rect.Left + (rect.Width / 2), rect.Top + (rect.Height / 2)), rect.Width / 2, rect.Height / 2);
                break;
            case ShapeKind.Highlight:
                context.DrawRoundedRectangle(CreateBrush(shape.FillColor == ArgbColor.Transparent ? ArgbColor.Highlight : shape.FillColor, 0.35), null, rect, 12, 12);
                break;
            default:
                context.DrawRoundedRectangle(fill, pen, rect, 12, 12);
                break;
        }
    }

    private static void DrawLine(DrawingContext context, LineAnnotation line)
    {
        context.DrawLine(CreatePen(line.StrokeColor, line.StrokeThickness, line.Opacity), ToPoint(line.Start), ToPoint(line.End));
    }

    private static void DrawArrow(DrawingContext context, ArrowAnnotation arrow)
    {
        var start = ToPoint(arrow.Start);
        var end = ToPoint(arrow.End);
        var pen = CreatePen(arrow.StrokeColor, arrow.StrokeThickness, arrow.Opacity);
        var direction = start - end;
        if (direction.Length < 0.5)
        {
            context.DrawLine(pen, start, end);
            return;
        }

        direction.Normalize();
        var perpendicular = new Vector(-direction.Y, direction.X);
        var headSize = Math.Max(10, arrow.HeadSize);
        var headPoint1 = end + (direction * headSize) + (perpendicular * (headSize * 0.45));
        var headPoint2 = end + (direction * headSize) - (perpendicular * (headSize * 0.45));
        var geometryGroup = new GeometryGroup();
        geometryGroup.Children.Add(new LineGeometry(start, end));
        geometryGroup.Children.Add(new LineGeometry(end, headPoint1));
        geometryGroup.Children.Add(new LineGeometry(end, headPoint2));
        geometryGroup.Freeze();
        context.DrawGeometry(null, pen, geometryGroup);
    }

    private static void DrawText(DrawingContext context, TextAnnotation text)
    {
        var rect = ToRect(text.Bounds);
        context.DrawRoundedRectangle(CreateBrush(text.FillColor, Math.Min(text.Opacity, 0.55)), null, rect, 8, 8);

        var formatted = new FormattedText(
            text.Text ?? string.Empty,
            CultureInfo.CurrentCulture,
            WpfFlowDirection.LeftToRight,
            new Typeface(new MediaFontFamily(text.FontFamily), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            text.FontSize,
            CreateBrush(text.StrokeColor, text.Opacity),
            1.0)
        {
            MaxTextWidth = Math.Max(1, rect.Width - 16),
            MaxTextHeight = Math.Max(1, rect.Height - 12),
            Trimming = TextTrimming.None
        };

        context.DrawText(formatted, new WpfPoint(rect.Left + 8, rect.Top + 6));
    }

    private static void DrawCallout(DrawingContext context, CalloutAnnotation callout)
    {
        var rect = ToRect(callout.Bounds);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var anchor = ToPoint(callout.Anchor);
        var connection = GetCalloutConnectionPoint(rect, anchor);
        var pen = CreatePen(callout.StrokeColor, Math.Max(1.4, callout.StrokeThickness), callout.Opacity);

        context.DrawLine(pen, anchor, connection);
        context.DrawEllipse(CreateBrush(callout.StrokeColor, callout.Opacity), null, anchor, 3.6, 3.6);
        context.DrawRoundedRectangle(CreateBrush(callout.FillColor, Math.Min(callout.Opacity, 0.92)), pen, rect, 12, 12);

        var formatted = new FormattedText(
            callout.Text ?? string.Empty,
            CultureInfo.CurrentCulture,
            WpfFlowDirection.LeftToRight,
            new Typeface(new MediaFontFamily(callout.FontFamily), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            callout.FontSize,
            CreateBrush(callout.StrokeColor, callout.Opacity),
            1.0)
        {
            MaxTextWidth = Math.Max(1, rect.Width - 20),
            MaxTextHeight = Math.Max(1, rect.Height - 16),
            Trimming = TextTrimming.None
        };

        context.DrawText(formatted, new WpfPoint(rect.Left + 10, rect.Top + 8));
    }

    private static void DrawStep(DrawingContext context, StepAnnotation step)
    {
        var rect = ToRect(step.Bounds);
        var center = new WpfPoint(rect.Left + (rect.Width / 2), rect.Top + (rect.Height / 2));
        var radiusX = rect.Width / 2;
        var radiusY = rect.Height / 2;

        context.DrawEllipse(CreateBrush(step.FillColor, step.Opacity), CreatePen(step.StrokeColor, step.StrokeThickness, step.Opacity), center, radiusX, radiusY);

        var formatted = new FormattedText(
            step.Number.ToString(CultureInfo.InvariantCulture),
            CultureInfo.CurrentCulture,
            WpfFlowDirection.LeftToRight,
            new Typeface(new MediaFontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            step.FontSize,
            CreateBrush(step.StrokeColor, step.Opacity),
            1.0);

        context.DrawText(formatted, new WpfPoint(center.X - (formatted.Width / 2), center.Y - (formatted.Height / 2)));
    }

    private static void DrawBlur(DrawingContext context, BlurAnnotation blur, BitmapSource source, PixelRect crop)
    {
        var fullBounds = new PixelRect(0, 0, source.PixelWidth, source.PixelHeight);
        var safe = blur.Bounds.Normalize().ClampWithin(fullBounds);
        if (safe.IsEmpty)
        {
            return;
        }

        var pixelated = CreatePixelatedRegion(source, safe, (int)Math.Round(blur.Strength));
        context.DrawImage(pixelated, ToRect(safe));
        context.DrawRoundedRectangle(
            new SolidColorBrush(MediaColor.FromArgb(24, 255, 255, 255)),
            CreatePen(blur.StrokeColor, Math.Max(1, blur.StrokeThickness), Math.Min(blur.Opacity, 0.6)),
            ToRect(safe),
            8,
            8);
    }

    private static BitmapSource CreatePixelatedRegion(BitmapSource source, PixelRect bounds, int blockSize)
    {
        var rect = new Int32Rect(
            Math.Max(0, (int)Math.Round(bounds.X)),
            Math.Max(0, (int)Math.Round(bounds.Y)),
            Math.Max(1, (int)Math.Round(bounds.Width)),
            Math.Max(1, (int)Math.Round(bounds.Height)));

        var sourceRect = new CroppedBitmap(source, rect);
        var stride = rect.Width * 4;
        var pixels = new byte[stride * rect.Height];
        sourceRect.CopyPixels(pixels, stride, 0);

        var size = Math.Max(2, blockSize);
        for (var y = 0; y < rect.Height; y += size)
        {
            for (var x = 0; x < rect.Width; x += size)
            {
                var sourceIndex = (y * stride) + (x * 4);
                var b = pixels[sourceIndex];
                var g = pixels[sourceIndex + 1];
                var r = pixels[sourceIndex + 2];
                var a = pixels[sourceIndex + 3];

                for (var yy = y; yy < Math.Min(rect.Height, y + size); yy++)
                {
                    for (var xx = x; xx < Math.Min(rect.Width, x + size); xx++)
                    {
                        var targetIndex = (yy * stride) + (xx * 4);
                        pixels[targetIndex] = b;
                        pixels[targetIndex + 1] = g;
                        pixels[targetIndex + 2] = r;
                        pixels[targetIndex + 3] = a;
                    }
                }
            }
        }

        var bitmap = new WriteableBitmap(rect.Width, rect.Height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, rect.Width, rect.Height), pixels, stride, 0);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource EnsureBgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32)
        {
            return source;
        }

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static Rect ToRect(PixelRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static WpfPoint ToPoint(PixelPoint point) => new(point.X, point.Y);

    private static WpfPoint GetCalloutConnectionPoint(Rect rect, WpfPoint anchor)
    {
        var center = new WpfPoint(rect.Left + (rect.Width / 2), rect.Top + (rect.Height / 2));
        var delta = anchor - center;
        if (delta.Length < 0.001)
        {
            return center;
        }

        var halfWidth = Math.Max(1, rect.Width / 2);
        var halfHeight = Math.Max(1, rect.Height / 2);
        var scaleX = Math.Abs(delta.X) < double.Epsilon ? double.PositiveInfinity : halfWidth / Math.Abs(delta.X);
        var scaleY = Math.Abs(delta.Y) < double.Epsilon ? double.PositiveInfinity : halfHeight / Math.Abs(delta.Y);
        var scale = Math.Min(scaleX, scaleY);
        return new WpfPoint(center.X + (delta.X * scale), center.Y + (delta.Y * scale));
    }

    private static MediaBrush CreateBrush(ArgbColor color, double opacity)
    {
        var brush = new SolidColorBrush(ImageHelper.ToMediaColor(color))
        {
            Opacity = Math.Clamp(opacity, 0, 1)
        };
        brush.Freeze();
        return brush;
    }

    private static MediaPen CreatePen(ArgbColor color, double thickness, double opacity)
    {
        var pen = new MediaPen(CreateBrush(color, opacity), Math.Max(1, thickness))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        pen.Freeze();
        return pen;
    }
}
