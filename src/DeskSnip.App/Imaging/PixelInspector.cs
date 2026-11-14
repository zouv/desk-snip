using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.Core;

namespace DeskSnip.App.Imaging;

/// <summary>取色与放大镜共用冻结桌面的物理像素，避免把遮罩采进来。</summary>
internal sealed class PixelInspector
{
    private readonly byte[] pixel = new byte[4];
    internal BitmapSource Image { get; }
    internal PixelRect Bounds { get; }
    internal PixelInspector(BitmapSource image, PixelRect bounds)
    {
        if (bounds.IsEmpty || image.PixelWidth != bounds.Width || image.PixelHeight != bounds.Height || image.Format != PixelFormats.Bgra32)
            throw new ArgumentException(TextCatalog.Get("冻结桌面图像需要匹配物理范围并使用 BGRA32 格式。"), nameof(image));
        Image = image; Bounds = bounds;
    }
    internal PixelPoint Clamp(PixelPoint point) => new PixelPoint(Math.Max(Bounds.X, Math.Min(Bounds.Right - 1, point.X)),
        Math.Max(Bounds.Y, Math.Min(Bounds.Bottom - 1, point.Y)));
    internal PixelColor Sample(PixelPoint point)
    {
        point = Clamp(point);
        Image.CopyPixels(new Int32Rect((int)point.X - Bounds.X, (int)point.Y - Bounds.Y, 1, 1), pixel, 4, 0);
        return new PixelColor(pixel[2], pixel[1], pixel[0]);
    }
    internal Int32Rect Viewport(PixelPoint point, int radius)
    {
        if (radius < 0 || radius > 100) throw new ArgumentOutOfRangeException(nameof(radius));
        point = Clamp(point);
        var x = (int)point.X - Bounds.X; var y = (int)point.Y - Bounds.Y;
        var left = Math.Max(0, x - radius); var top = Math.Max(0, y - radius);
        return new Int32Rect(left, top, Math.Min(Image.PixelWidth, x + radius + 1) - left, Math.Min(Image.PixelHeight, y + radius + 1) - top);
    }
}
