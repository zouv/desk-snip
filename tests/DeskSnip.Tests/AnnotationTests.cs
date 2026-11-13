using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public class AnnotationTests
{
    private static Annotation Shape(int x) => new Annotation(AnnotationKind.Arrow, new[] { new PixelPoint(x, 1), new PixelPoint(20, 20) }, 0xffff0000, 3);
    [Fact] public void NewEditAfterUndoDiscardsRedo()
    {
        var document = new AnnotationDocument(100, 100);
        document.Add(Shape(1)); document.Add(Shape(2)); document.Undo(); document.Add(Shape(3));
        Assert.False(document.CanRedo); Assert.Equal(3, document.Items[1].Points[0].X);
        document.Undo(); document.Redo(); Assert.Equal(2, document.Items.Count);
    }
    [Fact] public void ClearIsOneUndoableOperation()
    {
        var document = new AnnotationDocument(100, 100);
        document.Add(Shape(1)); document.Add(Shape(2)); document.Clear();
        Assert.Empty(document.Items); document.Undo(); Assert.Equal(2, document.Items.Count);
    }
    [Fact] public void InputPointsAreCopied()
    {
        var points = new[] { new PixelPoint(1, 2) };
        var item = new Annotation(AnnotationKind.Pen, points, 0xff000000, 2);
        points[0] = new PixelPoint(9, 9); Assert.Equal(1, item.Points[0].X);
    }
    [Fact] public void OutOfCanvasAnnotationIsRejected()
    {
        var document = new AnnotationDocument(10, 10);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => document.Add(Shape(1)));
    }
}
