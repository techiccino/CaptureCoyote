using System.Text.Json;

namespace CaptureCoyote.Infrastructure.Helpers;

internal static class JsonHelper
{
    public static readonly JsonSerializerOptions Default = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
