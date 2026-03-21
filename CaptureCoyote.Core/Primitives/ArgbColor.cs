using System.Globalization;

namespace CaptureCoyote.Core.Primitives;

public readonly record struct ArgbColor(byte A, byte R, byte G, byte B)
{
    public static readonly ArgbColor Transparent = new(0, 0, 0, 0);
    public static readonly ArgbColor White = new(255, 255, 255, 255);
    public static readonly ArgbColor Black = new(255, 0, 0, 0);
    public static readonly ArgbColor Accent = new(255, 24, 132, 196);
    public static readonly ArgbColor Highlight = new(120, 255, 220, 74);
    public static readonly ArgbColor Error = new(255, 224, 80, 80);

    public string ToHex() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";

    public static ArgbColor FromHex(string? value, ArgbColor fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim().TrimStart('#');

        if (normalized.Length == 6 &&
            int.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return new ArgbColor(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        if (normalized.Length == 8 &&
            int.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            return new ArgbColor((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        }

        return fallback;
    }
}
