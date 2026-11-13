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
    internal string Path { get; }
    internal PixelRect Region { get; }
    internal string Format { get; }
    internal int FramesPerSecond { get; }
    private readonly bool cursor;
    private readonly Func<PixelRect, IFrameSource> sourceFactory;
    internal RecordingSession(string path, PixelRect region, string format, int fps, bool cursor, Func<PixelRect, IFrameSource>? sourceFactory = null)
    {
        if (format != "MP4" && format != "GIF") throw new ArgumentException("Unsupported recording format.");
        if (fps < 1 || fps > 30) throw new ArgumentOutOfRangeException(nameof(fps));
        Path = AtomicFile.ValidateTarget(path); Region = format == "MP4" ? region.EvenSize() : region; Format = format;
        if (!string.Equals(System.IO.Path.GetExtension(Path), "." + format, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("保存文件的扩展名需要与录制格式一致。");
        FramesPerSecond = format == "GIF" ? Math.Min(15, fps) : fps; this.cursor = cursor;
        this.sourceFactory = sourceFactory ?? (r => new FrameGrabber(r));
        if (Region.Width < 2 || Region.Height < 2) throw new ArgumentException("录制区域至少需要 2 × 2 像素。");
        if (format == "GIF" && (long)region.Width * region.Height > 2_073_600) throw new ArgumentException("GIF 选区请保持在 207 万像素以内；较大区域可以录制 MP4。");
    }
    internal Task StartAsync()
    {
        if (worker != null) throw new InvalidOperationException("Already started.");
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
    private void Run()
    {
        // 输出先写同目录临时文件，编码收尾成功才发布，保护用户已有文件。
        var temp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, ".desksnip-" + Guid.NewGuid().ToString("N") + System.IO.Path.GetExtension(Path));
        Mp4Writer? mp4 = null; GifWriter? gif = null;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            using (var grabber = sourceFactory(Region))
            {
                if (Format == "MP4") mp4 = new Mp4Writer(temp, Region.Width, Region.Height, FramesPerSecond);
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
}
