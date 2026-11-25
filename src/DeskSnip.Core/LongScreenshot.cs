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
    internal StitchMatch(bool success, int overlap, double error, string message, int direction = 0, bool unchanged = false)
    {
        Success = success; Overlap = overlap; Error = error; Message = message; Direction = direction; Unchanged = unchanged; CommittedFrames = success ? 1 : 0;
    }
    public bool Success { get; }
    public int Overlap { get; }
    public double Error { get; }
    public string Message { get; }
    /// <summary>实际匹配方向：1 为向下/向右，-1 为向上/向左，失败时为 0。</summary>
    public int Direction { get; }
    /// <summary>画面与已有锚点一致，不代表匹配失败。</summary>
    public bool Unchanged { get; }
    public int CommittedFrames { get; internal set; }
}

/// <summary>识别选区内沿拼接轴移动的局部视口，并将固定边缘只合成一次。</summary>
public static class LongScreenshotViewport
{
    public static PixelRect? Detect(PixelBuffer first, PixelBuffer next, StitchAxis axis)
    {
        if (first.Width != next.Width || first.Height != next.Height) throw new ArgumentException(TextCatalog.Get("连续帧的大小必须相同，方向需要为自动、正向或反向。"));
        var size = axis == StitchAxis.Vertical ? first.Height : first.Width;
        var cross = axis == StitchAxis.Vertical ? first.Width : first.Height;
        var changed = new bool[size]; var samples = Math.Min(64, cross);
        for (var line = 0; line < size; line++)
        {
            long error = 0;
            for (var i = 0; i < samples; i++)
            {
                var point = (int)((i + .5) * cross / samples);
                var a = axis == StitchAxis.Vertical ? (line * first.Width + point) * 4 : (point * first.Width + line) * 4;
                var b = axis == StitchAxis.Vertical ? (line * next.Width + point) * 4 : (point * next.Width + line) * 4;
                error += Math.Abs(first.Data[a] - next.Data[b]) + Math.Abs(first.Data[a + 1] - next.Data[b + 1]) + Math.Abs(first.Data[a + 2] - next.Data[b + 2]);
            }
            changed[line] = error / (double)(samples * 3) > .75;
        }
        var bestStart = 0; var bestEnd = -1; var bestChanges = 0; var gapLimit = Math.Max(2, Math.Min(12, size / 32));
        for (var line = 0; line < size;)
        {
            if (!changed[line]) { line++; continue; }
            var start = line; var lastChanged = line; var changes = 0; var gap = 0;
            for (; line < size && gap <= gapLimit; line++)
            {
                if (!changed[line]) { gap++; continue; }
                lastChanged = line; changes++; gap = 0;
            }
            if (lastChanged - start > bestEnd - bestStart) { bestStart = start; bestEnd = lastChanged; bestChanges = changes; }
        }
        var length = bestEnd - bestStart + 1;
        if (bestEnd < 0) return null;
        if (bestStart <= gapLimit) bestStart = 0;
        if (size - 1 - bestEnd <= gapLimit) bestEnd = size - 1;
        length = bestEnd - bestStart + 1;
        if (length >= size || length < Math.Max(16, size / 4) || bestChanges < Math.Max(4, length / 8)) return null;
        var area = axis == StitchAxis.Vertical ? new PixelRect(0, bestStart, first.Width, length) : new PixelRect(bestStart, 0, length, first.Height);
        var probe = LongScreenshot.FindOverlap(first.Crop(area), next.Crop(area), axis, 0);
        if (!probe.Success) return area;
        var shift = length - probe.Overlap; var firstLine = axis == StitchAxis.Vertical ? area.Y : area.X; var lastLine = firstLine + length - 1;
        bool MovesTogether(int line)
        {
            var a = line + (probe.Direction > 0 ? shift : 0); var b = line + (probe.Direction < 0 ? shift : 0);
            if (a < 0 || b < 0 || a >= size || b >= size) return false;
            long error = 0;
            for (var i = 0; i < samples; i++)
            {
                var point = (int)((i + .5) * cross / samples);
                var ia = axis == StitchAxis.Vertical ? (a * first.Width + point) * 4 : (point * first.Width + a) * 4;
                var ib = axis == StitchAxis.Vertical ? (b * next.Width + point) * 4 : (point * next.Width + b) * 4;
                error += Math.Abs(first.Data[ia] - next.Data[ib]) + Math.Abs(first.Data[ia + 1] - next.Data[ib + 1]) + Math.Abs(first.Data[ia + 2] - next.Data[ib + 2]);
            }
            return error / (double)(samples * 3) <= .75;
        }
        while (firstLine > 0 && MovesTogether(firstLine - 1)) firstLine--;
        while (lastLine < size - 1 && MovesTogether(lastLine + 1)) lastLine++;
        return axis == StitchAxis.Vertical ? new PixelRect(0, firstLine, first.Width, lastLine - firstLine + 1) : new PixelRect(firstLine, 0, lastLine - firstLine + 1, first.Height);
    }
    public static PixelBuffer Compose(PixelBuffer initial, PixelRect viewport, PixelBuffer content, StitchAxis axis)
    {
        var vertical = axis == StitchAxis.Vertical;
        var valid = !viewport.IsEmpty && (vertical
            ? content.Width == initial.Width && viewport.X == 0 && viewport.Width == initial.Width && viewport.Y >= 0 && viewport.Bottom <= initial.Height
            : content.Height == initial.Height && viewport.Y == 0 && viewport.Height == initial.Height && viewport.X >= 0 && viewport.Right <= initial.Width);
        if (!valid) throw new ArgumentException(TextCatalog.Get("局部滚动视口或拼接结果尺寸无效。"));
        var width = vertical ? initial.Width : initial.Width - viewport.Width + content.Width;
        var height = vertical ? initial.Height - viewport.Height + content.Height : initial.Height;
        var output = new byte[checked(width * height * 4)];
        if (vertical)
        {
            CopyRows(initial, 0, 0, viewport.Y, output, width, 0);
            CopyRows(content, 0, 0, content.Height, output, width, viewport.Y);
            CopyRows(initial, viewport.Bottom, 0, initial.Height - viewport.Bottom, output, width, viewport.Y + content.Height);
        }
        else
        {
            CopyColumns(initial, 0, 0, viewport.X, output, width, 0);
            CopyColumns(content, 0, 0, content.Width, output, width, viewport.X);
            CopyColumns(initial, viewport.Right, 0, initial.Width - viewport.Right, output, width, viewport.X + content.Width);
        }
        return new PixelBuffer(width, height, output);
    }
    private static void CopyRows(PixelBuffer source, int sourceY, int sourceX, int rows, byte[] target, int targetWidth, int targetY)
    {
        for (var y = 0; y < rows; y++) Buffer.BlockCopy(source.Data, ((sourceY + y) * source.Width + sourceX) * 4, target, (targetY + y) * targetWidth * 4, source.Width * 4);
    }
    private static void CopyColumns(PixelBuffer source, int sourceX, int sourceY, int columns, byte[] target, int targetWidth, int targetX)
    {
        for (var y = 0; y < source.Height; y++) Buffer.BlockCopy(source.Data, ((sourceY + y) * source.Width + sourceX) * 4, target, ((sourceY + y) * targetWidth + targetX) * 4, columns * 4);
    }
}

