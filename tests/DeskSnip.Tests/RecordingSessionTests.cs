using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using DeskSnip.App.Recording;
using DeskSnip.App.Windows;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

[Collection("Recording session timing")]
public class RecordingSessionTests
{
    private sealed class SyntheticSource : IFrameSource
    {
        public void Copy(byte[] buffer, bool cursor)
        { for (var i = 0; i < buffer.Length; i += 4) { buffer[i + 2] = 255; buffer[i + 3] = 255; } }
        public void Dispose() { }
    }
    [Fact] public async Task PauseIsExcludedFromEncodedGifAndStopWhilePausedFinalizes()
    {
        var root = Path.Combine(Path.GetTempPath(), "DeskSnip-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var path = Path.Combine(root, "capture.gif");
        try
        {
            var session = new RecordingSession(path, new PixelRect(-100, -100, 32, 24), "GIF", 10, false, _ => new SyntheticSource());
            await session.StartAsync(); await Task.Delay(250); session.TogglePause();
            var paused = session.Elapsed; await Task.Delay(350); Assert.Equal(paused, session.Elapsed);
            session.TogglePause(); await Task.Delay(250); session.TogglePause();
            await Task.Delay(200); await session.StopAsync(); await session.StopAsync();
            using (var stream = File.OpenRead(path))
            {
                var decoded = new GifBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var duration = 0;
                foreach (var frame in decoded.Frames) duration += (ushort)((BitmapMetadata)frame.Metadata).GetQuery("/grctlext/Delay");
                Assert.InRange(duration / 100.0, .4, .85);
                Assert.InRange(Math.Abs(duration / 100.0 - session.Elapsed.TotalSeconds), 0, .16);
                Assert.True(decoded.Frames.Count >= 2);
            }
            Assert.Equal(RecordingState.Stopped, session.State);
            Assert.Single(Directory.GetFiles(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact] public async Task FailedCaptureLeavesExistingOutputAndCleansTemporaryFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "DeskSnip-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var path = Path.Combine(root, "existing.gif"); File.WriteAllText(path, "previous");
        try
        {
            var session = new RecordingSession(path, new PixelRect(0, 0, 16, 16), "GIF", 10, false, _ => throw new IOException("Synthetic capture failure."));
            await Assert.ThrowsAsync<IOException>(() => session.StartAsync());
            await Assert.ThrowsAsync<IOException>(() => session.Completion);
            Assert.Equal("previous", File.ReadAllText(path)); Assert.Single(Directory.GetFiles(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact] public async Task StopDuringInitializationCannotRemainRecording()
    {
        var root = Path.Combine(Path.GetTempPath(), "DeskSnip-immediate-stop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var path = Path.Combine(root, "capture.gif");
        try
        {
            var session = new RecordingSession(path, new PixelRect(0, 0, 16, 16), "GIF", 10, false, _ => new SyntheticSource());
            var start = session.StartAsync(); await session.StopAsync(); await start;
            Assert.Equal(RecordingState.Stopped, session.State); Assert.True(File.Exists(path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

// The integration tests assert short wall-clock intervals; run without competing UI/encoder tests.
[CollectionDefinition("Recording session timing", DisableParallelization = true)]
public sealed class RecordingSessionTimingCollection { }
