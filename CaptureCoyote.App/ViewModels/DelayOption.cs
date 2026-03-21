namespace CaptureCoyote.App.ViewModels;

public sealed class DelayOption
{
    public required string Label { get; init; }

    public required int Seconds { get; init; }

    public override string ToString() => Label;
}
