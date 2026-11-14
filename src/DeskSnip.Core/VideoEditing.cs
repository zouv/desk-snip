using System;
using System.Text;

namespace DeskSnip.Core;

public enum VideoEncoder { Software, Auto, Nvidia, Intel, Amd }

public sealed class VideoEditOptions
{
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public double Speed { get; set; } = 1;
    public PixelRect Crop { get; set; }
    public double SystemVolume { get; set; } = 1;
    public double MicrophoneVolume { get; set; } = 1;
    public int Quality { get; set; } = 80;
    public int FramesPerSecond { get; set; } = 24;
    public VideoEncoder Encoder { get; set; } = VideoEncoder.Software;
    public double OutputSeconds => (EndSeconds - StartSeconds) / Speed;
    public void Validate(double duration, int width, int height)
    {
        if (!Finite(StartSeconds) || !Finite(EndSeconds) || StartSeconds < 0 || EndSeconds <= StartSeconds || EndSeconds > duration + .1) throw new ArgumentException(TextCatalog.Get("剪辑开始和结束时间需在视频时长内，结束时间大于开始时间。"));
        if (!Finite(Speed) || Speed < .2 || Speed > 2) throw new ArgumentException(TextCatalog.Get("倍速需要在 0.2–2 之间。"));
        if (Crop.IsEmpty || !Crop.Intersect(new PixelRect(0, 0, width, height)).Equals(Crop) || Crop.Width < 2 || Crop.Height < 2) throw new ArgumentException(TextCatalog.Get("画面裁剪区域无效。"));
        if (!Finite(SystemVolume) || !Finite(MicrophoneVolume) || SystemVolume < 0 || MicrophoneVolume < 0 || SystemVolume > 2 || MicrophoneVolume > 2) throw new ArgumentException(TextCatalog.Get("音量需要在 0–200% 之间。"));
        if (Quality < 1 || Quality > 100 || FramesPerSecond < 1 || FramesPerSecond > 60 || !Enum.IsDefined(typeof(VideoEncoder), Encoder)) throw new ArgumentException(TextCatalog.Get("画质、帧率或编码器无效。"));
    }
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

/// <summary>Windows CreateProcess 参数转义，不经过命令行 shell。</summary>
public static class ProcessArguments
{
    public static string Quote(string value)
    {
        if (value == null || value.IndexOf('\0') >= 0) throw new ArgumentException(TextCatalog.Get("参数无效。"));
        var result = new StringBuilder("\""); var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            if (character == '"') result.Append('\\', slashes * 2 + 1).Append('"');
            else result.Append('\\', slashes).Append(character);
            slashes = 0;
        }
        result.Append('\\', slashes * 2).Append('"'); return result.ToString();
    }
}

public readonly struct AudioPacketPlacement
{
    public AudioPacketPlacement(long silenceFrames, int skipFrames, int frames) { SilenceFrames = silenceFrames; SkipFrames = skipFrames; Frames = frames; }
    public long SilenceFrames { get; }
    public int SkipFrames { get; }
    public int Frames { get; }
}

public static class AudioTimeline
{
    public static AudioPacketPlacement Place(long endTicks, int packetFrames, int sampleRate, long writtenFrames)
    {
        if (endTicks < 0 || packetFrames < 0 || sampleRate < 1 || sampleRate > 384000 || writtenFrames < 0) throw new ArgumentOutOfRangeException(nameof(endTicks));
        var end = (long)(endTicks / (double)TimeSpan.TicksPerSecond * sampleRate); var start = Math.Max(0, end - packetFrames);
        var prefix = (int)Math.Min(packetFrames, Math.Max(0, packetFrames - end));
        var skip = (int)Math.Min(packetFrames, prefix + Math.Max(0, writtenFrames - start));
        return new AudioPacketPlacement(Math.Max(0, start - writtenFrames), skip, (int)Math.Max(0, Math.Min(packetFrames - skip, end - Math.Max(writtenFrames, start))));
    }
}
