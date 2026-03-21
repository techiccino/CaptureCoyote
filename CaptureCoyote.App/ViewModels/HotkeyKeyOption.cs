namespace CaptureCoyote.App.ViewModels;

public sealed class HotkeyKeyOption
{
    public required string Label { get; init; }

    public required int VirtualKey { get; init; }

    public override string ToString() => Label;
}
