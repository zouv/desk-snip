using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.App;
using DeskSnip.App.Imaging;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

[Collection("AnnotationToolDefaults")]
public class AnnotationEditorUiTests
{
    private static BitmapSource Image()
    {
        var pixels = new byte[200 * 120 * 4]; for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var source = BitmapSource.Create(200, 120, 96, 96, PixelFormats.Bgra32, null, pixels, 800); source.Freeze(); return source;
    }
    private static Annotation Shape(AnnotationKind kind, uint color = 0xffff0000)
        => new Annotation(kind, new[] { new PixelPoint(20, 20), new PixelPoint(80, 80) }, color, 3);

    [Fact] public void HitTestUsesActualShapeAndRespectsTopmostAndToolFilter() => ImagingTests.Sta(() =>
    {
        var image = Image(); var arrow = Shape(AnnotationKind.Arrow); var rectangle = Shape(AnnotationKind.Rectangle);
        Assert.Equal(-1, AnnotationRenderer.HitTest(image, new[] { arrow }, new PixelPoint(20, 80)));
        Assert.Equal(0, AnnotationRenderer.HitTest(image, new[] { arrow }, new PixelPoint(50, 50)));
        Assert.Equal(0, AnnotationRenderer.HitTest(image, new[] { rectangle }, new PixelPoint(50, 50)));
        Assert.Equal(-1, AnnotationRenderer.HitTest(image, new[] { Shape(AnnotationKind.Ellipse) }, new PixelPoint(20, 20), tolerance: 0));
        Assert.Equal(1, AnnotationRenderer.HitTest(image, new[] { arrow, rectangle }, new PixelPoint(50, 50)));
        Assert.Equal(0, AnnotationRenderer.HitTest(image, new[] { arrow, rectangle }, new PixelPoint(50, 50), AnnotationKind.Arrow));
    });

    [Fact] public void PenAndMosaicRemainSelectableAtDrawnPixels() => ImagingTests.Sta(() =>
    {
        var image = Image();
        var pen = new Annotation(AnnotationKind.Pen, new[] { new PixelPoint(5, 5), new PixelPoint(20, 30), new PixelPoint(40, 10) }, 0xffff0000, 5);
        Assert.Equal(0, AnnotationRenderer.HitTest(image, new[] { pen }, new PixelPoint(20, 30)));
        Assert.Equal(-1, AnnotationRenderer.HitTest(image, new[] { pen }, new PixelPoint(35, 35), tolerance: 0));
        Assert.Equal(0, AnnotationRenderer.HitTest(image, new[] { Shape(AnnotationKind.Mosaic) }, new PixelPoint(40, 40)));
    });

    [Fact] public void TextBoundsUseFontSizeAndReflowWithinRemainingWidth() => ImagingTests.Sta(() =>
    {
        var image = Image(); var text = new Annotation(AnnotationKind.Text, new[] { new PixelPoint(100, 10) }, 0xffffffff, 3, "long sentence with several words", "Segoe UI", 24);
        var bounds = AnnotationRenderer.Bounds(image, text);
        Assert.True(bounds.Height > 30); Assert.True(bounds.Right <= 201);
        Assert.Equal(0, AnnotationRenderer.HitTest(image, new[] { text }, new PixelPoint(bounds.Left + 2, bounds.Top + 2)));
        var smaller = AnnotationRenderer.Bounds(image, text.WithStyle(text.Argb, text.Thickness, fontSize: 14));
        Assert.True(smaller.Height < bounds.Height);
    });

    [Fact] public void ObjectDragCommitsOnceAndCopyLeavesOriginal() => ImagingTests.Sta(() =>
    {
        var canvas = new AnnotationCanvas(Image()); var original = Shape(AnnotationKind.Rectangle); canvas.Document.Add(original); canvas.SetTool(null);
        Assert.True(canvas.SelectAt(new PixelPoint(50, 50))); var revision = canvas.Revision;
        canvas.BeginObjectDrag(new PixelPoint(50, 50), false);
        for (var x = 1; x <= 20; x++) canvas.UpdateObjectDrag(new PixelPoint(50 + x, 55), false);
        Assert.Equal(revision, canvas.Revision); canvas.EndObjectDrag(); Assert.Equal(revision + 1, canvas.Revision);
        Assert.Equal(40, canvas.Document.Items[0].Points[0].X);
        canvas.Document.Undo(); canvas.Refresh(); Assert.Same(original, canvas.Document.Items[0]);
        canvas.SelectAt(new PixelPoint(50, 50)); canvas.BeginObjectDrag(new PixelPoint(50, 50), true);
        canvas.UpdateObjectDrag(new PixelPoint(80, 60), true); canvas.EndObjectDrag();
        Assert.Equal(2, canvas.Document.Items.Count); Assert.Same(original, canvas.Document.Items[0]);
        Assert.Equal(50, canvas.Document.Items[1].Points[0].X); Assert.Equal(20, canvas.Document.Items[1].Points[0].Y);
        canvas.Document.Undo(); canvas.Refresh(); Assert.Single(canvas.Document.Items);
    });

