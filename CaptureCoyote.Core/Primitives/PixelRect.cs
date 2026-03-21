namespace CaptureCoyote.Core.Primitives;

public readonly record struct PixelRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PixelRect Empty => new(0, 0, 0, 0);

    public static PixelRect FromPoints(PixelPoint start, PixelPoint end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var right = Math.Max(start.X, end.X);
        var bottom = Math.Max(start.Y, end.Y);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    public PixelRect Normalize() => FromPoints(new PixelPoint(X, Y), new PixelPoint(Right, Bottom));

    public bool Contains(PixelPoint point) =>
        point.X >= X &&
        point.X <= Right &&
        point.Y >= Y &&
        point.Y <= Bottom;

    public PixelRect Offset(double x, double y) => new(X + x, Y + y, Width, Height);

    public PixelRect Inflate(double amount) =>
        new(X - amount, Y - amount, Width + (amount * 2), Height + (amount * 2));

    public PixelRect ClampWithin(PixelRect bounds)
    {
        var clampedLeft = Math.Max(bounds.X, Math.Min(X, bounds.Right));
        var clampedTop = Math.Max(bounds.Y, Math.Min(Y, bounds.Bottom));
        var clampedRight = Math.Max(bounds.X, Math.Min(Right, bounds.Right));
        var clampedBottom = Math.Max(bounds.Y, Math.Min(Bottom, bounds.Bottom));

        return FromPoints(new PixelPoint(clampedLeft, clampedTop), new PixelPoint(clampedRight, clampedBottom));
    }
}
