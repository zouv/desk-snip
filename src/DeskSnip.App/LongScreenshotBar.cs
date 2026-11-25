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

/// <summary>在截图选区旁显示完整长图，滚动选区内容时连续采集拼接。</summary>
internal sealed class LongScreenshotBar : ProductWindow
{
    private readonly PixelRect region;
    private readonly Action<BitmapSource, CaptureAction> finished;
    private readonly Action cancelled;
    private readonly Action? returnToSelection;
    private readonly FloatingToolbar toolbar = new FloatingToolbar { Name = "LongScreenshotControls" };
    private readonly TextBlock status = Ui.Text("", 11, FloatingTools.Ink);
    private readonly Image preview = new Image { Stretch = Stretch.Uniform };
    private readonly Border previewHost = new Border { Width = 280, Height = 420, Background = new SolidColorBrush(Color.FromRgb(245, 248, 251)), BorderBrush = FloatingTools.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(4) };
    private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly Size panelSize;
    private readonly Button automaticButton;
    private RecordingRegionWindow? regionBorder;
    private LongScreenshot? capture;
    private PixelBuffer? initialFrame;
    private PixelBuffer? previousFrame;
    private PixelRect? scrollViewport;
    private bool busy;
    private bool closed;
    private bool done;
    private bool automaticEnabled = true;
    private int frames;
    private int attempts;
    private StitchAxis axis;
    private int direction;
    private int? manualOverlap;
    private readonly bool shortcutsEnabled;
    private HwndSource? hotkeySource;
    private IntPtr hotkeyHandle;
    private const int PinHotkeyId = 0x534C;
    internal LongScreenshotBar(PixelRect region, AppSettings settings, Action<BitmapSource, CaptureAction> finished, Action cancelled, Action? returnToSelection = null)
    {
        this.region = region; this.finished = finished; this.cancelled = cancelled; this.returnToSelection = returnToSelection; shortcutsEnabled = settings.LongScreenshotShortcuts;
        axis = settings.LongScreenshotStart == "Horizontal" ? StitchAxis.Horizontal : StitchAxis.Vertical;
        Title = L.Get("DeskSnip · 长截图"); WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.HighQuality); previewHost.Child = preview;
        status.Width = 280; status.TextWrapping = TextWrapping.Wrap; status.MaxHeight = 54; status.Margin = new Thickness(0, 6, 0, 4);
        automaticButton = FloatingTools.Button(ToolGlyph.Pause, L.Get("暂停自动采集"), ToggleAutomatic);
        toolbar.Items.Children.Add(FloatingTools.Button(ToolGlyph.Check, L.Get("完成并复制"), () => Finish(CaptureAction.Copy), "Enter", true));
        toolbar.Items.Children.Add(FloatingTools.Button(ToolGlyph.Save, L.Get("保存长截图"), () => Finish(CaptureAction.Save), "Ctrl+S"));
        toolbar.Items.Children.Add(FloatingTools.Button(ToolGlyph.Undo, L.Get("撤回最后一步"), Undo, "Ctrl+Z"));
        toolbar.Items.Children.Add(automaticButton);
        var more = FloatingTools.Menu();
        more.Items.Add(FloatingTools.Item(L.Get("采集当前画面"), () => { _ = Collect(); }));
        more.Items.Add(FloatingTools.Item(L.Get("重新开始"), Restart));
        more.Items.Add(FloatingTools.Item(L.Get("丢弃待匹配帧"), () => { capture?.DiscardPending(); RefreshStatus(); }));
        var adjust = new MenuItem { Header = L.Get("方向与拼接"), Foreground = FloatingTools.Ink, Padding = new Thickness(8, 6, 8, 6), HorizontalContentAlignment = HorizontalAlignment.Left };
        adjust.Items.Add(FloatingTools.Item(L.Get("纵向"), () => SetAxis(StitchAxis.Vertical)));
        adjust.Items.Add(FloatingTools.Item(L.Get("横向"), () => SetAxis(StitchAxis.Horizontal)));
        adjust.Items.Add(new Separator());
        adjust.Items.Add(FloatingTools.Item(L.Get("自动判断滚动方向"), () => direction = 0));
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
        if (returnToSelection != null) toolbar.Items.Children.Add(FloatingTools.Button(ToolGlyph.Back, L.Get("返回普通截图"), ReturnToSelection));
        toolbar.Items.Children.Add(FloatingTools.Divider());
        toolbar.Items.Children.Add(FloatingTools.Button(ToolGlyph.Close, L.Get("退出截图"), Cancel, "Esc"));
        FloatingTools.Configure(toolbar.Items, settings.ToolbarIconSize, settings.ScreenshotShowShortcuts);
        toolbar.HorizontalAlignment = HorizontalAlignment.Center;
        toolbar.Background = Brushes.Transparent; toolbar.BorderThickness = new Thickness(0); toolbar.Padding = new Thickness(0); toolbar.Effect = null; toolbar.Margin = new Thickness(0, 4, 0, 0);
        var stack = new StackPanel { Margin = new Thickness(8) };
        stack.Children.Add(previewHost); stack.Children.Add(status); stack.Children.Add(toolbar);
        var panel = new Border { Width = 300, Background = Application.Current?.TryFindResource("PanelBrush") as Brush ?? Brushes.White, BorderBrush = FloatingTools.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = stack, Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 2, Opacity = .18 } };
        panel.Measure(new Size(300, double.PositiveInfinity)); panelSize = panel.DesiredSize;
        Width = panelSize.Width; SizeToContent = SizeToContent.Height; Content = panel;
        SizeChanged += (_, _) => Position();
        timer.Tick += async (_, _) => { if (!busy && automaticEnabled) await Collect(); };
        SourceInitialized += (_, _) => { var handle = new WindowInteropHelper(this).Handle; Native.SetWindowDisplayAffinity(handle, 0x11); hotkeyHandle = handle; hotkeySource = HwndSource.FromHwnd(handle); hotkeySource?.AddHook(HotkeyHook); Native.RegisterHotKey(handle, PinHotkeyId, 2 | 0x4000, 0x54); Position(); };
        Loaded += async (_, _) => { Position(); regionBorder = new RecordingRegionWindow(region); regionBorder.Show(); await Task.Delay(180); if (!closed) await Collect(); if (!closed && automaticEnabled) timer.Start(); };
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
    private void Position()
    {
        var handle = new WindowInteropHelper(this).Handle; if (handle == IntPtr.Zero) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(region.X + region.Width / 2, region.Y + region.Height / 2)); var work = screen.WorkingArea;
        var width = (int)Math.Ceiling((ActualWidth > 0 ? ActualWidth : panelSize.Width) * dpi.DpiScaleX);
        var height = (int)Math.Ceiling((ActualHeight > 0 ? ActualHeight : panelSize.Height) * dpi.DpiScaleY);
        var left = region.X - width - 12; var right = region.Right + 12;
        var x = left >= work.Left ? left : right + width <= work.Right ? right : Math.Max(work.Left, Math.Min(region.X, work.Right - width));
        var y = Math.Max(work.Top, Math.Min(region.Y + (region.Height - height) / 2, work.Bottom - height));
        Native.SetWindowPos(handle, Native.Topmost, x, y, width, height, 0x0040);
    }
    private void ToggleAutomatic()
    {
        automaticEnabled = !automaticEnabled;
        if (automaticEnabled && !closed) timer.Start(); else timer.Stop();
        FloatingTools.SetIcon(automaticButton, automaticEnabled ? ToolGlyph.Pause : ToolGlyph.Play, automaticEnabled ? L.Get("暂停自动采集") : L.Get("继续自动采集"));
        if (automaticEnabled) _ = Collect();
    }
    private async Task Collect()
    {
        if (busy || closed) return; busy = true;
        try
        {
            var bitmap = ScreenCapture.Capture(region, false);
            var frame = PixelBuffers.FromBitmap(bitmap); var refresh = false;
            if (capture == null)
            {
                capture = new LongScreenshot(frame, axis); initialFrame = previousFrame = frame; frames = 1; refresh = true;
                status.Text = L.Get("首帧已采集，请滚动内容。");
            }
            else
            {
                var session = capture; var previous = previousFrame; var selectedDirection = direction; var overlap = manualOverlap;
                var matched = await Task.Run(() => MatchFrame(session, previous, frame, selectedDirection, overlap));
                if (closed || !ReferenceEquals(capture, session) || !ReferenceEquals(previousFrame, previous)) return;
                capture = matched.Session; scrollViewport = matched.Viewport; previousFrame = frame;
                var result = matched.Result; var size = OutputSize();
                frames += result.CommittedFrames; refresh = result.CommittedFrames > 0;
                if (!result.Success && !result.Unchanged) attempts++;
                if (result.Success) status.Text = L.Format("已拼接 {0} 帧 · {1} × {2} px", frames, size.Width, size.Height);
                else if (result.Unchanged) status.Text = L.Format("已拼接 {0} 帧 · 等待滚动", frames);
                else status.Text = L.Format("已尝试 {0} 次 · {1}", attempts, result.Message);
                if (capture.PendingCount > 0) status.Text += L.Format(" · {0} 帧待连接", capture.PendingCount);
            }
            if (refresh && capture != null) RefreshPreview();
        }
        catch (Exception error) { automaticEnabled = false; timer.Stop(); FloatingTools.SetIcon(automaticButton, ToolGlyph.Play, L.Get("继续自动采集")); Ui.Error(this, error, L.Get("长截图采集")); }
        finally { busy = false; }
    }
    private (LongScreenshot Session, PixelRect? Viewport, StitchMatch Result) MatchFrame(LongScreenshot session, PixelBuffer? previous, PixelBuffer frame, int selectedDirection, int? overlap)
    {
        if (!scrollViewport.HasValue && frames == 1 && previous != null)
        {
            var detected = LongScreenshotViewport.Detect(previous, frame, axis);
            if (detected.HasValue)
            {
                var partial = new LongScreenshot(previous.Crop(detected.Value), axis);
                return (partial, detected, partial.Append(frame.Crop(detected.Value), selectedDirection, overlap));
            }
        }
        var sample = scrollViewport.HasValue ? frame.Crop(scrollViewport.Value) : frame;
        return (session, scrollViewport, session.Append(sample, selectedDirection, overlap));
    }
    private (int Width, int Height) OutputSize()
    {
        if (capture == null) return (0, 0);
        if (!scrollViewport.HasValue || initialFrame == null) return (capture.Image.Width, capture.Image.Height);
        return axis == StitchAxis.Vertical
            ? (initialFrame.Width, initialFrame.Height - scrollViewport.Value.Height + capture.Image.Height)
            : (initialFrame.Width - scrollViewport.Value.Width + capture.Image.Width, initialFrame.Height);
    }
    private PixelBuffer OutputImage() => scrollViewport.HasValue && initialFrame != null && capture != null
        ? LongScreenshotViewport.Compose(initialFrame, scrollViewport.Value, capture.Image, axis)
        : capture!.Image;
    private void RefreshStatus()
    {
        if (capture == null) { status.Text = L.Get("已清空，等待采集首帧。"); return; }
        var size = OutputSize();
        status.Text = L.Format("已拼接 {0} 帧 · {1} × {2} px", frames, size.Width, size.Height);
        if (capture.PendingCount > 0) status.Text += L.Format(" · {0} 帧待连接", capture.PendingCount);
    }
    private void Restart() { capture = null; initialFrame = previousFrame = null; scrollViewport = null; frames = attempts = 0; preview.Source = null; RefreshStatus(); }
    private void Undo()
    {
        if (capture == null) return;
        capture.Undo(); frames = Math.Max(1, frames - 1);
        var size = OutputSize();
        status.Text = L.Format("已撤回 · {0} × {1} px", size.Width, size.Height);
        RefreshPreview();
    }
    private void SetAxis(StitchAxis value)
    {
        if (capture != null) { status.Text = L.Get("已有拼接结果，重新开始后可切换方向。"); return; }
        axis = value; status.Text = value == StitchAxis.Vertical ? L.Get("纵向拼接已选择。") : L.Get("横向拼接已选择。");
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
        var image = OutputImage(); preview.Source = PixelBuffers.ToBitmap(image);
        Title = L.Format("DeskSnip · 长截图 {0} × {1}", image.Width, image.Height);
    }
    private void Finish(CaptureAction action)
    {
        if (done) return;
        if (busy) { status.Text = L.Get("正在匹配当前画面，请稍候。"); return; }
        if (capture == null) { status.Text = L.Get("请先采集一帧。"); return; }
        if (capture.PendingCount > 0) { status.Text = L.Get("仍有未连接画面；请继续滚动恢复，或在更多菜单中丢弃待匹配帧。"); return; }
        done = true; timer.Stop();
        var image = PixelBuffers.ToBitmap(OutputImage());
        try { finished(image, action); } finally { Close(); }
    }
    private void ReturnToSelection()
    {
        if (done || returnToSelection == null) return;
        done = true; timer.Stop();
        try { returnToSelection(); } finally { Close(); }
    }
    private void Cancel() { timer.Stop(); Close(); }
}
