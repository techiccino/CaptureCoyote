using CaptureCoyote.Core.Models;
using CaptureCoyote.Core.Primitives;

namespace CaptureCoyote.Services.Abstractions;

public interface IMonitorService
{
    IReadOnlyList<MonitorDescriptor> GetMonitors();

    PixelRect GetVirtualScreenBounds();
}
