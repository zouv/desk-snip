using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskSnip.App.Imaging;
using DeskSnip.App.Windows;
using DeskSnip.Core;

namespace DeskSnip.App;

/// <summary>在截图选区左侧/右侧显示长图，滚动选区内容时自动采集拼接。</summary>
internal sealed class LongScreenshotBar : ProductWindow
{
    private readonly PixelRect region;
    private readonly Action<BitmapSource, CaptureAction> finished;
    private readonly Action cancelled;
    private readonly FloatingToolbar toolbar = new FloatingToolbar { Name = "LongScreenshotControls" };
    private readonly TextBlock status = Ui.Text("", 11, FloatingTools.Ink);
    private readonly Image preview = new Image { Stretch = Stretch.Uniform };
    private readonly ScrollViewer previewScroll = new ScrollViewer { Width = 280, Height = 420, Background = new SolidColorBrush(Color.FromRgb(30, 37, 45)) };
    private readonly CheckBox automatic = new CheckBox { Content = L.Get("自动采集"), IsChecked = true, Margin = new Thickness(0, 6, 0, 4), VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly Size panelSize;
    private RecordingRegionWindow? regionBorder;
    private LongScreenshot? capture;
    private bool busy;
    private bool closed;
    private bool done;
    private int frames;
    private int attempts;
    private StitchAxis axis;
    private int direction = 1;
    private int? manualOverlap;
    private readonly bool shortcutsEnabled;
    private HwndSource? hotkeySource;
    private IntPtr hotkeyHandle;
    private const int PinHotkeyId = 0x534C;
    internal LongScreenshotBar(PixelRect region, AppSettings settings, Action<BitmapSource, CaptureAction> finished, Action cancelled)
    {
        this.region = region; this.finished = finished; this.cancelled = cancelled; shortcutsEnabled = settings.LongScreenshotShortcuts;
        axis = settings.LongScreenshotStart == "Horizontal" ? StitchAxis.Horizontal : StitchAxis.Vertical;
        Title = L.Get("DeskSnip · 长截图"); WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.HighQuality); previewScroll.Content = preview;
        UpdatePreviewLayout();
        status.Width = 280; status.TextWrapping = TextWrapping.Wrap; status.MaxHeight = 54;
        automatic.Click += (_, _) => { if (automatic.IsChecked == true) timer.Start(); else timer.Stop(); };
        toolbar.Items.Children.Add(FloatingTools.Button(ToolGlyph.Check, L.Get("完成并复制"), () => Finish(CaptureAction.Copy), "Enter", true));
        toolbar.Items.Children.Add(FloatingTools.Button(ToolGlyph.Save, L.Get("保存长截图"), () => Finish(CaptureAction.Save), "Ctrl+S"));
        toolbar.Items.Children.Add(FloatingTools.Button(ToolGlyph.Undo, L.Get("撤回最后一步"), Undo, "Ctrl+Z"));
        var more = FloatingTools.Menu();
        more.Items.Add(FloatingTools.Item(L.Get("采集当前画面"), () => { _ = Collect(); }));
        more.Items.Add(FloatingTools.Item(L.Get("重新开始"), () => { capture = null; frames = attempts = 0; preview.Source = null; status.Text = L.Get("已清空，等待采集首帧。"); }));
        var adjust = new MenuItem { Header = L.Get("方向与拼接") };
        adjust.Items.Add(FloatingTools.Item(L.Get("纵向"), () => SetAxis(StitchAxis.Vertical)));
        adjust.Items.Add(FloatingTools.Item(L.Get("横向"), () => SetAxis(StitchAxis.Horizontal)));
        adjust.Items.Add(new Separator());
        adjust.Items.Add(FloatingTools.Item(L.Get("向下 / 向右"), () => direction = 1));
        adjust.Items.Add(FloatingTools.Item(L.Get("向上 / 向左"), () => direction = -1));
        adjust.Items.Add(FloatingTools.Item(L.Get("重叠像素"), PromptOverlap));
        adjust.Items.Add(new Separator());
        adjust.Items.Add(FloatingTools.Item(L.Get("裁掉当前画面之前"), () => { capture?.TrimBeforeCurrent(); RefreshPreview(); }));
        adjust.Items.Add(FloatingTools.Item(L.Get("裁掉当前画面之后"), () => { capture?.TrimAfterCurrent(); RefreshPreview(); }));
        more.Items.Add(adjust);
        more.Items.Add(new Separator());
        more.Items.Add(FloatingTools.Item(L.Get("钉图"), () => Finish(CaptureAction.Pin), "Ctrl+T"));
        more.Items.Add(FloatingTools.Item(L.Get("文字识别"), () => Finish(CaptureAction.Ocr)));
        more.Items.Add(FloatingTools.Item(L.Get("联网识别 / 翻译"), () => Finish(CaptureAction.ServiceRecognition)));
        more.Items.Add(FloatingTools.Item(L.Get("编辑长图"), () => Finish(CaptureAction.Edit)));
        toolbar.Items.Children.Add(FloatingTools.MenuButton(ToolGlyph.More, L.Get("更多长截图操作"), more));
        toolbar.Items.Children.Add(FloatingTools.Divider());
        toolbar.Items.Children.Add(FloatingTools.Button(ToolGlyph.Close, L.Get("取消长截图"), Cancel, "Esc"));
        FloatingTools.Configure(toolbar.Items, settings.ToolbarIconSize, settings.ScreenshotShowShortcuts);
        toolbar.Background = Brushes.Transparent; toolbar.BorderThickness = new Thickness(0); toolbar.Padding = new Thickness(0); toolbar.Effect = null; toolbar.Margin = new Thickness(0, 4, 0, 0);
        var stack = new StackPanel { Margin = new Thickness(8) };
        stack.Children.Add(previewScroll); stack.Children.Add(status); stack.Children.Add(automatic); stack.Children.Add(toolbar);
        var panel = new Border { Width = 300, Background = Application.Current?.TryFindResource("PanelBrush") as Brush ?? Brushes.White, BorderBrush = FloatingTools.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = stack, Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 2, Opacity = .18 } };
        panel.Measure(new Size(300, double.PositiveInfinity)); panelSize = panel.DesiredSize;
        Width = panelSize.Width; Height = panelSize.Height; Content = panel;
        timer.Tick += async (_, _) => { if (!busy && automatic.IsChecked == true) await Collect(); };
        SourceInitialized += (_, _) => { var handle = new WindowInteropHelper(this).Handle; Native.SetWindowDisplayAffinity(handle, 0x11); hotkeyHandle = handle; hotkeySource = HwndSource.FromHwnd(handle); hotkeySource?.AddHook(HotkeyHook); Native.RegisterHotKey(handle, PinHotkeyId, 2 | 0x4000, 0x54); Position(); };
        Loaded += async (_, _) => { Position(); regionBorder = new RecordingRegionWindow(region); regionBorder.Show(); await Task.Delay(180); if (!closed) await Collect(); if (!closed) timer.Start(); };
        Closed += (_, _) => { closed = true; timer.Stop(); if (hotkeyHandle != IntPtr.Zero) Native.UnregisterHotKey(hotkeyHandle, PinHotkeyId); hotkeySource?.RemoveHook(HotkeyHook); hotkeySource = null; hotkeyHandle = IntPtr.Zero; regionBorder?.Close(); regionBorder = null; if (!done) cancelled(); };
        PreviewKeyDown += (_, e) =>
        {
            if (!shortcutsEnabled) return;
            var control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (e.Key == Key.Enter) { Finish(CaptureAction.Copy); e.Handled = true; }
            else if (e.Key == Key.S && control) { Finish(CaptureAction.Save); e.Handled = true; }
            else if (e.Key == Key.Z && control) { Undo(); e.Handled = true; }
            else if (e.Key == Key.T && control) { Finish(CaptureAction.Pin); e.Handled = true; }
            else if (e.Key == Key.Escape) { Cancel(); e.Handled = true; }
        };
        status.Text = L.Get("请滚动选区内容，自动拼接；完成后选择输出。");
    }
    private IntPtr HotkeyHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == Native.WmHotkey && wParam.ToInt32() == PinHotkeyId) { handled = true; Finish(CaptureAction.Pin); }
        return IntPtr.Zero;
    }
    private void UpdatePreviewLayout()
    {
        if (axis == StitchAxis.Vertical)
        {
            preview.Width = 256; preview.Height = double.NaN;
            previewScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled; previewScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
        else
        {
            preview.Width = double.NaN; preview.Height = 356;
            previewScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto; previewScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        }
    }
    private void Position()
    {
        var handle = new WindowInteropHelper(this).Handle; if (handle == IntPtr.Zero) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(region.X + region.Width / 2, region.Y + region.Height / 2)); var work = screen.WorkingArea;
        var width = (int)Math.Ceiling(panelSize.Width * dpi.DpiScaleX); var height = (int)Math.Ceiling(panelSize.Height * dpi.DpiScaleY);
        var left = region.X - width - 12; var right = region.Right + 12;
        var x = left >= work.Left ? left : right + width <= work.Right ? right : Math.Max(work.Left, Math.Min(region.X, work.Right - width));
        var y = Math.Max(work.Top, Math.Min(region.Y + (region.Height - height) / 2, work.Bottom - height));
        Native.SetWindowPos(handle, Native.Topmost, x, y, width, height, 0x0040);
    }
    private async Task Collect()
    {
        if (busy || closed) return; busy = true;
        await Task.Yield();
        try
        {
            var bitmap = ScreenCapture.Capture(region, false);
            if (capture == null)
            {
                capture = new LongScreenshot(PixelBuffers.FromBitmap(bitmap), axis); frames = 1;
                status.Text = L.Get("首帧已采集，请滚动内容。");
            }
            else
            {
                var result = capture.Append(PixelBuffers.FromBitmap(bitmap), direction, manualOverlap);
                status.Text = result.Success ? L.Format("已拼接 {0} 帧 · {1} × {2} px",++frames,capture.Image.Width,capture.Image.Height) : L.Format("已尝试 {0} 次 · {1}",++attempts,result.Message);
            }
            if (capture != null) RefreshPreview();
        }
        catch (Exception error) { automatic.IsChecked = false; timer.Stop(); Ui.Error(this, error, L.Get("长截图采集")); }
        finally { busy = false; }
    }
    private void Undo()
    {
        if (capture == null) return;
        capture.Undo(); frames = Math.Max(1, frames - 1);
        status.Text = L.Format("已撤回 · {0} × {1} px", capture.Image.Width, capture.Image.Height);
        RefreshPreview();
    }
    private void SetAxis(StitchAxis value)
    {
        if (capture != null) { status.Text = L.Get("已有拼接结果，重新开始后可切换方向。"); return; }
        axis = value; UpdatePreviewLayout(); status.Text = value == StitchAxis.Vertical ? L.Get("纵向拼接已选择。") : L.Get("横向拼接已选择。");
    }
    private void PromptOverlap()
    {
        var prompt = new TextPrompt(manualOverlap?.ToString() ?? "") { Title = L.Get("重叠像素"), Owner = this };
        if (prompt.ShowDialog() != true) return;
        if (string.IsNullOrWhiteSpace(prompt.Value)) { manualOverlap = null; status.Text = L.Get("已恢复自动匹配重叠。"); return; }
        if (!int.TryParse(prompt.Value, out var value) || value < 1) { status.Text = L.Get("重叠像素请输入正整数。"); return; }
        manualOverlap = value; status.Text = L.Format("下一次采集按 {0} px 重叠拼接。", value);
    }
    private void RefreshPreview()
    {
        if (capture == null) { preview.Source = null; return; }
        preview.Source = PixelBuffers.ToBitmap(capture.Image);
        Title = L.Format("DeskSnip · 长截图 {0} × {1}", capture.Image.Width, capture.Image.Height);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (axis == StitchAxis.Vertical) previewScroll.ScrollToVerticalOffset(double.MaxValue); else previewScroll.ScrollToHorizontalOffset(double.MaxValue);
        }), DispatcherPriority.Background);
    }
    private void Finish(CaptureAction action)
    {
        if (done) return;
        if (capture == null) { status.Text = L.Get("请先采集一帧。"); return; }
        done = true; timer.Stop();
        var image = PixelBuffers.ToBitmap(capture.Image);
        try { finished(image, action); } finally { Close(); }
    }
    private void Cancel() { timer.Stop(); Close(); }
}
