namespace CaptureCoyote.App.ViewModels;

public sealed class RetentionOption
{
    public required string Label { get; init; }

    public required int Days { get; init; }

    public override string ToString() => Label;
}
