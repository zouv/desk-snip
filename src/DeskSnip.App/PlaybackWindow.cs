using System;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
    private readonly RecordingJournal? journal;
    private readonly MediaElement player = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close };
    private readonly MediaElement systemPlayer = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Width = 0, Height = 0 };
    private readonly MediaElement microphonePlayer = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Width = 0, Height = 0 };
    private readonly TextBlock status = Ui.Text(L.Get("正在读取视频信息…"), 12, Ui.Subtle);
    private readonly Slider seek = new Slider { Minimum = 0, Maximum = 1, Margin = new Thickness(8) };
    private readonly TextBox start = Input("0");
    private readonly TextBox end = Input("0");
    private readonly TextBox[] crop = { Input("0"), Input("0"), Input("0"), Input("0") };
    private readonly ComboBox speed = new ComboBox { Width = 80, ItemsSource = new[] { .2, .5, .75, 1d, 1.5, 2d }, SelectedItem = 1d, Margin = new Thickness(4) };
    private readonly ComboBox encoder = new ComboBox { Width = 120, ItemsSource = new[] { L.Get("软件"), L.Get("自动硬件"), "NVIDIA", "Intel", "AMD" }, SelectedIndex = 0, Margin = new Thickness(4) };
    private readonly Slider systemVolume = new Slider { Width = 110, Minimum = 0, Maximum = 2, Value = 1, Margin = new Thickness(4) };
    private readonly Slider microphoneVolume = new Slider { Width = 110, Minimum = 0, Maximum = 2, Value = 1, Margin = new Thickness(4) };
    private readonly Slider quality = new Slider { Width = 120, Minimum = 1, Maximum = 100, Value = 80, Margin = new Thickness(4) };
    private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
    private CancellationTokenSource? cancellation;
    private MediaInfo? info;
    private bool updatingSeek;
    private bool playing;
    private bool closeRequested;
    private bool closed;
    private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
    private Task? loading;
    private Task? closeTask;
    private Task? export;
    private string? previewFile;
    private GifBitmapDecoder? gif;
    private Image? gifImage;
    private double[] gifTimes = Array.Empty<double>();
    private double gifPosition;
    private readonly bool closeAfterExport;
    private readonly Action<string>? outputSaved;
    private readonly PixelRect inlineRegion;
    internal PlaybackWindow(string path, string kind, string configured = "", RecordingJournal? journal = null, bool closeAfterExport = false, Action<string>? outputSaved = null, PixelRect inlineRegion = default)
    {
        this.closeAfterExport = closeAfterExport; this.outputSaved = outputSaved; this.inlineRegion = inlineRegion;
        this.path = path; this.configured = configured; this.journal = journal;
        Title = L.Get("DeskSnip · 回放与剪辑"); Width = 1020; Height = 780; MinWidth = 820; MinHeight = 560; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (!inlineRegion.IsEmpty)
        {
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.Manual;
        }
        var root = new DockPanel { Margin = new Thickness(16) }; Content = inlineRegion.IsEmpty ? (UIElement)root : new Border { BorderBrush = Ui.Accent, BorderThickness = new Thickness(2), Child = root };
        var top = new WrapPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        if (!inlineRegion.IsEmpty) top.Children.Add(Ui.Text(L.Get("DeskSnip · 回放与剪辑"), 14));
        top.Children.Add(Ui.Button(L.Get("播放 / 暂停"), Toggle)); top.Children.Add(Ui.Button(L.Get("从头播放"), () => { Position(0); Play(); })); top.Children.Add(speed);
        top.Children.Add(Ui.Button(L.Get("导出 MP4"), () => { _ = Export("MP4"); }, true)); top.Children.Add(Ui.Button(L.Get("导出 GIF"), () => { _ = Export("GIF"); })); top.Children.Add(Ui.Button(L.Get("导出 WebP"), () => { _ = Export("WebP"); }));
        top.Children.Add(Ui.Button(L.Get("复制录制文件"), CopyFile)); top.Children.Add(Ui.Button(L.Get("取消导出"), () => cancellation?.Cancel()));
        if (!inlineRegion.IsEmpty) top.Children.Add(Ui.Button(L.Get("关闭"), () => { _ = CloseSafelyAsync(); }));
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom); bottom.Children.Add(seek);
        var trims = new WrapPanel(); bottom.Children.Add(trims); trims.Children.Add(Ui.Text(L.Get("开始 / 结束秒"))); trims.Children.Add(start); trims.Children.Add(end);
        trims.Children.Add(Ui.Button(L.Get("以当前位置为开始"), () => start.Text = CurrentPosition().ToString("0.###", CultureInfo.InvariantCulture)));
        trims.Children.Add(Ui.Button(L.Get("以当前位置为结束"), () => end.Text = CurrentPosition().ToString("0.###", CultureInfo.InvariantCulture)));
        var options = new WrapPanel(); bottom.Children.Add(options); options.Children.Add(Ui.Text(L.Get("裁剪 X / Y / 宽 / 高"))); foreach (var field in crop) options.Children.Add(field);
        options.Children.Add(Ui.Text(L.Get("画质"))); options.Children.Add(quality); options.Children.Add(encoder);
        var audio = new WrapPanel(); bottom.Children.Add(audio); audio.Children.Add(Ui.Text(L.Get("系统声"))); audio.Children.Add(systemVolume); audio.Children.Add(Ui.Text(L.Get("麦克风"))); audio.Children.Add(microphoneVolume); audio.Children.Add(Ui.Text(L.Get("0 为静音；GIF/WebP 没有声音。"), 12, Ui.Subtle));
        bottom.Children.Add(status);
        var content = new Grid(); content.Children.Add(player); content.Children.Add(systemPlayer); content.Children.Add(microphonePlayer); root.Children.Add(content);
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
        player.MediaOpened += (_, _) => { if (info == null && player.NaturalDuration.HasTimeSpan) SetInfo(new MediaInfo { Width = player.NaturalVideoWidth, Height = player.NaturalVideoHeight, Duration = player.NaturalDuration.TimeSpan.TotalSeconds }); };
        player.MediaFailed += (_, _) => status.Text = L.Get("系统播放器无法解码，仍可尝试通过 FFmpeg 导出。");
        seek.ValueChanged += (_, _) => { if (!updatingSeek) Position(seek.Value); };
        speed.SelectionChanged += (_, _) => { if (speed.SelectedItem is double rate) { player.SpeedRatio = systemPlayer.SpeedRatio = microphonePlayer.SpeedRatio = rate; } };
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
            if (gif != null && playing) { gifPosition += .1 * (double)speed.SelectedItem; if (gifPosition >= gifTimes.Last()) gifPosition = 0; SetGifFrame(); }
            updatingSeek = true; seek.Value = CurrentPosition(); updatingSeek = false;
        }; timer.Start();
        Closing += (_, e) => { if (closed) return; e.Cancel = true; _ = CloseSafelyAsync(); };
        Closed += (_, _) =>
        {
            timer.Stop(); player.Close(); systemPlayer.Close(); microphonePlayer.Close(); lifetime.Dispose();
            if (previewFile != null && File.Exists(previewFile)) { try { File.Delete(previewFile); } catch (IOException) { } }
        };
    }
    private void PositionOverlay()
    {
        if (inlineRegion.IsEmpty) return;
        var handle = new WindowInteropHelper(this).Handle; if (handle == IntPtr.Zero) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(inlineRegion.X + inlineRegion.Width / 2, inlineRegion.Y + inlineRegion.Height / 2));
        var work = screen.WorkingArea;
        var width = Math.Min(work.Width, Math.Max(inlineRegion.Width, (int)Math.Ceiling(820 * dpi.DpiScaleX)));
        var height = Math.Min(work.Height, Math.Max(inlineRegion.Height, (int)Math.Ceiling(560 * dpi.DpiScaleY)));
        var x = Math.Max(work.Left, Math.Min(inlineRegion.X + (inlineRegion.Width - width) / 2, work.Right - width));
        var y = Math.Max(work.Top, Math.Min(inlineRegion.Y + (inlineRegion.Height - height) / 2, work.Bottom - height));
        Native.SetWindowPos(handle, Native.Topmost, x, y, width, height, 0x0040);
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
        Play();
    }
    private void SetInfo(MediaInfo media) { info = media; seek.Maximum = media.Duration; end.Text = media.Duration.ToString("0.###", CultureInfo.InvariantCulture); crop[2].Text = media.Width.ToString(); crop[3].Text = media.Height.ToString(); status.Text = L.Format("{0} × {1} · {2:0.0} 秒；设置导出范围、倍速、裁剪及音量。",media.Width,media.Height,media.Duration); }
    private double CurrentPosition() => gif != null ? gifPosition : player.Position.TotalSeconds;
    private void Position(double seconds) { if (gif != null) { gifPosition = seconds; SetGifFrame(); } else player.Position = TimeSpan.FromSeconds(seconds); systemPlayer.Position = microphonePlayer.Position = TimeSpan.FromSeconds(seconds); }
    private void SetGifFrame() { if (gif == null || gifImage == null) return; var index = Array.FindLastIndex(gifTimes, t => t <= gifPosition); gifImage.Source = gif.Frames[Math.Max(0, Math.Min(gif.Frames.Count - 1, index))]; }
    private void Play() { playing = true; player.Play(); if (systemPlayer.Source != null) systemPlayer.Play(); if (microphonePlayer.Source != null) microphonePlayer.Play(); }
    private void Toggle() { if (!playing) Play(); else { playing = false; player.Pause(); systemPlayer.Pause(); microphonePlayer.Pause(); } }
    private VideoEditOptions FullOptions() => new VideoEditOptions { EndSeconds = info!.Duration, Crop = new PixelRect(0, 0, info.Width, info.Height), Quality = 80, FramesPerSecond = 24 };
    private async Task Export(string format)
    {
        if (export != null || info == null || closeRequested) return;
        try
        {
            double Parse(TextBox input) => double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : throw new ArgumentException(L.Get("时间与裁剪请输入有效数字。"));
            var rectangle = crop.Select(field => { var value = Parse(field); if (value != Math.Truncate(value) || value < 0 || value > 100000) throw new ArgumentException(L.Get("裁剪请输入非负整数像素。")); return (int)value; }).ToArray();
            var options = new VideoEditOptions { StartSeconds = Parse(start), EndSeconds = Parse(end), Speed = (double)speed.SelectedItem, Crop = new PixelRect(rectangle[0], rectangle[1], rectangle[2], rectangle[3]),
                Quality = (int)quality.Value, Encoder = (VideoEncoder)encoder.SelectedIndex, FramesPerSecond = format == "MP4" ? 24 : 10, SystemVolume = systemVolume.Value, MicrophoneVolume = microphoneVolume.Value };
            options.Validate(info.Duration, info.Width, info.Height);
            var dialog = new SaveFileDialog { Filter = format + "|*." + format.ToLowerInvariant(), FileName = "DeskSnip-edit." + format.ToLowerInvariant(), AddExtension = true };
            if (dialog.ShowDialog(this) != true) return;
            var source = journal != null && File.Exists(journal.FilePath("source.mp4")) ? journal.FilePath("source.mp4") : path;
            var system = journal?.Snapshot.SystemAudio; var mic = journal?.Snapshot.MicrophoneAudio;
            cancellation = new CancellationTokenSource(); var token = cancellation.Token;
            var progress = new Progress<double>(percent => { if (!closed) status.Text = L.Format("正在导出 {0:0}%…",percent); });
            export = Task.Run(() => Ffmpeg.Export(source, dialog.FileName, options, configured, string.IsNullOrEmpty(system) ? null : journal!.FilePath(system!), string.IsNullOrEmpty(mic) ? null : journal!.FilePath(mic!), token, value => ((IProgress<double>)progress).Report(value)));
            await export; outputSaved?.Invoke(dialog.FileName); if (!closeRequested) { status.Text = L.Get("导出完成。"); if (closeAfterExport) _ = Dispatcher.BeginInvoke(new Action(() => { _ = CloseSafelyAsync(); })); }
        }
        catch (OperationCanceledException) { status.Text = L.Get("已取消导出，原文件和素材保留。"); }
        catch (InvalidOperationException error) { status.Text = error.Message; }
        catch (Exception error) { if (!closeRequested) Ui.Error(this, error, L.Get("剪辑导出")); }
        finally { export = null; cancellation?.Dispose(); cancellation = null; }
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
        if (export != null) { try { await export; } catch (Exception) { } }
        closed = true; Close();
    }
    private void CopyFile() { try { var files = new StringCollection { path }; Clipboard.SetFileDropList(files); status.Text = L.Get("录制文件已复制，可粘贴到文件夹或支持文件的应用。"); } catch (Exception error) { Ui.Error(this, error, L.Get("复制录制文件")); } }
}
