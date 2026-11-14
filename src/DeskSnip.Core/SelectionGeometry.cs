using System;
using System.Globalization;

namespace DeskSnip.Core;

[Flags]
public enum SelectionHandle
{
    None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8, Move = 16
}

/// <summary>选区交互只计算物理像素；桌面边界和至少一个像素的尺寸始终有效。</summary>
public static class SelectionGeometry
{
    public static SelectionHandle HitTest(PixelRect selection, PixelPoint point, double toleranceX, double toleranceY)
    {
        if (selection.IsEmpty) return SelectionHandle.None;
        if (point.X < selection.X - toleranceX || point.X > selection.Right + toleranceX ||
            point.Y < selection.Y - toleranceY || point.Y > selection.Bottom + toleranceY) return SelectionHandle.None;
        var handle = SelectionHandle.None;
        var leftDistance = Math.Abs(point.X - selection.X);
        var rightDistance = Math.Abs(point.X - selection.Right);
        var topDistance = Math.Abs(point.Y - selection.Y);
        var bottomDistance = Math.Abs(point.Y - selection.Bottom);
        if (Math.Min(leftDistance, rightDistance) <= toleranceX)
            handle |= leftDistance <= rightDistance ? SelectionHandle.Left : SelectionHandle.Right;
        if (Math.Min(topDistance, bottomDistance) <= toleranceY)
            handle |= topDistance <= bottomDistance ? SelectionHandle.Top : SelectionHandle.Bottom;
        if (handle != SelectionHandle.None) return handle;
        return point.X >= selection.X && point.X < selection.Right && point.Y >= selection.Y && point.Y < selection.Bottom
            ? SelectionHandle.Move : SelectionHandle.None;
    }