    [Fact] public void MovingNumberPreservesVisibleCircleInsideCanvas() => ImagingTests.Sta(() =>
    {
        var canvas = new AnnotationCanvas(Image());
        canvas.Document.Add(new Annotation(AnnotationKind.Number, new[] { new PixelPoint(50, 50) }, 0xffff0000, 3, "1"));
        canvas.SetTool(null); canvas.SelectAt(new PixelPoint(50, 50));
        canvas.BeginObjectDrag(new PixelPoint(50, 50), false); canvas.UpdateObjectDrag(new PixelPoint(-900, -900), false); canvas.EndObjectDrag();
        var bounds = AnnotationRenderer.Bounds(canvas.Image, canvas.Document.Items[0]);
        Assert.True(bounds.Left >= 0 && bounds.Top >= 0);
    });

    [Fact] public void TextEditStyleAndDeletionRestoreThroughUndo() => ImagingTests.Sta(() =>
    {
        var canvas = new AnnotationCanvas(Image()); var text = new Annotation(AnnotationKind.Text, new[] { new PixelPoint(10, 10) }, 0xffffffff, 3, "before", "Segoe UI", 18);
        canvas.Document.Add(text); canvas.SetTool(AnnotationKind.Text);
        Assert.True(canvas.SelectAt(new PixelPoint(20, 20)));
        canvas.ReplaceSelectedText("after\nsecond"); Assert.Equal("after\nsecond", canvas.Document.Items[0].Text);
        canvas.ChangeStyle(argb: 0xff00ff00, fontSize: 28); Assert.Equal(28, canvas.Document.Items[0].FontSize);
        canvas.ReplaceSelectedText(" "); Assert.Empty(canvas.Document.Items);
        canvas.Document.Undo(); canvas.Refresh(); Assert.Equal(28, canvas.Document.Items[0].FontSize);
        canvas.Document.Undo(); canvas.Refresh(); Assert.Equal(18, canvas.Document.Items[0].FontSize);
        canvas.Document.Undo(); canvas.Refresh(); Assert.Same(text, canvas.Document.Items[0]);
    });

