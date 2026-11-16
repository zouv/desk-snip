using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DeskSnip.App.Recording;
using DeskSnip.App.Windows;
using DeskSnip.Core;
using Microsoft.Win32;

namespace DeskSnip.App;

/// <summary>旧的独立录制设置对话框；当前录制流程不再打开，参数由 CaptureWindow.Plan 与设置页提供。</summary>
internal sealed class RecordingSetup : ProductWindow
{
    private readonly ComboBox format;
    private readonly ComboBox fps;
    private readonly ComboBox encoder;
    private readonly ComboBox delay;
    private readonly CheckBox cursor;
    private readonly CheckBox toolbar;
    private readonly CheckBox systemAudio;
    private readonly CheckBox microphone;
    private readonly ComboBox systemDevice;
    private readonly ComboBox microphoneDevice;
    private readonly TextBox ffmpeg;
    private readonly TextBox output;
    private readonly Slider quality;
    private readonly TextBlock status;
    internal string Format => (string)format.SelectedItem;
    internal int Fps => (int)fps.SelectedItem;
    internal bool CursorIncluded => cursor.IsChecked == true;
    internal bool ToolbarVisible => toolbar.IsChecked == true;
    internal int Delay => (int)delay.SelectedItem;
    internal int Quality => (int)quality.Value;
    internal string OutputPath => output.Text;
    internal RecordingOptions? Options { get; private set; }
    internal void ApplyPreferences(AppSettings settings)
    {
        settings.FfmpegPath = ffmpeg.Text.Trim(); settings.RecordSystemAudio = systemAudio.IsChecked == true; settings.RecordMicrophone = microphone.IsChecked == true;
        settings.VideoQuality = (int)quality.Value; settings.VideoEncoder = (VideoEncoder)encoder.SelectedIndex; settings.RecordingDelay = Delay; settings.FramesPerSecond = Fps; settings.RecordingFormat = Format; settings.CaptureCursor = CursorIncluded;
    }
    private readonly string dataRoot;
    internal RecordingSetup(PixelRect region, AppSettings settings, Func<string, string> defaultPath, string dataRoot)
    {
        this.dataRoot = dataRoot;
        Title = L.Get("DeskSnip · 开始录制"); Width = 620; Height = 440; MinHeight = 380; MinWidth = 560; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new StackPanel { Margin = new Thickness(20) }; Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(Ui.Text(L.Get("准备录屏"), 18)); root.Children.Add(Ui.Text($"{region.Width} × {region.Height} px · {region.X},{region.Y}", 12, Ui.Subtle));
        var options = new WrapPanel { Margin = new Thickness(0, 12, 0, 6) }; root.Children.Add(options);
        options.Children.Add(Ui.Text(L.Get("格式"))); format = new ComboBox { Width = 95, ItemsSource = new[] { "MP4", "GIF", "WebP" }, SelectedItem = settings.RecordingFormat, Margin = new Thickness(8) }; options.Children.Add(format);
        options.Children.Add(Ui.Text(L.Get("帧率"))); fps = new ComboBox { Width = 80, ItemsSource = new[] { settings.FramesPerSecond, 5, 16, 24, 30, 60 }.Distinct().OrderBy(f => f).ToArray(), SelectedItem = settings.FramesPerSecond, Margin = new Thickness(8) }; options.Children.Add(fps);
        options.Children.Add(Ui.Text(L.Get("倒计时"))); delay = new ComboBox { Width = 70, ItemsSource = new[] { 0, 1, 2, 3, 5, 10 }, SelectedItem = settings.RecordingDelay, Margin = new Thickness(8) }; if (delay.SelectedIndex < 0) delay.SelectedIndex = 0; options.Children.Add(delay);
        cursor = new CheckBox { Content = L.Get("包含鼠标指针"), IsChecked = settings.CaptureCursor, Margin = new Thickness(20, 10, 3, 5) };
        systemAudio = new CheckBox { Content = L.Get("系统播放声音"), IsChecked = settings.RecordSystemAudio, Margin = new Thickness(3, 10, 3, 5) };
        microphone = new CheckBox { Content = L.Get("麦克风"), IsChecked = settings.RecordMicrophone, Margin = new Thickness(3, 10, 3, 5) };
        var sounds = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) }; sounds.Children.Add(systemAudio); microphone.Margin = new Thickness(20, 10, 3, 5); sounds.Children.Add(microphone); sounds.Children.Add(cursor); root.Children.Add(sounds);
        toolbar = new CheckBox { Content = L.Get("显示录制工具栏（隐藏时从托盘停止录制）"), IsChecked = settings.RecordingToolbarDisplayMode != "Hidden" || settings.TrayIconMode == "Hidden", IsEnabled = settings.RecordingToolbarDisplayMode == "Ask" && settings.TrayIconMode != "Hidden", Margin = new Thickness(3, 4, 3, 10) };
        toolbar.ToolTip = settings.TrayIconMode == "Hidden" ? L.Get("托盘隐藏时保留工具栏，以便停止录制") : L.Get("默认行为可在截图设置中调整"); System.Windows.Automation.AutomationProperties.SetName(toolbar, L.Get("显示录制工具栏")); root.Children.Add(toolbar);
        var advanced = new StackPanel { Margin = new Thickness(5, 10, 5, 8) }; var advancedPanel = new Expander { Header = L.Get("声音设备与高级选项"), Content = advanced, Margin = new Thickness(0, 0, 0, 12) }; root.Children.Add(advancedPanel);
        systemDevice = new ComboBox { MinWidth = 330, Margin = new Thickness(3) }; microphoneDevice = new ComboBox { MinWidth = 330, Margin = new Thickness(3) };
        var audioRow = new DockPanel(); var audioLabel = Ui.Text(L.Get("系统声音"), 12); audioLabel.Width = 75; audioLabel.VerticalAlignment = VerticalAlignment.Center; audioRow.Children.Add(audioLabel); audioRow.Children.Add(systemDevice); advanced.Children.Add(audioRow);
        var micRow = new DockPanel(); var micLabel = Ui.Text(L.Get("麦克风"), 12); micLabel.Width = 75; micLabel.VerticalAlignment = VerticalAlignment.Center; micRow.Children.Add(micLabel); micRow.Children.Add(microphoneDevice); advanced.Children.Add(micRow);
        try { systemDevice.ItemsSource = AudioDevices.List(true); microphoneDevice.ItemsSource = AudioDevices.List(false); systemDevice.SelectedIndex = microphoneDevice.SelectedIndex = 0; }
        catch (Exception) { systemAudio.IsChecked = microphone.IsChecked = false; }
        advanced.Children.Add(Ui.Text(L.Get("FFmpeg 路径（声音 / WebP 需要；留空自动查找）"), 12, Ui.Subtle));
        ffmpeg = new TextBox { Text = settings.FfmpegPath, Padding = new Thickness(6), VerticalContentAlignment = VerticalAlignment.Center };
        var ffRow = new DockPanel { Margin = new Thickness(0, 8, 0, 10) }; var browseFf = Ui.Button(L.Get("选择…"), () => { var dialog = new OpenFileDialog { Filter = L.Get("FFmpeg|ffmpeg.exe|程序|*.exe") }; if (dialog.ShowDialog(this) == true) ffmpeg.Text = dialog.FileName; }); DockPanel.SetDock(browseFf, Dock.Right); ffRow.Children.Add(browseFf); ffRow.Children.Add(ffmpeg); advanced.Children.Add(ffRow);
        var encodeRow = new WrapPanel(); encodeRow.Children.Add(Ui.Text(L.Get("编码器"))); encoder = new ComboBox { Width = 145, ItemsSource = new[] { L.Get("软件"), L.Get("自动硬件 / 软件回退"), "NVIDIA", "Intel", "AMD" }, SelectedIndex = (int)settings.VideoEncoder, Margin = new Thickness(8) }; encodeRow.Children.Add(encoder);
        encodeRow.Children.Add(Ui.Text(L.Get("画质"))); quality = new Slider { Minimum = 1, Maximum = 100, Value = settings.VideoQuality, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 150, Margin = new Thickness(8) }; encodeRow.Children.Add(quality); advanced.Children.Add(encodeRow);
        root.Children.Add(Ui.Text(L.Get("保存位置"))); var pathRow = new DockPanel { Margin = new Thickness(0, 7, 0, 0) }; var browse = Ui.Button(L.Get("选择…"), Browse); DockPanel.SetDock(browse, Dock.Right); pathRow.Children.Add(browse);
        output = new TextBox { Text = defaultPath(Format), IsReadOnly = true, Padding = new Thickness(7), VerticalContentAlignment = VerticalAlignment.Center }; pathRow.Children.Add(output); root.Children.Add(pathRow);
        format.SelectionChanged += (_, _) => output.Text = defaultPath(Format);
        status = Ui.Text(L.Get("GIF / WebP 不含声音。停止后保存到所选位置。"), 11, Ui.Subtle); status.Margin = new Thickness(3, 8, 0, 0); root.Children.Add(status);
        foreach (var pair in new[] { (format, L.Get("录制格式")), (fps, L.Get("录制帧率")), (delay, L.Get("开始倒计时")), (encoder, L.Get("导出编码器")), (systemDevice, L.Get("系统声音设备")), (microphoneDevice, L.Get("麦克风设备")) }) System.Windows.Automation.AutomationProperties.SetName(pair.Item1, pair.Item2);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
        buttons.Children.Add(Ui.Button(L.Get("取消"), () => DialogResult = false)); buttons.Children.Add(Ui.Button(L.Get("开始录制"), Start, true)); root.Children.Add(buttons);
    }
    private void Start()
    {
        try
        {
            if (systemAudio.IsChecked == true && systemDevice.SelectedItem == null) throw new ArgumentException(L.Get("未检测到系统音频设备。"));
            if (microphone.IsChecked == true && microphoneDevice.SelectedItem == null) throw new ArgumentException(L.Get("未检测到麦克风设备。"));
            var requireFfmpeg = systemAudio.IsChecked == true || microphone.IsChecked == true || Format == "WebP" || encoder.SelectedIndex > 0;
            string? executable = null; try { executable = Ffmpeg.Find(ffmpeg.Text.Trim()); } catch (InvalidOperationException) when (!requireFfmpeg) { }
            if (executable != null) Options = new RecordingOptions { DataRoot = dataRoot, FfmpegPath = executable, SystemAudio = systemAudio.IsChecked == true, Microphone = microphone.IsChecked == true,
                SystemDeviceId = (systemDevice.SelectedItem as AudioDevice)?.Id ?? "", MicrophoneDeviceId = (microphoneDevice.SelectedItem as AudioDevice)?.Id ?? "", Quality = (int)quality.Value, Encoder = (VideoEncoder)encoder.SelectedIndex };
            DialogResult = true;
        }
        catch (InvalidOperationException error) { status.Text = error.Message; }
        catch (Exception error) { Ui.Error(this, error, L.Get("录制设置")); }
    }
    private void Browse()
    {
        var dialog = new SaveFileDialog { Filter = Format + "|*." + Format.ToLowerInvariant(), FileName = System.IO.Path.GetFileName(output.Text), AddExtension = true, DefaultExt = Format.ToLowerInvariant() };
        if (dialog.ShowDialog(this) == true) output.Text = dialog.FileName;
    }
}

