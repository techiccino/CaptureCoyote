using CaptureCoyote.Core.Models;
using CaptureCoyote.Core.Primitives;

namespace CaptureCoyote.Services.Abstractions;

public interface IScreenCaptureService
{
    DesktopSnapshot CaptureDesktopSnapshot();

    CaptureResult CaptureFullScreen(CaptureContext context);

    CaptureResult CreateCaptureFromSnapshot(DesktopSnapshot snapshot, PixelRect region, CaptureContext context);

    CaptureResult CaptureWindow(WindowDescriptor window, DesktopSnapshot snapshot, CaptureContext context);
}
