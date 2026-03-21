namespace CaptureCoyote.Core.Primitives;

public readonly record struct PixelPoint(double X, double Y)
{
    public static PixelPoint operator +(PixelPoint left, PixelPoint right) => new(left.X + right.X, left.Y + right.Y);

    public static PixelPoint operator -(PixelPoint left, PixelPoint right) => new(left.X - right.X, left.Y - right.Y);
}
