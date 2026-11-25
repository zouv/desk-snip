using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DeskSnip.App.Recording;
using Xunit;

namespace DeskSnip.Tests;

public class Mp4Tests
{
    [Fact] public async Task NativeEncoderFinalizesPlayableContainerWithCorrectDuration()
    {
        var path = Path.Combine(Path.GetTempPath(), "DeskSnip-video-test-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            await Task.Run(() =>
            {
                using (var writer = new Mp4Writer(path, 64, 48, 10))
                {
                    var frame = new byte[64 * 48 * 4];
                    for (var i = 0; i < frame.Length; i += 4) { frame[i + (i / (64 * 4) < 24 ? 2 : 0)] = 255; frame[i + 3] = 255; }
                    for (var i = 0; i < 10; i++) writer.WriteFrame(frame, i * TimeSpan.TicksPerSecond / 10, TimeSpan.TicksPerSecond / 10);
                    writer.Complete();
                }
            });
            var bytes = File.ReadAllBytes(path); var container = Encoding.ASCII.GetString(bytes);
            Assert.Contains("ftyp", container); Assert.Contains("moov", container); Assert.Contains("mdat", container); Assert.Contains("avc1", container);
            var mvhd = Find(bytes, Encoding.ASCII.GetBytes("mvhd")); Assert.True(mvhd > 0);
            // ISO BMFF mvhd v0：creation(4), modification(4), timescale(4), duration(4)。
            Assert.Equal(0, bytes[mvhd + 4]);
            var scale = ReadBe(bytes, mvhd + 16); var duration = ReadBe(bytes, mvhd + 20);
            Assert.InRange((double)duration / scale, .95, 1.05);
            await Task.Run(() =>
            {
                using (var reader = new VideoReader(path))
                {
                    Assert.Equal(64, reader.Width); Assert.Equal(48, reader.Height);
                    var count = 0;
                    while (reader.ReadFrame() is VideoFrame decoded)
                    {
                        Assert.True(decoded.Pixels[(8 * 64 + 8) * 4 + 2] > 220, "Top must remain red.");
                        Assert.True(decoded.Pixels[(40 * 64 + 8) * 4] > 220, "Bottom must remain blue.");
                        Assert.Equal(count * TimeSpan.TicksPerSecond / 10, decoded.Timestamp); count++;
                    }
                    Assert.Equal(10, count);
                }
            });
            var gifPath = Path.ChangeExtension(path, ".gif");
            try
            {
                await Task.Run(() => GifExporter.Export(path, gifPath, 5));
                using (var stream = File.OpenRead(gifPath))
                {
                    var decoder = new System.Windows.Media.Imaging.GifBitmapDecoder(stream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    Assert.Equal(5, decoder.Frames.Count);
                    Assert.Equal((ushort)20, ((System.Windows.Media.Imaging.BitmapMetadata)decoder.Frames[0].Metadata).GetQuery("/grctlext/Delay"));
                }
            }
            finally { if (File.Exists(gifPath)) File.Delete(gifPath); }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    [Fact] public async Task NativeDecoderAcceptsInitialFormatNegotiation()
    {
        var path = Path.Combine(Path.GetTempPath(), "DeskSnip-video-format-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            await Task.Run(() =>
            {
                const int width = 640, height = 360;
                using (var writer = new Mp4Writer(path, width, height, 10))
                {
                    var frame = new byte[width * height * 4];
                    for (var i = 0; i < frame.Length; i += 4) { frame[i + 1] = 255; frame[i + 3] = 255; }
                    writer.WriteFrame(frame, 0, TimeSpan.TicksPerSecond / 10);
                    for (var i = 0; i < frame.Length; i += 4) { frame[i + 1] = 0; frame[i + 2] = 255; }
                    writer.WriteFrame(frame, TimeSpan.TicksPerSecond / 10, TimeSpan.TicksPerSecond / 10);
                    writer.Complete();
                }
                using (var reader = new VideoReader(path))
                {
                    var first = reader.ReadFrame(); Assert.NotNull(first); Assert.True(first!.Pixels[1] > 220);
                    var second = reader.ReadFrame(); Assert.NotNull(second); Assert.True(second!.Pixels[2] > 220);
                }
            });
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    [Fact] public async Task NativeDecoderPreservesFrameLayoutWithPaddedVideoRows()
    {
        var path = Path.Combine(Path.GetTempPath(), "DeskSnip-video-stride-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            await Task.Run(() =>
            {
                const int width = 824, height = 416;
                var frame = new byte[width * height * 4];
                for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                {
                    var offset = (y * width + x) * 4;
                    var gray = (byte)((x / 4 * 7 + y / 4 * 13) & 255);
                    frame[offset] = frame[offset + 1] = frame[offset + 2] = gray; frame[offset + 3] = 255;
                }
                using (var writer = new Mp4Writer(path, width, height, 10))
                {
                    writer.WriteFrame(frame, 0, TimeSpan.TicksPerSecond / 10); writer.Complete();
                }
                using (var reader = new VideoReader(path))
                {
                    var decoded = reader.ReadFrame(); Assert.NotNull(decoded);
                    double error = 0;
                    for (var y = 0; y < height; y += 4) for (var x = 0; x < width; x += 4)
                    {
                        var offset = (y * width + x) * 4;
                        error += Math.Abs(decoded!.Pixels[offset] - frame[offset]);
                    }
                    error /= width * height / 16;
                    Assert.True(error < 20, "Decoded rows are misaligned; mean luminance error: " + error);
                }
            });
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    [Fact] public void OddEncodingDimensionsAreRejected() => Assert.Throws<ArgumentException>(() => new Mp4Writer("unused.mp4", 31, 31, 15));
    private static uint ReadBe(byte[] bytes, int offset) => (uint)bytes[offset] << 24 | (uint)bytes[offset + 1] << 16 | (uint)bytes[offset + 2] << 8 | bytes[offset + 3];
    private static int Find(byte[] bytes, byte[] pattern)
    {
        for (var i = 0; i < bytes.Length - pattern.Length; i++) if (pattern.Select((b, j) => bytes[i + j] == b).All(x => x)) return i;
        return -1;
    }
}
