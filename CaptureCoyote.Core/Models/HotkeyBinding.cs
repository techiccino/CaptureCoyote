using CaptureCoyote.Core.Enums;

namespace CaptureCoyote.Core.Models;

public sealed class HotkeyBinding
{
    public required string Name { get; set; }

    public required CaptureMode Mode { get; set; }

    public int VirtualKey { get; set; }

    public string KeyLabel { get; set; } = "None";

    public HotkeyModifiers Modifiers { get; set; }

    public override string ToString()
    {
        var parts = new List<string>();

        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(KeyLabel);
        return string.Join(" + ", parts);
    }
}
