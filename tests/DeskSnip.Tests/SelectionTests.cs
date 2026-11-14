using System;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public class SelectionTests
{
    private static readonly PixelRect Bounds = new PixelRect(-200, -100, 400, 300);
    private static readonly PixelRect Region = new PixelRect(-80, -40, 100, 60);

    [Theory]
    [InlineData(-80, -40, SelectionHandle.Left | SelectionHandle.Top)]
    [InlineData(-30, -40, SelectionHandle.Top)]
    [InlineData(20, -40, SelectionHandle.Right | SelectionHandle.Top)]
    [InlineData(-80, -10, SelectionHandle.Left)]
    [InlineData(20, -10, SelectionHandle.Right)]
    [InlineData(-80, 20, SelectionHandle.Left | SelectionHandle.Bottom)]
    [InlineData(-30, 20, SelectionHandle.Bottom)]
    [InlineData(20, 20, SelectionHandle.Right | SelectionHandle.Bottom)]
    [InlineData(-30, -10, SelectionHandle.Move)]
    [InlineData(60, -10, SelectionHandle.None)]
    public void EightHandlesAndInteriorHaveDistinctTargets(int x, int y, SelectionHandle expected)
        => Assert.Equal(expected, SelectionGeometry.HitTest(Region, new PixelPoint(x, y), 5, 5));

    [Fact] public void TinyRegionChoosesNearestEdge()
    {
        var region = new PixelRect(0, 0, 2, 2);
        Assert.Equal(SelectionHandle.Right | SelectionHandle.Bottom, SelectionGeometry.HitTest(region, new PixelPoint(2, 2), 8, 8));
    }

    [Fact] public void ReverseSquareDragFitsNegativeDesktop()
    {
        Assert.Equal(new PixelRect(-140, -100, 120, 120), SelectionGeometry.Create(new PixelPoint(-20, 20), new PixelPoint(-180, -100), Bounds, true));
    }

    [Fact] public void ResizePreservesOppositeCornerAndCannotInvert()
    {
        Assert.Equal(new PixelRect(19, 19, 1, 1), SelectionGeometry.Transform(Region, new PixelPoint(500, 500), SelectionHandle.Left | SelectionHandle.Top, Bounds));
        Assert.Equal(new PixelRect(-200, -100, 220, 120), SelectionGeometry.Transform(Region, new PixelPoint(-500, -500), SelectionHandle.Left | SelectionHandle.Top, Bounds));
    }

    [Fact] public void SymmetricResizeKeepsCenterAndClampsBothEdges()
    {
        var resized = SelectionGeometry.Transform(Region, new PixelPoint(20, 10), SelectionHandle.Right | SelectionHandle.Bottom, Bounds, symmetric: true);
        Assert.Equal(new PixelRect(-100, -50, 140, 80), resized);
        var clamped = SelectionGeometry.Transform(Region, new PixelPoint(999, 999), SelectionHandle.Right | SelectionHandle.Bottom, Bounds, symmetric: true);
        Assert.Equal(new PixelRect(-200, -100, 340, 180), clamped);
    }

    [Fact] public void SideHandleCanLockAspectRatioAroundOtherAxis()
    {
        Assert.Equal(new PixelRect(-80, -55, 150, 90), SelectionGeometry.Transform(Region, new PixelPoint(50, 0), SelectionHandle.Right, Bounds, aspectRatio: 5d / 3));
    }

    [Fact] public void RatioResizeClampsWithoutBreakingRatio()
    {
        Assert.Equal(new PixelRect(-80, -40, 240, 240), SelectionGeometry.Transform(Region, new PixelPoint(999, 999), SelectionHandle.Right | SelectionHandle.Bottom, Bounds, aspectRatio: 1));
    }

    [Fact] public void ShiftMovingAndSpaceMovingKeepDimensions()
    {
        Assert.Equal(new PixelRect(-40, -40, 100, 60), SelectionGeometry.Transform(Region, new PixelPoint(40, 10), SelectionHandle.Move, Bounds, axisLocked: true));
        Assert.Equal(new PixelRect(-60, -30, 100, 60), SelectionGeometry.Transform(Region, new PixelPoint(20, 10), SelectionHandle.Right, Bounds, temporaryMove: true));
    }

    [Theory]
    [InlineData(-1, 0, false, -79, -40, 99, 60)]
    [InlineData(-1, 0, true, -81, -40, 101, 60)]
    [InlineData(1, 0, false, -80, -40, 99, 60)]
    [InlineData(1, 0, true, -80, -40, 101, 60)]
    [InlineData(0, -1, false, -80, -39, 100, 59)]
    [InlineData(0, -1, true, -80, -41, 100, 61)]
    [InlineData(0, 1, false, -80, -40, 100, 59)]
    [InlineData(0, 1, true, -80, -40, 100, 61)]
    public void KeyboardAdjustsRequestedEdge(int dx, int dy, bool expand, int x, int y, int w, int h)
        => Assert.Equal(new PixelRect(x, y, w, h), SelectionGeometry.AdjustEdge(Region, dx, dy, Bounds, expand));

    [Fact] public void WheelScalePreservesCenterAndRatio()
        => Assert.Equal(new PixelRect(-90, -46, 120, 72), SelectionGeometry.Scale(Region, 10, Bounds));

    [Theory]
    [InlineData("-200", "-100", "400", "300", true)]
    [InlineData("-1", "0", "1", "1", true)]
    [InlineData("-201", "0", "1", "1", false)]
    [InlineData("0", "0", "0", "1", false)]
    [InlineData("0", "0", "201", "1", false)]
    [InlineData("2147483647", "0", "2147483647", "1", false)]
    [InlineData("1.5", "0", "1", "1", false)]
    public void ExactInputRejectsOverflowAndOutOfBounds(string x, string y, string width, string height, bool valid)
        => Assert.Equal(valid, SelectionGeometry.TryParse(x, y, width, height, Bounds, out _));

    [Fact] public void InvalidAspectRatioIsRejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() => SelectionGeometry.Transform(Region, new PixelPoint(1, 1), SelectionHandle.Right, Bounds, aspectRatio: double.NaN));
}
