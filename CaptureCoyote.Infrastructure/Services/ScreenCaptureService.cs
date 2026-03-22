using CaptureCoyote.Core.Models;
using CaptureCoyote.Core.Primitives;
using CaptureCoyote.Core.Enums;
using CaptureCoyote.Infrastructure.Helpers;
using CaptureCoyote.Services.Abstractions;

namespace CaptureCoyote.Infrastructure.Services;

public sealed class ScreenCaptureService(IMonitorService monitorService) : IScreenCaptureService
{
    public DesktopSnapshot CaptureDesktopSnapshot()
    {
        var bounds = monitorService.GetVirtualScreenBounds();
        var capture = ImageHelper.CaptureToPixelBuffer(bounds);
        return new DesktopSnapshot
        {
            PixelBuffer = capture.PixelBuffer,
            PixelWidth = capture.PixelWidth,
            PixelHeight = capture.PixelHeight,
            PixelStride = capture.PixelStride,
            VirtualBounds = bounds,
            Monitors = monitorService.GetMonitors()
        };
    }

    public CaptureResult CaptureFullScreen(CaptureContext context)
    {
        var snapshot = CaptureDesktopSnapshot();
        context.SourceBounds = snapshot.VirtualBounds;
        var pngBytes = ImageHelper.Encode(
            ImageHelper.CreateBitmapSource(snapshot.PixelBuffer, snapshot.PixelWidth, snapshot.PixelHeight, snapshot.PixelStride),
            ImageFileFormat.Png);

        return new CaptureResult
        {
            ImagePngBytes = pngBytes,
            PixelWidth = snapshot.PixelWidth,
            PixelHeight = snapshot.PixelHeight,
            Context = context
        };
    }

    public CaptureResult CreateCaptureFromSnapshot(DesktopSnapshot snapshot, PixelRect region, CaptureContext context)
    {
        var normalized = region.Normalize();
        var relative = new PixelRect(
            normalized.X - snapshot.VirtualBounds.X,
            normalized.Y - snapshot.VirtualBounds.Y,
            normalized.Width,
            normalized.Height);

        var croppedBytes = ImageHelper.CropBitmapToPng(snapshot.PixelBuffer, snapshot.PixelWidth, snapshot.PixelHeight, snapshot.PixelStride, relative);
        context.SourceBounds = normalized;

        return new CaptureResult
        {
            ImagePngBytes = croppedBytes,
            PixelWidth = (int)Math.Round(normalized.Width),
            PixelHeight = (int)Math.Round(normalized.Height),
            Context = context
        };
    }

    public CaptureResult CaptureWindow(WindowDescriptor window, DesktopSnapshot snapshot, CaptureContext context)
    {
        var normalized = window.Bounds.Normalize();
        context.SourceBounds = normalized;

        var windowBytes = ImageHelper.CaptureWindowToPng(window.Handle, normalized);
        if (windowBytes is not null)
        {
            return new CaptureResult
            {
                ImagePngBytes = windowBytes,
                PixelWidth = (int)Math.Round(normalized.Width),
                PixelHeight = (int)Math.Round(normalized.Height),
                Context = context
            };
        }

        return CreateCaptureFromSnapshot(snapshot, normalized.ClampWithin(snapshot.VirtualBounds), context);
    }
}
