using CaptureCoyote.Core.Models;
using CaptureCoyote.Core.Primitives;

namespace CaptureCoyote.Services.Abstractions;

public interface IWindowLocatorService
{
    WindowDescriptor? GetWindowAt(PixelPoint point, IEnumerable<nint>? excludedHandles = null);
}