/// <summary>只在重叠可信时提交新帧。失败帧短暂保留，后续可通过其它锚点恢复；支持手工重叠和撤回。</summary>
public sealed class LongScreenshot
{
    private const int AnchorLimit = 6, PendingLimit = 12;
    private sealed class Anchor
    {
        internal Anchor(PixelBuffer frame, int offset) { Frame = frame; Offset = offset; }
        internal PixelBuffer Frame { get; }
        internal int Offset { get; set; }
    }
    private sealed class Snapshot
    {
        internal Snapshot(PixelBuffer image, PixelBuffer lastFrame, int offset, Anchor[] savedAnchors, PixelBuffer[] savedPending)
        { Image = image; Last = lastFrame; Offset = offset; Anchors = savedAnchors; Pending = savedPending; }
        internal PixelBuffer Image { get; }
        internal PixelBuffer Last { get; }
        internal int Offset { get; }
        internal Anchor[] Anchors { get; }
        internal PixelBuffer[] Pending { get; }
    }
    private PixelBuffer last;
    private readonly object gate = new object();
    private List<Anchor> anchors;
    private List<PixelBuffer> pending = new List<PixelBuffer>();
    private Snapshot? previous;
    public LongScreenshot(PixelBuffer first, StitchAxis axis)
    {
        Image = last = first; Axis = axis; anchors = new List<Anchor> { new Anchor(first, 0) };
    }
    public StitchAxis Axis { get; }
    public PixelBuffer Image { get; private set; }
    public int CurrentOffset { get; private set; }
    public int PendingCount { get { lock (gate) return pending.Count; } }
    public void DiscardPending() { lock (gate) pending.Clear(); }
    public bool CanUndo => previous != null;
    public PixelRect CurrentArea => Axis == StitchAxis.Vertical ? new PixelRect(0, CurrentOffset, last.Width, last.Height) : new PixelRect(CurrentOffset, 0, last.Width, last.Height);
    public StitchMatch Append(PixelBuffer next, int direction = 1, int? manualOverlap = null)
    {
        lock (gate)
        {
            ValidateFrame(next, direction);
            var size = Axis == StitchAxis.Vertical ? last.Height : last.Width;
            if (manualOverlap.HasValue)
            {
                if (manualOverlap.Value < 1 || manualOverlap.Value >= size) throw new ArgumentOutOfRangeException(nameof(manualOverlap));
                var matchedDirection = direction;
                if (matchedDirection == 0)
                {
                    var shift = size - manualOverlap.Value;
                    matchedDirection = Score(last, next, Axis, 1, shift) <= Score(last, next, Axis, -1, shift) ? 1 : -1;
                }
                var manual = new StitchMatch(true, manualOverlap.Value, 0, TextCatalog.Get("已按手工重叠拼接。"), matchedDirection);
                var committed = Commit(anchors[anchors.Count - 1], next, manual);
                if (!committed.Success) return committed;
                var processed = ProcessPending(direction);
                manual.CommittedFrames += processed.CommittedFrames;
                return manual;
            }
            if (pending.Count >= PendingLimit) return new StitchMatch(false, 0, 255, TextCatalog.Get("连续画面之间缺少可靠重叠，请滚回最后成功位置后继续。"));
            pending.Add(next);
            return ProcessPending(direction);
        }
    }
    private StitchMatch ProcessPending(int direction)
    {
        var committed = 0; StitchMatch? lastMatch = null, lastFailure = null;
        while (pending.Count > 0)
        {
            var progressed = false;
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                var frame = pending[i];
                if (!TryFindAnchor(frame, direction, out var anchor, out var match)) { lastFailure = match; continue; }
                pending.RemoveAt(i);
                if (match.Unchanged) { progressed = true; break; }
                var result = Commit(anchor!, frame, match);
                if (!result.Success) { pending.Insert(i, frame); return result; }
                committed++; lastMatch = result; progressed = true; break;
            }
            if (!progressed) break;
        }
        if (committed > 0 && lastMatch != null) { lastMatch.CommittedFrames = committed; return lastMatch; }
        return lastFailure ?? new StitchMatch(false, 0, 0, TextCatalog.Get("尚未检测到滚动，请滚动后再采集。"), unchanged: true);
    }
    private bool TryFindAnchor(PixelBuffer frame, int direction, out Anchor? anchor, out StitchMatch match)
    {
        StitchMatch? failure = null;
        for (var i = anchors.Count - 1; i >= 0; i--)
        {
            var candidate = anchors[i]; match = FindOverlap(candidate.Frame, frame, Axis, direction);
            if (match.Success || match.Unchanged) { anchor = candidate; return true; }
            failure ??= match;
        }
        anchor = null; match = failure ?? new StitchMatch(false, 0, 255, TextCatalog.Get("重叠匹配不可靠。")); return false;
    }
    private StitchMatch Commit(Anchor anchor, PixelBuffer next, StitchMatch match)
    {
        var size = Axis == StitchAxis.Vertical ? next.Height : next.Width;
        var shift = size - match.Overlap; var nextOffset = anchor.Offset + match.Direction * shift;
        var combinedSize = Axis == StitchAxis.Vertical ? Image.Height : Image.Width;
        var first = Math.Min(0, nextOffset); var end = Math.Max(combinedSize, nextOffset + size);
        var width = Axis == StitchAxis.Vertical ? Image.Width : end - first; var height = Axis == StitchAxis.Vertical ? end - first : Image.Height;
        if ((long)width * height > 50_000_000) return new StitchMatch(false, match.Overlap, match.Error, TextCatalog.Get("长图达到 5000 万像素上限，请先保存再开始下一段。"));
        var output = new byte[checked(width * height * 4)];
        var oldX = Axis == StitchAxis.Horizontal ? -first : 0; var oldY = Axis == StitchAxis.Vertical ? -first : 0;
        Copy(Image, output, width, oldX, oldY);
        CopyMissing(next, output, width, Axis == StitchAxis.Horizontal ? nextOffset - first : 0, Axis == StitchAxis.Vertical ? nextOffset - first : 0, oldX, oldY, Image.Width, Image.Height);
        previous = TakeSnapshot();
        if (first < 0) foreach (var existing in anchors) existing.Offset -= first;
        Image = new PixelBuffer(width, height, output); last = next; CurrentOffset = nextOffset - first;
        anchors.Add(new Anchor(next, CurrentOffset));
        if (anchors.Count > AnchorLimit) anchors.RemoveAt(0);
        return match;
    }
    public void Undo()
    {
        lock (gate)
        {
            if (previous == null) return;
            Image = previous.Image; last = previous.Last; CurrentOffset = previous.Offset;
            anchors = previous.Anchors.ToList(); pending = previous.Pending.ToList(); previous = null;
        }
    }
    public void TrimBeforeCurrent() => Trim(true);
    public void TrimAfterCurrent() => Trim(false);
    private void Trim(bool before)
    {
        lock (gate)
        {
            var size = Axis == StitchAxis.Vertical ? Image.Height : Image.Width; var frameSize = Axis == StitchAxis.Vertical ? last.Height : last.Width;
            var start = before ? CurrentOffset : 0; var end = before ? size : CurrentOffset + frameSize;
            if (start == 0 && end == size) return;
            var area = Axis == StitchAxis.Vertical ? new PixelRect(0, start, Image.Width, end - start) : new PixelRect(start, 0, end - start, Image.Height);
            var cropped = Image.Crop(area); previous = TakeSnapshot(); Image = cropped; CurrentOffset -= start;
            anchors = new List<Anchor> { new Anchor(last, CurrentOffset) }; pending.Clear();
        }
    }
    private Snapshot TakeSnapshot() => new Snapshot(Image, last, CurrentOffset, anchors.Select(anchor => new Anchor(anchor.Frame, anchor.Offset)).ToArray(), pending.ToArray());
    private void ValidateFrame(PixelBuffer next, int direction)
    {
        if (next.Width != last.Width || next.Height != last.Height || direction < -1 || direction > 1) throw new ArgumentException(TextCatalog.Get("连续帧的大小必须相同，方向需要为自动、正向或反向。"));
    }
    private static void Copy(PixelBuffer source, byte[] target, int width, int x, int y)
    {
        for (var row = 0; row < source.Height; row++) Buffer.BlockCopy(source.Data, row * source.Width * 4, target, ((row + y) * width + x) * 4, source.Width * 4);
    }
    private static void CopyMissing(PixelBuffer source, byte[] target, int targetWidth, int x, int y, int oldX, int oldY, int oldWidth, int oldHeight)
    {
        for (var row = 0; row < source.Height; row++)
        {
            var targetRow = y + row;
            if (targetRow < oldY || targetRow >= oldY + oldHeight)
            {
                Buffer.BlockCopy(source.Data, row * source.Width * 4, target, (targetRow * targetWidth + x) * 4, source.Width * 4);
                continue;
            }
            var left = Math.Max(0, Math.Min(source.Width, oldX - x));
            if (left > 0) Buffer.BlockCopy(source.Data, row * source.Width * 4, target, (targetRow * targetWidth + x) * 4, left * 4);
            var right = Math.Max(0, Math.Min(source.Width, oldX + oldWidth - x));
            if (right < source.Width) Buffer.BlockCopy(source.Data, (row * source.Width + right) * 4, target, (targetRow * targetWidth + x + right) * 4, (source.Width - right) * 4);
        }
    }
    public static StitchMatch FindOverlap(PixelBuffer first, PixelBuffer next, StitchAxis axis, int direction)
    {
        if (first.Width != next.Width || first.Height != next.Height || direction < -1 || direction > 1) throw new ArgumentException(TextCatalog.Get("帧尺寸或方向无效。"));
        var unchanged = Score(first, next, axis, 1, 0);
        if (unchanged < .02) return new StitchMatch(false, 0, unchanged, TextCatalog.Get("尚未检测到滚动，请滚动后再采集。"), unchanged: true);
        var size = axis == StitchAxis.Vertical ? first.Height : first.Width;
        var minimum = Math.Max(4, Math.Min(16, size / 20));
        var candidates = new List<(int Direction, int Shift, double Error)>();
        var directions = direction == 0 ? new[] { 1, -1 } : new[] { direction };
        foreach (var candidateDirection in directions)
            for (var shift = 1; shift <= size - minimum; shift++) candidates.Add((candidateDirection, shift, Score(first, next, axis, candidateDirection, shift)));
        if (candidates.Count == 0) return new StitchMatch(false, 0, 255, TextCatalog.Get("选区太小，无法匹配重叠。"));
        var best = candidates.OrderBy(c => c.Error).Take(4)
            .Select(c => (c.Direction, c.Shift, Error: DenseScore(first, next, axis, c.Direction, c.Shift))).OrderBy(c => c.Error).First();
        var alternative = candidates.Where(c => c.Direction != best.Direction || Math.Abs(c.Shift - best.Shift) > 2).Select(c => c.Error).DefaultIfEmpty(255).Min();
        if (unchanged - best.Error < .05) return new StitchMatch(false, 0, best.Error, TextCatalog.Get("尚未检测到滚动，请滚动后再采集。"), unchanged: true);
        if (best.Error > 7 || alternative - best.Error < 1.5) return new StitchMatch(false, 0, best.Error, TextCatalog.Get("重叠匹配不可靠。已有长图保留；回到最后成功位置或填写重叠像素继续。"));
        return new StitchMatch(true, size - best.Shift, best.Error, TextCatalog.Get("重叠匹配成功。"), best.Direction);
    }
    private static double Score(PixelBuffer a, PixelBuffer b, StitchAxis axis, int direction, int shift)
    {
        var size = axis == StitchAxis.Vertical ? a.Height : a.Width; var cross = axis == StitchAxis.Vertical ? a.Width : a.Height;
        long error = 0; var samples = 0;
        var alongSamples = Math.Min(32, size - shift); var crossSamples = Math.Min(24, cross);
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
    private static double DenseScore(PixelBuffer a, PixelBuffer b, StitchAxis axis, int direction, int shift)
    {
        var size = axis == StitchAxis.Vertical ? a.Height : a.Width; var cross = axis == StitchAxis.Vertical ? a.Width : a.Height;
        var alongSamples = Math.Min(96, size - shift); var crossSamples = Math.Min(48, cross);
        var errors = new int[alongSamples * crossSamples]; var index = 0;
        for (var i = 0; i < alongSamples; i++) for (var j = 0; j < crossSamples; j++)
        {
            var p = (int)((i + .5) * (size - shift) / alongSamples); var q = (int)((j + .5) * cross / crossSamples);
            var pa = p + (direction > 0 ? shift : 0); var pb = p + (direction < 0 ? shift : 0);
            var ia = axis == StitchAxis.Vertical ? (pa * a.Width + q) * 4 : (q * a.Width + pa) * 4;
            var ib = axis == StitchAxis.Vertical ? (pb * b.Width + q) * 4 : (q * b.Width + pb) * 4;
            errors[index++] = (Math.Abs(a.Data[ia] - b.Data[ib]) + Math.Abs(a.Data[ia + 1] - b.Data[ib + 1]) + Math.Abs(a.Data[ia + 2] - b.Data[ib + 2])) / 3;
        }
        Array.Sort(errors);
        var count = Math.Max(1, errors.Length * 9 / 10); long error = 0;
        for (var i = 0; i < count; i++) error += errors[i];
        return error / (double)count;
    }
}
