using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.Core;

namespace DeskSnip.App.Imaging;

internal static class CaptureMasks
{
    internal static BitmapSource Apply(BitmapSource image, CaptureShape shape, IReadOnlyList<PixelPoint>? points = null)
    {
        if (shape == CaptureShape.Rectangle) return image;
        Geometry mask;
        if (shape == CaptureShape.Ellipse) mask = new EllipseGeometry(new Rect(0, 0, image.PixelWidth, image.PixelHeight));
        else
        {
            if (points == null || points.Count < 3) throw new ArgumentException(L.Get("自由形状或多边形至少需要三个点。"));
            var path = new StreamGeometry();
            using (var context = path.Open()) { context.BeginFigure(new Point(points[0].X, points[0].Y), true, true); foreach (var point in points.Skip(1)) context.LineTo(new Point(point.X, point.Y), true, false); }
            mask = path;
        }
        var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) { drawing.PushClip(mask); drawing.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight)); drawing.Pop(); }
        var output = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32); output.Render(visual); output.Freeze(); return output;
    }
}
