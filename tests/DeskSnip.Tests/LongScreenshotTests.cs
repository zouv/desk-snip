using System;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public sealed class LongScreenshotTests
{
    private static PixelBuffer Pattern(int width = 128, int height = 160)
    {
        var data = new byte[width * height * 4]; var random = new Random(137);
        for (var i = 0; i < data.Length; i += 4) { data[i] = (byte)random.Next(256); data[i + 1] = (byte)random.Next(256); data[i + 2] = (byte)random.Next(256); data[i + 3] = 255; }
        return new PixelBuffer(width, height, data);
    }
    [Theory] [InlineData(StitchAxis.Vertical)] [InlineData(StitchAxis.Horizontal)]
    public void MatchingProducesExactPixelsAndReverseScrollDoesNotDuplicate(StitchAxis axis)
    {
        var original = Pattern();
        var a = axis == StitchAxis.Vertical ? new PixelRect(0, 0, 128, 64) : new PixelRect(0, 0, 64, 160);
        var b = axis == StitchAxis.Vertical ? new PixelRect(0, 30, 128, 64) : new PixelRect(30, 0, 64, 160);
        var expected = axis == StitchAxis.Vertical ? new PixelRect(0, 0, 128, 94) : new PixelRect(0, 0, 94, 160);
        var capture = new LongScreenshot(original.Crop(a), axis); var result = capture.Append(original.Crop(b));
        Assert.True(result.Success, result.Message); Assert.Equal(34, result.Overlap); Assert.Equal(original.Crop(expected).CopyPixels(), capture.Image.CopyPixels());
        Assert.True(capture.Append(original.Crop(a), -1).Success); Assert.Equal(original.Crop(expected).CopyPixels(), capture.Image.CopyPixels()); Assert.Equal(0, capture.CurrentOffset);
    }
    [Theory] [InlineData(StitchAxis.Vertical, 1)] [InlineData(StitchAxis.Vertical, -1)] [InlineData(StitchAxis.Horizontal, 1)] [InlineData(StitchAxis.Horizontal, -1)]
    public void TransientBlackOverlapCannotOverwriteCommittedPixels(StitchAxis axis, int direction)
    {
        var original = Pattern();
        var firstArea = axis == StitchAxis.Vertical ? new PixelRect(0, direction > 0 ? 0 : 30, 128, 64) : new PixelRect(direction > 0 ? 0 : 30, 0, 64, 160);
        var nextArea = axis == StitchAxis.Vertical ? new PixelRect(0, direction > 0 ? 30 : 0, 128, 64) : new PixelRect(direction > 0 ? 30 : 0, 0, 64, 160);
        var nextPixels = original.Crop(nextArea).CopyPixels(); var nextWidth = axis == StitchAxis.Vertical ? 128 : 64; var nextHeight = axis == StitchAxis.Vertical ? 64 : 160;
        for (var y = 0; y < nextHeight; y++) for (var x = 0; x < nextWidth; x++)
        {
            var line = axis == StitchAxis.Vertical ? y : x;
            if (direction > 0 ? line >= 34 : line < 30) continue;
            var offset = (y * nextWidth + x) * 4; nextPixels[offset] = nextPixels[offset + 1] = nextPixels[offset + 2] = 0; nextPixels[offset + 3] = 255;
        }
        var capture = new LongScreenshot(original.Crop(firstArea), axis);
        var result = capture.Append(new PixelBuffer(nextWidth, nextHeight, nextPixels), direction, 34);
        Assert.True(result.Success, result.Message);
        var expected = axis == StitchAxis.Vertical ? original.Crop(new PixelRect(0, 0, 128, 94)) : original.Crop(new PixelRect(0, 0, 94, 160));
        Assert.Equal(expected.CopyPixels(), capture.Image.CopyPixels());
    }
    [Theory] [InlineData(StitchAxis.Vertical)] [InlineData(StitchAxis.Horizontal)]
    public void AutomaticDirectionFollowsForwardAndBackwardScrollingOnTextLikeContent(StitchAxis axis)
    {
        var original = Document();
        PixelBuffer Frame(int offset) => original.Crop(axis == StitchAxis.Vertical ? new PixelRect(0, offset, 128, 64) : new PixelRect(offset, 0, 64, 160));
        var capture = new LongScreenshot(Frame(0), axis);
        var offsets = new[] { 24, 48, 72, 48, 24, 0 };
        var directions = new[] { 1, 1, 1, -1, -1, -1 };
        for (var i = 0; i < offsets.Length; i++)
        {
            var result = capture.Append(Frame(offsets[i]), 0);
            Assert.True(result.Success, result.Message); Assert.Equal(directions[i], result.Direction); Assert.Equal(offsets[i], capture.CurrentOffset);
        }
        var expected = axis == StitchAxis.Vertical ? new PixelRect(0, 0, 128, 136) : new PixelRect(0, 0, 136, 160);
        Assert.Equal(original.Crop(expected).CopyPixels(), capture.Image.CopyPixels());
    }
    [Fact] public void AutomaticDirectionRejectsRepeatedFramesWithoutCommitting()
    {
        PixelBuffer Frame(int scroll)
        {
            var frame = new byte[128 * 64 * 4];
            for (var y = 0; y < 64; y++) for (var x = 0; x < 128; x++)
            {
                var offset = (y * 128 + x) * 4; var value = (byte)((y + scroll) % 60 < 30 ? 35 : 220);
                frame[offset] = frame[offset + 1] = frame[offset + 2] = value; frame[offset + 3] = 255;
            }
            return new PixelBuffer(128, 64, frame);
        }
        var first = Frame(0); var capture = new LongScreenshot(first, StitchAxis.Vertical);
        var result = capture.Append(Frame(30), 0);
        Assert.False(result.Success); Assert.Same(first, capture.Image); Assert.Equal(0, capture.CurrentOffset);
    }
    private static PixelBuffer Document()
    {
        const int width = 220, height = 220;
        var data = new byte[width * height * 4];
        for (var i = 0; i < data.Length; i += 4) { data[i] = data[i + 1] = data[i + 2] = 255; data[i + 3] = 255; }
        for (var line = 0; line < height / 14; line++) for (var y = line * 14 + 3; y < line * 14 + 10; y++) for (var x = 4; x < width - 4; x++)
        {
            var identifier = x < 56 && ((line >> ((x - 4) / 6)) & 1) != 0 && x % 6 < 3;
            var body = x >= 64 && (x * 7 + line * 11) % 23 < 12;
            if (!identifier && !body) continue;
            var offset = (y * width + x) * 4; var gray = (byte)(35 + (line * 17 + x) % 100);
            data[offset] = data[offset + 1] = data[offset + 2] = gray;
        }
        return new PixelBuffer(width, height, data);
    }
    [Fact] public void FixedHeaderIsDetectedAndIncludedOnlyOnce()
    {
        var original = Document(); var first = PartialFrame(original, 0); var next = PartialFrame(original, 24);
        var viewport = LongScreenshotViewport.Detect(first, next, StitchAxis.Vertical);
        Assert.Equal(new PixelRect(0, 64, 128, 64), viewport);
        var content = new LongScreenshot(first.Crop(viewport!.Value), StitchAxis.Vertical);
        var result = content.Append(next.Crop(viewport.Value), 0);
        Assert.True(result.Success, result.Message);
        var output = LongScreenshotViewport.Compose(first, viewport.Value, content.Image, StitchAxis.Vertical);
        var expected = new PixelBuffer(128, 152, new byte[128 * 152 * 4]);
        var expectedPixels = expected.CopyPixels();
        Buffer.BlockCopy(first.CopyPixels(), 0, expectedPixels, 0, 128 * 64 * 4);
        var scrolled = original.Crop(new PixelRect(0, 64, 128, 88)).CopyPixels();
        Buffer.BlockCopy(scrolled, 0, expectedPixels, 128 * 64 * 4, scrolled.Length);
        Assert.Equal(expectedPixels, output.CopyPixels());
    }
    [Fact] public void ViewportDetectionUsesMatchedShiftToRecoverBlankBoundaryRows()
    {
        var originalPixels = Document().CopyPixels();
        foreach (var start in new[] { 64, 88 }) for (var y = start; y < start + 3; y++) for (var x = 0; x < 220; x++)
        {
            var offset = (y * 220 + x) * 4; originalPixels[offset] = originalPixels[offset + 1] = originalPixels[offset + 2] = originalPixels[offset + 3] = 255;
        }
        var original = new PixelBuffer(220, 220, originalPixels); var first = PartialFrame(original, 0); var next = PartialFrame(original, 24);
        var viewport = LongScreenshotViewport.Detect(first, next, StitchAxis.Vertical);
        Assert.Equal(new PixelRect(0, 64, 128, 64), viewport);
    }
    [Fact] public void FullFrameOrSmallLocalChangeDoesNotCreatePartialViewport()
    {
        var original = Document(); var first = original.Crop(new PixelRect(0, 0, 128, 64));
        Assert.Null(LongScreenshotViewport.Detect(first, original.Crop(new PixelRect(0, 24, 128, 64)), StitchAxis.Vertical));
        var pixels = first.CopyPixels();
        for (var y = 30; y < 34; y++) for (var x = 40; x < 60; x++) pixels[(y * 128 + x) * 4] = 255;
        Assert.Null(LongScreenshotViewport.Detect(first, new PixelBuffer(128, 64, pixels), StitchAxis.Vertical));
    }
    private static PixelBuffer PartialFrame(PixelBuffer original, int scroll)
    {
        var pixels = new byte[128 * 128 * 4]; var source = original.CopyPixels();
        Buffer.BlockCopy(source, 0, pixels, 0, 128 * 64 * 4);
        for (var y = 0; y < 64; y++) Buffer.BlockCopy(source, (64 + scroll + y) * original.Width * 4, pixels, (64 + y) * 128 * 4, 128 * 4);
        return new PixelBuffer(128, 128, pixels);
    }
    [Fact] public void SmallScrollIsDetectedFromOverlapInsteadOfFixedMotionThreshold()
    {
        var original = Document(); var capture = new LongScreenshot(original.Crop(new PixelRect(0, 0, 128, 64)), StitchAxis.Vertical);
        var result = capture.Append(original.Crop(new PixelRect(0, 1, 128, 64)), 0);
        Assert.True(result.Success, result.Message); Assert.Equal(63, result.Overlap); Assert.Equal(1, capture.CurrentOffset);
        Assert.Equal(original.Crop(new PixelRect(0, 0, 128, 65)).CopyPixels(), capture.Image.CopyPixels());
    }
    [Fact] public void BufferedFramesRecoverLargeScrollThroughLaterBridgeFrame()
    {
        var original = Pattern(128, 300);
        PixelBuffer Frame(int offset) => original.Crop(new PixelRect(0, offset, 128, 64));
        var capture = new LongScreenshot(Frame(0), StitchAxis.Vertical);
        var skipped = capture.Append(Frame(110), 0);
        Assert.False(skipped.Success); Assert.Equal(1, capture.PendingCount); Assert.Equal(64, capture.Image.Height);
        var recovered = capture.Append(Frame(55), 0);
        Assert.True(recovered.Success, recovered.Message); Assert.Equal(2, recovered.CommittedFrames); Assert.Equal(0, capture.PendingCount);
        Assert.Equal(110, capture.CurrentOffset); Assert.Equal(original.Crop(new PixelRect(0, 0, 128, 174)).CopyPixels(), capture.Image.CopyPixels());
        capture.Undo(); Assert.Equal(original.Crop(new PixelRect(0, 0, 128, 119)).CopyPixels(), capture.Image.CopyPixels()); Assert.Equal(55, capture.CurrentOffset);
    }
    [Fact] public void PrependingShiftsOlderAnchorsBeforeLaterRecovery()
    {
        var original = Pattern(128, 300);
        PixelBuffer Frame(int offset) => original.Crop(new PixelRect(0, offset, 128, 64));
        var first = Frame(50); var capture = new LongScreenshot(first, StitchAxis.Vertical);
        Assert.True(capture.Append(Frame(0), 0).Success); Assert.Equal(114, capture.Image.Height); Assert.Equal(0, capture.CurrentOffset);
        capture.Undo(); Assert.Same(first, capture.Image); Assert.Equal(0, capture.CurrentOffset);
        Assert.True(capture.Append(Frame(0), 0).Success);
        var recovered = capture.Append(Frame(110), 0);
        Assert.True(recovered.Success, recovered.Message); Assert.Equal(110, capture.CurrentOffset);
        Assert.Equal(original.Crop(new PixelRect(0, 0, 128, 174)).CopyPixels(), capture.Image.CopyPixels());
    }
    [Fact] public void LocalDynamicPixelsDoNotMoveTrustedOverlap()
    {
        var original = Document(); var next = original.Crop(new PixelRect(0, 24, 128, 64));
        var pixels = next.CopyPixels();
        for (var y = 20; y < 24; y++) for (var x = 70; x < 74; x++)
        {
            var offset = (y * 128 + x) * 4; pixels[offset] = 255; pixels[offset + 1] = pixels[offset + 2] = pixels[offset + 3] = 0;
        }
        var capture = new LongScreenshot(original.Crop(new PixelRect(0, 0, 128, 64)), StitchAxis.Vertical);
        var result = capture.Append(new PixelBuffer(128, 64, pixels), 0);
        Assert.True(result.Success, result.Message); Assert.Equal(24, capture.CurrentOffset); Assert.Equal(88, capture.Image.Height);
    }
    [Fact] public void FailurePreservesResultAndManualOverlapCanRecover()
    {
        var original = Pattern(); var first = original.Crop(new PixelRect(0, 0, 128, 64)); var capture = new LongScreenshot(first, StitchAxis.Vertical);
        Assert.False(capture.Append(new PixelBuffer(128, 64, new byte[128 * 64 * 4])).Success); Assert.Same(first, capture.Image);
        Assert.False(capture.Append(first).Success);
        Assert.True(capture.Append(original.Crop(new PixelRect(0, 30, 128, 64)), manualOverlap: 34).Success); Assert.Equal(94, capture.Image.Height);
        capture.Undo(); Assert.Same(first, capture.Image); Assert.False(capture.CanUndo);
    }
    [Fact] public void CroppingKeepsCurrentFrameAndCanBeUndone()
    {
        var original = Pattern(); var first = original.Crop(new PixelRect(0, 0, 128, 64)); var capture = new LongScreenshot(first, StitchAxis.Vertical);
        capture.Append(original.Crop(new PixelRect(0, 30, 128, 64))); capture.TrimBeforeCurrent(); Assert.Equal(64, capture.Image.Height); Assert.Equal(0, capture.CurrentOffset);
        capture.Undo(); Assert.Equal(94, capture.Image.Height); capture.Append(first, -1); capture.TrimAfterCurrent(); Assert.Equal(first.CopyPixels(), capture.Image.CopyPixels());
    }
    [Fact] public void InvalidManualInputCannotChangeImage()
    {
        var first = Pattern(64, 64); var capture = new LongScreenshot(first, StitchAxis.Vertical);
        Assert.Throws<ArgumentOutOfRangeException>(() => capture.Append(first, manualOverlap: 64)); Assert.Same(first, capture.Image);
        Assert.Throws<ArgumentException>(() => capture.Append(Pattern(80, 64))); Assert.Same(first, capture.Image);
    }
}
