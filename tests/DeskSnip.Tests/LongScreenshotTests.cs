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
