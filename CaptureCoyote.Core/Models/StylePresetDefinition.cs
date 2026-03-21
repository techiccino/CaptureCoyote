using CaptureCoyote.Core.Enums;
using CaptureCoyote.Core.Primitives;

namespace CaptureCoyote.Core.Models;

public sealed class StylePresetDefinition
{
    public required StylePresetKind Kind { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required ArgbColor AccentColor { get; init; }

    public required ArgbColor StrokeColor { get; init; }

    public required ArgbColor FillColor { get; init; }

    public required double StrokeThickness { get; init; }

    public required double Opacity { get; init; }

    public required double FontSize { get; init; }

    public required double BlurStrength { get; init; }

    public override string ToString() => DisplayName;
}
