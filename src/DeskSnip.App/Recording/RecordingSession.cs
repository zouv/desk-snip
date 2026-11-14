using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DeskSnip.App.Windows;
using DeskSnip.Core;

namespace DeskSnip.App.Recording;

internal sealed class RecordingSession
{
    private readonly object gate = new object();
    private readonly Stopwatch clock = new Stopwatch();
    private readonly RecordingTimeline timeline = new RecordingTimeline();
    private readonly TaskCompletionSource<bool> initialized = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? worker;
    private bool stopRequested;
    private bool discardRequested;
    private readonly int quality;
    internal string Path { get; }
    internal PixelRect Region { get; }
    internal string Format { get; }
    internal int FramesPerSecond { get; }
    internal bool SystemAudioEnabled => options?.SystemAudio == true;
    internal bool MicrophoneEnabled => options?.Microphone == true;
    internal bool CursorIncluded => cursor;
    private readonly bool cursor;
    private readonly Func<PixelRect, IFrameSource> sourceFactory;
    private readonly RecordingOptions? options;
    internal RecordingJournal? Journal { get; private set; }
    internal double ExportProgress { get; private set; }
    internal string EncoderUsed { get; private set; } = "Windows H.264";
    internal RecordingSession(string path, PixelRect region, string format, int fps, bool cursor, Func<PixelRect, IFrameSource>? sourceFactory = null, RecordingOptions? options = null, int quality = 80)
    {
        if (format != "MP4" && format != "GIF" && format != "WebP") throw new ArgumentException(TextCatalog.Get("不支持此录制格式。"));
        if (fps < 1 || fps > 60) throw new ArgumentOutOfRangeException(nameof(fps));
        if (format == "WebP" && options == null) throw new ArgumentException(L.Get("WebP 录制需要 FFmpeg 增强模式。"));
        this.options = options;
        if (quality < 1 || quality > 100) throw new ArgumentOutOfRangeException(nameof(quality)); this.quality = quality;
        Path = AtomicFile.ValidateTarget(path); Region = format == "MP4" || options != null ? region.EvenSize() : region; Format = format;
        if (!string.Equals(System.IO.Path.GetExtension(Path), "." + format, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(L.Get("保存文件的扩展名需要与录制格式一致。"));
        FramesPerSecond = format == "GIF" ? Math.Min(15, fps) : fps; this.cursor = cursor;
        this.sourceFactory = sourceFactory ?? (r => new FrameGrabber(r));
        if (Region.Width < 2 || Region.Height < 2) throw new ArgumentException(L.Get("录制区域至少需要 2 × 2 像素。"));
        if (format == "GIF" && (long)region.Width * region.Height > 2_073_600) throw new ArgumentException(L.Get("GIF 选区请保持在 207 万像素以内；较大区域可以录制 MP4。"));
    }
    internal Task StartAsync()
    {
        if (worker != null) throw new InvalidOperationException(TextCatalog.Get("录制已经开始。"));
        worker = Task.Factory.StartNew(Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        return initialized.Task;
    }
    internal RecordingState State { get { lock (gate) return timeline.State; } }
    internal TimeSpan Elapsed { get { lock (gate) return timeline.Elapsed(clock.Elapsed); } }
    internal Task Completion => worker ?? Task.CompletedTask;
    internal void TogglePause()
    {
        lock (gate)
        {
            if (timeline.State == RecordingState.Recording) timeline.Pause(clock.Elapsed);
            else if (timeline.State == RecordingState.Paused) timeline.Resume(clock.Elapsed);
            Monitor.PulseAll(gate);
        }
    }
    internal Task StopAsync()
    {
        lock (gate)
        {
            if (!stopRequested)
            {
                stopRequested = true;
                if (timeline.State == RecordingState.Recording || timeline.State == RecordingState.Paused) timeline.Stop(clock.Elapsed);
                Monitor.PulseAll(gate);
            }
        }
        return Completion;
    }
    internal Task DiscardAsync() { lock (gate) discardRequested = true; return StopAsync(); }
    private void Run()
    {
        if (options != null) { RunEnhanced(options); return; }
        // 输出先写同目录临时文件，编码收尾成功才发布，保护用户已有文件。
        var temp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, ".desksnip-" + Guid.NewGuid().ToString("N") + System.IO.Path.GetExtension(Path));
        Mp4Writer? mp4 = null; GifWriter? gif = null;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            using (var grabber = sourceFactory(Region))
            {
                if (Format == "MP4") mp4 = new Mp4Writer(temp, Region.Width, Region.Height, FramesPerSecond, quality);
                else gif = new GifWriter(new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None), Region.Width, Region.Height);
                var current = new byte[checked(Region.Width * Region.Height * 4)];
                var pending = new byte[current.Length];
                var nominal = TimeSpan.TicksPerSecond / FramesPerSecond;
                var pendingTime = -1L;
                var nextFrame = 0L;
                lock (gate)
                {
                    clock.Start(); timeline.Start(TimeSpan.Zero);
                    if (stopRequested) timeline.Stop(clock.Elapsed);
                }
                initialized.TrySetResult(true);
                void Write(byte[] frame, long time, long duration)
                {
                    if (mp4 != null) mp4.WriteFrame(frame, time, Math.Max(1, duration));
                    else gif!.WriteFrame(frame, TimeSpan.FromTicks(Math.Max(1, duration)));
                }
                while (true)
                {
                    long elapsed;
                    lock (gate)
                    {
                        if (stopRequested) break;
                        if (timeline.State == RecordingState.Paused) { Monitor.Wait(gate, 100); continue; }
                        elapsed = timeline.Elapsed(clock.Elapsed).Ticks;
                        if (elapsed < nextFrame) { Monitor.Wait(gate, Math.Max(1, Math.Min(50, (int)((nextFrame - elapsed) / TimeSpan.TicksPerMillisecond)))); continue; }
                    }
                    grabber.Copy(current, cursor);
                    if (pendingTime >= 0) Write(pending, pendingTime, elapsed - pendingTime);
                    var swap = pending; pending = current; current = swap;
                    pendingTime = elapsed; nextFrame = elapsed + nominal;
                }
                var end = Elapsed.Ticks;
                if (pendingTime < 0) { grabber.Copy(pending, cursor); pendingTime = 0; }
                Write(pending, pendingTime, Math.Max(nominal, end - pendingTime));
                mp4?.Complete(); gif?.Dispose(); gif = null; mp4?.Dispose(); mp4 = null;
            }
            if (discardRequested) return;
            if (File.Exists(Path)) File.Replace(temp, Path, null);
            else File.Move(temp, Path);
        }
        catch (Exception e) { initialized.TrySetException(e); throw; }
        finally
        {
            try { gif?.Dispose(); }
            finally
            {
                try { mp4?.Dispose(); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }
    }
    private (bool Recording, long Ticks) AudioPosition()
    {
        lock (gate) return (timeline.State == RecordingState.Recording, timeline.Elapsed(clock.Elapsed).Ticks);
    }
    private void RunEnhanced(RecordingOptions settings)
    {
        Mp4Writer? writer = null; AudioCapture? system = null; AudioCapture? microphone = null;
        var segmentNumber = 0; var segmentStart = 0L; var segmentDuration = 0L;
        void CompleteSegment()
        {
            if (writer == null) return;
            writer.Complete(); writer.Dispose(); writer = null;
            if (segmentDuration > 0) Journal!.AddSegment(Journal.SegmentPath(segmentNumber), segmentDuration);
            segmentNumber++; segmentDuration = 0;
        }
        void Write(byte[] pixels, long timestamp, long duration)
        {
            if (writer == null) { segmentStart = timestamp; writer = new Mp4Writer(Journal!.SegmentPath(segmentNumber), Region.Width, Region.Height, FramesPerSecond, settings.Quality); }
            writer.WriteFrame(pixels, timestamp - segmentStart, Math.Max(1, duration)); segmentDuration = timestamp - segmentStart + Math.Max(1, duration);
            if (segmentDuration >= TimeSpan.FromSeconds(2).Ticks) CompleteSegment();
        }
        try
        {
            Ffmpeg.Find(settings.FfmpegPath);
            Journal = RecordingJournal.Create(settings.DataRoot, Region.Width, Region.Height, FramesPerSecond); Journal.SetAudio(settings.SystemAudio, settings.Microphone);
            using (var grabber = sourceFactory(Region))
            {
                if (settings.SystemAudio) { system = new AudioCapture(Journal.FilePath("system.wav"), true, settings.SystemDeviceId, AudioPosition); system.Start(); }
                if (settings.Microphone) { microphone = new AudioCapture(Journal.FilePath("microphone.wav"), false, settings.MicrophoneDeviceId, AudioPosition); microphone.Start(); }
                var current = new byte[checked(Region.Width * Region.Height * 4)]; var pending = new byte[current.Length];
                var nominal = TimeSpan.TicksPerSecond / FramesPerSecond; var pendingTime = -1L; var nextFrame = 0L;
                lock (gate) { clock.Start(); timeline.Start(TimeSpan.Zero); if (stopRequested) timeline.Stop(clock.Elapsed); }
                initialized.TrySetResult(true);
                while (true)
                {
                    long elapsed;
                    lock (gate)
                    {
                        if (stopRequested) break;
                        if (timeline.State == RecordingState.Paused) { Monitor.Wait(gate, 100); continue; }
                        elapsed = timeline.Elapsed(clock.Elapsed).Ticks;
                        if (elapsed < nextFrame) { Monitor.Wait(gate, Math.Max(1, Math.Min(50, (int)((nextFrame - elapsed) / TimeSpan.TicksPerMillisecond)))); continue; }
                    }
                    if (system?.Failure != null || microphone?.Failure != null) throw new InvalidOperationException(L.Get("音频设备中断，已完成录制片段保留，可重新导出。"));
                    grabber.Copy(current, cursor);
                    if (pendingTime >= 0) Write(pending, pendingTime, elapsed - pendingTime);
                    var swap = pending; pending = current; current = swap; pendingTime = elapsed; nextFrame = elapsed + nominal;
                }
                var end = Elapsed.Ticks; if (pendingTime < 0) { grabber.Copy(pending, cursor); pendingTime = 0; }
                Write(pending, pendingTime, Math.Max(nominal, end - pendingTime)); CompleteSegment();
                system?.Stop(end); microphone?.Stop(end); system?.Dispose(); system = null; microphone?.Dispose(); microphone = null;
            }
            if (discardRequested) { Journal.Discard(); return; }
            var source = Journal.FilePath("source.mp4"); Ffmpeg.Join(Journal, source, settings.FfmpegPath, CancellationToken.None);
            var info = Ffmpeg.Probe(source, settings.FfmpegPath);
            var edit = new VideoEditOptions { EndSeconds = info.Duration, Crop = new PixelRect(0, 0, Region.Width, Region.Height), FramesPerSecond = FramesPerSecond, Quality = settings.Quality, Encoder = settings.Encoder };
            EncoderUsed = Ffmpeg.Export(source, Path, edit, settings.FfmpegPath, settings.SystemAudio ? Journal.FilePath("system.wav") : null,
                settings.Microphone ? Journal.FilePath("microphone.wav") : null, CancellationToken.None, percent => ExportProgress = percent);
            Journal.MarkPublished();
        }
        catch (Exception error)
        {
            try { if (writer != null && segmentDuration > 0) CompleteSegment(); } catch (Exception) { }
            initialized.TrySetException(error); throw;
        }
        finally
        {
            try { system?.Dispose(); }
            finally { try { microphone?.Dispose(); } finally { writer?.Dispose(); } }
        }
    }
}
