using System;

namespace DeskSnip.Core;

public readonly struct PixelPoint
{
    public PixelPoint(double x, double y)
    {
        if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y))
            throw new ArgumentOutOfRangeException(nameof(x));
        X = x;
        Y = y;
    }
    public double X { get; }
    public double Y { get; }
}

/// <summary>半开区间，坐标为桌面物理像素，允许副屏负坐标。</summary>
public readonly struct PixelRect : IEquatable<PixelRect>
{
    public PixelRect(int x, int y, int width, int height)
    {
        if (width < 0 || height < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if ((long)x + width > int.MaxValue || (long)y + height > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(width));
        X = x; Y = y; Width = width; Height = height;
    }
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width == 0 || Height == 0;

    public static PixelRect FromPoints(int ax, int ay, int bx, int by)
        => new PixelRect(Math.Min(ax, bx), Math.Min(ay, by), checked((int)Math.Abs((long)bx - ax)), checked((int)Math.Abs((long)by - ay)));

    public PixelRect Intersect(PixelRect bounds)
    {
        var left = Math.Max(X, bounds.X);
        var top = Math.Max(Y, bounds.Y);
        var right = Math.Min(Right, bounds.Right);
        var bottom = Math.Min(Bottom, bounds.Bottom);
        return right <= left || bottom <= top ? new PixelRect(left, top, 0, 0) : new PixelRect(left, top, right - left, bottom - top);
    }

    public PixelRect Nudge(int dx, int dy, PixelRect bounds, bool resize)
    {
        if (IsEmpty || bounds.IsEmpty) return this;
        if (resize)
            return new PixelRect(X, Y, (int)Math.Max(1, Math.Min((long)bounds.Right - X, (long)Width + dx)),
                (int)Math.Max(1, Math.Min((long)bounds.Bottom - Y, (long)Height + dy)));
        var width = Math.Min(Width, bounds.Width);
        var height = Math.Min(Height, bounds.Height);
        return new PixelRect((int)Math.Max(bounds.X, Math.Min((long)bounds.Right - width, (long)X + dx)),
            (int)Math.Max(bounds.Y, Math.Min((long)bounds.Bottom - height, (long)Y + dy)), width, height);
    }

    public PixelRect EvenSize() => new PixelRect(X, Y, Width - Width % 2, Height - Height % 2);
    public bool Equals(PixelRect other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
    public override bool Equals(object? obj) => obj is PixelRect other && Equals(other);
    public override int GetHashCode() => unchecked(((X * 397 ^ Y) * 397 ^ Width) * 397 ^ Height);
    public override string ToString() => $"{X}, {Y} · {Width} × {Height}";
}
