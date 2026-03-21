using CaptureCoyote.Core.Models;

namespace CaptureCoyote.Services.Abstractions;

public interface IScrollingCaptureService
{
    Task<CaptureResult?> CaptureScrollingWindowAsync(
        WindowDescriptor window,
        CaptureContext context,
        CancellationToken cancellationToken = default);
}