internal sealed class CountdownWindow : ProductWindow
{
    internal CountdownWindow(int seconds)
    {
        Title = L.Get("DeskSnip · 倒计时"); Width = 280; Height = 210; WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true; ResizeMode = ResizeMode.NoResize;
        var root = new StackPanel { Margin = new Thickness(24) }; Content = root; var label = Ui.Text(seconds.ToString(), 60, Ui.Accent); label.TextAlignment = TextAlignment.Center; root.Children.Add(label);
        root.Children.Add(Ui.Button(L.Get("取消"), () => DialogResult = false)); var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => { seconds--; if (seconds <= 0) { timer.Stop(); DialogResult = true; } else label.Text = seconds.ToString(); };
        Loaded += (_, _) => timer.Start(); Closed += (_, _) => timer.Stop();
    }
}

internal sealed class RecordingBar : ProductWindow
{
    private readonly RecordingSession session;
    private readonly Action stop;
    private readonly Button pause;
    private readonly Button stopButton;
    private readonly TextBlock label;
    private readonly System.Windows.Shapes.Ellipse indicator;
    private readonly Button? restartButton;
    private readonly Button? discardButton;
    private readonly Button dragButton;
    private readonly Size toolbarSize;
    private RecordingRegionWindow? regionWindow;
    private bool stopRequested;
    private readonly string pauseHotkey;
    private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
    internal bool Finishing { get; set; }
    internal RecordingBar(RecordingSession session, Action stop, Action? discard = null, Action? restart = null, AppSettings? settings = null, RecordingAnnotationOverlay? overlay = null)
    {
        this.session = session; this.stop = stop;
        pauseHotkey = settings?.ScreenshotShowShortcuts == false ? "" : settings?.RecordHotkey ?? "Ctrl+Alt+R";
        Title = L.Get("DeskSnip · 录制中"); WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        var root = new FloatingToolbar { Name = "RecordingControls", Margin = new Thickness(6) }; Content = root;
        var state = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        indicator = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = FloatingTools.Red, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }; state.Children.Add(indicator);
        label = Ui.Text("00:00", 12, FloatingTools.Ink); label.Width = 78; label.FontFamily = new FontFamily("Consolas"); label.TextWrapping = TextWrapping.NoWrap; label.VerticalAlignment = VerticalAlignment.Center; state.Children.Add(label); root.Items.Children.Add(state);
        var fps = Ui.Text(session.FramesPerSecond + " FPS", 11, FloatingTools.MutedInk); fps.Margin = new Thickness(0, 0, 8, 0); fps.VerticalAlignment = VerticalAlignment.Center; root.Items.Children.Add(fps);
        var details = FloatingTools.Menu(); details.Items.Add(new MenuItem { Header = L.Join(session.Format,L.Get(" · 区域录制")), IsEnabled = false }); details.Items.Add(new MenuItem { Header = $"{session.Region.Width} × {session.Region.Height} px", IsEnabled = false }); details.Items.Add(new MenuItem { Header = session.CursorIncluded ? L.Get("包含鼠标指针") : L.Get("不包含鼠标指针"), IsEnabled = false });
        var format = FloatingTools.MenuButton(ToolGlyph.Window, L.Get("查看录制参数"), details); var formatContent = new StackPanel { Orientation = Orientation.Horizontal }; formatContent.Children.Add(Ui.Text(session.Format, 11, FloatingTools.Ink)); formatContent.Children.Add(new ToolIcon(ToolGlyph.Chevron) { Width = 10, Height = 12, Margin = new Thickness(5, 0, 0, 0), Color = FloatingTools.MutedInk }); format.Content = formatContent; format.Width = 62; root.Items.Children.Add(format);
        root.Items.Children.Add(FloatingTools.Divider());
        stopButton = FloatingTools.Button(ToolGlyph.Stop, L.Get("停止录制"), RequestStop); stopButton.Foreground = FloatingTools.Red; root.Items.Children.Add(stopButton);
        pause = FloatingTools.Button(ToolGlyph.Pause, L.Get("暂停录制"), TogglePause, pauseHotkey); root.Items.Children.Add(pause);
        var sound = FloatingTools.Menu(); sound.Items.Add(new MenuItem { Header = L.Get("声音在开始录制时固定"), IsEnabled = false }); sound.Items.Add(new MenuItem { Header = session.SystemAudioEnabled ? L.Get("系统声音：已开启") : L.Get("系统声音：未开启"), IsEnabled = false }); sound.Items.Add(new MenuItem { Header = session.MicrophoneEnabled ? L.Get("麦克风：已开启") : L.Get("麦克风：未开启"), IsEnabled = false });
        root.Items.Children.Add(FloatingTools.MenuButton(session.SystemAudioEnabled || session.MicrophoneEnabled ? ToolGlyph.Sound : ToolGlyph.Muted, L.Get("查看声音状态"), sound));
        if (overlay != null)
        {
            var activeOverlay = overlay; var markingButtons = new List<(Button Button, AnnotationKind?[] Kinds)>();
            void UpdateMarking() { foreach (var pair in markingButtons) FloatingTools.SetSelected(pair.Button, pair.Kinds.Contains(activeOverlay.Canvas.Kind)); }
            void SelectMarking(AnnotationKind? kind) { activeOverlay.SetTool(kind); UpdateMarking(); }
            void AddMarkingGroup(ToolGlyph glyph, string name, params AnnotationKind[] kinds)
            {
                var menu = FloatingTools.Menu(); Button button = null!;
                foreach (var kind in kinds) { var tool = kind; var item = FloatingTools.Item(EditorWindow.ToolNames[(int)tool], () => SelectMarking(tool)); item.Tag = tool; menu.Items.Add(item); }
                menu.Opened += (_, _) => { for (var index = 0; index < kinds.Length; index++) ((MenuItem)menu.Items[index]).IsChecked = activeOverlay.Canvas.Kind == kinds[index]; };
                button = FloatingTools.SplitMenuButton(glyph, name, () => SelectMarking(kinds[0]), menu); root.Items.Children.Add(button); markingButtons.Add((button, kinds.Select(kind => (AnnotationKind?)kind).ToArray()));
            }
            root.Items.Children.Add(FloatingTools.Divider());
            AddMarkingGroup(ToolGlyph.Rectangle, L.Get("录制标注：形状"), AnnotationKind.Rectangle, AnnotationKind.Ellipse, AnnotationKind.Line);
            AddMarkingGroup(ToolGlyph.Brush, L.Get("录制标注：画笔"), AnnotationKind.Pen, AnnotationKind.Highlight);
            var markingArrow = FloatingTools.Button(ToolGlyph.Arrow, L.Get("录制标注：箭头"), () => SelectMarking(AnnotationKind.Arrow)); root.Items.Children.Add(markingArrow); markingButtons.Add((markingArrow, new AnnotationKind?[] { AnnotationKind.Arrow }));
            var markingText = FloatingTools.Button(ToolGlyph.Text, L.Get("录制标注：文字"), () => SelectMarking(AnnotationKind.Text)); root.Items.Children.Add(markingText); markingButtons.Add((markingText, new AnnotationKind?[] { AnnotationKind.Text }));
            var markingCover = FloatingTools.Button(ToolGlyph.Mosaic, L.Get("录制标注：遮挡"), () => SelectMarking(AnnotationKind.Cover)); root.Items.Children.Add(markingCover); markingButtons.Add((markingCover, new AnnotationKind?[] { AnnotationKind.Cover }));
            var exitMarking = FloatingTools.Button(ToolGlyph.Cursor, L.Get("结束标注 / 移动控制条"), () => SelectMarking(null)); root.Items.Children.Add(exitMarking); markingButtons.Add((exitMarking, new AnnotationKind?[] { null }));
            UpdateMarking();
        }
        root.Items.Children.Add(FloatingTools.Divider());
        if (restart != null)
        {
            restartButton = FloatingTools.Button(ToolGlyph.Restart, L.Get("重新录制"), () => restart()); restartButton.Foreground = FloatingTools.Red; root.Items.Children.Add(restartButton);
        }
        if (discard != null)
        {
            discardButton = FloatingTools.Button(ToolGlyph.Close, L.Get("放弃录制"), () => discard()); discardButton.Foreground = FloatingTools.Red; root.Items.Children.Add(discardButton);
        }
        dragButton = FloatingTools.Button(ToolGlyph.Grip, L.Get("录制中不可拖动"), () => { }); dragButton.Foreground = FloatingTools.MutedInk; dragButton.IsEnabled = false; root.Items.Children.Add(dragButton);
        if (settings != null) FloatingTools.Configure(root.Items, settings.ToolbarIconSize, settings.ScreenshotShowShortcuts);
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); toolbarSize = root.DesiredSize; Width = toolbarSize.Width; Height = toolbarSize.Height;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle; Native.SetWindowDisplayAffinity(handle, settings?.RecordingExcludeToolbar == false ? 0u : 0x11u);
            Position();
        };
        Loaded += (_, _) => { Position(); if (!Finishing) { regionWindow = new RecordingRegionWindow(session.Region); regionWindow.Show(); } };
        timer.Tick += (_, _) => Update(); timer.Start();
        Closing += (_, e) => { if (!Finishing) { e.Cancel = true; RequestStop(); } }; Closed += (_, _) => { timer.Stop(); regionWindow?.Close(); regionWindow = null; };
        Update();
    }
    private void Position()
    {
        var region = session.Region; var monitor = ScreenCapture.MonitorBounds(new PixelPoint(region.X + region.Width / 2, region.Y + region.Height / 2)); var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Ceiling(toolbarSize.Width * dpi.DpiScaleX); var height = (int)Math.Ceiling(toolbarSize.Height * dpi.DpiScaleY);
        var placed = FloatingLayout.Actions(new Rect(monitor.X, monitor.Y, monitor.Width, monitor.Height), new Rect(region.X, region.Y, region.Width, region.Height), new Size(width, height));
        Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.Topmost, (int)placed.X, (int)placed.Y, width, height, 0x0040);
    }
    private void RequestStop()
    {
        if (Finishing || stopRequested) return; stopRequested = true; pause.IsEnabled = stopButton.IsEnabled = false;
        if (restartButton != null) restartButton.IsEnabled = false; if (discardButton != null) discardButton.IsEnabled = false;
        stop();
    }
    internal void TogglePause() { if (!Finishing && !stopRequested) { session.TogglePause(); Update(); } }
    internal void Update()
    {
        if (Finishing) { label.Text = L.Format("正在完成录制 {0:0}%",session.ExportProgress); label.ToolTip = L.Get("正在完成录制，请稍候"); indicator.Fill = FloatingTools.Blue; return; }
        var elapsed = session.Elapsed; var paused = session.State == RecordingState.Paused;
        label.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}"; label.ToolTip = paused ? L.Get("已暂停，暂停时间不计入录制") : L.Get("录制时长"); indicator.Fill = paused ? Brushes.DarkOrange : FloatingTools.Red;
        FloatingTools.SetIcon(pause, paused ? ToolGlyph.Play : ToolGlyph.Pause, paused ? L.Get("继续录制") : L.Get("暂停录制"), pauseHotkey); FloatingTools.SetSelected(pause, paused);
        Title = paused ? L.Get("DeskSnip · 录制已暂停") : L.Get("DeskSnip · 录制中");
    }
    internal void ShowFinishing() { Finishing = true; pause.IsEnabled = stopButton.IsEnabled = false; if (restartButton != null) restartButton.IsEnabled = false; if (discardButton != null) discardButton.IsEnabled = false; regionWindow?.Close(); regionWindow = null; Update(); }
}

