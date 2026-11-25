using System;
using System.Runtime.InteropServices;
using DeskSnip.App.Windows;

namespace DeskSnip.App.Recording;

public sealed class VideoFrame
{
    internal VideoFrame(byte[] pixels, long time, long duration) { Pixels = pixels; Timestamp = time; Duration = duration; }
    public byte[] Pixels { get; }
    public long Timestamp { get; }
    public long Duration { get; }
}

/// <summary>同步 Media Foundation 解码，视频帧转换为顶向下 BGRA32。</summary>
public sealed class VideoReader : IDisposable
{
    private const uint VideoStream = 0xfffffffc;
    private IntPtr reader;
    private bool started;
    private bool comInitialized;
    private bool decodedFrame;
    private int stride;
    public int Width { get; }
    public int Height { get; }
    public long FrameDuration { get; }
    public VideoReader(string path)
    {
        IntPtr attributes = IntPtr.Zero, output = IntPtr.Zero, actual = IntPtr.Zero;
        try
        {
            Mf.Check(Native.CoInitializeEx(IntPtr.Zero, 0)); comInitialized = true;
            Mf.Check(Mf.MFStartup(0x00020070, 0)); started = true;
            Mf.Check(Mf.MFCreateAttributes(out attributes, 1));
            Mf.Uint(attributes, "fb394f3d-ccf1-42ee-bbb3-f9b845d5681d", 1);
            Mf.Check(Mf.MFCreateSourceReaderFromURL(path, attributes, out reader));
            Mf.Check(Mf.Method<Mf.SelectStream>(reader, 4)(reader, 0xfffffffe, 0));
            Mf.Check(Mf.Method<Mf.SelectStream>(reader, 4)(reader, VideoStream, 1));
            Mf.Check(Mf.MFCreateMediaType(out output));
            Mf.GuidValue(output, "48eba18e-f8c9-4687-bf11-0a74c9f96a8f", "73646976-0000-0010-8000-00aa00389b71");
            Mf.GuidValue(output, "f7e34c9a-42e8-4714-b74b-cb29d72c35e5", "00000016-0000-0010-8000-00aa00389b71"); // RGB32
            Mf.Check(Mf.Method<Mf.SetType>(reader, 7)(reader, VideoStream, IntPtr.Zero, output));
            Mf.Check(Mf.Method<Mf.GetMediaType>(reader, 6)(reader, VideoStream, out actual));
            var sizeKey = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
            Mf.Check(Mf.Method<Mf.GetUlong>(actual, 8)(actual, ref sizeKey, out var dimensions));
            Width = checked((int)(dimensions >> 32)); Height = checked((int)(dimensions & 0xffffffff));
            if (Width < 1 || Height < 1 || (long)Width * Height > 8_294_400) throw new ArgumentException(L.Get("视频尺寸超出处理范围。"));
            var rateKey = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
            var rateResult = Mf.Method<Mf.GetUlong>(actual, 8)(actual, ref rateKey, out var rate);
            FrameDuration = rateResult >= 0 && (rate >> 32) > 0 ? checked(TimeSpan.TicksPerSecond * (long)(rate & 0xffffffff) / (long)(rate >> 32)) : TimeSpan.TicksPerSecond / 15;
            var strideKey = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
            var strideResult = Mf.Method<Mf.GetUint>(actual, 7)(actual, ref strideKey, out var pitch);
            stride = strideResult >= 0 ? unchecked((int)pitch) : Width * 4;
        }
        catch { Dispose(); throw; }
        finally { Mf.Release(ref actual); Mf.Release(ref output); Mf.Release(ref attributes); }
    }
    public VideoFrame? ReadFrame()
    {
        if (reader == IntPtr.Zero) throw new ObjectDisposedException(nameof(VideoReader));
        while (true)
        {
            IntPtr sample = IntPtr.Zero, buffer = IntPtr.Zero, imageBuffer = IntPtr.Zero;
            try
            {
                Mf.Check(Mf.Method<Mf.ReadSample>(reader, 9)(reader, VideoStream, 0, out _, out var flags, out var time, out sample));
                if ((flags & 1) != 0) throw new InvalidOperationException(L.Get("视频解码失败。"));
                if ((flags & 0x30) != 0)
                {
                    // 解码器常在首帧前报告一次格式协商；核对输出尺寸后接受，真正中途变尺寸仍拒绝。
                    if (decodedFrame) throw new ArgumentException(L.Get("不支持录制中途改变尺寸的视频。"));
                    if (sample == IntPtr.Zero) continue;
                    ValidateInitialFormatChange();
                }
                if (sample == IntPtr.Zero) { if ((flags & 2) != 0) return null; continue; }
                var durationResult = Mf.Method<Mf.GetLong>(sample, 37)(sample, out var duration);
                Mf.Check(Mf.Method<Mf.GetPointer>(sample, 41)(sample, out buffer));
                var pixels = new byte[checked(Width * Height * 4)];
                var iid2D = new Guid("7dc9d5f9-9ed9-44ec-9bbf-0600bb589fbb");
                if (Marshal.QueryInterface(buffer, ref iid2D, out imageBuffer) >= 0)
                {
                    Mf.Check(Mf.Method<Mf.Lock2D>(imageBuffer, 3)(imageBuffer, out var scanline, out var pitch));
                    try
                    {
                        if (Math.Abs(pitch) < Width * 4) throw new InvalidOperationException(L.Get("视频行宽不匹配。"));
                        // Lock2D 给出的 scanline0 始终是顶行，保留带符号 pitch。
                        for (var y = 0; y < Height; y++) Marshal.Copy(IntPtr.Add(scanline, y * pitch), pixels, y * Width * 4, Width * 4);
                    }
                    finally { Mf.Check(Mf.Method<Mf.Simple>(imageBuffer, 4)(imageBuffer)); }
                }
                else
                {
                    Mf.Check(Mf.Method<Mf.LockBuffer>(buffer, 3)(buffer, out var data, out _, out var length));
                    try
                    {
                        var pitch = Math.Abs(stride);
                        if (pitch < Width * 4 || length < (long)pitch * Height) throw new InvalidOperationException(L.Get("视频帧缓冲区尺寸不匹配。"));
                        for (var y = 0; y < Height; y++) Marshal.Copy(IntPtr.Add(data, (stride < 0 ? Height - 1 - y : y) * pitch), pixels, y * Width * 4, Width * 4);
                    }
                    finally { Mf.Check(Mf.Method<Mf.Simple>(buffer, 4)(buffer)); }
                }
                for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
                decodedFrame = true;
                return new VideoFrame(pixels, time, durationResult >= 0 && duration > 0 ? duration : FrameDuration);
            }
            finally { Mf.Release(ref imageBuffer); Mf.Release(ref buffer); Mf.Release(ref sample); }
        }
    }
    private void ValidateInitialFormatChange()
    {
        var current = IntPtr.Zero;
        try
        {
            Mf.Check(Mf.Method<Mf.GetMediaType>(reader, 6)(reader, VideoStream, out current));
            var sizeKey = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
            Mf.Check(Mf.Method<Mf.GetUlong>(current, 8)(current, ref sizeKey, out var dimensions));
            var currentWidth = (int)(dimensions >> 32); var currentHeight = (int)(dimensions & 0xffffffff);
            var alignedWidth = (Width + 15) & ~15; var alignedHeight = (Height + 15) & ~15;
            if ((currentWidth != Width && currentWidth != alignedWidth) || (currentHeight != Height && currentHeight != alignedHeight))
                throw new ArgumentException(L.Get("不支持录制中途改变尺寸的视频。"));
            // H.264 解码器可把首帧缓冲区补齐到 16 像素边界；逻辑画面尺寸不变，但连续缓冲必须改用协商后的行宽。
            var strideKey = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
            var strideResult = Mf.Method<Mf.GetUint>(current, 7)(current, ref strideKey, out var pitch);
            stride = strideResult >= 0 ? unchecked((int)pitch) : (stride < 0 ? -currentWidth : currentWidth) * 4;
        }
        finally { Mf.Release(ref current); }
    }
    public void Dispose()
    {
        Mf.Release(ref reader);
        if (started) { Mf.MFShutdown(); started = false; }
        if (comInitialized) { Native.CoUninitialize(); comInitialized = false; }
    }
}
