using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.App.Imaging;
using DeskSnip.App.Windows;
using DeskSnip.Core;
using Microsoft.Win32;

namespace DeskSnip.App;

internal sealed class PinWindow : Window
{
    private readonly Image image;
    private readonly Border frame;
    private readonly StackPanel controls;
    private readonly Action<BitmapSource> edit;
    private IntPtr handle;
    internal BitmapSource Bitmap { get; private set; }
    internal PinPresentation Presentation { get; } = new PinPresentation();
    internal Action? PresentationChanged { get; set; }
    internal PinWindow(BitmapSource source, Action<BitmapSource> edit)
    {
        Bitmap = source; this.edit = edit;
        Title = "DeskSnip · 钉图"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        image = new Image { Source = source, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var grid = new Grid(); grid.Children.Add(image);
        controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
        controls.Children.Add(Ui.Button("···", Menu)); controls.Children.Add(Ui.Button("×", Close)); grid.Children.Add(controls);
        frame = new Border { Child = grid, BorderThickness = new Thickness(1), BorderBrush = Ui.Accent, Background = Brushes.White }; Content = frame;
        Width = Math.Min(source.PixelWidth + 2, 900); Height = Width / source.PixelWidth * source.PixelHeight;
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle).AddHook(Hook); SetInteraction(); ResizeImage();
        };
        Loaded += (_, _) => ResizeImage();
        MouseEnter += (_, _) => { if (!Presentation.ClickThrough) controls.Visibility = Visibility.Visible; };
        MouseLeave += (_, _) => controls.Visibility = Visibility.Collapsed;
        MouseLeftButtonDown += (_, e) =>
        {
            if (Presentation.Locked || e.OriginalSource is Button || OverControls(e.GetPosition(controls))) return;
            if (e.ClickCount == 2) { Presentation.ResetScale(); ResizeImage(); return; }
            try { DragMove(); } catch (InvalidOperationException) { }
        };
        MouseRightButtonUp += (_, e) => { Menu(); e.Handled = true; };
        MouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) Presentation.Fade(e.Delta);
            else Presentation.Zoom(e.Delta);
            ResizeImage(); e.Handled = true;
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
    private bool OverControls(Point point) => controls.Visibility == Visibility.Visible && point.X >= 0 && point.Y >= 0 && point.X < controls.ActualWidth && point.Y < controls.ActualHeight;
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return new IntPtr(3); }
        return IntPtr.Zero;
    }
    private void ResizeImage()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var work = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        var baseScale = Math.Min(1, Math.Min(work.Width * .9 / Bitmap.PixelWidth, work.Height * .9 / Bitmap.PixelHeight));
        Width = Math.Max(20, Bitmap.PixelWidth * baseScale * Presentation.Scale / dpi.DpiScaleX + 2);
        Height = Math.Max(20, Bitmap.PixelHeight * baseScale * Presentation.Scale / dpi.DpiScaleY + 2);
        Opacity = Presentation.Opacity;
        PresentationChanged?.Invoke();
    }
    private void SetInteraction()
    {
        if (handle == IntPtr.Zero) return;
        var style = Native.GetWindowLongPtr(handle, Native.GwlExStyle).ToInt64() | Native.WsExToolWindow | Native.WsExNoActivate;
        style = Presentation.ClickThrough ? style | Native.WsExTransparent : style & ~Native.WsExTransparent;
        Native.SetWindowLongPtr(handle, Native.GwlExStyle, new IntPtr(style));
        Native.SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
        frame.BorderBrush = Presentation.ClickThrough ? Brushes.Transparent : Presentation.Locked ? Brushes.Gray : Ui.Accent;
        if (Presentation.ClickThrough) controls.Visibility = Visibility.Collapsed;
        PresentationChanged?.Invoke();
    }
    internal void RestoreInteraction()
    {
        Presentation.RestoreInteraction(); SetInteraction(); ResizeImage();
        if (!IsVisible) Show(); Topmost = true;
    }
    internal string Description => $"{Bitmap.PixelWidth} × {Bitmap.PixelHeight} · {Presentation.Opacity:P0}{(Presentation.Locked ? " · 锁定" : "")}{(Presentation.ClickThrough ? " · 穿透" : "")}";
    private void Menu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Ui.Menu("复制图片", () => { try { Clipboard.SetImage(Bitmap); } catch (Exception e) { Ui.Error(this, e, "复制钉图"); } }));
        menu.Items.Add(Ui.Menu("保存图片…", Save));
        menu.Items.Add(Ui.Menu("继续标注", () => { edit(Bitmap); Close(); }));
        menu.Items.Add(Ui.Menu("顺时针旋转 90°", () => Transform(ImageTransform.RotateClockwise)));
        menu.Items.Add(Ui.Menu("水平翻转", () => Transform(ImageTransform.FlipHorizontal)));
        menu.Items.Add(Ui.Menu("垂直翻转", () => Transform(ImageTransform.FlipVertical)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Ui.Menu("放大  +", () => { Presentation.Zoom(1); ResizeImage(); }));
        menu.Items.Add(Ui.Menu("缩小  −", () => { Presentation.Zoom(-1); ResizeImage(); }));
        menu.Items.Add(Ui.Menu("适合屏幕 / 双击", () => { Presentation.ResetScale(); ResizeImage(); }));
        menu.Items.Add(Ui.Menu("增加透明度 / Ctrl+滚轮", () => { Presentation.Fade(-1); ResizeImage(); }));
        menu.Items.Add(Ui.Menu("恢复不透明", () => { while (Presentation.Opacity < 1) Presentation.Fade(1); ResizeImage(); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Ui.Menu("锁定位置和尺寸", () => { Presentation.Locked = !Presentation.Locked; SetInteraction(); }, Presentation.Locked));
        menu.Items.Add(Ui.Menu("鼠标穿透（控制台 / 托盘可解除）", () => { Presentation.ClickThrough = !Presentation.ClickThrough; SetInteraction(); }, Presentation.ClickThrough));
        menu.Items.Add(Ui.Menu("关闭（可在控制台恢复）", Close));
        menu.PlacementTarget = this; menu.IsOpen = true;
    }
    private void Save()
    {
        var dialog = new SaveFileDialog { Filter = "PNG 图片|*.png|JPEG 图片|*.jpg|BMP 图片|*.bmp", FileName = "DeskSnip-pin.png", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try { ImageFiles.Save(Bitmap, dialog.FileName); }
        catch (Exception e) { Ui.Error(this, e, "保存钉图"); }
    }
    private void Transform(ImageTransform transform)
    {
        try { Bitmap = ImageTransforms.Apply(Bitmap, transform); image.Source = Bitmap; ResizeImage(); }
        catch (Exception e) { Ui.Error(this, e, "变换钉图"); }
    }
}
