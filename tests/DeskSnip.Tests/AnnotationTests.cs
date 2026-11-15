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

    [Fact] public void TextStylePreservesBoldAndItalicThroughClones()
    {
        var text = new Annotation(AnnotationKind.Text, new[] { new PixelPoint(1, 1) }, 0xff112233, 3, "sample", bold: true, italic: true);
        Assert.True(text.Bold); Assert.True(text.Italic);
        var styled = text.WithStyle(0xff445566, 4);
        Assert.True(styled.Bold); Assert.True(styled.Italic);
        Assert.True(text.WithText("changed").Bold); Assert.True(text.WithPoints(new[] { new PixelPoint(2, 2) }).Italic);
    }
    [Fact] public void CornerRadiusIsBoundedAndPreservedByEditorStyle()
    {
        var appearance = new AnnotationAppearance(cornerRadius: 12);
        Assert.Equal(12, appearance.CornerRadius);
        Assert.Equal(12, appearance.WithEditorStyle(true, AnnotationDash.Dash, .5, AnnotationArrow.Outline, 4, true).CornerRadius);
        Assert.Equal(0, appearance.WithEditorStyle(false, AnnotationDash.Solid, 1, AnnotationArrow.Filled, 2, false, 0).CornerRadius);
        Assert.Throws<System.ArgumentException>(() => new AnnotationAppearance(cornerRadius: -1));
        Assert.Throws<System.ArgumentException>(() => new AnnotationAppearance(cornerRadius: 401));
        Assert.Throws<System.ArgumentException>(() => new AnnotationAppearance(cornerRadius: double.NaN));
    }
}
