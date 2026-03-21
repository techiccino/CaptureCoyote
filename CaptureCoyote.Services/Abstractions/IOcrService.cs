namespace CaptureCoyote.Services.Abstractions;

public interface IOcrService
{
    Task<string> ExtractTextAsync(
        byte[] pngBytes,
        string? preferredLanguageTag = null,
        CancellationToken cancellationToken = default);
}
