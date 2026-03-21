using System.Windows.Forms;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Core.Primitives;
using CaptureCoyote.Infrastructure.Interop;
using CaptureCoyote.Services.Abstractions;

namespace CaptureCoyote.Infrastructure.Services;

public sealed class MonitorService : IMonitorService
{
    public IReadOnlyList<MonitorDescriptor> GetMonitors()
    {
        return Screen.AllScreens
            .Select(screen =>
            {
                var dpiScale = 1d;
                var center = new NativeMethods.POINT
                {
                    X = screen.Bounds.Left + (screen.Bounds.Width / 2),
                    Y = screen.Bounds.Top + (screen.Bounds.Height / 2)
                };

                var monitorHandle = NativeMethods.MonitorFromPoint(center, NativeMethods.MONITOR_DEFAULTTONEAREST);
                if (monitorHandle != nint.Zero &&
                    NativeMethods.GetDpiForMonitor(monitorHandle, NativeMethods.MonitorDpiType.EffectiveDpi, out var dpiX, out _) == 0)
                {
                    dpiScale = dpiX / 96d;
                }

                return new MonitorDescriptor
                {
                    DeviceName = screen.DeviceName,
                    FriendlyName = screen.DeviceName.Replace("\\\\.\\", string.Empty),
                    IsPrimary = screen.Primary,
                    ScaleFactor = dpiScale,
                    Bounds = new PixelRect(screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height),
                    WorkingArea = new PixelRect(screen.WorkingArea.Left, screen.WorkingArea.Top, screen.WorkingArea.Width, screen.WorkingArea.Height)
                };
            })
            .OrderBy(monitor => monitor.IsPrimary ? 0 : 1)
            .ThenBy(monitor => monitor.Bounds.X)
            .ThenBy(monitor => monitor.Bounds.Y)
            .ToList();
    }

    public PixelRect GetVirtualScreenBounds()
    {
        var virtualBounds = SystemInformation.VirtualScreen;
        return new PixelRect(virtualBounds.Left, virtualBounds.Top, virtualBounds.Width, virtualBounds.Height);
    }
}
