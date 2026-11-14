using System;
using System.Linq;

namespace DeskSnip.Core;

public static class CapturePreferenceRules
{
    public static PixelRect DetectionBounds(PixelRect target, int padding, PixelRect desktop)
    {
        if (target.IsEmpty) return default;
        var horizontal = Math.Max(padding, -(target.Width - 1) / 2);
        var vertical = Math.Max(padding, -(target.Height - 1) / 2);
        return new PixelRect(target.X - horizontal, target.Y - vertical, target.Width + horizontal * 2, target.Height + vertical * 2).Intersect(desktop);
    }
    public static PixelRect ExpandToPoint(PixelRect selection, PixelPoint point, PixelRect desktop)
    {
        if (selection.IsEmpty) return selection;
        var x = Math.Max(desktop.X, Math.Min(desktop.Right - 1, (int)Math.Floor(point.X)));
        var y = Math.Max(desktop.Y, Math.Min(desktop.Bottom - 1, (int)Math.Floor(point.Y)));
        var left = Math.Min(selection.X, x); var top = Math.Min(selection.Y, y);
        return new PixelRect(left, top, Math.Max(selection.Right, x + 1) - left, Math.Max(selection.Bottom, y + 1) - top).Intersect(desktop);
    }
    public static PixelColorFormat[] ColorFormats(string order) => order.Split(',').Select(value => (PixelColorFormat)Enum.Parse(typeof(PixelColorFormat), value)).ToArray();
    public static double CopyScale(int dpiLimit, double screenScale)
    {
        if (double.IsNaN(screenScale) || double.IsInfinity(screenScale) || screenScale <= 0) throw new ArgumentOutOfRangeException(nameof(screenScale));
        return dpiLimit <= 0 ? 1 : Math.Min(1, dpiLimit / (100 * screenScale));
    }
}

public static class PinPreferenceRules
{
    public static bool MovementAllowed(AppSettings settings, bool locked) => !locked || !settings.PinLockMovement;
    public static bool CloseAllowed(AppSettings settings, bool locked, bool keyboard) => !locked || !(keyboard ? settings.PinLockKeyClose : settings.PinLockMouseClose);
    public static PixelPoint ResizePosition(PixelPoint position, double oldWidth, double oldHeight, double newWidth, double newHeight, string anchor, PixelPoint mouse)
    {
        var x = anchor == "Mouse" ? Math.Max(0, Math.Min(1, (mouse.X - position.X) / Math.Max(1, oldWidth))) : anchor == "Center" ? .5 : anchor == "TopRight" || anchor == "BottomRight" ? 1 : 0;
        var y = anchor == "Mouse" ? Math.Max(0, Math.Min(1, (mouse.Y - position.Y) / Math.Max(1, oldHeight))) : anchor == "Center" ? .5 : anchor == "BottomLeft" || anchor == "BottomRight" ? 1 : 0;
        return new PixelPoint(position.X + (oldWidth - newWidth) * x, position.Y + (oldHeight - newHeight) * y);
    }
}
