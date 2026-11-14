using System;
using System.IO;
using System.Text;

namespace DeskSnip.Core;

public static class WaveRecovery
{
    /// <summary>修复崩溃前未更新的 RIFF/data 长度；不改 PCM 内容，失败保留原文件。</summary>
    public static bool Repair(string path)
    {
        var target = AtomicFile.ValidateTarget(path); long dataOffset = -1; long dataLength = 0; uint riffLength;
        using (var file = File.OpenRead(target)) using (var reader = new BinaryReader(file, Encoding.ASCII))
        {
            if (file.Length < 44 || file.Length > uint.MaxValue) throw new ArgumentException(TextCatalog.Get("音轨不是受支持的 WAV 文件。"));
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") throw new ArgumentException(TextCatalog.Get("音轨不是 RIFF WAV。"));
            riffLength = reader.ReadUInt32(); if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") throw new ArgumentException(TextCatalog.Get("音轨不是 WAV。"));
            while (file.Position <= file.Length - 8)
            {
                var name = Encoding.ASCII.GetString(reader.ReadBytes(4)); var size = reader.ReadUInt32();
                if (name == "data") { dataOffset = file.Position - 4; dataLength = file.Length - file.Position; if (size == dataLength && riffLength == file.Length - 8) return false; break; }
                if (size > file.Length - file.Position) throw new ArgumentException(TextCatalog.Get("WAV 文件头无效。"));
                file.Position += size + (size % 2);
            }
            if (dataOffset < 0) throw new ArgumentException(TextCatalog.Get("WAV 没有可恢复的音频块。"));
        }
        AtomicFile.Write(target, output =>
        {
            using (var input = File.OpenRead(target)) input.CopyTo(output);
            using (var writer = new BinaryWriter(output, Encoding.ASCII, true))
            { output.Position = 4; writer.Write(checked((uint)(output.Length - 8))); output.Position = dataOffset; writer.Write(checked((uint)dataLength)); }
        }); return true;
    }
}