    public static PixelRect Create(PixelPoint anchor, PixelPoint point, PixelRect bounds, bool square)
    {
        if (bounds.IsEmpty) return default;
        var ax = Clamp(anchor.X, bounds.X, bounds.Right);
        var ay = Clamp(anchor.Y, bounds.Y, bounds.Bottom);
        var bx = Clamp(point.X, bounds.X, bounds.Right);
        var by = Clamp(point.Y, bounds.Y, bounds.Bottom);
        if (square)
        {
            var sx = bx < ax ? -1 : 1;
            var sy = by < ay ? -1 : 1;
            var size = Math.Min(Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay)),
                Math.Min(sx < 0 ? ax - bounds.X : bounds.Right - ax, sy < 0 ? ay - bounds.Y : bounds.Bottom - ay));
            bx = ax + sx * size; by = ay + sy * size;
        }
        return PixelRect.FromPoints(Round(ax), Round(ay), Round(bx), Round(by));
    }

    public static PixelRect Transform(PixelRect selection, PixelPoint delta, SelectionHandle handle, PixelRect bounds,
        bool axisLocked = false, bool symmetric = false, double? aspectRatio = null, bool temporaryMove = false)
    {
        if (selection.IsEmpty || bounds.IsEmpty) return selection;
        var start = selection.Intersect(bounds);
        if (start.IsEmpty) return start;
        if (handle == SelectionHandle.None) return start;
        if (handle == SelectionHandle.Move || temporaryMove)
        {
            var dx = delta.X; var dy = delta.Y;
            if (axisLocked) { if (Math.Abs(dx) >= Math.Abs(dy)) dy = 0; else dx = 0; }
            return new PixelRect(Round(Clamp(start.X + dx, bounds.X, bounds.Right - start.Width)),
                Round(Clamp(start.Y + dy, bounds.Y, bounds.Bottom - start.Height)), start.Width, start.Height);
        }
        if (aspectRatio.HasValue && (double.IsNaN(aspectRatio.Value) || double.IsInfinity(aspectRatio.Value) || aspectRatio.Value <= 0))
            throw new ArgumentOutOfRangeException(nameof(aspectRatio));
        var left = (handle & SelectionHandle.Left) != 0;
        var right = (handle & SelectionHandle.Right) != 0;
        var top = (handle & SelectionHandle.Top) != 0;
        var bottom = (handle & SelectionHandle.Bottom) != 0;
        if ((left && right) || (top && bottom)) throw new ArgumentException(TextCatalog.Get("选区边缘互相冲突。"), nameof(handle));
        var horizontal = left || right; var vertical = top || bottom;
        var cx = start.X + start.Width / 2d; var cy = start.Y + start.Height / 2d;
        var anchorX = symmetric || !horizontal ? cx : left ? start.Right : start.X;
        var anchorY = symmetric || !vertical ? cy : top ? start.Bottom : start.Y;
        var maxWidth = symmetric || !horizontal ? 2 * Math.Min(cx - bounds.X, bounds.Right - cx) : left ? anchorX - bounds.X : bounds.Right - anchorX;
        var maxHeight = symmetric || !vertical ? 2 * Math.Min(cy - bounds.Y, bounds.Bottom - cy) : top ? anchorY - bounds.Y : bounds.Bottom - anchorY;
        var width = Math.Max(1, start.Width + (right ? delta.X : left ? -delta.X : 0) * (symmetric ? 2 : 1));
        var height = Math.Max(1, start.Height + (bottom ? delta.Y : top ? -delta.Y : 0) * (symmetric ? 2 : 1));
        if (aspectRatio.HasValue)
        {
            var ratio = aspectRatio.Value;
            if (!vertical || (horizontal && Math.Abs(delta.X) >= Math.Abs(delta.Y) * ratio)) height = width / ratio;
            else width = height * ratio;
            var scale = Math.Min(1, Math.Min(maxWidth / width, maxHeight / height));
            width *= scale; height *= scale;
        }
        else { width = Math.Min(width, maxWidth); height = Math.Min(height, maxHeight); }
        var w = Math.Max(1, Math.Min(bounds.Width, Round(width)));
        var h = Math.Max(1, Math.Min(bounds.Height, Round(height)));
        var x = symmetric || !horizontal ? anchorX - w / 2d : left ? anchorX - w : anchorX;
        var y = symmetric || !vertical ? anchorY - h / 2d : top ? anchorY - h : anchorY;
        return new PixelRect(Round(Clamp(x, bounds.X, bounds.Right - w)), Round(Clamp(y, bounds.Y, bounds.Bottom - h)), w, h);
    }

    public static PixelRect AdjustEdge(PixelRect selection, int dx, int dy, PixelRect bounds, bool expand)
    {
        var handle = dx < 0 ? SelectionHandle.Left : dx > 0 ? SelectionHandle.Right : dy < 0 ? SelectionHandle.Top : SelectionHandle.Bottom;
        return Transform(selection, new PixelPoint(expand ? dx : -dx, expand ? dy : -dy), handle, bounds);
    }

    public static PixelRect Scale(PixelRect selection, int pixels, PixelRect bounds, double? aspectRatio = null)
    {
        if (selection.IsEmpty) return selection;
        return Transform(selection, new PixelPoint(pixels, pixels * selection.Height / (double)selection.Width),
            SelectionHandle.Right | SelectionHandle.Bottom, bounds, symmetric: true, aspectRatio: aspectRatio ?? selection.Width / (double)selection.Height);
    }

    public static bool TryParse(string x, string y, string width, string height, PixelRect bounds, out PixelRect selection)
    {
        selection = default;
        if (!int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var px) ||
            !int.TryParse(y, NumberStyles.Integer, CultureInfo.InvariantCulture, out var py) ||
            !int.TryParse(width, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) ||
            !int.TryParse(height, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) ||
            w < 1 || h < 1 || px < bounds.X || py < bounds.Y || (long)px + w > bounds.Right || (long)py + h > bounds.Bottom) return false;
        selection = new PixelRect(px, py, w, h); return true;
    }

    private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
    private static int Round(double value) => checked((int)Math.Round(value, MidpointRounding.AwayFromZero));
}
