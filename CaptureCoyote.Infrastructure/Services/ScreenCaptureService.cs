using CaptureCoyote.Core.Models;
using CaptureCoyote.Core.Primitives;
using CaptureCoyote.Infrastructure.Helpers;
using CaptureCoyote.Services.Abstractions;

namespace CaptureCoyote.Infrastructure.Services;

public sealed class ScreenCaptureService(IMonitorService monitorService) : IScreenCaptureService
{
    public DesktopSnapshot CaptureDesktopSnapshot()
    {
        var bounds = monitorService.GetVirtualScreenBounds();
        var bytes = ImageHelper.CaptureToPng(bounds);
        return new DesktopSnapshot
        {
            ImagePngBytes = bytes,
            PixelWidth = (int)Math.Round(bounds.Width),
            PixelHeight = (int)Math.Round(bounds.Height),
            VirtualBounds = bounds,
            Monitors = monitorService.GetMonitors()
        };
    }

    public CaptureResult CaptureFullScreen(CaptureContext context)
    {
        var snapshot = CaptureDesktopSnapshot();
        context.SourceBounds = snapshot.VirtualBounds;
        return new CaptureResult
        {
            ImagePngBytes = snapshot.ImagePngBytes,
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

        var croppedBytes = ImageHelper.CropPng(snapshot.ImagePngBytes, relative);
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
