using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.App.Imaging;
using DeskSnip.Core;

namespace DeskSnip.App;

internal sealed class PixelMagnifier : FrameworkElement
{
    private int Radius => Math.Max(1, (int)Math.Ceiling(GridSize / Cell / 2));
    private double Cell => settings.MagnifierZoom;
    private const double Padding = 12;
    private const double GridSize = 121;
    private readonly PixelInspector inspector;
    private readonly AppSettings settings;
    private readonly Brush gridBrush;
    private PixelPoint point;
    private PixelRect selection;
    private PixelColorFormat format;
    private bool relative;
    internal PixelMagnifier(PixelInspector inspector, AppSettings? settings = null)
    {
        this.inspector = inspector; this.settings = settings?.Clone() ?? new AppSettings();
        gridBrush = (Brush)new BrushConverter().ConvertFromString(this.settings.MagnifierGridColor)!;
        Width = GridSize + Padding * 2; Height = GridSize + (this.settings.MagnifierShowShortcuts ? 113 : 75);
        IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(this, this.settings.MagnifierSmooth ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
    }
    internal void Update(PixelPoint position, PixelRect region, PixelColorFormat colorFormat, bool selectionRelative)
    {
        point = inspector.Clamp(position); selection = region; format = colorFormat; relative = selectionRelative;
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(248, 23, 38, 50)), new Pen(Brushes.White, 1), new Rect(0, 0, Width, Height), 7, 7);
        var viewport = inspector.Viewport(point, Radius);
        var localX = (int)point.X - inspector.Bounds.X; var localY = (int)point.Y - inspector.Bounds.Y;
        var sample = new CroppedBitmap(inspector.Image, viewport);
        var grid = new Rect(Padding, Padding, GridSize, GridSize);
        dc.PushClip(new RectangleGeometry(grid));
        dc.DrawRectangle(Brushes.Black, null, grid);
        var origin = Padding + (GridSize - Cell) / 2;
        dc.DrawImage(sample, new Rect(origin + (viewport.X - localX) * Cell, origin + (viewport.Y - localY) * Cell, viewport.Width * Cell, viewport.Height * Cell));
        var pen = new Pen(gridBrush, .5);
        if (settings.MagnifierGrid) for (var i = -Radius; i <= Radius + 1; i++)
        {
            dc.DrawLine(pen, new Point(origin + i * Cell, Padding), new Point(origin + i * Cell, Padding + GridSize));
            dc.DrawLine(pen, new Point(Padding, origin + i * Cell), new Point(Padding + GridSize, origin + i * Cell));
        }
        if (settings.MagnifierCrosshair)
        {
            var center = Padding + GridSize / 2; var cross = new Pen(Brushes.Yellow, 1);
            dc.DrawLine(cross, new Point(Padding, center), new Point(origin - 2, center)); dc.DrawLine(cross, new Point(origin + Cell + 2, center), new Point(Padding + GridSize, center));
            dc.DrawLine(cross, new Point(center, Padding), new Point(center, origin - 2)); dc.DrawLine(cross, new Point(center, origin + Cell + 2), new Point(center, Padding + GridSize));
        }
        if (settings.MagnifierCenter) dc.DrawRectangle(null, new Pen(Brushes.Yellow, 2), new Rect(origin, origin, Cell, Cell));
        dc.Pop();
        var color = inspector.Sample(point);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue)), new Pen(Brushes.White, 1), new Rect(Padding, GridSize + 24, 16, 16));
        Text(dc, color.Format(format), Padding, GridSize + 45, 12);
        if (settings.MagnifierCoordinates) Text(dc, (relative && !selection.IsEmpty ? L.Get("选区 ") : L.Get("屏幕 ")) + PixelColor.Coordinates(point, selection, relative), Padding + 23, GridSize + 24, 11);
        if (settings.MagnifierShowShortcuts) { Text(dc, L.Get("C 取色 · Shift 格式 · P 坐标"), Padding, GridSize + 65, 10); Text(dc, L.Get("Alt+C 复制坐标 · WASD 微调"), Padding, GridSize + 82, 10); }
    }
    private void Text(DrawingContext dc, string text, double x, double y, double size)
    {
        var value = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(settings.InterfaceFont), size,
            Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = Width - Padding * 2 };
        dc.DrawText(value, new Point(x, y));
    }
}
