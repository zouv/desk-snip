using System;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public class AnnotationEditingTests
{
    private static Annotation Arrow() => new Annotation(AnnotationKind.Arrow,
        new[] { new PixelPoint(10, 10), new PixelPoint(70, 50) }, 0xffff0000, 3);

    [Fact] public void MoveClampsWholeObjectAndLeavesPreviousSnapshotUntouched()
    {
        var document = new AnnotationDocument(100, 80); var original = Arrow(); document.Add(original);
        document.Move(0, new PixelPoint(999, -999));
        Assert.Equal(40, document.Items[0].Points[0].X); Assert.Equal(0, document.Items[0].Points[0].Y);
        Assert.Equal(100, document.Items[0].Points[1].X); Assert.Equal(40, document.Items[0].Points[1].Y);
        Assert.Equal(10, original.Points[0].X); Assert.Equal(10, original.Points[0].Y);
        document.Undo(); Assert.Same(original, document.Items[0]);
        document.Redo(); Assert.Equal(40, document.Items[0].Points[0].X);
    }

    [Fact] public void DragPreviewDoesNotCreateUndoEntriesAndCommitIsOneOperation()
    {
        var document = new AnnotationDocument(100, 80); document.Add(Arrow()); var revision = document.Revision;
        for (var x = 1; x <= 20; x++) document.PreviewMove(0, new PixelPoint(x, 0));
        Assert.Equal(revision, document.Revision); Assert.Equal(10, document.Items[0].Points[0].X);
        document.Move(0, new PixelPoint(20, 0)); Assert.Equal(revision + 1, document.Revision);
        document.Undo(); Assert.Equal(10, document.Items[0].Points[0].X); Assert.Single(document.Items);
    }

    [Fact] public void CopyPreservesTextAndStyleAndCanUndoOnlyTheCopy()
    {
        var document = new AnnotationDocument(200, 100);
        var original = new Annotation(AnnotationKind.Text, new[] { new PixelPoint(10, 10) }, 0xff008978, 2, "one\ntwo", "Segoe UI", 28);
        document.Add(original);
        var copiedIndex = document.Move(0, new PixelPoint(20, 5), copy: true, axisLocked: true);
        Assert.Equal(1, copiedIndex); Assert.Same(original, document.Items[0]);
        var copy = document.Items[1]; Assert.NotSame(original, copy);
        Assert.Equal(30, copy.Points[0].X); Assert.Equal(10, copy.Points[0].Y);
        Assert.Equal(original.Text, copy.Text); Assert.Equal(original.FontSize, copy.FontSize); Assert.Equal(original.FontFamily, copy.FontFamily); Assert.Equal(original.Argb, copy.Argb);
        document.Undo(); Assert.Single(document.Items); Assert.Same(original, document.Items[0]);
        document.Redo(); Assert.Equal(2, document.Items.Count);
    }

    [Fact] public void DeleteAndStyleReplacementHaveIndependentUndoSteps()
    {
        var document = new AnnotationDocument(100, 80); var original = Arrow(); document.Add(original);
        var styled = original.WithStyle(0xff0000ff, 8); document.Replace(0, styled); document.Remove(0);
        Assert.Empty(document.Items); document.Undo(); Assert.Same(styled, document.Items[0]);
        document.Undo(); Assert.Same(original, document.Items[0]);
        document.Redo(); Assert.Same(styled, document.Items[0]);
    }

    [Fact] public void InvalidEditPreservesCurrentSnapshotAndRedo()
    {
        var document = new AnnotationDocument(100, 80); document.Add(Arrow()); document.Add(Arrow()); document.Undo();
        var revision = document.Revision;
        var invalid = Arrow().WithPoints(new[] { new PixelPoint(101, 10) });
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Replace(0, invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Remove(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Move(9, new PixelPoint(10, 0)));
        Assert.Equal(revision, document.Revision); Assert.Single(document.Items); Assert.True(document.CanRedo);
        document.Move(0, new PixelPoint(0, 0)); Assert.True(document.CanRedo); Assert.Equal(revision, document.Revision);
    }

    [Fact] public void EditingAfterUndoCutsOffOldRedo()
    {
        var document = new AnnotationDocument(100, 80); document.Add(Arrow()); document.Move(0, new PixelPoint(10, 0)); document.Undo();
        document.Replace(0, document.Items[0].WithStyle(0xff000000, 4));
        Assert.False(document.CanRedo); Assert.Equal(10, document.Items[0].Points[0].X);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(481)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidFontSizeIsRejected(double size)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new Annotation(AnnotationKind.Text, new[] { new PixelPoint(0, 0) }, 0xff000000, 3, "text", fontSize: size));
}
