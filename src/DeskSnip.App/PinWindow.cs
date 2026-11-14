using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using DeskSnip.App.Imaging;
using DeskSnip.App.Windows;
using DeskSnip.Core;
using Microsoft.Win32;

namespace DeskSnip.App;

internal sealed partial class PinWindow : ProductWindow
{
    private readonly Image image;
    private readonly Border frame;
    private readonly StackPanel controls;
    private readonly Action<BitmapSource> edit;
    private readonly Action<BitmapSource>? persist;
    private readonly bool restored;
    private readonly AppSettings initialSettings;
    private readonly PixelRect captureRegion;
    private Window? rescueToolbar;
    private AppSettings Settings => SettingsRequested?.Invoke() ?? initialSettings;
    internal BitmapSource OutputBitmap => Settings.PinOutputImage == "Original" ? original : Bitmap;
    internal bool CanMove => PinPreferenceRules.MovementAllowed(Settings, Presentation.Locked);
    private Native.Point dragStart;
    private double initialScale;
    private double initialLeft;
    private double initialTop;
    private bool resizing;
    private bool moving;
    private SelectionHandle resizeHandle;
    private bool forceClose;
    private bool closing;
    private bool closed;
    private BitmapSource original;
    private IntPtr handle;
    internal BitmapSource Bitmap { get; private set; }
    internal PinPresentation Presentation { get; } = new PinPresentation();
    internal Action? PresentationChanged { get; set; }
    internal Action<PinWindow, bool>? SelectRequested { get; set; }
    internal Action<PinWindow, int, int>? MoveRequested { get; set; }
    internal Action<BitmapSource>? RecognizeRequested { get; set; }
    internal Action<BitmapSource>? ServiceRecognitionRequested { get; set; }
    internal Func<AppSettings>? SettingsRequested { get; set; }
    internal Action<BitmapSource>? CopyRequested { get; set; }
    internal Func<BitmapSource, Window, string?>? SaveRequested { get; set; }
    internal Action<BitmapSource>? QuickSaveRequested { get; set; }
    private bool selected;
    internal bool Selected { get => selected; set { selected = value; UpdateFrame(); } }
    internal string Id { get; }
    internal string GroupId { get; set; }
    internal bool WantedVisible { get; set; } = true;
    internal PinWindow(BitmapSource source, Action<BitmapSource> edit, PinRecord? record = null, Action<BitmapSource>? persist = null, bool restored = false, BitmapSource? originalSource = null, AppSettings? settings = null, PixelRect captureRegion = default)
    {
        Bitmap = source; original = originalSource ?? source; this.edit = edit; this.persist = persist; this.restored = restored;
        initialSettings = settings?.Clone() ?? new AppSettings(); this.captureRegion = captureRegion;
        Id = record?.Id ?? Guid.NewGuid().ToString("N"); GroupId = record?.GroupId ?? "default";
        Title = L.Get("DeskSnip · 钉图"); WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = record?.Topmost ?? initialSettings.PinDefaultTopmost; Presentation.Topmost = Topmost; ShowInTaskbar = !Topmost && initialSettings.PinTaskbarWhenUnpinned; ShowActivated = false; WindowStartupLocation = WindowStartupLocation.Manual;
        Presentation.SetOpacity(initialSettings.PinDefaultOpacity / 100d);
        if (record != null)
        {
            Title = record.Title; Presentation.SetScale(record.Scale); Presentation.SetOpacity(record.Opacity);
            Presentation.Locked = record.Locked; Presentation.ClickThrough = record.ClickThrough; Presentation.Topmost = record.Topmost;
            WantedVisible = record.Visible;
            if (restored) { Left = record.X; Top = record.Y; }
        }
        image = new Image { Source = source, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, initialSettings.PinSmoothScaling ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
        var grid = new Grid(); grid.Children.Add(image);
        controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
        controls.Children.Add(Ui.Button("···", Menu)); controls.Children.Add(Ui.Button("×", () => RequestCloseFromInput(false))); grid.Children.Add(controls);
        frame = new Border { Child = grid, BorderThickness = new Thickness(1), BorderBrush = Ui.Accent, Background = Brushes.White }; Content = frame;
        Width = Math.Min(source.PixelWidth + 2, 900); Height = Width / source.PixelWidth * source.PixelHeight;
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle).AddHook(Hook);
            if (!this.restored && Settings.PinAutoFit) FitToScreen(); SetInteraction(); ResizeImage();
        };
        Loaded += (_, _) =>
        {
            ResizeImage();
            if (this.restored && record != null && Native.GetWindowRect(handle, out var rect)) Native.SetWindowPos(handle, IntPtr.Zero, (int)record.X, (int)record.Y, rect.Right - rect.Left, rect.Bottom - rect.Top, 0x0014);
            if (!this.restored) PositionNew();
            if (Settings.PinRescueOnShow) Rescue();
            RefreshAppearance();
        };
        Closed += (_, _) => { closed = true; CloseRescueToolbar(); };
        LocationChanged += (_, _) => { PositionRescueToolbar(); PresentationChanged?.Invoke(); };
        IsVisibleChanged += (_, _) => UpdateRescueToolbar();
        MouseEnter += (_, _) => { if (!Presentation.ClickThrough) controls.Visibility = Visibility.Visible; };
        MouseLeave += (_, _) => controls.Visibility = Visibility.Collapsed;
        InitializeConfiguredMouse();
        PreviewKeyDown += Keys;
        AllowDrop = true;
        Drop += (_, e) =>
        {
            if (Presentation.Locked && Settings.PinLockDrop) return;
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length == 1) Replace(ImageFiles.Load(files[0]));
                else if (e.Data.GetDataPresent(DataFormats.Bitmap) && e.Data.GetData(DataFormats.Bitmap) is BitmapSource bitmap) Replace(ImageFiles.Normalize(bitmap));
            }
            catch (Exception error) { Ui.Error(this, error, L.Get("替换钉图")); }
        };
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!forceClose && !PinPreferenceRules.CloseAllowed(Settings, Presentation.Locked, false)) e.Cancel = true;
        base.OnClosing(e);
        if (!e.Cancel) { closing = true; CloseRescueToolbar(); }
    }
    private bool OverControls(Point point) => controls.Visibility == Visibility.Visible && point.X >= 0 && point.Y >= 0 && point.X < controls.ActualWidth && point.Y < controls.ActualHeight;
    internal void MouseAction(string action, int direction, string anchor = "TopLeft")
    {
        try
        {
            switch (action)
            {
                case "Zoom": ResizeAnchored(() => Presentation.Zoom(direction, (Keyboard.Modifiers & ModifierKeys.Alt) != 0, Settings.PinZoomStep, Settings.PinFineZoomStep), anchor); break;
                case "Opacity": Presentation.Fade(direction, Settings.PinOpacityStep); ResizeImage(); break;
                case "Original": ResizeAnchored(Presentation.ToggleOriginal, anchor); break;
                case "Close": RequestCloseFromInput(false); break;
                case "Hide": WantedVisible = false; Hide(); PresentationChanged?.Invoke(); break;
                case "Copy": Copy(); break;
                case "Lock": Presentation.Locked = !Presentation.Locked; SetInteraction(); break;
            }
        }
        catch (Exception error) { Ui.Error(this, error, L.Get("钉图鼠标操作")); }
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0021 && Presentation.ClickThrough) { handled = true; return new IntPtr(3); }
        return IntPtr.Zero;
    }
    private void ResizeImage()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        Width = Math.Max(3, Bitmap.PixelWidth * Presentation.Scale / dpi.DpiScaleX + ChromeSize);
        Height = Math.Max(3, Bitmap.PixelHeight * Presentation.Scale / dpi.DpiScaleY + ChromeSize);
        Opacity = Presentation.ClickThrough ? Math.Min(Settings.PinClickThroughOpacity / 100d, Presentation.Opacity) : Presentation.Opacity;
        PositionRescueToolbar();
        PresentationChanged?.Invoke();
    }
    private void SetInteraction()
    {
        if (handle == IntPtr.Zero) return;
        var style = Native.GetWindowLongPtr(handle, Native.GwlExStyle).ToInt64() & ~Native.WsExNoActivate;
        style = Presentation.Topmost || !Settings.PinTaskbarWhenUnpinned ? style | Native.WsExToolWindow : style & ~Native.WsExToolWindow;
        style = Presentation.ClickThrough ? style | Native.WsExTransparent : style & ~Native.WsExTransparent;
        Native.SetWindowLongPtr(handle, Native.GwlExStyle, new IntPtr(style));
        Topmost = Presentation.Topmost; ShowInTaskbar = !Topmost && Settings.PinTaskbarWhenUnpinned;
        AllowDrop = !Presentation.Locked || !Settings.PinLockDrop;
        Native.SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
        UpdateFrame();
        if (Presentation.ClickThrough) controls.Visibility = Visibility.Collapsed;
        UpdateRescueToolbar(); ResizeImage();
        PresentationChanged?.Invoke();
    }
    private void UpdateFrame()
    {
        var current = Settings;
        frame.BorderBrush = Selected ? Brushes.Goldenrod : Presentation.ClickThrough ? Brush(current.ClickThroughColor) : Presentation.Locked && current.PinShowLockHint ? Brush(current.LockedPinColor) : Brush(current.AccentColor);
        frame.ToolTip = Presentation.Locked && current.PinShowLockHint ? L.Get("钉图已锁定；L 解锁") : null;
        frame.Effect = current.PinShowShadow ? new DropShadowEffect { Color = Presentation.ClickThrough ? ((SolidColorBrush)Brush(current.ClickThroughColor)).Color : Colors.Black, BlurRadius = 6, ShadowDepth = 1, Opacity = .3 } : null;
    }
    private static Brush Brush(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;
    private double ChromeSize => Settings.PinShowShadow ? 14 : 2;
    internal void RefreshAppearance()
    {
        if (closed) return;
        var current = Settings;
        RenderOptions.SetBitmapScalingMode(image, current.PinSmoothScaling ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
        frame.Margin = new Thickness(current.PinShowShadow ? 6 : 0);
        frame.Effect = current.PinShowShadow ? new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 1, Opacity = .3 } : null;
        frame.Background = current.PinTransparentBackground == "Transparent" ? Brushes.Transparent : current.PinTransparentBackground == "Light" ? Brushes.White : current.PinTransparentBackground == "Dark" ? Brush("#202020") : Checkerboard();
        AllowDrop = !Presentation.Locked || !current.PinLockDrop;
        SetInteraction(); ResizeImage();
    }
    private static Brush Checkerboard()
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        drawing.Children.Add(new GeometryDrawing(Brush("#DADADA"), null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        drawing.Children.Add(new GeometryDrawing(Brush("#DADADA"), null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
        var brush = new DrawingBrush(drawing) { Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute, TileMode = TileMode.Tile, Stretch = Stretch.None }; brush.Freeze(); return brush;
    }
    private void ChangeScale(Action operation)
    {
        if (!CanMove) return;
        var locked = Presentation.Locked; Presentation.Locked = false;
        try { operation(); } finally { Presentation.Locked = locked; }
    }
    private void ResizeAnchored(Action operation, string anchor)
    {
        if (!CanMove || anchor == "Disabled") return;
        var exists = handle != IntPtr.Zero && Native.GetWindowRect(handle, out _);
        var oldWidth = Width; var oldHeight = Height; var oldPosition = new PixelPoint(double.IsNaN(Left) ? 0 : Left, double.IsNaN(Top) ? 0 : Top);
        if (exists && Native.GetWindowRect(handle, out var before)) { oldWidth = before.Right - before.Left; oldHeight = before.Bottom - before.Top; oldPosition = new PixelPoint(before.Left, before.Top); }
        Native.GetCursorPos(out var mouse); ChangeScale(operation); ResizeImage();
        var dpi = VisualTreeHelper.GetDpi(this);
        var position = PinPreferenceRules.ResizePosition(oldPosition, oldWidth, oldHeight, Width * (exists ? dpi.DpiScaleX : 1), Height * (exists ? dpi.DpiScaleY : 1), anchor, new PixelPoint(mouse.X, mouse.Y));
        if (exists) Native.SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(position.X), (int)Math.Round(position.Y), 0, 0, 0x0015);
        else { Left = position.X; Top = position.Y; }
        PresentationChanged?.Invoke();
    }
    private void PositionNew()
    {
        if (handle == IntPtr.Zero) return;
        Native.GetCursorPos(out var mouse);
        var screen = Settings.PinDisplayPosition == "PrimaryCenter" ? System.Windows.Forms.Screen.PrimaryScreen : System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(mouse.X, mouse.Y));
        var work = screen.WorkingArea; var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Ceiling(Width * dpi.DpiScaleX); var height = (int)Math.Ceiling(Height * dpi.DpiScaleY);
        var x = work.X + (work.Width - width) / 2; var y = work.Y + (work.Height - height) / 2;
        if (Settings.PinDisplayPosition == "Mouse") { x = mouse.X; y = mouse.Y; }
        else if (Settings.PinDisplayPosition == "Screenshot" && !captureRegion.IsEmpty) { x = captureRegion.X - (int)Math.Round(ChromeSize / 2 * dpi.DpiScaleX); y = captureRegion.Y - (int)Math.Round(ChromeSize / 2 * dpi.DpiScaleY); }
        Native.SetWindowPos(handle, IntPtr.Zero, x, y, width, height, 0x0014);
    }
    private void UpdateRescueToolbar()
    {
        if (closed || closing) return;
        if (!Presentation.ClickThrough || !Settings.PinClickThroughToolbar)
        { CloseRescueToolbar(); return; }
        if (rescueToolbar == null && IsLoaded && IsVisible)
        {
            rescueToolbar = new ProductWindow { Title = L.Get("DeskSnip · 解除钉图穿透"), WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight, Topmost = true, ShowInTaskbar = false, ShowActivated = false, Owner = this };
            var bar = new FloatingToolbar { Margin = new Thickness(6) }; rescueToolbar.Content = bar;
            bar.Items.Children.Add(FloatingTools.Button(ToolGlyph.Pin, L.Get("解除鼠标穿透"), () => { Presentation.ClickThrough = false; SetInteraction(); }));
            FloatingTools.Configure(bar.Items, Settings.ToolbarIconSize, Settings.PinShowMenuShortcuts);
            rescueToolbar.Loaded += (_, _) => PositionRescueToolbar();
        }
        if (rescueToolbar != null) { if (IsVisible) rescueToolbar.Show(); else rescueToolbar.Hide(); PositionRescueToolbar(); }
    }
    private void CloseRescueToolbar()
    {
        var toolbar = rescueToolbar; rescueToolbar = null; toolbar?.Close();
    }
    private void PositionRescueToolbar()
    {
        if (rescueToolbar == null || !rescueToolbar.IsLoaded || handle == IntPtr.Zero || !Native.GetWindowRect(handle, out var rect)) return;
        var dpi = VisualTreeHelper.GetDpi(rescueToolbar); var width = Math.Max(1, (int)Math.Ceiling(rescueToolbar.ActualWidth * dpi.DpiScaleX)); var height = Math.Max(1, (int)Math.Ceiling(rescueToolbar.ActualHeight * dpi.DpiScaleY));
        var work = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        Native.SetWindowPos(new WindowInteropHelper(rescueToolbar).Handle, Native.Topmost, Math.Max(work.Left, Math.Min(rect.Right - width, work.Right - width)), Math.Max(work.Top, Math.Min(rect.Top - height, work.Bottom - height)), width, height, 0x0010);
    }
    internal void MoveByPhysical(int dx, int dy)
    {
        if (!CanMove) return;
        if (handle != IntPtr.Zero && Native.GetWindowRect(handle, out var rect)) Native.SetWindowPos(handle, IntPtr.Zero, rect.Left + dx, rect.Top + dy, 0, 0, 0x0015);
        else { var dpi = VisualTreeHelper.GetDpi(this); Left += dx / dpi.DpiScaleX; Top += dy / dpi.DpiScaleY; }
        PresentationChanged?.Invoke();
    }
    private void FitToScreen()
    {
        var work = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        ChangeScale(() => Presentation.SetScale(Math.Min(1, Math.Min(work.Width * .9 / Bitmap.PixelWidth, work.Height * .9 / Bitmap.PixelHeight))));
    }
    private void ResizeDrag()
    {
        if (!resizing || !CanMove) return;
        Native.GetCursorPos(out var point);
        var dx = point.X - dragStart.X; var dy = point.Y - dragStart.Y;
        var horizontal = (resizeHandle & (SelectionHandle.Left | SelectionHandle.Right)) != 0;
        var vertical = (resizeHandle & (SelectionHandle.Top | SelectionHandle.Bottom)) != 0;
        var changeX = ((resizeHandle & SelectionHandle.Left) != 0 ? -dx : dx) / (double)Bitmap.PixelWidth;
        var changeY = ((resizeHandle & SelectionHandle.Top) != 0 ? -dy : dy) / (double)Bitmap.PixelHeight;
        var change = horizontal && vertical ? Math.Abs(changeX) > Math.Abs(changeY) ? changeX : changeY : horizontal ? changeX : changeY;
        ChangeScale(() => Presentation.SetScale(Math.Max(.01, initialScale + change))); ResizeImage(); var dpi = VisualTreeHelper.GetDpi(this);
        if ((resizeHandle & SelectionHandle.Left) != 0) Left = initialLeft - Bitmap.PixelWidth * (Presentation.Scale - initialScale) / dpi.DpiScaleX;
        if ((resizeHandle & SelectionHandle.Top) != 0) Top = initialTop - Bitmap.PixelHeight * (Presentation.Scale - initialScale) / dpi.DpiScaleY;
    }
    internal void Rescue()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var screens = System.Windows.Forms.Screen.AllScreens.Select(s => new PixelRect(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height)).ToArray();
        var rectangle = handle != IntPtr.Zero && Native.GetWindowRect(handle, out var rect) ? new PixelRect(rect.Left, rect.Top, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top)) :
            new PixelRect((int)(double.IsNaN(Left) ? 0 : Left * dpi.DpiScaleX), (int)(double.IsNaN(Top) ? 0 : Top * dpi.DpiScaleY), Math.Max(1, (int)(Width * dpi.DpiScaleX)), Math.Max(1, (int)(Height * dpi.DpiScaleY)));
        var rescued = ScreenPlacement.Rescue(rectangle, screens);
        if (handle != IntPtr.Zero) Native.SetWindowPos(handle, IntPtr.Zero, rescued.X, rescued.Y, rescued.Width, rescued.Height, 0x0014);
        var locked = Presentation.Locked; Presentation.Locked = false;
        Presentation.SetScale(Math.Max(.01, Math.Min((rescued.Width - ChromeSize * dpi.DpiScaleX) / Bitmap.PixelWidth, (rescued.Height - ChromeSize * dpi.DpiScaleY) / Bitmap.PixelHeight)));
        Presentation.Locked = locked;
        PresentationChanged?.Invoke();
    }
    internal void CloseForExit() { forceClose = true; Close(); }
    internal void RequestClose() { if (PinPreferenceRules.CloseAllowed(Settings, Presentation.Locked, false)) Close(); }
    internal void CloseForDestruction() { forceClose = true; Close(); }
    private void RequestCloseFromInput(bool keyboard)
    {
        if (!PinPreferenceRules.CloseAllowed(Settings, Presentation.Locked, keyboard)) return;
        if ((keyboard ? Settings.PinConfirmKeyClose : Settings.PinConfirmMouseClose) && MessageBox.Show(this, L.Get("关闭当前钉图？关闭后可从历史恢复。"), L.Get("关闭钉图"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        forceClose = true; Close();
    }
    internal void Rename(string title) { if (string.IsNullOrWhiteSpace(title) || title.Length > 128) throw new ArgumentException(L.Get("标题需要 1–128 字符。")); Title = title; PresentationChanged?.Invoke(); }
    internal PinRecord CaptureRecord(PinRecord record)
    {
        var value = record.Clone(); value.Title = Title; value.GroupId = GroupId; value.Visible = WantedVisible;
        if (handle != IntPtr.Zero && Native.GetWindowRect(handle, out var rect)) { value.X = rect.Left; value.Y = rect.Top; }
        else { value.X = double.IsNaN(Left) ? 0 : Left; value.Y = double.IsNaN(Top) ? 0 : Top; }
        value.Scale = Presentation.Scale; value.Opacity = Presentation.Opacity; value.Locked = Presentation.Locked; value.ClickThrough = Presentation.ClickThrough; value.Topmost = Presentation.Topmost;
        value.PixelWidth = Bitmap.PixelWidth; value.PixelHeight = Bitmap.PixelHeight; return value;
    }
    internal void Replace(BitmapSource replacement)
    {
        replacement = ImageFiles.Normalize(replacement); persist?.Invoke(replacement); Bitmap = replacement; image.Source = Bitmap; ResizeImage();
    }
    private void Keys(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox) return;
        if (HandleConfiguredKey(e) || Settings.BuiltinShortcuts != null) return;
        var control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Shift) != 0) { RecognizeRequested?.Invoke(Bitmap); e.Handled = true; }
        else if (e.Key == Key.Escape) { RequestCloseFromInput(true); e.Handled = true; }
        else if (e.Key == Key.L) { Presentation.Locked = !Presentation.Locked; SetInteraction(); e.Handled = true; }
        else if (e.Key == Key.T && !control) { Presentation.Topmost = !Presentation.Topmost; SetInteraction(); e.Handled = true; }
        else if (e.Key == Key.B) { WantedVisible = false; Hide(); PresentationChanged?.Invoke(); e.Handled = true; }
        else if (e.Key == Key.C && control) { Copy(); e.Handled = true; }
        else if (e.Key == Key.S && control) { Save(); e.Handled = true; }
        else if (e.Key == Key.V && control && (!Presentation.Locked || !Settings.PinLockDrop)) { var bitmap = Clipboard.GetImage(); if (bitmap != null) Replace(bitmap); e.Handled = true; }
        else if (CanMove)
        {
            var dx = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0; var dy = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
            if (dx != 0 || dy != 0) { if (MoveRequested != null) MoveRequested(this, dx, dy); else MoveByPhysical(dx, dy); e.Handled = true; }
        }
    }
    internal void RestoreInteraction()
    {
        Presentation.RestoreInteraction(); Presentation.Topmost = true; SetInteraction(); ResizeImage();
        if (!IsVisible) Show();
    }
    internal string Description => L.Format("{0} × {1} · {2:P0}{3}{4}",Bitmap.PixelWidth,Bitmap.PixelHeight,Presentation.Opacity,(Presentation.Locked ? L.Get(" · 锁定") : ""),(Presentation.ClickThrough ? L.Get(" · 穿透") : ""));
    private void Menu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Ui.Menu(L.Get("复制图片"), Copy));
        menu.Items.Add(Ui.Menu(L.Get("复制原始图片"), () => Copy(original)));
        menu.Items.Add(Ui.Menu(L.Get("保存图片…"), Save));
        menu.Items.Add(Ui.Menu(L.Get("快速保存"), () => QuickSaveRequested?.Invoke(OutputBitmap)));
        menu.Items.Add(Ui.Menu(L.Get("继续标注"), () => edit(Bitmap)));
        menu.Items.Add(Ui.Menu(L.Get("本地文字识别"), () => RecognizeRequested?.Invoke(Bitmap)));
        menu.Items.Add(Ui.Menu(L.Get("联网识别 / 翻译"), () => ServiceRecognitionRequested?.Invoke(Bitmap)));
        menu.Items.Add(Ui.Menu(L.Get("顺时针旋转 90°"), () => Transform(ImageTransform.RotateClockwise)));
        menu.Items.Add(Ui.Menu(L.Get("水平翻转"), () => Transform(ImageTransform.FlipHorizontal)));
        menu.Items.Add(Ui.Menu(L.Get("垂直翻转"), () => Transform(ImageTransform.FlipVertical)));
        menu.Items.Add(Ui.Menu(L.Get("灰度"), () => Transform(ImageTransform.Grayscale))); menu.Items.Add(Ui.Menu(L.Get("反色"), () => Transform(ImageTransform.Invert)));
        menu.Items.Add(Ui.Menu(L.Get("增加亮度"), () => Transform(ImageTransform.Brighter))); menu.Items.Add(Ui.Menu(L.Get("降低亮度"), () => Transform(ImageTransform.Darker)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Ui.Menu(L.Get("放大"), () => MouseAction("Zoom", 1, Settings.PinWheelAnchor)));
        menu.Items.Add(Ui.Menu(L.Get("缩小"), () => MouseAction("Zoom", -1, Settings.PinWheelAnchor)));
        menu.Items.Add(Ui.Menu(L.Get("原始大小 / 切回"), () => MouseAction("Original", 1, Settings.PinMiddleAnchor)));
        menu.Items.Add(Ui.Menu(L.Get("适合屏幕"), () => { FitToScreen(); ResizeImage(); Rescue(); }));
        menu.Items.Add(Ui.Menu(L.Get("增加透明度"), () => MouseAction("Opacity", -1)));
        menu.Items.Add(Ui.Menu(L.Get("恢复不透明"), () => { while (Presentation.Opacity < 1) Presentation.Fade(1); ResizeImage(); }));
        menu.Items.Add(new Separator());
        var topmost = Ui.Menu(L.Get("窗口置顶"), () => { Presentation.Topmost = !Presentation.Topmost; SetInteraction(); }, Presentation.Topmost); topmost.InputGestureText = Settings.PinShowMenuShortcuts ? "T" : ""; menu.Items.Add(topmost);
        menu.Items.Add(Ui.Menu(L.Get("锁定位置和尺寸"), () => { Presentation.Locked = !Presentation.Locked; SetInteraction(); }, Presentation.Locked));
        menu.Items.Add(Ui.Menu(L.Get("鼠标穿透（控制台 / 托盘可解除）"), () => { Presentation.ClickThrough = !Presentation.ClickThrough; SetInteraction(); }, Presentation.ClickThrough));
        menu.Items.Add(Ui.Menu(L.Get("移回可见屏幕"), Rescue));
        menu.Items.Add(Ui.Menu(L.Get("修改标题"), () => { var prompt = new TextPrompt(Title) { Title = L.Get("钉图标题"), Owner = this }; if (prompt.ShowDialog() == true) Rename(prompt.Value); }));
        var close = Ui.Menu(L.Get("关闭（可在控制台恢复）"), () => RequestCloseFromInput(false)); close.InputGestureText = Settings.PinShowMenuShortcuts ? "Esc" : ""; menu.Items.Add(close);
        menu.PlacementTarget = this; menu.IsOpen = true;
    }
    private void Save()
    {
        if (SaveRequested != null) { SaveRequested(OutputBitmap, this); return; }
        var dialog = new SaveFileDialog { Filter = L.Get("PNG 图片|*.png|JPEG 图片|*.jpg|BMP 图片|*.bmp"), FileName = "DeskSnip-pin.png", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try { ImageFiles.Save(OutputBitmap, dialog.FileName, Settings.JpegQuality, Settings.SaveExif); }
        catch (Exception e) { Ui.Error(this, e, L.Get("保存钉图")); }
    }
    private void Copy() => Copy(OutputBitmap);
    private void Copy(BitmapSource source)
    {
        try { if (CopyRequested != null) CopyRequested(source); else Clipboard.SetImage(source); }
        catch (Exception error) { Ui.Error(this, error, L.Get("复制钉图")); }
    }
    private void Transform(ImageTransform transform)
    {
        try { Replace(ImageTransforms.Apply(Bitmap, transform)); }
        catch (Exception e) { Ui.Error(this, e, L.Get("变换钉图")); }
    }
}
