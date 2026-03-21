using CaptureCoyote.Core.Enums;

namespace CaptureCoyote.Core.Models;

public static class CaptureModeDisplay
{
    public static string ToDisplayText(CaptureMode mode)
    {
        return mode switch
        {
            CaptureMode.FullScreen => "Full Screen",
            CaptureMode.Scrolling => "Scrolling",
            _ => mode.ToString()
        };
    }
}
