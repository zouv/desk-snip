using System;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public class GeometryTests
{
    [Fact] public void ReverseDragOnNegativeMonitorNormalizes()
    {
        Assert.Equal(new PixelRect(-900, -200, 600, 400), PixelRect.FromPoints(-300, 200, -900, -200));
    }
    [Fact] public void CropClipsAcrossDesktopOrigin()
    {
        Assert.Equal(new PixelRect(-100, 0, 150, 80), new PixelRect(-150, -40, 200, 120).Intersect(new PixelRect(-100, 0, 500, 500)));
    }
    [Fact] public void TouchingEdgesAreEmpty() => Assert.True(new PixelRect(0, 0, 10, 10).Intersect(new PixelRect(10, 0, 10, 10)).IsEmpty);
    [Fact] public void NudgeKeepsDimensionsAndClamps()
    {
        Assert.Equal(new PixelRect(-100, 70, 20, 30), new PixelRect(-50, 60, 20, 30).Nudge(-1000, 1000, new PixelRect(-100, -100, 200, 200), false));
    }
    [Fact] public void ResizeNeverReversesOrExitsScreen()
    {
        Assert.Equal(new PixelRect(10, 10, 1, 90), new PixelRect(10, 10, 30, 30).Nudge(-999, 999, new PixelRect(0, 0, 100, 100), true));
    }
    [Fact] public void EncodingUsesEvenDimensions() => Assert.Equal(new PixelRect(-5, 3, 100, 200), new PixelRect(-5, 3, 101, 201).EvenSize());
    [Fact] public void OverflowIsRejected() => Assert.Throws<ArgumentOutOfRangeException>(() => new PixelRect(int.MaxValue, 0, 10, 10));
}
