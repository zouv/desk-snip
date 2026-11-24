using System;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskSnip.App.Recording;
using DeskSnip.App.Windows;
using DeskSnip.Core;
using Microsoft.Win32;

namespace DeskSnip.App;

internal sealed class PlaybackWindow : ProductWindow, IBackgroundWindow
{
    private readonly string path;
    private readonly string configured;
    private readonly string kind;
    private readonly RecordingJournal? journal;
    private readonly MediaElement player = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, ScrubbingEnabled = true };
    private readonly MediaElement systemPlayer = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Width = 0, Height = 0 };
    private readonly MediaElement microphonePlayer = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Width = 0, Height = 0 };
    private readonly TextBlock status = Ui.Text(L.Get("正在读取视频信息…"), 12, Ui.Subtle);
    private readonly ProgressBar transfer = new ProgressBar { Height = 4, Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed, Margin = new Thickness(4, 2, 4, 0), Foreground = Ui.Accent, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly Slider seek = new Slider { Minimum = 0, Maximum = 1, Margin = new Thickness(8) };
    private readonly TextBox start = Input("0");
    private readonly TextBox end = Input("0");
    private readonly TextBox[] crop = { Input("0"), Input("0"), Input("0"), Input("0") };
    private readonly ComboBox speed = new ComboBox { Width = 72, ItemsSource = new[] { "0.2X", "0.5X", "0.75X", "1.0X", "1.5X", "2.0X" }, SelectedItem = "1.0X", Margin = new Thickness(4), FontSize = 11, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ComboBox saveFormat = new ComboBox { Width = 72, ItemsSource = new[] { "MP4", "GIF", "WebP" }, Margin = new Thickness(4), FontSize = 11, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock time = new TextBlock { Text = "00:00", FontFamily = new FontFamily("Consolas"), Foreground = FloatingTools.Ink, VerticalAlignment = VerticalAlignment.Center, Width = 48, TextAlignment = TextAlignment.Center };
    private Button? playButton;
    private Button? topmostButton;
    private Button? copyButton;
    private Popup? advancedPopup;
    private readonly string saveDirectory;
    private readonly ComboBox encoder = new ComboBox { Width = 120, ItemsSource = new[] { L.Get("软件"), L.Get("自动硬件"), "NVIDIA", "Intel", "AMD" }, SelectedIndex = 0, Margin = new Thickness(4) };
    private readonly Slider systemVolume = new Slider { Width = 110, Minimum = 0, Maximum = 2, Value = 1, Margin = new Thickness(4) };
    private readonly Slider microphoneVolume = new Slider { Width = 110, Minimum = 0, Maximum = 2, Value = 1, Margin = new Thickness(4) };
    private readonly Slider quality = new Slider { Width = 120, Minimum = 1, Maximum = 100, Value = 80, Margin = new Thickness(4) };
    private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
    private CancellationTokenSource? cancellation;
    private MediaInfo? info;
    private bool updatingSeek;
    private bool seekDragging;
    private bool resumeAfterSeek;
    private bool playing;
    private bool closeRequested;
    private bool closed;
    private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
    private Task? loading;
    private Task? closeTask;
    private Task? export;
    private Task? copyTask;
    private string? previewFile;
    private GifBitmapDecoder? gif;
    private Image? gifImage;
    private readonly Image firstFrame = new Image { Stretch = Stretch.Uniform, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private bool firstFrameDismissed;
    private Border? inlineVideo;
    private Border? inlineToolbar;
    private double[] gifTimes = Array.Empty<double>();
    private double gifPosition;
    private readonly bool closeAfterExport;
    private readonly Action<string>? outputSaved;
    private readonly PixelRect inlineRegion;
    private readonly bool pending;
    private readonly string defaultSaveFormat;
    private readonly Action? discardPending;
    private readonly bool sourceRequiresExport;
    internal PlaybackWindow(string path, string kind, string configured = "", RecordingJournal? journal = null, bool closeAfterExport = false, Action<string>? outputSaved = null, PixelRect inlineRegion = default, string? outputDirectory = null,
        bool pending = false, string? defaultSaveFormat = null, Action? discardPending = null, bool sourceRequiresExport = false, AppSettings? settings = null)
    {
        this.closeAfterExport = closeAfterExport; this.outputSaved = outputSaved; this.inlineRegion = inlineRegion;
        this.path = path; this.configured = configured; this.journal = journal; saveDirectory = outputDirectory ?? ""; this.kind = kind;
        this.pending = pending; this.defaultSaveFormat = defaultSaveFormat ?? kind; this.discardPending = discardPending; this.sourceRequiresExport = sourceRequiresExport;
        Title = L.Get("DeskSnip · 回放与剪辑"); Width = 1020; Height = 780; MinWidth = 820; MinHeight = 560; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (!inlineRegion.IsEmpty)
        {
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = false; ShowInTaskbar = true; WindowStartupLocation = WindowStartupLocation.Manual;
            AllowsTransparency = true; Background = Brushes.Transparent; MinWidth = 0; MinHeight = 0; Width = Math.Max(1, inlineRegion.Width + 4); Height = Math.Max(1, inlineRegion.Height + 160);
        }
        if (!inlineRegion.IsEmpty) saveFormat.SelectedItem = this.defaultSaveFormat;
        var content = new Grid(); content.Children.Add(player); content.Children.Add(systemPlayer); content.Children.Add(microphonePlayer); content.Children.Add(firstFrame);
        if (inlineRegion.IsEmpty)
        {
            var root = new DockPanel { Margin = new Thickness(16) };
            Content = root;
            BuildFullUi(root, content);
        }
        else
        {
            var outer = new StackPanel { Orientation = Orientation.Vertical, Background = Brushes.Transparent };
            Content = outer;
            BuildInlineUi(outer, content, settings);
        }
        if (kind == "GIF")
        {
            using (var stream = File.OpenRead(path)) gif = new GifBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            gifTimes = new double[gif.Frames.Count + 1];
            for (var i = 0; i < gif.Frames.Count; i++)
            {
                var metadata = gif.Frames[i].Metadata as BitmapMetadata; object? delay = null;
                try { delay = metadata?.GetQuery("/grctlext/Delay"); } catch (NotSupportedException) { }
                gifTimes[i + 1] = gifTimes[i] + Math.Max(.01, delay is ushort value ? value / 100d : .1);
            }
            gifImage = new Image { Source = gif.Frames[0] }; content.Children.Add(gifImage); player.Visibility = Visibility.Collapsed;
        }
        if (!inlineRegion.IsEmpty && kind != "GIF") _ = LoadFirstFrameAsync();
        player.MediaOpened += (_, _) =>
        {
            if (info == null && player.NaturalDuration.HasTimeSpan) SetInfo(new MediaInfo { Width = player.NaturalVideoWidth, Height = player.NaturalVideoHeight, Duration = player.NaturalDuration.TimeSpan.TotalSeconds });
            if (!inlineRegion.IsEmpty && !firstFrameDismissed && !playing)
            {
                firstFrameDismissed = true; firstFrame.Visibility = Visibility.Collapsed; player.Visibility = Visibility.Visible;
                var muted = player.IsMuted; player.Position = TimeSpan.Zero;
                try { player.IsMuted = true; player.Play(); player.Pause(); } finally { player.IsMuted = muted; }
            }
        };
        player.MediaEnded += (_, _) => { playing = false; UpdatePlayButton(); };
        player.MediaFailed += (_, _) => status.Text = L.Get("系统播放器无法解码，仍可尝试通过 FFmpeg 导出。");
        seek.ValueChanged += (_, _) => { if (!updatingSeek) Position(seek.Value); };
        speed.SelectionChanged += (_, _) => { var rate = SpeedValue; player.SpeedRatio = systemPlayer.SpeedRatio = microphonePlayer.SpeedRatio = rate; };
        systemVolume.ValueChanged += (_, _) => { systemPlayer.Volume = Math.Min(1, systemVolume.Value); if (systemPlayer.Source == null) player.Volume = Math.Min(1, systemVolume.Value); };
        microphoneVolume.ValueChanged += (_, _) => microphonePlayer.Volume = Math.Min(1, microphoneVolume.Value);
        Loaded += (_, _) => { if (!closeRequested) loading = Load(kind); };
        if (!inlineRegion.IsEmpty)
        {
            SourceInitialized += (_, _) => PositionOverlay();
            Loaded += (_, _) => PositionOverlay();
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { _ = CloseSafelyAsync(); e.Handled = true; } };
        }
        timer.Tick += (_, _) =>
        {
            if (gif != null && playing) { gifPosition += .1 * SpeedValue; if (gifPosition >= gifTimes.Last()) gifPosition = 0; SetGifFrame(); }
            if (!seekDragging) { updatingSeek = true; seek.Value = CurrentPosition(); updatingSeek = false; }
            if (!inlineRegion.IsEmpty) { time.Text = Clock(CurrentPosition()); status.ToolTip = status.Text; }
        }; timer.Start();
        Closing += (_, e) => { if (closed) return; e.Cancel = true; _ = CloseSafelyAsync(); };
        Closed += (_, _) =>
        {
            timer.Stop(); player.Close(); systemPlayer.Close(); microphonePlayer.Close(); lifetime.Dispose();
            if (advancedPopup != null) advancedPopup.IsOpen = false;
            if (previewFile != null && File.Exists(previewFile)) { try { File.Delete(previewFile); } catch (IOException) { } }
            if (pending) { try { discardPending?.Invoke(); } catch (Exception) { } }
        };
    }
    private async Task LoadFirstFrameAsync()
    {
        try
        {
            var bitmap = await Task.Run(() =>
            {
                using var reader = new VideoReader(path);
                var frame = reader.ReadFrame();
                if (frame == null) return null;
                var image = BitmapSource.Create(reader.Width, reader.Height, 96, 96, PixelFormats.Bgr32, null, frame.Pixels, reader.Width * 4);
                image.Freeze();
                return image;
            });
            if (bitmap == null || closed || firstFrameDismissed) return;
            await Dispatcher.InvokeAsync(() => { if (!closed && !firstFrameDismissed) { firstFrame.Source = bitmap; firstFrame.Visibility = Visibility.Visible; } });
        }
        catch { }
    }
    private void BuildFullUi(DockPanel root, Grid content)
    {
        var top = new WrapPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(Ui.Button(L.Get("播放 / 暂停"), Toggle)); top.Children.Add(Ui.Button(L.Get("从头播放"), () => { Position(0); Play(); })); top.Children.Add(speed);
        top.Children.Add(Ui.Button(L.Get("导出 MP4"), () => { _ = Export("MP4"); }, true)); top.Children.Add(Ui.Button(L.Get("导出 GIF"), () => { _ = Export("GIF"); })); top.Children.Add(Ui.Button(L.Get("导出 WebP"), () => { _ = Export("WebP"); }));
        copyButton = Ui.Button(L.Get("复制录制文件"), () => { _ = CopyFile(); }); top.Children.Add(copyButton); top.Children.Add(Ui.Button(L.Get("取消导出"), () => cancellation?.Cancel()));
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom); bottom.Children.Add(seek);
        var trims = new WrapPanel(); bottom.Children.Add(trims); trims.Children.Add(Ui.Text(L.Get("开始 / 结束秒"))); trims.Children.Add(start); trims.Children.Add(end);
        trims.Children.Add(Ui.Button(L.Get("以当前位置为开始"), () => start.Text = CurrentPosition().ToString("0.###", CultureInfo.InvariantCulture)));
        trims.Children.Add(Ui.Button(L.Get("以当前位置为结束"), () => end.Text = CurrentPosition().ToString("0.###", CultureInfo.InvariantCulture)));
        var options = new WrapPanel(); bottom.Children.Add(options); options.Children.Add(Ui.Text(L.Get("裁剪 X / Y / 宽 / 高"))); foreach (var field in crop) options.Children.Add(field);
        options.Children.Add(Ui.Text(L.Get("画质"))); options.Children.Add(quality); options.Children.Add(encoder);
        var audio = new WrapPanel(); bottom.Children.Add(audio); audio.Children.Add(Ui.Text(L.Get("系统声"))); audio.Children.Add(systemVolume); audio.Children.Add(Ui.Text(L.Get("麦克风"))); audio.Children.Add(microphoneVolume); audio.Children.Add(Ui.Text(L.Get("0 为静音；GIF/WebP 没有声音。"), 12, Ui.Subtle));
        bottom.Children.Add(status);
        bottom.Children.Add(transfer);
        root.Children.Add(content);
    }
    private void BuildInlineUi(Panel root, Grid content, AppSettings? settings)
    {
        content.Background = Brushes.Black;
        inlineVideo = new Border
        {
            BorderBrush = FloatingTools.Blue, BorderThickness = new Thickness(2), Background = Brushes.Black, Child = content,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Width = Math.Max(1, inlineRegion.Width + 4), Height = Math.Max(1, inlineRegion.Height + 4)
        };
        root.Children.Add(inlineVideo);
        seek.Height = 18; seek.Margin = new Thickness(4, 0, 4, 2); seek.IsMoveToPointEnabled = true; seek.VerticalAlignment = VerticalAlignment.Bottom;
        Panel.SetZIndex(seek, 10); content.Children.Add(seek);
        seek.PreviewMouseLeftButtonDown += (_, e) => BeginSeek(e);
        seek.PreviewMouseMove += (_, e) => { if (seekDragging && e.LeftButton == MouseButtonState.Pressed) { UpdateSeek(e.GetPosition(seek).X); e.Handled = true; } };
        seek.PreviewMouseLeftButtonUp += (_, e) => { EndSeek(); e.Handled = true; };
        seek.LostMouseCapture += (_, _) => EndSeek();
        var bar = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var grip = new Border { Width = 20, Height = 28, Cursor = Cursors.SizeAll, Background = Brushes.Transparent, ToolTip = L.Get("拖动回放窗口"), Child = new ToolIcon(ToolGlyph.Grip) { Width = 12, Height = 18, Color = FloatingTools.MutedInk } };
        grip.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); e.Handled = true; };
        playButton = FloatingTools.Button(ToolGlyph.Play, L.Get("播放"), Toggle); bar.Children.Add(playButton);
        bar.Children.Add(time);
        var speedMenu = FloatingTools.Menu();
        var speedButton = FloatingTools.TextMenuButton(speed.SelectedItem as string ?? "1.0X", L.Get("播放倍速"), speedMenu, out var speedText, 68);
        foreach (var value in new[] { "0.2X", "0.5X", "0.75X", "1.0X", "1.5X", "2.0X" })
        {
            var rate = value; var item = FloatingTools.Item(rate, () => { speed.SelectedItem = rate; speedText.Text = rate; }); item.IsCheckable = true; item.Tag = rate; speedMenu.Items.Add(item);
        }
        speedMenu.Opened += (_, _) => { foreach (var item in speedMenu.Items.OfType<MenuItem>()) item.IsChecked = Equals(item.Tag, speed.SelectedItem); };
        bar.Children.Add(speedButton);
        bar.Children.Add(FloatingTools.Divider());
        var advanced = FloatingTools.Button(ToolGlyph.Crop, L.Get("剪辑与导出选项"), () => advancedPopup!.IsOpen = !advancedPopup.IsOpen); bar.Children.Add(advanced);
        var formatMenu = FloatingTools.Menu();
        var formatButton = FloatingTools.TextMenuButton(saveFormat.SelectedItem as string ?? "MP4", L.Get("保存格式"), formatMenu, out var formatText, 68);
        foreach (var value in new[] { "MP4", "GIF", "WebP" })
        {
            var format = value; var item = FloatingTools.Item(format, () => { saveFormat.SelectedItem = format; formatText.Text = format; }); item.IsCheckable = true; item.Tag = format; formatMenu.Items.Add(item);
        }
        formatMenu.Opened += (_, _) => { foreach (var item in formatMenu.Items.OfType<MenuItem>()) item.IsChecked = Equals(item.Tag, saveFormat.SelectedItem); };
        bar.Children.Add(formatButton);
        bar.Children.Add(FloatingTools.Divider());
        topmostButton = FloatingTools.Button(ToolGlyph.Pin, Topmost ? L.Get("取消置顶") : L.Get("窗口置顶"), ToggleTopmost); FloatingTools.SetSelected(topmostButton, Topmost); bar.Children.Add(topmostButton);
        bar.Children.Add(grip);
        bar.Children.Add(FloatingTools.Button(ToolGlyph.Close, L.Get("关闭"), () => { _ = CloseSafelyAsync(); }));
        bar.Children.Add(FloatingTools.Button(ToolGlyph.Save, L.Get("保存录屏"), () => { _ = SaveRecording(); }, "", true));
        copyButton = FloatingTools.Button(ToolGlyph.Copy, L.Get("复制录制文件"), () => { _ = CopyFile(); }); bar.Children.Add(copyButton);
        bar.Children.Add(FloatingTools.Button(ToolGlyph.Restart, L.Get("从头播放"), () => { Position(0); Play(); }));
        inlineToolbar = new Border
        {
            Background = Brushes.White, BorderBrush = FloatingTools.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7),
            Padding = new Thickness(4), Margin = new Thickness(2, 6, 2, 0),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 2, Opacity = .14 },
            Child = bar, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
        };
        inlineToolbar.PreviewMouseLeftButtonDown += (_, e) => { if (e.ChangedButton == MouseButton.Left && !IsInteractive(e.OriginalSource as DependencyObject)) { DragMove(); e.Handled = true; } };
        root.Children.Add(inlineToolbar);
        status.FontSize = 11; status.Margin = new Thickness(4, 2, 4, 0); status.TextWrapping = TextWrapping.NoWrap; status.TextTrimming = TextTrimming.CharacterEllipsis; status.Foreground = FloatingTools.MutedInk; status.HorizontalAlignment = HorizontalAlignment.Left; root.Children.Add(status);
        root.Children.Add(transfer);
        FloatingTools.Configure(bar, settings?.ToolbarIconSize ?? "Medium", settings?.ScreenshotShowShortcuts ?? true);
        advancedPopup = BuildAdvancedPopup(advanced);
        PreviewMouseDown += (_, _) => { if (advancedPopup?.IsOpen == true && !advanced.IsMouseOver) advancedPopup.IsOpen = false; };
        Deactivated += (_, _) => { if (advancedPopup != null) advancedPopup.IsOpen = false; };
    }
    private Popup BuildAdvancedPopup(FrameworkElement target)
    {
        var panel = new StackPanel { Width = 540 };
        TextBlock Label(string text) => new TextBlock { Text = text, Foreground = FloatingTools.Ink, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        var trims = new WrapPanel(); panel.Children.Add(trims); trims.Children.Add(Label(L.Get("开始 / 结束秒"))); trims.Children.Add(start); trims.Children.Add(end);
        trims.Children.Add(Ui.Button(L.Get("以当前位置为开始"), () => start.Text = CurrentPosition().ToString("0.###", CultureInfo.InvariantCulture)));
        trims.Children.Add(Ui.Button(L.Get("以当前位置为结束"), () => end.Text = CurrentPosition().ToString("0.###", CultureInfo.InvariantCulture)));
        var options = new WrapPanel(); panel.Children.Add(options); options.Children.Add(Label(L.Get("裁剪 X / Y / 宽 / 高"))); foreach (var field in crop) options.Children.Add(field);
        options.Children.Add(Label(L.Get("画质"))); options.Children.Add(quality); options.Children.Add(encoder);
        var audio = new WrapPanel(); panel.Children.Add(audio); audio.Children.Add(Label(L.Get("系统声"))); audio.Children.Add(systemVolume); audio.Children.Add(Label(L.Get("麦克风"))); audio.Children.Add(microphoneVolume); audio.Children.Add(Label(L.Get("0 为静音；GIF/WebP 没有声音。")));
        panel.Children.Add(new TextBlock { Text = L.Get("开始与结束秒、裁剪、画质和音量用于重新编码；无改动时保存会直接复制原文件。"), Foreground = FloatingTools.MutedInk, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        var border = new Border
        {
            Background = Brushes.White, BorderBrush = FloatingTools.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 2, Opacity = .18 },
            Child = new ScrollViewer { Content = panel, MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
        return new Popup { PlacementTarget = target, Placement = PlacementMode.Top, StaysOpen = true, AllowsTransparency = true, Child = border };
    }
    private void PositionOverlay()
    {
        if (inlineRegion.IsEmpty) return;
        var handle = new WindowInteropHelper(this).Handle; if (handle == IntPtr.Zero) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        if (inlineVideo != null)
        {
            inlineVideo.Width = Math.Max(1, (inlineRegion.Width + 4) / dpi.DpiScaleX);
            inlineVideo.Height = Math.Max(1, (inlineRegion.Height + 4) / dpi.DpiScaleY);
            inlineVideo.BorderThickness = new Thickness(2 / dpi.DpiScaleX, 2 / dpi.DpiScaleY, 2 / dpi.DpiScaleX, 2 / dpi.DpiScaleY);
        }
        if (inlineToolbar != null) inlineToolbar.Margin = new Thickness(2 / dpi.DpiScaleX, 6 / dpi.DpiScaleY, 2 / dpi.DpiScaleX, 0);
        var outer = Content as FrameworkElement; if (outer == null) return;
        outer.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = Math.Max(inlineVideo?.Width ?? 1, outer.DesiredSize.Width);
        var height = outer.DesiredSize.Height;
        Width = width; Height = height;
        Native.SetWindowPos(handle, Topmost ? Native.Topmost : IntPtr.Zero, inlineRegion.X - 2, inlineRegion.Y - 2, (int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY), 0x0040);
    }
    private static TextBox Input(string value) => new TextBox { Text = value, Width = 70, Margin = new Thickness(4), Padding = new Thickness(4), VerticalContentAlignment = VerticalAlignment.Center };
    private async Task Load(string kind)
    {
        try { var media = await Task.Run(() => Ffmpeg.Probe(path, configured, lifetime.Token)); if (closeRequested) return; SetInfo(media); }
        catch (OperationCanceledException) { return; }
        catch (Exception) { if (gif != null) SetInfo(new MediaInfo { Width = gif.Frames[0].PixelWidth, Height = gif.Frames[0].PixelHeight, Duration = gifTimes.Last() }); status.Text = L.Get("剪辑导出需要 FFmpeg；基础回放可继续使用。"); }
        if (closeRequested) return;
        if (kind == "WebP")
        {
            if (info == null) return;
            previewFile = Path.Combine(Path.GetTempPath(), "DeskSnip-preview-" + Guid.NewGuid().ToString("N") + ".mp4");
            var options = FullOptions();
            try { await Task.Run(() => Ffmpeg.Export(path, previewFile, options, configured, null, null, lifetime.Token)); if (closeRequested) return; player.Source = new Uri(previewFile); }
            catch (OperationCanceledException) { return; }
            catch (Exception) { status.Text = L.Get("WebP 预览未完成，原文件保留。"); return; }
        }
        else if (gif == null) player.Source = new Uri(path);
        if (journal != null)
        {
            var draft = journal.Snapshot;
            if (!string.IsNullOrEmpty(draft.SystemAudio) && File.Exists(journal.FilePath(draft.SystemAudio))) systemPlayer.Source = new Uri(journal.FilePath(draft.SystemAudio));
            if (!string.IsNullOrEmpty(draft.MicrophoneAudio) && File.Exists(journal.FilePath(draft.MicrophoneAudio))) microphonePlayer.Source = new Uri(journal.FilePath(draft.MicrophoneAudio));
            if (systemPlayer.Source != null || microphonePlayer.Source != null) player.IsMuted = true;
        }
        if (inlineRegion.IsEmpty) Play(); else UpdatePlayButton();
    }
    private void SetInfo(MediaInfo media) { info = media; seek.Maximum = media.Duration; end.Text = media.Duration.ToString("0.###", CultureInfo.InvariantCulture); crop[2].Text = media.Width.ToString(); crop[3].Text = media.Height.ToString(); status.Text = L.Format("{0} × {1} · {2:0.0} 秒；设置导出范围、倍速、裁剪及音量。",media.Width,media.Height,media.Duration); if (!inlineRegion.IsEmpty) PositionOverlay(); }
    private double CurrentPosition() => gif != null ? gifPosition : player.Position.TotalSeconds;
    private double SpeedValue => speed.SelectedItem is string value && double.TryParse(value.TrimEnd('X'), NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) ? rate : 1;
    private static string Clock(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : span.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }
    private void UpdatePlayButton()
    {
        if (playButton == null) return;
        FloatingTools.SetIcon(playButton, playing ? ToolGlyph.Pause : ToolGlyph.Play, playing ? L.Get("暂停") : L.Get("播放"));
    }
    private void ToggleTopmost()
    {
        Topmost = !Topmost;
        if (topmostButton != null) { FloatingTools.SetSelected(topmostButton, Topmost); FloatingTools.SetName(topmostButton, Topmost ? L.Get("取消置顶") : L.Get("窗口置顶")); }
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) Native.SetWindowPos(handle, Topmost ? Native.Topmost : IntPtr.Zero, 0, 0, 0, 0, 0x0013);
    }
    private static bool IsInteractive(DependencyObject? source)
    {
        for (var node = source; node != null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is Button || node is ComboBox || node is TextBox || node is Slider || node is ScrollBar) return true;
        return false;
    }
    private void Position(double seconds) { firstFrameDismissed = true; firstFrame.Visibility = Visibility.Collapsed; player.Visibility = Visibility.Visible; if (gif != null) { gifPosition = seconds; SetGifFrame(); } else player.Position = TimeSpan.FromSeconds(seconds); systemPlayer.Position = microphonePlayer.Position = TimeSpan.FromSeconds(seconds); }
    private void SetGifFrame() { if (gif == null || gifImage == null) return; var index = Array.FindLastIndex(gifTimes, t => t <= gifPosition); gifImage.Source = gif.Frames[Math.Max(0, Math.Min(gif.Frames.Count - 1, index))]; }
    private void BeginSeek(MouseButtonEventArgs e)
    {
        if (seekDragging) return;
        seekDragging = true;
        resumeAfterSeek = playing;
        if (playing) Toggle();
        seek.CaptureMouse();
        UpdateSeek(e.GetPosition(seek).X);
        e.Handled = true;
    }
    private void UpdateSeek(double x)
    {
        if (seek.ActualWidth <= 0) return;
        seek.Value = seek.Minimum + (seek.Maximum - seek.Minimum) * Math.Max(0, Math.Min(1, x / seek.ActualWidth));
    }
    private void EndSeek()
    {
        if (!seekDragging) return;
        seekDragging = false;
        if (seek.IsMouseCaptured) seek.ReleaseMouseCapture();
        if (resumeAfterSeek) { resumeAfterSeek = false; Play(); }
    }
    private void Play() { firstFrameDismissed = true; firstFrame.Visibility = Visibility.Collapsed; player.Visibility = Visibility.Visible; playing = true; player.Play(); if (systemPlayer.Source != null) systemPlayer.Play(); if (microphonePlayer.Source != null) microphonePlayer.Play(); UpdatePlayButton(); }
    private void Toggle() { if (!playing) Play(); else { playing = false; player.Pause(); systemPlayer.Pause(); microphonePlayer.Pause(); UpdatePlayButton(); } }
    private VideoEditOptions FullOptions() => new VideoEditOptions { EndSeconds = info!.Duration, Crop = new PixelRect(0, 0, info.Width, info.Height), Quality = 80, FramesPerSecond = 24 };
    private async Task Export(string format)
    {
        if (export != null || copyTask != null || info == null || closeRequested) return;
        var dialog = new SaveFileDialog { Filter = format + "|*." + format.ToLowerInvariant(), FileName = "DeskSnip-edit." + format.ToLowerInvariant(), AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        await ExportTo(format, dialog.FileName);
    }
    private async Task<bool> ExportTo(string format, string destination, bool notify = true, string? progressFormat = null)
    {
        if (export != null || info == null || closeRequested) return false;
        var copyMode = copyTask != null;
        try
        {
            double Parse(TextBox input) => double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : throw new ArgumentException(L.Get("时间与裁剪请输入有效数字。"));
            var rectangle = crop.Select(field => { var value = Parse(field); if (value != Math.Truncate(value) || value < 0 || value > 100000) throw new ArgumentException(L.Get("裁剪请输入非负整数像素。")); return (int)value; }).ToArray();
            var options = new VideoEditOptions { StartSeconds = Parse(start), EndSeconds = Parse(end), Speed = SpeedValue, Crop = new PixelRect(rectangle[0], rectangle[1], rectangle[2], rectangle[3]),
                Quality = (int)quality.Value, Encoder = (VideoEncoder)encoder.SelectedIndex, FramesPerSecond = format == "MP4" ? 24 : 10, SystemVolume = systemVolume.Value, MicrophoneVolume = microphoneVolume.Value };
            options.Validate(info.Duration, info.Width, info.Height);
            var source = journal != null && File.Exists(journal.FilePath("source.mp4")) ? journal.FilePath("source.mp4") : path;
            var system = journal?.Snapshot.SystemAudio; var mic = journal?.Snapshot.MicrophoneAudio;
            if (!copyMode) BeginTransfer(L.Get("正在准备导出…"), indeterminate: true);
            cancellation = new CancellationTokenSource(); var token = cancellation.Token;
            var lastPercent = -1;
            var progress = new Progress<double>(percent => { if (!closed) ReportTransfer(percent, progressFormat ?? "正在导出 {0:0}%…"); });
            export = Task.Run(() => Ffmpeg.Export(source, destination, options, configured, string.IsNullOrEmpty(system) ? null : journal!.FilePath(system!), string.IsNullOrEmpty(mic) ? null : journal!.FilePath(mic!), token, value =>
            {
                var whole = (int)Math.Max(0, Math.Min(100, value));
                if (whole == lastPercent) return;
                lastPercent = whole;
                ((IProgress<double>)progress).Report(whole);
            }));
            await export;
            if (notify) { outputSaved?.Invoke(destination); }
            if (!closeRequested) { if (!copyMode) status.Text = L.Get("导出完成。"); if (notify && closeAfterExport) _ = Dispatcher.BeginInvoke(new Action(() => { _ = CloseSafelyAsync(); })); }
            return true;
        }
        catch (OperationCanceledException) { status.Text = L.Get(copyMode ? "已取消复制。" : "已取消导出，原文件和素材保留。"); return false; }
        catch (InvalidOperationException error) { status.Text = error.Message; return false; }
        catch (Exception error) { if (!closeRequested) Ui.Error(this, error, L.Get("剪辑导出")); return false; }
        finally { export = null; cancellation?.Dispose(); cancellation = null; if (!copyMode) EndTransfer(); }
    }
    private async Task SaveRecording()
    {
        if (export != null || copyTask != null || closeRequested) return;
        var format = saveFormat.SelectedItem as string ?? defaultSaveFormat;
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = L.Get("选择保存录屏的文件夹"), ShowNewFolderButton = true, SelectedPath = InitialSaveDirectory() };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var destination = UniqueRecordingPath(dialog.SelectedPath, format);
        if (format == kind && !sourceRequiresExport && SpeedValue == 1 && NoEdits())
        {
            try
            {
                File.Copy(path, destination, false);
                outputSaved?.Invoke(destination);
                status.Text = L.Get("录制已保存。");
                if (closeAfterExport) _ = CloseSafelyAsync();
            }
            catch (Exception error) { Ui.Error(this, error, L.Get("保存录屏")); }
            return;
        }
        await ExportTo(format, destination);
    }
    private bool NoEdits()
    {
        if (info == null) return false;
        double Parse(TextBox input, double fallback) => double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        var rectangle = crop.Select(field => Parse(field, -1)).ToArray();
        return Parse(start, -1) == 0 && Math.Abs(Parse(end, -1) - info.Duration) < .01 &&
            rectangle[0] == 0 && rectangle[1] == 0 && rectangle[2] == info.Width && rectangle[3] == info.Height &&
            systemVolume.Value == 1 && microphoneVolume.Value == 1 && quality.Value == 80 && encoder.SelectedIndex == 0;
    }
    private string InitialSaveDirectory()
    {
        if (!string.IsNullOrEmpty(saveDirectory) && Directory.Exists(saveDirectory)) return saveDirectory;
        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        return Directory.Exists(videos) ? videos : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }
    private static string UniqueRecordingPath(string directory, string format)
    {
        var extension = "." + format.ToLowerInvariant(); var baseName = "DeskSnip-recording-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var candidate = Path.Combine(directory, baseName + extension);
        for (var index = 2; File.Exists(candidate); index++) candidate = Path.Combine(directory, baseName + " (" + index + ")" + extension);
        return candidate;
    }
    public Task CloseSafelyAsync()
    {
        if (closeTask != null) return closeTask;
        closeRequested = true; lifetime.Cancel(); cancellation?.Cancel(); status.Text = L.Get("正在停止预览和导出，原文件保留…"); closeTask = CloseCore(); return closeTask;
    }
    private async Task CloseCore()
    {
        await Task.Yield();
        if (loading != null) { try { await loading; } catch (Exception) { } }
        if (copyTask != null) { try { await copyTask; } catch (Exception) { } }
        if (export != null) { try { await export; } catch (Exception) { } }
        closed = true; Close();
    }
    private void BeginTransfer(string message, bool indeterminate = true, double percent = 0)
    {
        if (closed || closeRequested) return;
        transfer.Visibility = Visibility.Visible;
        transfer.IsIndeterminate = indeterminate;
        transfer.Value = indeterminate ? 0 : Math.Max(0, Math.Min(100, percent));
        status.Text = message;
        if (copyButton != null) copyButton.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        if (!inlineRegion.IsEmpty) PositionOverlay();
    }
    private void ReportTransfer(double percent, string format)
    {
        if (closed || closeRequested) return;
        var whole = (int)Math.Max(0, Math.Min(100, percent));
        if (transfer.Visibility == Visibility.Visible && !transfer.IsIndeterminate && (int)transfer.Value == whole) return;
        transfer.Visibility = Visibility.Visible;
        transfer.IsIndeterminate = false;
        transfer.Value = whole;
        status.Text = L.Format(format, whole);
    }
    private void EndTransfer()
    {
        Mouse.OverrideCursor = null;
        if (closed) return;
        transfer.Visibility = Visibility.Collapsed;
        transfer.IsIndeterminate = false;
        transfer.Value = 0;
        if (copyButton != null) copyButton.IsEnabled = true;
        if (!inlineRegion.IsEmpty) PositionOverlay();
    }
    private async Task CopyFile()
    {
        if (copyTask != null || export != null || closeRequested) return;
        copyTask = CopyFileCore();
        try { await copyTask; }
        catch (OperationCanceledException) { if (!closed) status.Text = L.Get("已取消复制。"); }
        catch (TimeoutException) { if (!closed) status.Text = L.Get("剪贴板暂时不可用，请稍后重试。"); }
        catch (System.Runtime.InteropServices.ExternalException) { if (!closed) status.Text = L.Get("剪贴板暂时不可用，请稍后重试。"); }
        catch (Exception error) { if (!closed) Ui.Error(this, error, L.Get("复制录制文件")); }
        finally { copyTask = null; EndTransfer(); }
    }
    private async Task CopyFileCore()
    {
        if (!pending)
        {
            await SetClipboardFileAsync(path);
            return;
        }
        var format = saveFormat.SelectedItem as string ?? defaultSaveFormat;
        var temp = Path.Combine(Path.GetTempPath(), "DeskSnip-recording-" + Guid.NewGuid().ToString("N") + "." + format.ToLowerInvariant());
        var prepared = false;
        try
        {
            BeginTransfer(L.Get("正在准备复制…"), indeterminate: true);
            if (format == kind && !sourceRequiresExport && SpeedValue == 1 && NoEdits())
            {
                ReportTransfer(0, "正在复制 {0:0}%…");
                var progress = new Progress<double>(percent => ReportTransfer(percent, "正在复制 {0:0}%…"));
                await CopyFileWithProgressAsync(path, temp, lifetime.Token, progress);
            }
            else if (!await ExportTo(format, temp, notify: false, progressFormat: "正在复制 {0:0}%…")) return;
            if (!File.Exists(temp) || closeRequested) return;
            prepared = true;
            await SetClipboardFileAsync(temp);
        }
        finally
        {
            if (!prepared) { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } }
        }
    }
    private async Task SetClipboardFileAsync(string file)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = completion.Task.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        var thread = new Thread(() =>
        {
            try { ClipboardRetry.SetFileDropList(new StringCollection { file }); completion.TrySetResult(true); }
            catch (Exception error) { completion.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (await Task.WhenAny(completion.Task, Task.Delay(5000)) != completion.Task) throw new TimeoutException();
        await completion.Task;
        void Apply() => status.Text = L.Get("录制文件已复制，可粘贴到文件夹或支持文件的应用。");
        if (Dispatcher.CheckAccess()) Apply();
        else await Dispatcher.InvokeAsync(Apply);
    }
    private static async Task CopyFileWithProgressAsync(string source, string destination, CancellationToken token, IProgress<double> progress)
    {
        const int bufferSize = 1024 * 1024;
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous);
        var total = input.Length;
        var buffer = new byte[bufferSize];
        long copied = 0;
        var lastPercent = -1;
        int read;
        while ((read = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
            copied += read;
            if (total <= 0) continue;
            var percent = Math.Min(100d, copied * 100d / total);
            var whole = (int)percent;
            if (whole == lastPercent) continue;
            lastPercent = whole;
            progress.Report(percent);
        }
        await output.FlushAsync(token).ConfigureAwait(false);
        if (lastPercent < 100) progress.Report(100);
    }
}