internal sealed class RecordingRegionWindow : ProductWindow
{
    internal RecordingRegionWindow(PixelRect region)
    {
        Title = L.Get("DeskSnip · 录制边框"); WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Left = region.X; Top = region.Y; Width = region.Width + 4; Height = region.Height + 4; IsHitTestVisible = false;
        var border = new Border { BorderBrush = FloatingTools.Blue, BorderThickness = new Thickness(2) }; Content = border;
        void PositionBorder()
        {
            var dpi = VisualTreeHelper.GetDpi(this); border.BorderThickness = new Thickness(2 / dpi.DpiScaleX, 2 / dpi.DpiScaleY, 2 / dpi.DpiScaleX, 2 / dpi.DpiScaleY);
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.Topmost, region.X - 2, region.Y - 2, region.Width + 4, region.Height + 4, 0x0050);
        }
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle; var style = Native.GetWindowLongPtr(handle, Native.GwlExStyle).ToInt64(); Native.SetWindowLongPtr(handle, Native.GwlExStyle, new IntPtr(style | Native.WsExTransparent | Native.WsExNoActivate | Native.WsExToolWindow)); Native.SetWindowDisplayAffinity(handle, 0x11);
            PositionBorder();
        };
        Loaded += (_, _) => PositionBorder();
    }
}
