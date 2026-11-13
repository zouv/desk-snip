using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.Core;

namespace DeskSnip.App.Imaging;

public static class AnnotationRenderer
{
    public static BitmapSource Flatten(BitmapSource image, IEnumerable<Annotation> annotations)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) Draw(dc, image, annotations);
        var result = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual); result.Freeze(); return result;
    }
    internal static void Draw(DrawingContext dc, BitmapSource image, IEnumerable<Annotation> annotations)
    {
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, image.PixelWidth, image.PixelHeight)));
        dc.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
        foreach (var annotation in annotations) DrawItem(dc, image, annotation);
        dc.Pop();
    }
    private static void DrawItem(DrawingContext dc, BitmapSource image, Annotation item)
    {
        var first = item.Points[0]; var last = item.Points[item.Points.Count - 1];
        var start = new Point(first.X, first.Y); var end = new Point(last.X, last.Y);
        var rect = new Rect(start, end);
        var brush = new SolidColorBrush(Color.FromArgb((byte)(item.Argb >> 24), (byte)(item.Argb >> 16), (byte)(item.Argb >> 8), (byte)item.Argb));
        var pen = new Pen(brush, item.Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        switch (item.Kind)
        {
            case AnnotationKind.Rectangle: dc.DrawRectangle(null, pen, rect); break;
            case AnnotationKind.Cover: dc.DrawRectangle(brush, null, rect); break;
            case AnnotationKind.Ellipse: dc.DrawEllipse(null, pen, new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), rect.Width / 2, rect.Height / 2); break;
            case AnnotationKind.Line: case AnnotationKind.Arrow:
                dc.DrawLine(pen, start, end);
                if (item.Kind == AnnotationKind.Arrow)
                {
                    var direction = start - end;
                    if (direction.Length < 1) break;
                    direction.Normalize(); var normal = new Vector(-direction.Y, direction.X);
                    var size = Math.Max(12, item.Thickness * 4);
                    var triangle = new StreamGeometry();
                    using (var g = triangle.Open())
                    {
                        g.BeginFigure(end, true, true); g.LineTo(end + direction * size + normal * size / 2, true, false); g.LineTo(end + direction * size - normal * size / 2, true, false);
                    }
                    dc.DrawGeometry(brush, null, triangle);
                }
                break;
            case AnnotationKind.Pen: case AnnotationKind.Highlight:
                if (item.Kind == AnnotationKind.Highlight)
                {
                    brush = new SolidColorBrush(Color.FromArgb(85, brush.Color.R, brush.Color.G, brush.Color.B));
                    pen = new Pen(brush, Math.Max(12, item.Thickness * 5)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                }
                var path = new StreamGeometry();
                using (var g = path.Open())
                {
                    g.BeginFigure(start, false, false);
                    for (var i = 1; i < item.Points.Count; i++) g.LineTo(new Point(item.Points[i].X, item.Points[i].Y), true, false);
                    if (item.Points.Count == 1) g.LineTo(start + new Vector(.01, .01), true, false);
                }
                dc.DrawGeometry(null, pen, path); break;
            case AnnotationKind.Text:
                dc.DrawText(Text(item.Text, Math.Max(14, item.Thickness * 6), brush), start); break;
            case AnnotationKind.Number:
                var radius = Math.Max(13, item.Thickness * 4);
                dc.DrawEllipse(brush, null, start, radius, radius);
                var label = Text(item.Text, radius * 1.2, Brushes.White);
                dc.DrawText(label, new Point(start.X - label.Width / 2, start.Y - label.Height / 2)); break;
            case AnnotationKind.Mosaic:
                if (rect.Width < 1 || rect.Height < 1) break;
                var bounds = new Int32Rect((int)rect.X, (int)rect.Y, Math.Max(1, (int)rect.Width), Math.Max(1, (int)rect.Height));
                bounds.Width = Math.Min(bounds.Width, image.PixelWidth - bounds.X); bounds.Height = Math.Min(bounds.Height, image.PixelHeight - bounds.Y);
                if (bounds.Width < 1 || bounds.Height < 1) break;
                var block = Math.Max(12, item.Thickness * 4);
                var small = new RenderTargetBitmap(Math.Max(1, (int)(rect.Width / block)), Math.Max(1, (int)(rect.Height / block)), 96, 96, PixelFormats.Pbgra32);
                var patch = new DrawingVisual();
                using (var p = patch.RenderOpen()) p.DrawImage(new CroppedBitmap(image, bounds), new Rect(0, 0, small.PixelWidth, small.PixelHeight));
                small.Render(patch);
                var mosaic = new DrawingGroup(); RenderOptions.SetBitmapScalingMode(mosaic, BitmapScalingMode.NearestNeighbor);
                using (var m = mosaic.Open()) m.DrawImage(small, rect);
                dc.DrawDrawing(mosaic); break;
        }
    }
    private static FormattedText Text(string text, double size, Brush brush)
        => new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), size, brush, 1.0);
}
