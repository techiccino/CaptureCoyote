using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Primitives;
using CaptureCoyote.Infrastructure.Interop;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingCopyPixelOperation = System.Drawing.CopyPixelOperation;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;

namespace CaptureCoyote.Infrastructure.Helpers;

internal static class ImageHelper
{
    public static byte[] CaptureToPng(PixelRect bounds)
    {
        using var bitmap = new DrawingBitmap((int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height), DrawingPixelFormat.Format32bppPArgb);
        using var graphics = DrawingGraphics.FromImage(bitmap);
        graphics.CopyFromScreen(
            (int)Math.Round(bounds.X),
            (int)Math.Round(bounds.Y),
            0,
            0,
            new DrawingSize(bitmap.Width, bitmap.Height),
            DrawingCopyPixelOperation.SourceCopy);

        using var stream = new MemoryStream();
        bitmap.Save(stream, DrawingImageFormat.Png);
        return stream.ToArray();
    }

    public static byte[]? CaptureWindowToPng(nint windowHandle, PixelRect bounds)
    {
        var width = Math.Max(1, (int)Math.Round(bounds.Width));
        var height = Math.Max(1, (int)Math.Round(bounds.Height));

        using var bitmap = new DrawingBitmap(width, height, DrawingPixelFormat.Format32bppPArgb);
        using var graphics = DrawingGraphics.FromImage(bitmap);
        var hdc = graphics.GetHdc();

        try
        {
            if (!NativeMethods.PrintWindow(windowHandle, hdc, NativeMethods.PW_RENDERFULLCONTENT) &&
                !NativeMethods.PrintWindow(windowHandle, hdc, 0))
            {
                return null;
            }
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, DrawingImageFormat.Png);
        return stream.ToArray();
    }

    public static byte[] CropPng(byte[] pngBytes, PixelRect sourceRegion)
    {
        using var sourceStream = new MemoryStream(pngBytes);
        using var bitmap = new DrawingBitmap(sourceStream);
        var safeX = Math.Max(0, (int)Math.Round(sourceRegion.X));
        var safeY = Math.Max(0, (int)Math.Round(sourceRegion.Y));
        var safeWidth = Math.Min(bitmap.Width - safeX, Math.Max(1, (int)Math.Round(sourceRegion.Width)));
        var safeHeight = Math.Min(bitmap.Height - safeY, Math.Max(1, (int)Math.Round(sourceRegion.Height)));
        using var cropped = bitmap.Clone(new DrawingRectangle(safeX, safeY, safeWidth, safeHeight), DrawingPixelFormat.Format32bppPArgb);
        using var output = new MemoryStream();
        cropped.Save(output, DrawingImageFormat.Png);
        return output.ToArray();
    }

    public static BitmapSource ToBitmapSource(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    public static byte[] Encode(BitmapSource bitmap, ImageFileFormat format, int jpegQuality = 92)
    {
        BitmapEncoder encoder = format switch
        {
            ImageFileFormat.Jpg => new JpegBitmapEncoder { QualityLevel = jpegQuality },
            _ => new PngBitmapEncoder()
        };

        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public static byte[] CreateThumbnailPng(byte[] pngBytes, int maxWidth = 320, int maxHeight = 180)
    {
        var source = ToBitmapSource(pngBytes);
        var scale = Math.Min((double)maxWidth / source.PixelWidth, (double)maxHeight / source.PixelHeight);
        scale = Math.Min(scale, 1);

        var width = Math.Max(1, (int)Math.Round(source.PixelWidth * scale));
        var height = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new System.Windows.Rect(0, 0, width, height));
            context.DrawImage(source, new System.Windows.Rect(0, 0, width, height));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return Encode(bitmap, ImageFileFormat.Png);
    }

    public static System.Windows.Media.Color ToMediaColor(ArgbColor color) =>
        System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B);
}
