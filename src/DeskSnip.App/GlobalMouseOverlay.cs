using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DeskSnip.App.Windows;
using DeskSnip.Core;

namespace DeskSnip.App;

internal sealed class GlobalMouseOverlay : ProductWindow
{
    private readonly PixelRect desktop;
    private readonly OverlayVisual visual;
    internal GlobalMouseOverlay(AppSettings settings)
    {
        desktop = ScreenCapture.DesktopBounds; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = true; AllowsTransparency = true; Background = Brushes.Transparent;
        visual = new OverlayVisual(desktop, (Brush)new BrushConverter().ConvertFromString(settings.CaptureMaskColor)!); Content = visual;
        SourceInitialized += (_, _) => { var handle = new WindowInteropHelper(this).Handle; var style = Native.GetWindowLongPtr(handle, Native.GwlExStyle).ToInt64(); Native.SetWindowLongPtr(handle, Native.GwlExStyle, new IntPtr(style | Native.WsExNoActivate | Native.WsExTransparent | Native.WsExToolWindow)); Native.SetWindowPos(handle, Native.Topmost, desktop.X, desktop.Y, desktop.Width, desktop.Height, 0x0050); Native.SetWindowDisplayAffinity(handle, 0x11); };
    }
    internal void SetRegion(PixelRect region) { visual.Region = region; visual.InvalidateVisual(); }
    private sealed class OverlayVisual : FrameworkElement
    {
        private readonly PixelRect bounds;
        private readonly Brush mask;
        internal PixelRect Region { get; set; }
        internal OverlayVisual(PixelRect bounds, Brush mask) { this.bounds = bounds; this.mask = mask; }
        protected override void OnRender(DrawingContext dc)
        {
            var sx = ActualWidth / bounds.Width; var sy = ActualHeight / bounds.Height; var region = Region.Intersect(bounds); var selected = new Rect((region.X - bounds.X) * sx, (region.Y - bounds.Y) * sy, region.Width * sx, region.Height * sy);
            dc.DrawGeometry(mask, null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)), new RectangleGeometry(selected)));
            if (!region.IsEmpty) dc.DrawRectangle(null, new Pen(FloatingTools.Blue, 1.5), selected);
        }
    }
}
