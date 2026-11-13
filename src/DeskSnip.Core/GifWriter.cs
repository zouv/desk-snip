using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DeskSnip.Core;

/// <summary>流式 GIF89a，固定 RGB332 调色板；只保留当前帧，不积累录制内存。</summary>
public sealed class GifWriter : IDisposable
{
    private readonly BinaryWriter writer;
    private readonly int width;
    private readonly int height;
    private bool disposed;
    public GifWriter(Stream stream, int width, int height, bool leaveOpen = false)
    {
        if (width < 1 || height < 1 || width > 65535 || height > 65535) throw new ArgumentOutOfRangeException(nameof(width));
        this.width = width; this.height = height;
        writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen);
        writer.Write(Encoding.ASCII.GetBytes("GIF89a"));
        writer.Write((ushort)width); writer.Write((ushort)height);
        writer.Write((byte)0xf7); writer.Write((byte)0); writer.Write((byte)0);
        for (var i = 0; i < 256; i++)
        {
            writer.Write((byte)(((i >> 5) & 7) * 255 / 7));
            writer.Write((byte)(((i >> 2) & 7) * 255 / 7));
            writer.Write((byte)((i & 3) * 255 / 3));
        }
        writer.Write(new byte[] { 0x21, 0xff, 11 });
        writer.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
        writer.Write(new byte[] { 3, 1, 0, 0, 0 });
    }

    public void WriteFrame(byte[] bgra, TimeSpan duration)
    {
        if (disposed) throw new ObjectDisposedException(nameof(GifWriter));
        if (bgra == null || bgra.Length != checked(width * height * 4)) throw new ArgumentException("Frame size mismatch.", nameof(bgra));
        var remaining = Math.Max(1L, (long)Math.Round(duration.TotalMilliseconds / 10));
        // GIF 单帧 delay 为 ushort，超长暂停后的静帧拆开写。
        do
        {
            var delay = (ushort)Math.Min(remaining, ushort.MaxValue);
            writer.Write(new byte[] { 0x21, 0xf9, 4, 4 });
            writer.Write(delay); writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((byte)0x2c); writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write((ushort)width); writer.Write((ushort)height); writer.Write((byte)0);
            writer.Write((byte)8);
            WriteLzw(bgra);
            remaining -= delay;
        } while (remaining > 0);
    }

    private void WriteLzw(byte[] pixels)
    {
        var dictionary = new Dictionary<int, int>(4096);
        var nextCode = 258;
        var bits = 9;
        var block = new byte[255];
        var blockSize = 0;
        uint bitBuffer = 0;
        var bitCount = 0;
        void Byte(byte value)
        {
            block[blockSize++] = value;
            if (blockSize == 255) { writer.Write((byte)blockSize); writer.Write(block, 0, blockSize); blockSize = 0; }
        }
        void Code(int code)
        {
            bitBuffer |= (uint)code << bitCount;
            bitCount += bits;
            while (bitCount >= 8) { Byte((byte)bitBuffer); bitBuffer >>= 8; bitCount -= 8; }
        }
        int Color(int offset) => (pixels[offset + 2] & 0xe0) | ((pixels[offset + 1] >> 3) & 0x1c) | (pixels[offset] >> 6);
        Code(256);
        var prefix = Color(0);
        for (var offset = 4; offset < pixels.Length; offset += 4)
        {
            var color = Color(offset);
            var key = prefix << 8 | color;
            if (dictionary.TryGetValue(key, out var existing)) { prefix = existing; continue; }
            Code(prefix);
            // Decoder 的字典比 encoder 晚一个条目，先写边界 code 再扩位。
            if (nextCode == 1 << bits && bits < 12) bits++;
            if (nextCode < 4096) dictionary.Add(key, nextCode++);
            else { Code(256); dictionary.Clear(); nextCode = 258; bits = 9; }
            prefix = color;
        }
        Code(prefix);
        if (nextCode == 1 << bits && bits < 12) bits++;
        Code(257);
        if (bitCount > 0) Byte((byte)bitBuffer);
        if (blockSize > 0) { writer.Write((byte)blockSize); writer.Write(block, 0, blockSize); }
        writer.Write((byte)0);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { writer.Write((byte)0x3b); }
        finally { writer.Dispose(); }
    }
}
