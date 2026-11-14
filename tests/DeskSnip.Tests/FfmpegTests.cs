using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeskSnip.App.Recording;
using DeskSnip.App.Windows;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public sealed class FfmpegTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "DeskSnip-media-" + Guid.NewGuid().ToString("N"));
    private string? Executable()
    {
        try { return Ffmpeg.Find(); } catch (InvalidOperationException) { return null; }
    }
    [Fact] public void TrimCropSpeedAudioAndAnimatedFormatsProduceRealFiles()
    {
        var executable = Executable(); if (executable == null) { Assert.Throws<InvalidOperationException>(() => Ffmpeg.Find()); return; }
        Directory.CreateDirectory(root); var source = Path.Combine(root, "source.mp4");
        Ffmpeg.Run(executable, new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=96x64:rate=10", "-t", "2", "-c:v", "libx264", source }, CancellationToken.None);
        var system = Path.Combine(root, "system.wav"); var mic = Path.Combine(root, "mic.wav");
        foreach (var track in new[] { system, mic }) Ffmpeg.Run(executable, new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "2", track }, CancellationToken.None);
        var options = new VideoEditOptions { StartSeconds = .25, EndSeconds = 1.75, Speed = 2, Crop = new PixelRect(4, 2, 64, 48), FramesPerSecond = 10, SystemVolume = .2, MicrophoneVolume = .3 };
        var output = Path.Combine(root, "edited.mp4"); var progress = 0d;
        Ffmpeg.Export(source, output, options, executable, system, mic, CancellationToken.None, value => progress = value);
        var info = Ffmpeg.Probe(output, executable); Assert.Equal(64, info.Width); Assert.Equal(48, info.Height); Assert.True(info.Audio); Assert.InRange(info.Duration, .7, .95); Assert.Equal(100, progress);
        using (var reader = new VideoReader(output)) { Assert.Equal(64, reader.Width); Assert.NotNull(reader.ReadFrame()); }
        foreach (var format in new[] { "gif", "webp" })
        {
            var animation = Path.Combine(root, "edited." + format); Ffmpeg.Export(source, animation, options, executable, system, mic, CancellationToken.None);
            var bytes = File.ReadAllBytes(animation); Assert.True(bytes.Length > 100);
            Assert.Equal(format == "gif" ? "GIF" : "RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, format == "gif" ? 3 : 4));
        }
        File.WriteAllText(output, "previous"); var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => Ffmpeg.Export(source, output, options, executable, null, null, cancellation.Token)); Assert.Equal("previous", File.ReadAllText(output));
    }
    private sealed class Synthetic : IFrameSource
    {
        public void Copy(byte[] pixels, bool cursor) { for (var i = 0; i < pixels.Length; i += 4) { pixels[i + 1] = 128; pixels[i + 2] = 200; pixels[i + 3] = 255; } }
        public void Dispose() { }
    }
    [Fact] public async Task EnhancedRecordingKeepsRecoverableSourceAndExports()
    {
        var executable = Executable(); if (executable == null) return;
        Directory.CreateDirectory(root); var path = Path.Combine(root, "capture.mp4");
        var session = new RecordingSession(path, new PixelRect(0, 0, 64, 48), "MP4", 10, false, _ => new Synthetic(), new RecordingOptions { DataRoot = root, FfmpegPath = executable });
        await session.StartAsync(); await Task.Delay(300); session.TogglePause(); var paused = session.Elapsed; await Task.Delay(200); Assert.Equal(paused, session.Elapsed); await session.StopAsync();
        Assert.True(File.Exists(path)); Assert.NotNull(session.Journal); Assert.True(session.Journal!.Snapshot.Published); Assert.NotEmpty(session.Journal.Snapshot.Segments);
        Assert.Empty(RecordingJournal.List(root)); Assert.Single(RecordingJournal.List(root, true));
        var info = Ffmpeg.Probe(path, executable); Assert.InRange(info.Duration, .2, .7);
    }
    [Fact] public void HardwareProbeReturnsWorkingEncoderOrSoftware()
    {
        var executable = Executable(); if (executable == null) return;
        Assert.Contains(Ffmpeg.TestEncoder(executable, VideoEncoder.Auto), new[] { "libx264", "h264_nvenc", "h264_qsv", "h264_amf" });
    }
    private sealed class InterruptedSource : IFrameSource
    {
        private int copies;
        public void Copy(byte[] pixels, bool cursor)
        {
            if (++copies > 7) throw new IOException("Synthetic interruption after video frames.");
            for (var index = 3; index < pixels.Length; index += 4) pixels[index] = 255;
        }
        public void Dispose() { }
    }
    [Fact] public async Task InterruptedEnhancedRecordingLeavesUsableRecoveryAndPreviousOutput()
    {
        var executable = Executable(); if (executable == null) return;
        Directory.CreateDirectory(root); var output = Path.Combine(root, "existing.mp4"); File.WriteAllText(output, "previous");
        var session = new RecordingSession(output, new PixelRect(0, 0, 64, 48), "MP4", 10, false, _ => new InterruptedSource(), new RecordingOptions { DataRoot = root, FfmpegPath = executable });
        await session.StartAsync(); await Assert.ThrowsAsync<IOException>(() => session.Completion);
        Assert.Equal("previous", File.ReadAllText(output)); var draft = RecordingJournal.List(root).Single(); Assert.NotEmpty(draft.Segments);
        var recovered = Path.Combine(root, "recovered.mp4"); var journal = RecordingJournal.Open(root, draft.Id); Ffmpeg.Join(journal, recovered, executable, CancellationToken.None); var info = Ffmpeg.Probe(recovered, executable); Assert.InRange(info.Duration, .3, 1);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
