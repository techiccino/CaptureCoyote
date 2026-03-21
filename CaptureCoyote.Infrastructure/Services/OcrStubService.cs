using CaptureCoyote.Services.Abstractions;

namespace CaptureCoyote.Infrastructure.Services;

public sealed class OcrStubService : IOcrService
{
    public Task<string> ExtractTextAsync(
        byte[] pngBytes,
        string? preferredLanguageTag = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(string.Empty);
    }
}
