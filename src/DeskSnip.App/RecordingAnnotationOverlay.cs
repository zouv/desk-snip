using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.App.Windows;
using DeskSnip.Core;

namespace DeskSnip.App;

/// <summary>录屏区域的透明标注层：位于实时画面之上，默认鼠标穿透，选择标记工具后才接收绘制。</summary>
internal sealed class RecordingAnnotationOverlay : Window
{
    private readonly PixelRect region;
    private readonly AnnotationCanvas canvas;
    internal RecordingAnnotationOverlay(PixelRect region, IEnumerable<Annotation>? annotations = null)
    {
        this.region = region;
        Title = L.Get("DeskSnip · 录屏标注"); WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; WindowStartupLocation = WindowStartupLocation.Manual;
        canvas = new AnnotationCanvas(Transparent(region.Width, region.Height)) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, HideEditorChrome = true, ExcludeTextPopupFromCapture = true };
        if (annotations != null) foreach (var annotation in annotations) { try { canvas.Document.Add(annotation); } catch (ArgumentException) { } }
        Content = canvas;
        Width = region.Width; Height = region.Height;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle; var dpi = VisualTreeHelper.GetDpi(this);
            Width = region.Width / dpi.DpiScaleX; Height = region.Height / dpi.DpiScaleY;
            Native.SetWindowPos(handle, Native.Topmost, region.X, region.Y, region.Width, region.Height, 0x0040);
            SetClickThrough(true); UpdateCanvasScale();
        };
        Loaded += (_, _) => { UpdateCanvasScale(); SetClickThrough(!canvas.Kind.HasValue); };
        SizeChanged += (_, _) => UpdateCanvasScale();
    }
    internal AnnotationCanvas Canvas => canvas;
    internal IReadOnlyList<Annotation> Annotations => canvas.Document.Items;
    internal void SetTool(AnnotationKind? kind)
    {
        canvas.SetTool(kind); canvas.IsHitTestVisible = kind.HasValue;
        SetClickThrough(!kind.HasValue);
        if (kind.HasValue) { Show(); Activate(); canvas.Focus(); Keyboard.Focus(canvas); }
    }
    internal void SetClickThrough(bool enabled)
    {
        var handle = new WindowInteropHelper(this).Handle; if (handle == IntPtr.Zero) return;
        var style = Native.GetWindowLongPtr(handle, Native.GwlExStyle).ToInt64();
        if (enabled) style |= Native.WsExTransparent | Native.WsExNoActivate | Native.WsExToolWindow;
        else style &= ~(Native.WsExTransparent | Native.WsExNoActivate);
        Native.SetWindowLongPtr(handle, Native.GwlExStyle, new IntPtr(style));
        Native.SetWindowPos(handle, Native.Topmost, 0, 0, 0, 0, 0x0013);
    }
    internal void CloseForExit() => Close();
    private void UpdateCanvasScale()
    {
        if (canvas.Image.PixelWidth < 1 || ActualWidth < 1 || ActualHeight < 1) return;
        canvas.LayoutTransform = new ScaleTransform(ActualWidth / canvas.Image.PixelWidth, ActualHeight / canvas.Image.PixelHeight);
    }
    private static BitmapSource Transparent(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var index = 3; index < pixels.Length; index += 4) pixels[index] = 1;
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        image.Freeze(); return image;
    }
}
