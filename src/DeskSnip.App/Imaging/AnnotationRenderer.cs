using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.Core;

namespace DeskSnip.App.Imaging;

public static class AnnotationRenderer
{
    private static readonly ConditionalWeakTable<BitmapSource, ConditionalWeakTable<Annotation, DrawingGroup>> cache = new ConditionalWeakTable<BitmapSource, ConditionalWeakTable<Annotation, DrawingGroup>>();
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
        var items = annotations.ToArray(); var spots = items.Where(a => a.Kind == AnnotationKind.Spotlight).ToArray();
        if (spots.Length > 0)
        {
            Geometry bright = Geometry.Empty;
            foreach (var spot in spots) bright = new CombinedGeometry(GeometryCombineMode.Union, bright, new RectangleGeometry(new Rect(new Point(spot.Points[0].X, spot.Points[0].Y), new Point(spot.Points.Last().X, spot.Points.Last().Y))));
            var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, image.PixelWidth, image.PixelHeight)), bright);
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), null, outside);
        }
        foreach (var annotation in items) dc.DrawDrawing(Drawing(image, annotation));
        dc.Pop();
    }
    internal static Rect Bounds(BitmapSource image, Annotation item)
    {
        var drawing = Drawing(image, item);
        return drawing.Bounds;
    }
    internal static int HitTest(BitmapSource image, IReadOnlyList<Annotation> annotations, PixelPoint point, AnnotationKind? kind = null, double tolerance = 4)
    {
        for (var i = annotations.Count - 1; i >= 0; i--)
        {
            var item = annotations[i];
            if (kind.HasValue && item.Kind != kind.Value) continue;
            var drawing = Drawing(image, item);
            if (item.Kind == AnnotationKind.Text)
            {
                var bounds = drawing.Bounds;
                if (!bounds.IsEmpty) { bounds.Inflate(tolerance, tolerance); if (bounds.Contains(new Point(point.X, point.Y))) return i; }
            }
            else if (Contains(drawing, new Point(point.X, point.Y), tolerance, false)) return i;
        }
        return -1;
    }
    private static DrawingGroup Drawing(BitmapSource image, Annotation item)
    {
        return cache.GetValue(image, _ => new ConditionalWeakTable<Annotation, DrawingGroup>()).GetValue(item, annotation =>
        {
            var drawing = new DrawingGroup(); using (var dc = drawing.Open()) DrawItem(dc, image, annotation); if (drawing.CanFreeze) drawing.Freeze(); return drawing;
        });
    }
    private static bool Contains(Drawing drawing, Point point, double tolerance, bool selectInterior)
    {
        if (drawing is DrawingGroup group)
        {
            if (group.ClipGeometry != null && !group.ClipGeometry.FillContains(point)) return false;
            foreach (var child in group.Children) if (Contains(child, point, tolerance, selectInterior)) return true;
            return false;
        }
        if (drawing is GeometryDrawing geometry)
        {
            if ((geometry.Brush != null || selectInterior) && geometry.Geometry.FillContains(point)) return true;
            return geometry.Pen != null && geometry.Geometry.StrokeContains(new Pen(Brushes.Black, geometry.Pen.Thickness + tolerance * 2)
                { StartLineCap = geometry.Pen.StartLineCap, EndLineCap = geometry.Pen.EndLineCap, LineJoin = geometry.Pen.LineJoin }, point);
        }
        return drawing is ImageDrawing image && image.Rect.Contains(point);
    }
    private static double CornerRadius(Annotation item, Rect rect) => Math.Max(0, Math.Min(item.Appearance.CornerRadius, Math.Min(rect.Width, rect.Height) / 2));
    private static void DrawItem(DrawingContext dc, BitmapSource image, Annotation item)
    {
        var first = item.Points[0]; var last = item.Points[item.Points.Count - 1];
        var start = new Point(first.X, first.Y); var end = new Point(last.X, last.Y);
        var rect = new Rect(start, end);
        var brush = new SolidColorBrush(Color.FromArgb((byte)((item.Argb >> 24) * item.Appearance.Opacity), (byte)(item.Argb >> 16), (byte)(item.Argb >> 8), (byte)item.Argb));
        var pen = new Pen(brush, item.Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (item.Kind == AnnotationKind.Polyline)
        {
            pen.LineJoin = item.Appearance.LineJoin == AnnotationLineJoin.Miter ? PenLineJoin.Miter : item.Appearance.LineJoin == AnnotationLineJoin.Bevel ? PenLineJoin.Bevel : PenLineJoin.Round;
            pen.StartLineCap = pen.EndLineCap = item.Appearance.LineCap == AnnotationLineCap.Flat ? PenLineCap.Flat : item.Appearance.LineCap == AnnotationLineCap.Square ? PenLineCap.Square : PenLineCap.Round;
        }
        pen.DashStyle = item.Appearance.Dash == AnnotationDash.Dash ? DashStyles.Dash : item.Appearance.Dash == AnnotationDash.Dot ? DashStyles.Dot : DashStyles.Solid;
        switch (item.Kind)
        {
            case AnnotationKind.Rectangle:
                if (item.Appearance.CornerRadius > 0) { var rounded = CornerRadius(item, rect); dc.DrawRoundedRectangle(item.Appearance.Fill ? brush : null, pen, rect, rounded, rounded); }
                else dc.DrawRectangle(item.Appearance.Fill ? brush : null, pen, rect);
                break;
            case AnnotationKind.RoundedRectangle:
                var corner = item.Appearance.CornerRadius > 0 ? CornerRadius(item, rect) : Math.Min(rect.Width, rect.Height) / 6;
                dc.DrawRoundedRectangle(item.Appearance.Fill ? brush : null, pen, rect, corner, corner); break;
            case AnnotationKind.Spotlight: dc.DrawRectangle(null, pen, rect); break;
            case AnnotationKind.Cover: dc.DrawRectangle(brush, null, rect); break;
            case AnnotationKind.Ellipse: dc.DrawEllipse(item.Appearance.Fill ? brush : null, pen, new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), rect.Width / 2, rect.Height / 2); break;
            case AnnotationKind.Line: case AnnotationKind.Arrow:
                dc.DrawLine(pen, start, end);
                if (item.Kind == AnnotationKind.Arrow)
                {
                    void Head(Point tip, Point tail)
                    {
                        var direction = tail - tip; if (direction.Length < 1) return;
                        direction.Normalize(); var normal = new Vector(-direction.Y, direction.X); var size = Math.Max(12, item.Thickness * 4);
                        if (item.Appearance.Arrow == AnnotationArrow.Dimension) { dc.DrawLine(pen, tip + normal * size / 2, tip - normal * size / 2); return; }
                        var triangle = new StreamGeometry(); using (var g = triangle.Open()) { g.BeginFigure(tip, true, true); g.LineTo(tip + direction * size + normal * size / 2, true, false); g.LineTo(tip + direction * size - normal * size / 2, true, false); }
                        dc.DrawGeometry(item.Appearance.Arrow == AnnotationArrow.Outline ? null : brush, item.Appearance.Arrow == AnnotationArrow.Outline ? pen : null, triangle);
                    }
                    Head(end, start); if (item.Appearance.Arrow == AnnotationArrow.Double || item.Appearance.Arrow == AnnotationArrow.Dimension) Head(start, end);
                }
                break;
            case AnnotationKind.Pen: case AnnotationKind.Highlight: case AnnotationKind.Polyline:
                if (item.Kind == AnnotationKind.Highlight)
                {
                    brush = new SolidColorBrush(Color.FromArgb(85, brush.Color.R, brush.Color.G, brush.Color.B));
                    pen = new Pen(brush, Math.Max(12, item.Thickness * 5)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, DashStyle = item.Appearance.Dash == AnnotationDash.Dash ? DashStyles.Dash : item.Appearance.Dash == AnnotationDash.Dot ? DashStyles.Dot : DashStyles.Solid };
                }
                var path = new StreamGeometry();
                using (var g = path.Open())
                {
                    g.BeginFigure(start, false, false);
                    for (var i = 1; i < item.Points.Count; i++)
                    {
                        var current = new Point(item.Points[i].X, item.Points[i].Y);
                        if (item.Kind == AnnotationKind.Pen && item.Appearance.SmoothPencil && i < item.Points.Count - 1)
                        { var next = item.Points[i + 1]; g.QuadraticBezierTo(current, new Point((current.X + next.X) / 2, (current.Y + next.Y) / 2), true, true); }
                        else g.LineTo(current, true, false);
                    }
                    if (item.Points.Count == 1) g.LineTo(start + new Vector(.01, .01), true, false);
                }
                dc.DrawGeometry(item.Kind == AnnotationKind.Polyline && item.Appearance.Fill ? brush : null, pen, path); break;
            case AnnotationKind.Text:
                var content = item.Appearance.TextWrap == AnnotationTextWrap.Character ? string.Join("\u200B", item.Text.Select(c => c.ToString())) : item.Text;
                var typeface = new Typeface(new FontFamily(item.FontFamily), item.Italic ? FontStyles.Italic : FontStyles.Normal, item.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
                var text = new FormattedText(content, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, item.FontSize, brush, 1.0);
                if (item.Appearance.TextWrap != AnnotationTextWrap.None) text.MaxTextWidth = Math.Max(1, image.PixelWidth - start.X);
                if (item.Appearance.TextBackground) dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)), null, new Rect(start.X - 3, start.Y - 3, text.Width + 6, text.Height + 6), 3, 3);
                dc.DrawText(text, start); break;
            case AnnotationKind.Number:
                var radius = Math.Max(13, item.Thickness * 4);
                var serialBrush = item.Appearance.SerialBackground.HasValue ? new SolidColorBrush(Color.FromArgb((byte)(item.Appearance.SerialBackground.Value >> 24), (byte)(item.Appearance.SerialBackground.Value >> 16), (byte)(item.Appearance.SerialBackground.Value >> 8), (byte)item.Appearance.SerialBackground.Value)) : brush;
                dc.DrawEllipse(serialBrush, null, start, radius, radius);
                var label = Text(item.Text, radius * item.Appearance.SerialTextScale, Brushes.White);
                dc.DrawText(label, new Point(start.X - label.Width / 2, start.Y - label.Height / 2)); break;
            case AnnotationKind.Mosaic:
                if (rect.Width < 1 || rect.Height < 1) break;
                rect.Intersect(new Rect(0, 0, image.PixelWidth, image.PixelHeight)); if (rect.IsEmpty || rect.Width < 1 || rect.Height < 1) break;
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
            case AnnotationKind.Blur:
                var area = new PixelRect((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height).Intersect(new PixelRect(0, 0, image.PixelWidth, image.PixelHeight)); if (area.IsEmpty) break;
                var blurred = PixelEffects.Blur(PixelBuffers.FromBitmap(new CroppedBitmap(image, new Int32Rect(area.X, area.Y, area.Width, area.Height))), (int)Math.Max(1, item.Appearance.Effect * 3));
                dc.DrawImage(PixelBuffers.ToBitmap(blurred), new Rect(area.X, area.Y, area.Width, area.Height)); break;
            case AnnotationKind.Magnifier:
                var sourceArea = new PixelRect((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height).Intersect(new PixelRect(0, 0, image.PixelWidth, image.PixelHeight)); if (sourceArea.IsEmpty) break;
                var zoom = item.Appearance.Effect; var w = Math.Min(image.PixelWidth, sourceArea.Width * zoom); var h = Math.Min(image.PixelHeight, sourceArea.Height * zoom);
                var destination = new Rect(Math.Max(0, Math.Min(image.PixelWidth - w, sourceArea.Right + 12)), Math.Max(0, Math.Min(image.PixelHeight - h, sourceArea.Y)), w, h);
                dc.DrawLine(pen, new Point(sourceArea.X + sourceArea.Width / 2d, sourceArea.Y + sourceArea.Height / 2d), new Point(destination.X + destination.Width / 2, destination.Y + destination.Height / 2));
                dc.DrawRectangle(null, pen, new Rect(sourceArea.X, sourceArea.Y, sourceArea.Width, sourceArea.Height)); dc.DrawImage(new CroppedBitmap(image, new Int32Rect(sourceArea.X, sourceArea.Y, sourceArea.Width, sourceArea.Height)), destination); dc.DrawRectangle(null, pen, destination); break;
            case AnnotationKind.Eraser: dc.PushClip(new RectangleGeometry(rect)); dc.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight)); dc.Pop(); break;
            case AnnotationKind.MosaicPen: case AnnotationKind.CoverPen: case AnnotationKind.EraserPen:
                var stroke = new StreamGeometry(); using (var context = stroke.Open()) { context.BeginFigure(start, false, false); foreach (var point in item.Points.Skip(1)) context.LineTo(new Point(point.X, point.Y), true, false); if (item.Points.Count == 1) context.LineTo(start + new Vector(.01, .01), true, false); }
                var clip = stroke.GetWidenedPathGeometry(new Pen(Brushes.Black, Math.Max(8, item.Thickness * 5)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });
                dc.PushClip(clip);
                if (item.Kind == AnnotationKind.CoverPen) dc.DrawGeometry(brush, null, clip);
                else if (item.Kind == AnnotationKind.EraserPen) dc.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
                else DrawItem(dc, image, new Annotation(AnnotationKind.Mosaic, new[] { new PixelPoint(0, 0), new PixelPoint(image.PixelWidth, image.PixelHeight) }, item.Argb, item.Appearance.Effect * 4));
                dc.Pop(); break;
            case AnnotationKind.Watermark:
                var watermark = new FormattedText(item.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(item.FontFamily), item.FontSize, brush, 1);
                for (var y = start.Y; y < image.PixelHeight; y += Math.Max(40, watermark.Height * 3)) for (var x = start.X; x < image.PixelWidth; x += Math.Max(100, watermark.Width * 1.5)) dc.DrawText(watermark, new Point(x, y)); break;
        }
    }
    private static FormattedText Text(string text, double size, Brush brush)
        => new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), size, brush, 1.0);
}
