using System;
using System.IO;
using System.Threading;
using DeskSnip.Core;

namespace DeskSnip.App.Recording;

public static class GifExporter
{
    public static void Export(string source, string target, int fps = 10, CancellationToken cancellation = default)
    {
        if (fps < 1 || fps > 15) throw new ArgumentOutOfRangeException(nameof(fps));
        using (var reader = new VideoReader(source))
        {
            if ((long)reader.Width * reader.Height > 2_073_600) throw new ArgumentException("GIF 转换最多支持 207 万像素。");
            AtomicFile.Write(target, stream =>
            {
                using (var writer = new GifWriter(stream, reader.Width, reader.Height, true))
                {
                    VideoFrame? pending = null;
                    var next = 0L; var end = 0L;
                    while (true)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var frame = reader.ReadFrame(); if (frame == null) break;
                        end = frame.Timestamp + frame.Duration;
                        if (pending != null && frame.Timestamp < next) continue;
                        if (pending != null) writer.WriteFrame(pending.Pixels, TimeSpan.FromTicks(Math.Max(1, frame.Timestamp - pending.Timestamp)));
                        pending = frame; next = frame.Timestamp + TimeSpan.TicksPerSecond / fps;
                    }
                    if (pending == null) throw new InvalidDataException("视频没有可导出的帧。");
                    writer.WriteFrame(pending.Pixels, TimeSpan.FromTicks(Math.Max(1, end - pending.Timestamp)));
                }
            });
        }
    }
}
