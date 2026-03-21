using CaptureCoyote.Core.Enums;

namespace CaptureCoyote.App.ViewModels;

public sealed class StylePresetOption
{
    public required StylePresetKind Kind { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public override string ToString() => DisplayName;
}
