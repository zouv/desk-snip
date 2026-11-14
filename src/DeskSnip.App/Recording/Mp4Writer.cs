using System;
using System.Runtime.InteropServices;
using DeskSnip.App.Windows;
using DeskSnip.Core;

namespace DeskSnip.App.Recording;

/// <summary>Windows 原生 H.264 / MP4。实例必须在同一工作线程创建、写入、释放。</summary>
public sealed class Mp4Writer : IDisposable
{
    private IntPtr sink;
    private readonly uint streamIndex;
    private readonly int width;
    private readonly int height;
    private readonly byte[] nv12;
    private bool started;
    private bool comInitialized;
    private bool complete;
    private long lastTimestamp = -1;
    public Mp4Writer(string path, int width, int height, int fps, int quality = 80)
    {
        if (width < 2 || height < 2 || width % 2 != 0 || height % 2 != 0 || width > 4096 || height > 4096 || (long)width * height > 8_294_400)
            throw new ArgumentException(L.Get("MP4 选区需为偶数尺寸，且不超过 4096 边长和 829 万像素，请缩小选区。"));
        if (fps < 1 || fps > 60 || quality < 1 || quality > 100) throw new ArgumentOutOfRangeException(nameof(fps));
        this.width = width; this.height = height; nv12 = new byte[width * height * 3 / 2];
        IntPtr attributes = IntPtr.Zero, output = IntPtr.Zero, input = IntPtr.Zero;
        try
        {
            Mf.Check(Native.CoInitializeEx(IntPtr.Zero, 0)); comInitialized = true;
            Mf.Check(Mf.MFStartup(0x00020070, 0)); started = true;
            Mf.Check(Mf.MFCreateAttributes(out attributes, 2));
            Mf.Uint(attributes, "08b845d8-2b74-4afe-9d53-be16d2d5ae4f", 1); // DISABLE_THROTTLING：节奏由录制循环控制。
            Mf.Check(Mf.MFCreateSinkWriterFromURL(path, IntPtr.Zero, attributes, out sink));
            Mf.Check(Mf.MFCreateMediaType(out output)); SetVideoType(output, "34363248-0000-0010-8000-00aa00389b71", fps);
            Mf.Uint(output, "20332624-fb0d-4d9e-bd0d-cbf6786c102e", (uint)Math.Max(1_000_000, Math.Min(40_000_000, width * (double)height * fps * quality / 400)));
            Mf.Check(Mf.Method<Mf.AddStream>(sink, 3)(sink, output, out streamIndex));
            Mf.Check(Mf.MFCreateMediaType(out input)); SetVideoType(input, "3231564e-0000-0010-8000-00aa00389b71", fps); // NV12
            Mf.Uint(input, "644b4e48-1e02-4516-b0eb-c01ca9d49ac6", (uint)width);
            Mf.Check(Mf.Method<Mf.SetInput>(sink, 4)(sink, streamIndex, input, IntPtr.Zero));
            Mf.Check(Mf.Method<Mf.Simple>(sink, 5)(sink));
        }
        catch { Dispose(); throw; }
        finally { Mf.Release(ref input); Mf.Release(ref output); Mf.Release(ref attributes); }
    }
    private void SetVideoType(IntPtr type, string subtype, int fps)
    {
        Mf.GuidValue(type, "48eba18e-f8c9-4687-bf11-0a74c9f96a8f", "73646976-0000-0010-8000-00aa00389b71");
        Mf.GuidValue(type, "f7e34c9a-42e8-4714-b74b-cb29d72c35e5", subtype);
        Mf.Ulong(type, "1652c33d-d6b2-4012-b834-72030849a37d", (ulong)(uint)width << 32 | (uint)height);
        Mf.Ulong(type, "c459a2e8-3d2c-4e44-b132-fee5156c7bb0", (ulong)(uint)fps << 32 | 1);
        Mf.Ulong(type, "c6376a1e-8d0a-4027-be45-6d9a0ad39bb6", 1ul << 32 | 1);
        Mf.Uint(type, "e2724bb8-e676-4806-b4b2-a8d6efb44ccd", 2); // progressive
        Mf.Uint(type, "3e23d450-2c75-4d25-a00e-b91670d12327", 2); // MF_MT_YUV_MATRIX：BT.601
        Mf.Uint(type, "c21b8ee5-b956-4071-8daf-325edf5cab11", 2); // MF_MT_VIDEO_NOMINAL_RANGE：16-235
    }
    public void WriteFrame(byte[] bgra, long timestampTicks, long durationTicks)
    {
        if (sink == IntPtr.Zero || complete) throw new ObjectDisposedException(nameof(Mp4Writer));
        if (bgra.Length != width * height * 4 || durationTicks <= 0 || timestampTicks < 0 || timestampTicks <= lastTimestamp)
            throw new ArgumentException(TextCatalog.Get("帧或时间戳无效。"));
        ConvertNv12(bgra);
        IntPtr sample = IntPtr.Zero, buffer = IntPtr.Zero;
        try
        {
            Mf.Check(Mf.MFCreateMemoryBuffer(nv12.Length, out buffer));
            Mf.Check(Mf.Method<Mf.LockBuffer>(buffer, 3)(buffer, out var pointer, out _, out _));
            try { Marshal.Copy(nv12, 0, pointer, nv12.Length); }
            finally { Mf.Check(Mf.Method<Mf.Simple>(buffer, 4)(buffer)); }
            Mf.Check(Mf.Method<Mf.SetLength>(buffer, 6)(buffer, nv12.Length));
            Mf.Check(Mf.MFCreateSample(out sample));
            Mf.Check(Mf.Method<Mf.SetPointer>(sample, 42)(sample, buffer));
            Mf.Check(Mf.Method<Mf.SetLong>(sample, 36)(sample, timestampTicks));
            Mf.Check(Mf.Method<Mf.SetLong>(sample, 38)(sample, durationTicks));
            Mf.Check(Mf.Method<Mf.WriteSample>(sink, 6)(sink, streamIndex, sample));
            lastTimestamp = timestampTicks;
        }
        finally { Mf.Release(ref sample); Mf.Release(ref buffer); }
    }
    private void ConvertNv12(byte[] pixels)
    {
        var chroma = width * height;
        for (var y = 0; y < height; y += 2)
            for (var x = 0; x < width; x += 2)
            {
                var rSum = 0; var gSum = 0; var bSum = 0;
                for (var row = 0; row < 2; row++) for (var col = 0; col < 2; col++)
                {
                    var index = (y + row) * width + x + col; var offset = index * 4;
                    var b = pixels[offset]; var g = pixels[offset + 1]; var r = pixels[offset + 2];
                    nv12[index] = Clamp(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);
                    rSum += r; gSum += g; bSum += b;
                }
                var rAvg = rSum / 4; var gAvg = gSum / 4; var bAvg = bSum / 4;
                var uv = chroma + y / 2 * width + x;
                nv12[uv] = Clamp(((-38 * rAvg - 74 * gAvg + 112 * bAvg + 128) >> 8) + 128);
                nv12[uv + 1] = Clamp(((112 * rAvg - 94 * gAvg - 18 * bAvg + 128) >> 8) + 128);
            }
    }
    private static byte Clamp(int value) => (byte)Math.Max(0, Math.Min(255, value));
    public void Complete()
    {
        if (complete) return;
        if (sink == IntPtr.Zero) throw new ObjectDisposedException(nameof(Mp4Writer));
        Mf.Check(Mf.Method<Mf.Simple>(sink, 11)(sink)); complete = true;
    }
    public void Dispose()
    {
        Mf.Release(ref sink);
        if (started) { Mf.MFShutdown(); started = false; }
        if (comInitialized) { Native.CoUninitialize(); comInitialized = false; }
    }
}
