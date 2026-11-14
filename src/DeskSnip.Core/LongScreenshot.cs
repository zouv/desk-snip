using System;
using System.Collections.Generic;
using System.Linq;

namespace DeskSnip.Core;

public sealed class PixelBuffer
{
    internal byte[] Data { get; }
    public int Width { get; }
    public int Height { get; }
    public PixelBuffer(int width, int height, byte[] bgra)
    {
        if (width < 1 || height < 1 || (long)width * height > 50_000_000 || bgra.Length != (long)width * height * 4) throw new ArgumentException(TextCatalog.Get("图像尺寸或像素数据无效，长图最多 5000 万像素。"));
        Width = width; Height = height; Data = (byte[])bgra.Clone();
    }
    public byte[] CopyPixels() => (byte[])Data.Clone();
    public PixelBuffer Crop(PixelRect area)
    {
        if (area.IsEmpty || !area.Intersect(new PixelRect(0, 0, Width, Height)).Equals(area)) throw new ArgumentException(TextCatalog.Get("裁剪区域超出图片。"));
        var data = new byte[checked(area.Width * area.Height * 4)];
        for (var y = 0; y < area.Height; y++) Buffer.BlockCopy(Data, ((area.Y + y) * Width + area.X) * 4, data, y * area.Width * 4, area.Width * 4);
        return new PixelBuffer(area.Width, area.Height, data);
    }
}

public enum StitchAxis { Vertical, Horizontal }
public sealed class StitchMatch
{
    internal StitchMatch(bool success, int overlap, double error, string message) { Success = success; Overlap = overlap; Error = error; Message = message; }
    public bool Success { get; }
    public int Overlap { get; }
    public double Error { get; }
    public string Message { get; }
}

