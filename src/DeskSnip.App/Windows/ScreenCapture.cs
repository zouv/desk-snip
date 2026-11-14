using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.Core;

namespace DeskSnip.App.Windows;

public interface IFrameSource : IDisposable
{
    void Copy(byte[] destination, bool cursor);
}

public sealed class FrameGrabber : IFrameSource
{
    private readonly PixelRect region;
    private IntPtr screen;
    private IntPtr memory;
    private IntPtr bitmap;
    private IntPtr previous;
    private IntPtr bits;
    public FrameGrabber(PixelRect region)
    {
        if (region.IsEmpty || (long)region.Width * region.Height > 100_000_000) throw new ArgumentOutOfRangeException(nameof(region));
        this.region = region;
        try
        {
            screen = Native.GetDC(IntPtr.Zero);
            memory = Native.CreateCompatibleDC(screen);
            var info = new Native.BitmapInfo { Size = 40, Width = region.Width, Height = -region.Height, Planes = 1, BitCount = 32 };
            bitmap = Native.CreateDIBSection(screen, ref info, 0, out bits, IntPtr.Zero, 0);
            if (screen == IntPtr.Zero || memory == IntPtr.Zero || bitmap == IntPtr.Zero || bits == IntPtr.Zero)
                throw new Win32Exception(L.Get("无法分配桌面图像，请缩小捕获范围。"));
            previous = Native.SelectObject(memory, bitmap);
        }
        catch { Dispose(); throw; }
    }
    public void Copy(byte[] destination, bool cursor)
    {
        if (bitmap == IntPtr.Zero) throw new ObjectDisposedException(nameof(FrameGrabber));
        if (destination.Length != checked(region.Width * region.Height * 4)) throw new ArgumentException(TextCatalog.Get("帧尺寸不匹配。"));
        if (!Native.BitBlt(memory, 0, 0, region.Width, region.Height, screen, region.X, region.Y, 0x40cc0020))
            throw new Win32Exception(Marshal.GetLastWin32Error(), L.Get("桌面捕获失败。"));
        if (cursor) DrawCursor();
        Marshal.Copy(bits, destination, 0, destination.Length);
        // GDI 的保留 alpha 字节不可靠，所有桌面像素设为不透明。
        for (var i = 3; i < destination.Length; i += 4) destination[i] = 255;
    }
    internal void CopyCursorFrame(byte[] destination)
    {
        if (bitmap == IntPtr.Zero) throw new ObjectDisposedException(nameof(FrameGrabber));
        if (destination.Length != checked(region.Width * region.Height * 4)) throw new ArgumentException(TextCatalog.Get("帧尺寸不匹配。"));
        DrawCursor(); Marshal.Copy(bits, destination, 0, destination.Length);
        for (var i = 3; i < destination.Length; i += 4) destination[i] = 255;
    }
    private void DrawCursor()
    {
        var info = new Native.CursorInfo { Size = Marshal.SizeOf<Native.CursorInfo>() };
        if (!Native.GetCursorInfo(ref info) || info.Flags != 1 || !Native.GetIconInfo(info.Cursor, out var icon)) return;
        try { Native.DrawIconEx(memory, info.Position.X - region.X - icon.HotspotX, info.Position.Y - region.Y - icon.HotspotY, info.Cursor, 0, 0, 0, IntPtr.Zero, 3); }
        finally
        {
            if (icon.Mask != IntPtr.Zero) Native.DeleteObject(icon.Mask);
            if (icon.Color != IntPtr.Zero) Native.DeleteObject(icon.Color);
        }
    }
    public void Dispose()
    {
        if (previous != IntPtr.Zero && memory != IntPtr.Zero) Native.SelectObject(memory, previous);
        if (bitmap != IntPtr.Zero) Native.DeleteObject(bitmap);
        if (memory != IntPtr.Zero) Native.DeleteDC(memory);
        if (screen != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, screen);
        previous = bitmap = memory = screen = bits = IntPtr.Zero;
    }
}

internal static class ScreenCapture
{
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Native.Point point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    internal static double MonitorScale(PixelPoint point)
    {
        var monitor = MonitorFromPoint(new Native.Point { X = (int)point.X, Y = (int)point.Y }, 2);
        return GetDpiForMonitor(monitor, 0, out var x, out _) >= 0 && x > 0 ? x / 96d : 1;
    }
    internal static PixelRect DesktopBounds => new PixelRect(Native.GetSystemMetrics(76), Native.GetSystemMetrics(77), Native.GetSystemMetrics(78), Native.GetSystemMetrics(79));
    internal static PixelRect MonitorBounds(PixelPoint point)
    {
        var monitor = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)point.X, (int)point.Y)).Bounds;
        return new PixelRect(monitor.X, monitor.Y, monitor.Width, monitor.Height);
    }
    internal static BitmapSource Capture(PixelRect region, bool cursor)
    {
        using (var grabber = new FrameGrabber(region))
        {
            var buffer = new byte[checked(region.Width * region.Height * 4)];
            grabber.Copy(buffer, cursor);
            var image = BitmapSource.Create(region.Width, region.Height, 96, 96, PixelFormats.Bgra32, null, buffer, region.Width * 4);
            image.Freeze(); return image;
        }
    }
    internal static (BitmapSource Image, BitmapSource WithCursor) CapturePair(PixelRect region)
    {
        using (var grabber = new FrameGrabber(region))
        {
            var buffer = new byte[checked(region.Width * region.Height * 4)]; grabber.Copy(buffer, false);
            var clean = BitmapSource.Create(region.Width, region.Height, 96, 96, PixelFormats.Bgra32, null, buffer, region.Width * 4); clean.Freeze();
            grabber.CopyCursorFrame(buffer);
            var cursor = BitmapSource.Create(region.Width, region.Height, 96, 96, PixelFormats.Bgra32, null, buffer, region.Width * 4); cursor.Freeze();
            return (clean, cursor);
        }
    }
    internal static BitmapSource Crop(BitmapSource desktop, PixelRect bounds, PixelRect selection)
    {
        var clipped = selection.Intersect(bounds);
        if (clipped.IsEmpty) throw new ArgumentException(TextCatalog.Get("截图区域为空。"));
        var image = new CroppedBitmap(desktop, new System.Windows.Int32Rect(clipped.X - bounds.X, clipped.Y - bounds.Y, clipped.Width, clipped.Height));
        image.Freeze(); return image;
    }
}