    [Fact] public void SelectingAndPreviewingDoNotAlterExportButCompletedMoveDoes() => ImagingTests.Sta(() =>
    {
        var canvas = new AnnotationCanvas(Image()); canvas.Document.Add(new Annotation(AnnotationKind.Line, new[] { new PixelPoint(10, 20), new PixelPoint(80, 20) }, 0xffff0000, 4));
        canvas.SetTool(null); canvas.SelectAt(new PixelPoint(40, 20)); var revision = canvas.Revision;
        var before = Pixel(AnnotationRenderer.Flatten(canvas.Image, canvas.Document.Items), 40, 20); Assert.True(before[2] > 240);
        canvas.BeginObjectDrag(new PixelPoint(40, 20), false); canvas.UpdateObjectDrag(new PixelPoint(40, 40), false);
        Assert.Equal(revision, canvas.Revision); Assert.Equal(before, Pixel(AnnotationRenderer.Flatten(canvas.Image, canvas.Document.Items), 40, 20));
        canvas.EndObjectDrag(); var output = AnnotationRenderer.Flatten(canvas.Image, canvas.Document.Items);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, Pixel(output, 40, 20)); Assert.True(Pixel(output, 40, 40)[2] > 240);
        canvas.ChangeStyle(argb: 0xff0000ff); output = AnnotationRenderer.Flatten(canvas.Image, canvas.Document.Items);
        Assert.True(Pixel(output, 40, 40)[0] > 240); Assert.Equal(0, Pixel(output, 40, 40)[2]);
    });

    [Theory]
    [InlineData(820, 580)]
    [InlineData(1060, 760)]
    public void EditorControlsFitAndExposeObjectEditingAtSupportedSizes(int width, int height) => ImagingTests.Sta(() =>
    {
        var editor = new EditorWindow(Image(), _ => { }, _ => null!);
        var root = (DockPanel)editor.Content;
        root.Background = Brushes.White;
        var size = new Size(width, height); root.Measure(size); root.Arrange(new Rect(new Point(), size)); root.UpdateLayout();
        var buttons = Descendants(root).OfType<Button>().Where(b => b.IsVisible || b.ActualWidth > 0).ToArray();
        var menuItems = buttons.Where(b => b.ContextMenu != null).SelectMany(b => b.ContextMenu.Items.OfType<MenuItem>()).ToArray();
        Assert.Contains(menuItems, item => Equals(item.Header, "删除标注"));
        Assert.Contains(menuItems, item => Equals(item.Header, "复制标注"));
        Assert.Contains(menuItems, item => Equals(item.Header, "修改文字"));
        Assert.DoesNotContain(buttons, button => AutomationProperties.GetName(button) == "选择 / 移动");
        var annotationCanvas = Descendants(root).OfType<AnnotationCanvas>().Single();
        Assert.Equal(Enum.GetValues(typeof(AnnotationKind)).Length, menuItems.Count(item => item.Tag is AnnotationKind) + buttons.Count(button => button.Tag is AnnotationKind));
        foreach (var item in menuItems.Where(item => item.Tag is AnnotationKind)) { item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Assert.Equal((AnnotationKind)item.Tag, annotationCanvas.Kind); }
        var viewport = Descendants(root).OfType<ScrollViewer>().Single(view => view.Name == "AnnotationViewport"); Assert.True(viewport.ActualHeight >= height - 160);
        foreach (var button in buttons)
        {
            var bounds = button.TransformToAncestor(root).TransformBounds(new Rect(new Point(), button.RenderSize));
            Assert.True(new Rect(new Point(), size).Contains(bounds), $"Button outside editor: {AutomationProperties.GetName(button)}");
        }
        var previewDirectory = Environment.GetEnvironmentVariable("DESK_SNIP_TEST_PREVIEW_DIR");
        if (!string.IsNullOrEmpty(previewDirectory))
        {
            Directory.CreateDirectory(previewDirectory); var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var output = File.Create(Path.Combine(previewDirectory, $"annotation-editor-{width}.png"))) encoder.Save(output);
        }
        editor.Close();
    });

    [Fact] public void GroupedMenusAndExpandedStylesEditObjectsAndKeepUndo() => ImagingTests.Sta(() =>
    {
        var editor = new EditorWindow(Image(), _ => { }, _ => null!); var root = (DockPanel)editor.Content; root.Measure(new Size(820, 460)); root.Arrange(new Rect(0, 0, 820, 460)); root.UpdateLayout();
        var canvas = Descendants(root).OfType<AnnotationCanvas>().Single(); canvas.Document.Add(Shape(AnnotationKind.Rectangle)); canvas.SetTool(null); Assert.True(canvas.SelectAt(new PixelPoint(20, 30)));
        var styles = Descendants(root).OfType<Border>().Single(border => border.Name == "AnnotationStyles"); var buttons = Descendants(root).OfType<Button>().ToArray(); buttons.Single(button => AutomationProperties.GetName(button) == "标注样式").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); root.UpdateLayout(); Assert.Equal(Visibility.Visible, styles.Visibility);
        var color = Descendants(styles).OfType<ComboBox>().Single(box => AutomationProperties.GetName(box) == "颜色"); color.SelectedIndex = 2; Assert.Equal(0xff318bf1u, canvas.Selected!.Argb);
        var remove = buttons.Where(button => button.ContextMenu != null).SelectMany(button => button.ContextMenu.Items.OfType<MenuItem>()).Single(item => Equals(item.Header, "删除标注")); Assert.True(remove.IsEnabled); remove.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Assert.Empty(canvas.Document.Items);
        buttons.Single(button => AutomationProperties.GetName(button) == "撤销").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(0xff318bf1u, Assert.Single(canvas.Document.Items).Argb);
        foreach (var box in Descendants(styles).OfType<ComboBox>()) { var bounds = box.TransformToAncestor(root).TransformBounds(new Rect(new Point(), box.RenderSize)); Assert.True(new Rect(0, 0, 820, 460).Contains(bounds)); }
        editor.Close();
    });

    [Fact] public void ActiveToolSelectsSameCategoryAndCornersCanSwitch() => ImagingTests.Sta(() =>
    {
        var canvas = new AnnotationCanvas(Image());
        canvas.Document.Add(Shape(AnnotationKind.Ellipse)); canvas.Document.Add(Shape(AnnotationKind.Rectangle));
        canvas.SetTool(AnnotationKind.Ellipse);
        Assert.True(canvas.SelectAt(new PixelPoint(50, 50))); Assert.Equal(AnnotationKind.Rectangle, canvas.Selected!.Kind);
        canvas.ChangeCorner(true); Assert.Equal(AnnotationKind.RoundedRectangle, canvas.Selected!.Kind);
        canvas.ChangeCorner(false); Assert.Equal(AnnotationKind.Rectangle, canvas.Selected!.Kind);
    });

    [Fact] public void EndpointAndShapeCornerHandlesResizeSelectedObject() => ImagingTests.Sta(() =>
    {
        var canvas = new AnnotationCanvas(Image());
        canvas.Document.Add(Shape(AnnotationKind.Arrow)); canvas.SetTool(null);
        Assert.True(canvas.SelectAt(new PixelPoint(20, 20))); Assert.True(canvas.TryBeginHandleDrag(new PixelPoint(20, 20)));
        canvas.UpdateHandleDrag(new PixelPoint(40, 30)); canvas.EndHandleDrag();
        Assert.Equal(new PixelPoint(40, 30), canvas.Selected!.Points[0]);
        canvas.Document.Add(Shape(AnnotationKind.Rectangle));
        Assert.True(canvas.SelectAt(new PixelPoint(50, 50)));
        var bounds = AnnotationRenderer.Bounds(canvas.Image, canvas.Selected!);
        var before = canvas.Selected!.Points[1];
        Assert.True(canvas.TryBeginHandleDrag(new PixelPoint((int)bounds.Right, (int)bounds.Bottom)));
        canvas.UpdateHandleDrag(new PixelPoint((int)bounds.Right + 20, (int)bounds.Bottom + 20)); canvas.EndHandleDrag();
        Assert.NotEqual(before, canvas.Selected!.Points[1]);
    });

    [Fact] public void EditorStylePanelReflectsSavedToolStyleWithoutSelection() => ImagingTests.Sta(() =>
    {
        var saved = new AnnotationStyle(0xff00aa00, 8, true, AnnotationDash.Dot, AnnotationArrow.Outline, .5, 4, true, 0, "Arial", 24, true, true);
        var settings = new AppSettings { MarkToolStyles = AnnotationStyleRules.Serialize(new Dictionary<string, AnnotationStyle> { ["Arrow"] = saved }, null) };
        var editor = new EditorWindow(Image(), _ => { }, _ => null!, settings: settings, initialTool: AnnotationKind.Arrow);
        try
        {
            var styles = Descendants((DependencyObject)editor.Content).OfType<Border>().Single(border => border.Name == "AnnotationStyles");
            var boxes = Descendants(styles).OfType<ComboBox>().ToArray();
            Assert.Equal("#FF00AA00", boxes.Single(box => AutomationProperties.GetName(box) == "颜色").SelectedItem);
            Assert.Equal("8", boxes.Single(box => AutomationProperties.GetName(box) == "线宽（px）").SelectedItem);
            Assert.Equal(2, boxes.Single(box => AutomationProperties.GetName(box) == "线型").SelectedIndex);
            Assert.Equal(1, boxes.Single(box => AutomationProperties.GetName(box) == "箭头样式").SelectedIndex);
            Assert.Equal(4d, boxes.Single(box => AutomationProperties.GetName(box) == "效果强度 / 放大倍率").SelectedItem);
            Assert.Equal(.5d, boxes.Single(box => AutomationProperties.GetName(box) == "不透明度").SelectedItem);
            Assert.Equal("Arial", boxes.Single(box => AutomationProperties.GetName(box) == "字体").Text);
            Assert.Equal("24", boxes.Single(box => AutomationProperties.GetName(box) == "字号").Text);
            Assert.Contains(Descendants(styles).OfType<CheckBox>(), box => Equals(box.Content, "填充") && box.IsChecked == true);
            Assert.Contains(Descendants(styles).OfType<CheckBox>(), box => Equals(box.Content, "文字底色") && box.IsChecked == true);
            Assert.Contains(Descendants(styles).OfType<CheckBox>(), box => Equals(box.Content, "粗体") && box.IsChecked == true);
            Assert.Contains(Descendants(styles).OfType<CheckBox>(), box => Equals(box.Content, "斜体") && box.IsChecked == true);
        }
        finally { editor.Close(); }
    });

    [Fact] public void ToolStyleSubBarStatePersistsPerGroupAndSharedColor() => ImagingTests.Sta(() =>
    {
        var rect = new AnnotationStyle(0xff112233, 5, true, AnnotationDash.Dash, AnnotationArrow.Filled, .75, 4, true, 12, "Segoe UI", 24, true, false);
        var arrow = new AnnotationStyle(0xff445566, 8, false, AnnotationDash.Dot, AnnotationArrow.Outline, 1, 2, false, 0, "Arial", 18, false, true);
        var styles = new Dictionary<string, AnnotationStyle> { ["Rectangle"] = rect, ["Arrow"] = arrow };
        var settings = new AppSettings { MarkSharedColor = false, MarkToolStyles = AnnotationStyleRules.Serialize(styles, 0xff778899), MarkPolylineJoin = "Miter" };
        var canvas = new AnnotationCanvas(Image(), settings);
        canvas.SetTool(AnnotationKind.Rectangle);
        Assert.Equal(0xff112233u, canvas.Argb); Assert.Equal(5, canvas.Thickness); Assert.Equal(AnnotationDash.Dash, canvas.Appearance.Dash);
        Assert.Equal(12, canvas.Appearance.CornerRadius); Assert.True(canvas.Appearance.Fill); Assert.True(canvas.Bold); Assert.Equal(AnnotationLineJoin.Miter, canvas.Appearance.LineJoin);
        canvas.ChangeStyle(argb: 0xff00aa00, thickness: 6);
        var saved = AnnotationStyleRules.Parse(canvas.PersistedStyles);
        Assert.Equal(0xff00aa00u, saved.Styles["Rectangle"].Argb); Assert.Equal(6, saved.Styles["Rectangle"].Thickness);
        canvas.SetTool(AnnotationKind.Arrow);
        Assert.Equal(0xff445566u, canvas.Argb); Assert.Equal(8, canvas.Thickness); Assert.Equal(AnnotationDash.Dot, canvas.Appearance.Dash); Assert.True(canvas.Italic);
        canvas.SetTool(AnnotationKind.Rectangle);
        Assert.Equal(0xff00aa00u, canvas.Argb); Assert.Equal(6, canvas.Thickness);
        var shared = new AnnotationCanvas(Image(), new AppSettings { MarkSharedColor = true, MarkToolStyles = AnnotationStyleRules.Serialize(styles, 0xff778899) });
        shared.SetTool(AnnotationKind.Rectangle); Assert.Equal(0xff778899u, shared.Argb);
        shared.ChangeStyle(argb: 0xff00aa00); shared.SetTool(AnnotationKind.Arrow); Assert.Equal(0xff00aa00u, shared.Argb);
    });

    [Fact] public void SquareCornerActionClearsExplicitRadius() => ImagingTests.Sta(() =>
    {
        var canvas = new AnnotationCanvas(Image());
        canvas.Document.Add(new Annotation(AnnotationKind.RoundedRectangle, new[] { new PixelPoint(20, 20), new PixelPoint(80, 80) }, 0xffff0000, 3, appearance: new AnnotationAppearance(cornerRadius: 18)));
        canvas.SetTool(null); Assert.True(canvas.SelectAt(new PixelPoint(50, 50)));
        canvas.ChangeCorner(false); Assert.Equal(AnnotationKind.Rectangle, canvas.Selected!.Kind); Assert.Equal(0, canvas.Selected.Appearance.CornerRadius);
    });

    [Fact] public void CornerRadiusRoundsRectangleFillInExport() => ImagingTests.Sta(() =>
    {
        var item = new Annotation(AnnotationKind.Rectangle, new[] { new PixelPoint(10, 10), new PixelPoint(100, 100) }, 0xffff0000, 3, appearance: new AnnotationAppearance(fill: true, cornerRadius: 24));
        var output = AnnotationRenderer.Flatten(Image(), new[] { item });
        Assert.Equal(0, Pixel(output, 11, 11)[2]);
        Assert.True(Pixel(output, 55, 55)[2] > 240);
    });

    [Fact] public void InlineToolbarHasNoSelectToolAndShowsContextOptions() => ImagingTests.Sta(() =>
    {
        AnnotationToolDefaults.Load("");
        try
        {
            var canvas = new AnnotationCanvas(Image());
            var bar = new InlineAnnotationBar(canvas, new AppSettings(), withFinish: true, showShortcuts: false);
            bar.Measure(new Size(1200, 400)); bar.Arrange(new Rect(0, 0, 1200, 400)); bar.UpdateLayout();
            Assert.DoesNotContain(bar.Items.Children.OfType<Button>(), button => AutomationProperties.GetName(button) == "选择 / 移动");
            canvas.SetTool(AnnotationKind.Rectangle);
            Assert.Equal(Visibility.Visible, bar.Options.Visibility);
            var previewDirectory = Environment.GetEnvironmentVariable("DESK_SNIP_TEST_PREVIEW_DIR");
            Preview(bar, previewDirectory, "inline-toolbar-shape.png");
            var options = Descendants(bar.Options).ToArray();
            var fillBox = options.OfType<CheckBox>().First(box => Equals(box.Content, "填充"));
            fillBox.IsChecked = true; fillBox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.True(canvas.Appearance.Fill);
            Assert.Contains(options.OfType<ComboBox>(), box => AutomationProperties.GetName(box) == "圆角半径");
            var swatches = options.OfType<Border>().Where(border => border.ToolTip is string tip && tip.StartsWith("#", StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(swatches);
            var swatch = swatches.First(); var expected = 0xff000000u | uint.Parse(((string)swatch.ToolTip!).Substring(1), System.Globalization.NumberStyles.HexNumber);
            swatch.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            Assert.Equal(expected, canvas.Argb);
            var ellipse = bar.Options.Items.Children.OfType<Button>().First(button => AutomationProperties.GetName(button) == "椭圆");
            ellipse.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(AnnotationKind.Ellipse, canvas.Kind);
            Assert.DoesNotContain(Descendants(bar.Options).OfType<ComboBox>(), box => AutomationProperties.GetName(box) == "圆角半径");
            canvas.SetTool(AnnotationKind.Text);
            var textOptions = Descendants(bar.Options).ToArray();
            Preview(bar, previewDirectory, "inline-toolbar-text.png");
            Assert.Contains(textOptions.OfType<ComboBox>(), box => AutomationProperties.GetName(box) == "字体");
            Assert.Contains(textOptions.OfType<ComboBox>(), box => AutomationProperties.GetName(box) == "字号");
            Assert.DoesNotContain(textOptions.OfType<CheckBox>(), box => Equals(box.Content, "填充"));
            canvas.SetTool(AnnotationKind.Blur);
            Assert.DoesNotContain(Descendants(bar.Options).OfType<Border>(), border => border.ToolTip is string tip && tip.StartsWith("#", StringComparison.Ordinal));
        }
        finally { AnnotationToolDefaults.Load(""); }
    });

    private static void Preview(FrameworkElement element, string? directory, string name)
    {
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        element.Measure(new Size(1200, double.PositiveInfinity));
        var size = element.DesiredSize;
        element.Arrange(new Rect(new Point(), size)); element.UpdateLayout();
        if (element is Panel panel && panel.Background == null) panel.Background = new SolidColorBrush(Color.FromRgb(232, 236, 242));
        var width = Math.Max(1, (int)Math.Ceiling(size.Width)); var height = Math.Max(1, (int)Math.Ceiling(size.Height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name)); encoder.Save(output);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var item in LogicalTreeHelper.GetChildren(root))
            if (item is DependencyObject child)
            {
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
    }

    private static byte[] Pixel(BitmapSource image, int x, int y)
    {
        var pixel = new byte[4]; image.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0); return pixel;
    }
}