/// <summary>仅在重叠可信时提交新帧。失败不改已有图片，支持手工重叠和撤回最后一步。</summary>
public sealed class LongScreenshot
{
    private PixelBuffer last;
    private (PixelBuffer Image, PixelBuffer Last, int Offset)? previous;
    public LongScreenshot(PixelBuffer first, StitchAxis axis) { Image = last = first; Axis = axis; }
    public StitchAxis Axis { get; }
    public PixelBuffer Image { get; private set; }
    public int CurrentOffset { get; private set; }
    public bool CanUndo => previous.HasValue;
    public PixelRect CurrentArea => Axis == StitchAxis.Vertical ? new PixelRect(0, CurrentOffset, last.Width, last.Height) : new PixelRect(CurrentOffset, 0, last.Width, last.Height);
    public StitchMatch Append(PixelBuffer next, int direction = 1, int? manualOverlap = null)
    {
        if (next.Width != last.Width || next.Height != last.Height || (direction != 1 && direction != -1)) throw new ArgumentException(TextCatalog.Get("连续帧的大小必须相同，方向需要为正向或反向。"));
        var size = Axis == StitchAxis.Vertical ? last.Height : last.Width;
        StitchMatch match;
        if (manualOverlap.HasValue)
        {
            if (manualOverlap.Value < 1 || manualOverlap.Value >= size) throw new ArgumentOutOfRangeException(nameof(manualOverlap));
            match = new StitchMatch(true, manualOverlap.Value, 0, TextCatalog.Get("已按手工重叠拼接。"));
        }
        else match = FindOverlap(last, next, Axis, direction);
        if (!match.Success) return match;
        var shift = size - match.Overlap; var nextOffset = CurrentOffset + direction * shift;
        var combinedSize = Axis == StitchAxis.Vertical ? Image.Height : Image.Width;
        var first = Math.Min(0, nextOffset); var end = Math.Max(combinedSize, nextOffset + size);
        var width = Axis == StitchAxis.Vertical ? Image.Width : end - first; var height = Axis == StitchAxis.Vertical ? end - first : Image.Height;
        if ((long)width * height > 50_000_000) return new StitchMatch(false, match.Overlap, match.Error, TextCatalog.Get("长图达到 5000 万像素上限，请先保存再开始下一段。"));
        var output = new byte[checked(width * height * 4)];
        Copy(Image, output, width, Axis == StitchAxis.Horizontal ? -first : 0, Axis == StitchAxis.Vertical ? -first : 0);
        Copy(next, output, width, Axis == StitchAxis.Horizontal ? nextOffset - first : 0, Axis == StitchAxis.Vertical ? nextOffset - first : 0);
        previous = (Image, last, CurrentOffset); Image = new PixelBuffer(width, height, output); last = next; CurrentOffset = nextOffset - first; return match;
    }
    public void Undo()
    {
        if (!previous.HasValue) return; Image = previous.Value.Image; last = previous.Value.Last; CurrentOffset = previous.Value.Offset; previous = null;
    }
    public void TrimBeforeCurrent() => Trim(true);
    public void TrimAfterCurrent() => Trim(false);
    private void Trim(bool before)
    {
        var size = Axis == StitchAxis.Vertical ? Image.Height : Image.Width; var frameSize = Axis == StitchAxis.Vertical ? last.Height : last.Width;
        var start = before ? CurrentOffset : 0; var end = before ? size : CurrentOffset + frameSize;
        if (start == 0 && end == size) return;
        var area = Axis == StitchAxis.Vertical ? new PixelRect(0, start, Image.Width, end - start) : new PixelRect(start, 0, end - start, Image.Height);
        var cropped = Image.Crop(area); previous = (Image, last, CurrentOffset); Image = cropped; CurrentOffset -= start;
    }
    private static void Copy(PixelBuffer source, byte[] target, int width, int x, int y)
    {
        for (var row = 0; row < source.Height; row++) Buffer.BlockCopy(source.Data, row * source.Width * 4, target, ((row + y) * width + x) * 4, source.Width * 4);
    }
    public static StitchMatch FindOverlap(PixelBuffer first, PixelBuffer next, StitchAxis axis, int direction)
    {
        if (first.Width != next.Width || first.Height != next.Height || (direction != 1 && direction != -1)) throw new ArgumentException(TextCatalog.Get("帧尺寸或方向无效。"));
        if (Score(first, next, axis, direction, 0) < .5) return new StitchMatch(false, 0, 0, TextCatalog.Get("尚未检测到滚动，请滚动后再采集。"));
        var size = axis == StitchAxis.Vertical ? first.Height : first.Width;
        var minimum = Math.Max(4, Math.Min(32, size / 5));
        var candidates = new List<(int Shift, double Error)>();
        for (var shift = 1; shift <= size - minimum; shift++) candidates.Add((shift, Score(first, next, axis, direction, shift)));
        if (candidates.Count == 0) return new StitchMatch(false, 0, 255, TextCatalog.Get("选区太小，无法匹配重叠。"));
        var best = candidates.OrderBy(c => c.Error).First();
        var alternative = candidates.Where(c => Math.Abs(c.Shift - best.Shift) > 2).Select(c => c.Error).DefaultIfEmpty(255).Min();
        if (best.Error > 7 || alternative - best.Error < 1.5) return new StitchMatch(false, 0, best.Error, TextCatalog.Get("重叠匹配不可靠。已有长图保留；回到最后成功位置或填写重叠像素继续。"));
        return new StitchMatch(true, size - best.Shift, best.Error, TextCatalog.Get("重叠匹配成功。"));
    }
    private static double Score(PixelBuffer a, PixelBuffer b, StitchAxis axis, int direction, int shift)
    {
        var size = axis == StitchAxis.Vertical ? a.Height : a.Width; var cross = axis == StitchAxis.Vertical ? a.Width : a.Height;
        long error = 0; var samples = 0;
        var alongSamples = Math.Min(28, size - shift); var crossSamples = Math.Min(20, cross);
        for (var i = 0; i < alongSamples; i++) for (var j = 0; j < crossSamples; j++)
        {
            var p = (int)((i + .5) * (size - shift) / alongSamples); var q = (int)((j + .5) * cross / crossSamples);
            var pa = p + (direction > 0 ? shift : 0); var pb = p + (direction < 0 ? shift : 0);
            var ia = axis == StitchAxis.Vertical ? (pa * a.Width + q) * 4 : (q * a.Width + pa) * 4;
            var ib = axis == StitchAxis.Vertical ? (pb * b.Width + q) * 4 : (q * b.Width + pb) * 4;
            for (var channel = 0; channel < 3; channel++) { error += Math.Abs(a.Data[ia + channel] - b.Data[ib + channel]); samples++; }
        }
        return samples == 0 ? 255 : error / (double)samples;
    }
}
