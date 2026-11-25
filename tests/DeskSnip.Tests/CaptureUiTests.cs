using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using DeskSnip.App;
using DeskSnip.App.Imaging;
using DeskSnip.App.Windows;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

[Collection("AnnotationToolDefaults")]
public class CaptureUiTests
{
    private static BitmapSource Image(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                pixels[i] = 31; pixels[i + 1] = (byte)y; pixels[i + 2] = (byte)x; pixels[i + 3] = 255;
            }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        image.Freeze(); return image;
    }

    [Fact] public void SamplerReadsGlobalPhysicalPixelsAndClampsDesktopEdges() => ImagingTests.Sta(() =>
    {
        var inspector = new PixelInspector(Image(20, 12), new PixelRect(-20, -12, 20, 12));
        Assert.Equal("#03051F", inspector.Sample(new PixelPoint(-17, -7)).Format(PixelColorFormat.HashHex));
        Assert.Equal("#130B1F", inspector.Sample(new PixelPoint(0, 0)).Format(PixelColorFormat.HashHex));
        Assert.Equal("#00001F", inspector.Sample(new PixelPoint(-500, -500)).Format(PixelColorFormat.HashHex));
        Assert.Equal(new Int32Rect(0, 0, 6, 6), inspector.Viewport(new PixelPoint(-20, -12), 5));
        Assert.Throws<ArgumentException>(() => new PixelInspector(Image(10, 10), new PixelRect(0, 0, 10, 9)));
    });

    [Theory]
    [InlineData(-20, -12, 0, 0)]
    [InlineData(-1, -1, 19, 11)]
    [InlineData(-12, -4, 8, 8)]
    public void MagnifierCenterAlwaysMatchesSelectedPixelAtScreenEdges(int x, int y, byte red, byte green) => ImagingTests.Sta(() =>
    {
        var magnifier = new PixelMagnifier(new PixelInspector(Image(20, 12), new PixelRect(-20, -12, 20, 12)));
        magnifier.Update(new PixelPoint(x, y), default, PixelColorFormat.HashHex, false);
        magnifier.Measure(new Size(magnifier.Width, magnifier.Height)); magnifier.Arrange(new Rect(0, 0, magnifier.Width, magnifier.Height));
        var output = new RenderTargetBitmap((int)magnifier.Width, (int)magnifier.Height, 96, 96, PixelFormats.Pbgra32);
        output.Render(magnifier);
        var pixel = new byte[4]; output.CopyPixels(new Int32Rect(72, 72, 1, 1), pixel, 4, 0);
        Assert.Equal(new byte[] { 31, green, red, 255 }, pixel);
    });

    [Fact] public void InfoBarAppliesAllFieldsTogetherAndRejectsInvalidInput() => ImagingTests.Sta(() =>
    {
        var selected = new PixelRect(-80, -40, 100, 60);
        var info = new SelectionInfoBar(new PixelRect(-200, -100, 400, 300), value => selected = value, _ => { }, _ => { });
        info.Update(selected, "#000000");
        Assert.False(info.IsExpanded); info.SetExpanded(true); Assert.True(info.IsExpanded);
        var boxes = Descendants(info).OfType<TextBox>().ToDictionary(AutomationProperties.GetName);
        boxes["选区 X 坐标"].Text = "-100"; boxes["选区 Y 坐标"].Text = "-50";
        boxes["选区宽度"].Text = "120"; boxes["选区高度"].Text = "80";
        // 鼠标移动刷新颜色时，不覆盖尚未应用的输入。
        info.Update(new PixelRect(-80, -40, 100, 60), "#FFFFFF");
        var apply = Descendants(info).OfType<Button>().Single(b => Equals(b.Content, "应用"));
        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(new PixelRect(-100, -50, 120, 80), selected);
        Assert.False(info.IsExpanded); info.SetExpanded(true);
        boxes["选区宽度"].Text = "2147483647";
        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(new PixelRect(-100, -50, 120, 80), selected);
    });

    [Fact] public void CaptureInteractionWiresResizeMoveAndTemporarySpaceWithoutJumps() => ImagingTests.Sta(() =>
    {
        var window = new CaptureWindow(Image(400, 300), new PixelRect(-200, -100, 400, 300), false);
        window.SetSelection(new PixelRect(-80, -40, 100, 60));
        window.BeginInteraction(new PixelPoint(20, 20));
        window.UpdateInteraction(new PixelPoint(40, 30), false, false, false);
        Assert.Equal(new PixelRect(-80, -40, 120, 70), window.Selection);
        window.UpdateInteraction(new PixelPoint(40, 30), false, false, true);
        window.UpdateInteraction(new PixelPoint(50, 40), false, false, true);
        Assert.Equal(new PixelRect(-70, -30, 120, 70), window.Selection);
        window.UpdateInteraction(new PixelPoint(50, 40), false, false, false);
        window.UpdateInteraction(new PixelPoint(60, 50), false, false, false);
        Assert.Equal(new PixelRect(-70, -30, 130, 80), window.Selection);
        window.EndInteraction(); window.Close();
    });

    [Fact] public void CaptureInteractionCreatesSquareAndLocksMovingAxis() => ImagingTests.Sta(() =>
    {
        var window = new CaptureWindow(Image(400, 300), new PixelRect(-200, -100, 400, 300), true);
        window.BeginInteraction(new PixelPoint(-100, -60));
        window.UpdateInteraction(new PixelPoint(-20, -20), true, false, false); window.EndInteraction();
        Assert.Equal(new PixelRect(-100, -60, 80, 80), window.Selection);
        window.BeginInteraction(new PixelPoint(-60, -20));
        window.UpdateInteraction(new PixelPoint(-45, 20), true, false, false); window.EndInteraction();
        Assert.Equal(new PixelRect(-100, -20, 80, 80), window.Selection);
        window.Close();
    });

    [Theory]
    [InlineData(0, 0, 1280, 720, 1, false)]
    [InlineData(0, 0, 1280, 720, 2, true)]
    [InlineData(750, 580, 300, 130, 1, false)]
    [InlineData(1160, 10, 100, 24, 1, true)]
    [InlineData(100, 60, 600, 360, 1.5, false)]
    [InlineData(0, 0, 1280, 720, 2.5, true)]
    public void SelectionToolsRemainVisibleAndSeparateNearScreenEdges(int x, int y, int width, int height, double scale, bool expanded) => ImagingTests.Sta(() =>
    {
        var window = new CaptureWindow(Image(1280, 720), new PixelRect(-1280, -80, 1280, 720), false);
        var content = (Grid)window.Content;
        var size = new Size(1280 / scale, 720 / scale);
        content.Measure(size); content.Arrange(new Rect(new Point(), size)); content.UpdateLayout();
        window.SetSelection(new PixelRect(x - 1280, y - 80, width, height)); content.UpdateLayout();
        var canvas = content.Children.OfType<Canvas>().Single();
        var info = canvas.Children.OfType<SelectionInfoBar>().Single();
        info.SetExpanded(expanded); content.UpdateLayout();
        var actions = canvas.Children.OfType<FloatingToolbar>().Single(p => p.Name == "CaptureActions");
        var modes = canvas.Children.OfType<FloatingToolbar>().Single(p => p.Name == "CaptureModes");
        var infoRect = new Rect(Canvas.GetLeft(info), Canvas.GetTop(info), info.ActualWidth, info.ActualHeight);
        var actionRect = new Rect(Canvas.GetLeft(actions), Canvas.GetTop(actions), actions.ActualWidth, actions.ActualHeight);
        var modeRect = new Rect(Canvas.GetLeft(modes), Canvas.GetTop(modes), modes.ActualWidth, modes.ActualHeight);
        Assert.True(info.ActualWidth > 0 && actions.ActualWidth > 0);
        Assert.True(new Rect(new Point(), size).Contains(infoRect));
        Assert.True(new Rect(new Point(), size).Contains(actionRect));
        Assert.True(new Rect(new Point(), size).Contains(modeRect));
        Assert.False(infoRect.IntersectsWith(actionRect));
        Assert.False(infoRect.IntersectsWith(modeRect)); Assert.False(actionRect.IntersectsWith(modeRect));
        Assert.All(Descendants(actions).OfType<Button>(), b => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(b))));
        var previewDirectory = Environment.GetEnvironmentVariable("DESK_SNIP_TEST_PREVIEW_DIR");
        if (!string.IsNullOrEmpty(previewDirectory))
        {
            Directory.CreateDirectory(previewDirectory);
            var output = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
            output.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(output));
            using (var stream = File.Create(Path.Combine(previewDirectory, $"selection-{x}-{y}-{scale}-{expanded}.png"))) encoder.Save(stream);
        }
        window.Close();
    });

    [Fact] public void RecordingMarkingToolsWorkBeforeStartAndExposeLiveAnnotations() => ImagingTests.Sta(() =>
    {
        var window = new CaptureWindow(Image(800, 500), new PixelRect(0, 0, 800, 500), true);
        try
        {
            Assert.True(window.AllowsTransparency); Assert.Same(Brushes.Transparent, window.Background);
            var content = (Grid)window.Content; content.Measure(new Size(800, 500)); content.Arrange(new Rect(0, 0, 800, 500)); content.UpdateLayout();
            window.SetSelection(new PixelRect(40, 40, 400, 300)); content.UpdateLayout();
            var buttons = Descendants(content).OfType<Button>().ToArray();
            var shape = buttons.Single(button => (AutomationProperties.GetName(button) ?? "").StartsWith("录制标注：形状"));
            Assert.True(shape.IsEnabled);
            ((Action)shape.Tag!)(); Assert.Equal(AnnotationKind.Rectangle, window.RecordingMarkingKind);
            Assert.NotNull(window.RecordingCanvas); Assert.True(window.RecordingCanvas!.IsHitTestVisible);
            window.RecordingCanvas.Document.Add(new Annotation(AnnotationKind.Rectangle, new[] { new PixelPoint(5, 5), new PixelPoint(50, 40) }, 0xffff0000, 3));
            window.RecordingCanvas.Refresh();
            Assert.Single(window.RecordingAnnotations);
            content.UpdateLayout();
            Assert.Equal(AnnotationKind.Rectangle, window.RecordingMarkingKind);
            Assert.Same(FloatingTools.Selected, shape.Background);
            Preview(content, 800, 500, "recording-markup.png");
            var exit = buttons.Single(button => AutomationProperties.GetName(button) == "移动选区 / 结束标注");
            exit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Null(window.RecordingMarkingKind); Assert.False(window.RecordingCanvas!.IsHitTestVisible);
            var recordBar = Descendants(content).OfType<FloatingToolbar>().Single(toolbar => toolbar.Name == "CaptureRecordActions");
            var start = recordBar.Items.Children.OfType<Button>().Single(button => AutomationProperties.GetName(button) == "开始录制");
            Assert.InRange(recordBar.Items.Children.IndexOf(start), 0, 1);
            var dot = Assert.IsType<System.Windows.Shapes.Ellipse>(start.Content);
            Assert.Equal(FloatingTools.RecordDotSize, dot.Width); Assert.Equal(FloatingTools.RecordDotSize, dot.Height);
            var time = Descendants(recordBar).OfType<TextBlock>().Single(text => text.Text == "00:00");
            Assert.Equal(FloatingTools.RecordTimeFontSize, time.FontSize); Assert.Equal(FloatingTools.RecordTimeMinWidth, time.MinWidth); Assert.True(double.IsNaN(time.Width));
            var fpsText = Descendants(recordBar).OfType<TextBlock>().Single(text => text.Text.EndsWith(" FPS"));
            Assert.Equal(FloatingTools.RecordFpsFontSize, fpsText.FontSize); Assert.Same(FloatingTools.MutedInk, fpsText.Foreground);
            var fpsButton = recordBar.Items.Children.OfType<Button>().Single(button => AutomationProperties.GetName(button) == "录制帧率");
            Assert.True(double.IsNaN(fpsButton.Width));
        }
        finally { window.Close(); }
    });

    [Fact] public void InlineEditingStartsInSelectModeUnlessToolIsRemembered() => ImagingTests.Sta(() =>
    {
        var window = new CaptureWindow(Image(800, 500), new PixelRect(0, 0, 800, 500), false);
        try
        {
            var content = (Grid)window.Content; content.Measure(new Size(800, 500)); content.Arrange(new Rect(0, 0, 800, 500)); content.UpdateLayout();
            window.SetSelection(new PixelRect(40, 40, 400, 300)); content.UpdateLayout();
            window.BeginInlineEdit(null);
            Assert.Null(window.InlineCanvas!.Kind); Assert.Null(window.LastInlineTool);
            var select = Descendants(content).OfType<InlineAnnotationBar>().Single().Items.Children.OfType<Button>().Single(button => AutomationProperties.GetName(button) == "选择");
            Assert.Same(FloatingTools.Selected, select.Background);
        }
        finally { window.Close(); }
        var remembered = new CaptureWindow(Image(800, 500), new PixelRect(0, 0, 800, 500), false, settings: new AppSettings { MarkRememberScreenshotTool = true, MarkLastScreenshotTool = "Ellipse" });
        try
        {
            remembered.SetSelection(new PixelRect(40, 40, 400, 300));
            remembered.BeginInlineEdit(null);
            Assert.Equal(AnnotationKind.Ellipse, remembered.InlineCanvas!.Kind);
        }
        finally { remembered.Close(); }
    });

    [Fact] public void InlineAnnotationToolbarSitsAtSelectionBottomLeftWithCaptureToolbarRight() => ImagingTests.Sta(() =>
    {
        var window = new CaptureWindow(Image(1200, 800), new PixelRect(0, 0, 1200, 800), false);
        try
        {
            var content = (Grid)window.Content; content.Measure(new Size(1200, 800)); content.Arrange(new Rect(0, 0, 1200, 800)); content.UpdateLayout();
            window.SetSelection(new PixelRect(40, 40, 600, 350)); content.UpdateLayout();
            window.BeginInlineEdit(AnnotationKind.Rectangle); content.UpdateLayout();
            window.InlineCanvas!.Document.Add(new Annotation(AnnotationKind.Rectangle, new[] { new PixelPoint(10, 10), new PixelPoint(80, 60) }, 0xffff0000, 3)); window.InlineCanvas.Refresh(); content.UpdateLayout();
            Preview(content, 1200, 800, "inline-toolbar.png");
            var actions = Descendants(content).OfType<FloatingToolbar>().Single(bar => bar.Name == "CaptureActions");
            var inline = Descendants(content).OfType<InlineAnnotationBar>().Single();
            var actionRect = actions.TransformToAncestor(content).TransformBounds(new Rect(new Point(), actions.RenderSize));
            var inlineRect = inline.TransformToAncestor(content).TransformBounds(new Rect(new Point(), inline.RenderSize));
            Assert.Equal(40, inlineRect.Left, 1);
            Assert.True(actionsRectIsRightOf(inlineRect, actionRect), $"inline={inlineRect} actions={actionRect}");
            Assert.True(Math.Abs(inlineRect.Top - actionRect.Top) < 1, $"inline={inlineRect} actions={actionRect}");
            var list = Descendants(content).OfType<AnnotationListBar>().Single();
            Assert.Equal(Visibility.Visible, list.Visibility);
            var listRect = list.TransformToAncestor(content).TransformBounds(new Rect(new Point(), list.RenderSize));
            Assert.Equal(40, listRect.Top, 1);
            var item = Descendants(list).OfType<Button>().Single();
            Assert.Equal("1 · 矩形", AutomationProperties.GetName(item));
            item.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Assert.Equal(0, window.InlineCanvas.SelectedIndex);
            window.InlineCanvas.Document.Add(new Annotation(AnnotationKind.Ellipse, new[] { new PixelPoint(20, 20), new PixelPoint(90, 70) }, 0xff00ff00, 3));
            window.InlineCanvas.Refresh(); content.UpdateLayout();
            var items = Descendants(list).OfType<Button>().ToArray();
            Assert.Equal(2, items.Length);
            Assert.Equal("2 · 椭圆", AutomationProperties.GetName(items[1]));
            items[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Assert.Equal(1, window.InlineCanvas.SelectedIndex);
        }
        finally { window.Close(); }
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RightLongPressMovesSelectionAndKeepsAnnotationCanvas(bool recording) => ImagingTests.Sta(() =>
    {
        var window = new CaptureWindow(Image(800, 500), new PixelRect(0, 0, 800, 500), recording);
        try
        {
            window.SetSelection(new PixelRect(40, 40, 400, 300));
            if (recording) window.SetRecordingMarking(AnnotationKind.Rectangle); else window.BeginInlineEdit(AnnotationKind.Rectangle);
            var canvas = recording ? window.RecordingCanvas! : window.InlineCanvas!;
            canvas.Document.Add(new Annotation(AnnotationKind.Rectangle, new[] { new PixelPoint(5, 5), new PixelPoint(40, 30) }, 0xffff0000, 3));
            var start = new PixelPoint(100, 100); Assert.True(window.BeginRightDrag(start));
            Assert.False(window.UpdateRightDrag(new PixelPoint(150, 150), 199)); Assert.True(window.EndRightDrag(start, 100)); Assert.Equal(new PixelRect(40, 40, 400, 300), window.Selection);
            Assert.True(window.BeginRightDrag(start)); Assert.True(window.UpdateRightDrag(new PixelPoint(120, 110), 250));
            Assert.Equal(new PixelRect(60, 50, 400, 300), window.Selection); Assert.Same(canvas, recording ? window.RecordingCanvas : window.InlineCanvas); Assert.Single(canvas.Document.Items);
            Assert.False(window.EndRightDrag(new PixelPoint(120, 110), 260));
        }
        finally { window.Close(); }
    });

    [Fact] public void SwitchingCaptureToRecordingRestoresLiveScreenAndLeavesInteriorClickThrough() => ImagingTests.Sta(() =>
    {
        var window = new CaptureWindow(Image(800, 500), new PixelRect(0, 0, 800, 500), false);
        try
        {
            var content = (Grid)window.Content; content.Measure(new Size(800, 500)); content.Arrange(new Rect(0, 0, 800, 500)); content.UpdateLayout();
            window.SetSelection(new PixelRect(40, 40, 400, 300)); content.UpdateLayout();
            Assert.True(window.AllowsTransparency); Assert.False(window.LiveScreenPreview);
            Assert.Equal(Visibility.Visible, window.DesktopSnapshotVisibility); Assert.True(window.MaskAlpha > 8);
            window.SwitchToRecording(); content.UpdateLayout();
            Assert.True(window.RecordingMode); Assert.True(window.LiveScreenPreview);
            Assert.Equal(Visibility.Collapsed, window.DesktopSnapshotVisibility);
            Assert.Equal(1, window.MaskAlpha); Assert.True(window.EdgeGrabVisible); Assert.False(window.MaskVisible);
            Assert.True(window.IsRecordingInterior(new PixelPoint(200, 200)));
            Assert.False(window.IsRecordingInterior(new PixelPoint(40, 200)));
            Assert.False(window.IsRecordingInterior(new PixelPoint(239, 44)));
            Assert.False(window.IsRecordingInterior(new PixelPoint(240, 340)));
            Assert.False(window.IsRecordingInterior(new PixelPoint(20, 20)));
        }
        finally { window.Close(); }
    });

    [Fact] public void RecordingMarkingRestoresSelectionInputOnlyWhileToolIsActive() => ImagingTests.Sta(() =>
    {
        var window = new CaptureWindow(Image(800, 500), new PixelRect(0, 0, 800, 500), false);
        try
        {
            var content = (Grid)window.Content; content.Measure(new Size(800, 500)); content.Arrange(new Rect(0, 0, 800, 500)); content.UpdateLayout();
            window.SetSelection(new PixelRect(40, 40, 400, 300)); content.UpdateLayout();
            window.SwitchToRecording(); content.UpdateLayout();
            window.SetRecordingMarking(AnnotationKind.Rectangle); content.UpdateLayout();
            Assert.True(window.RecordingInputPlateVisible); Assert.False(window.IsRecordingInterior(new PixelPoint(200, 200)));
            var pixel = new byte[4]; window.RecordingCanvas!.Image.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
            Assert.Equal(0, pixel[3]);
            window.SetRecordingMarking(null); content.UpdateLayout();
            Assert.False(window.RecordingInputPlateVisible); Assert.True(window.IsRecordingInterior(new PixelPoint(200, 200)));
            window.SetSelection(new PixelRect(80, 60, 200, 100)); content.UpdateLayout();
            Assert.Null(window.RecordingCanvas); Assert.False(window.RecordingInputPlateVisible);
        }
        finally { window.Close(); }
    });

    [Fact] public void SwitchingToRecordingDuringInlineEditingCarriesMarkupAndRestoresPassThrough() => ImagingTests.Sta(() =>
    {
        var window = new CaptureWindow(Image(800, 500), new PixelRect(0, 0, 800, 500), false);
        try
        {
            var content = (Grid)window.Content; content.Measure(new Size(800, 500)); content.Arrange(new Rect(0, 0, 800, 500)); content.UpdateLayout();
            window.SetSelection(new PixelRect(40, 40, 400, 300)); content.UpdateLayout();
            window.BeginInlineEdit(AnnotationKind.Rectangle);
            window.InlineCanvas!.Document.Add(new Annotation(AnnotationKind.Rectangle, new[] { new PixelPoint(20, 20), new PixelPoint(120, 90) }, 0xffff0000, 3));
            window.SwitchToRecording(); content.UpdateLayout();
            Assert.True(window.RecordingMode); Assert.True(window.LiveScreenPreview);
            Assert.Null(window.InlineCanvas); Assert.NotNull(window.RecordingCanvas);
            Assert.Single(window.RecordingAnnotations); Assert.Equal(AnnotationKind.Rectangle, window.RecordingAnnotations[0].Kind);
            Assert.Null(window.RecordingMarkingKind); Assert.False(window.RecordingInputPlateVisible);
            Assert.True(window.IsRecordingInterior(new PixelPoint(200, 200))); Assert.True(window.EdgeGrabVisible);
            Assert.False(window.RecordingBackdropVisible);
        }
        finally { window.Close(); }
    });

    [Fact] public void SwitchingToRecordingWithoutSelectionKeepsAFullScreenInputLayer() => ImagingTests.Sta(() =>
    {
        var window = new CaptureWindow(Image(800, 500), new PixelRect(0, 0, 800, 500), false);
        try
        {
            var content = (Grid)window.Content; content.Measure(new Size(800, 500)); content.Arrange(new Rect(0, 0, 800, 500)); content.UpdateLayout();
            window.SwitchToRecording(); content.UpdateLayout();
            Assert.True(window.RecordingMode); Assert.True(window.Selection.IsEmpty);
            Assert.Equal(Visibility.Collapsed, window.DesktopSnapshotVisibility); Assert.True(window.RecordingBackdropVisible); Assert.True(window.MaskVisible);
            window.BeginInteraction(new PixelPoint(100, 120));
            window.UpdateInteraction(new PixelPoint(260, 300), false, false, false); window.EndInteraction();
            Assert.Equal(new PixelRect(100, 120, 160, 180), window.Selection);
            content.UpdateLayout(); Assert.False(window.RecordingBackdropVisible); Assert.False(window.MaskVisible);
        }
        finally { window.Close(); }
    });

    [Fact] public void RecordingAfterSelectionLocksOuterClicksButKeepsBorderAdjustments() => ImagingTests.Sta(() =>
    {
        var window = new CaptureWindow(Image(800, 500), new PixelRect(0, 0, 800, 500), true);
        try
        {
            var content = (Grid)window.Content; content.Measure(new Size(800, 500)); content.Arrange(new Rect(0, 0, 800, 500)); content.UpdateLayout();
            window.SetSelection(new PixelRect(40, 40, 400, 300)); content.UpdateLayout();
            Assert.False(window.RecordingBackdropVisible); Assert.False(window.MaskVisible); Assert.True(window.EdgeGrabVisible);
            Assert.True(window.IsRecordingOutsideLocked(new PixelPoint(10, 10)));
            Assert.True(window.IsRecordingOutsideLocked(new PixelPoint(20, 200)));
            Assert.False(window.IsRecordingOutsideLocked(new PixelPoint(36, 200)));
            Assert.False(window.IsRecordingOutsideLocked(new PixelPoint(200, 200)));
            Assert.False(window.IsRecordingOutsideLocked(new PixelPoint(40, 200)));
        }
        finally { window.Close(); }
    });

    [Fact] public void GReturnsToScreenshotWithMarkupAndBackToRecording() => ImagingTests.Sta(() =>
    {
        var window = new CaptureWindow(Image(800, 500), new PixelRect(0, 0, 800, 500), false);
        try
        {
            var content = (Grid)window.Content; content.Measure(new Size(800, 500)); content.Arrange(new Rect(0, 0, 800, 500)); content.UpdateLayout();
            window.SetSelection(new PixelRect(40, 40, 400, 300)); content.UpdateLayout();
            window.ToggleRecording(); content.UpdateLayout();
            window.SetRecordingMarking(AnnotationKind.Rectangle); content.UpdateLayout();
            window.RecordingCanvas!.Document.Add(new Annotation(AnnotationKind.Rectangle, new[] { new PixelPoint(20, 20), new PixelPoint(120, 90) }, 0xffff0000, 3));
            window.ToggleRecording(); content.UpdateLayout();
            Assert.False(window.RecordingMode); Assert.Null(window.RecordingCanvas);
            Assert.Equal(Visibility.Visible, window.DesktopSnapshotVisibility); Assert.True(window.MaskVisible); Assert.True(window.MaskAlpha > 8);
            Assert.True(window.InlineEditing); Assert.NotNull(window.InlineCanvas);
            Assert.Single(window.InlineCanvas!.Document.Items); Assert.Equal(AnnotationKind.Rectangle, window.InlineCanvas.Document.Items[0].Kind);
            Assert.Null(window.RecordingMarkingKind);
            window.ToggleRecording(); content.UpdateLayout();
            Assert.True(window.RecordingMode); Assert.Null(window.InlineCanvas); Assert.NotNull(window.RecordingCanvas);
            Assert.Single(window.RecordingAnnotations); Assert.False(window.MaskVisible);
        }
        finally { window.Close(); }
    });

    private static bool actionsRectIsRightOf(Rect inline, Rect actions) => actions.Left >= inline.Right - 1 && actions.Left <= inline.Right + 8;

    [Fact] public void CaptureToolbarWithoutMarkupToolAlignsToSelectionLeft() => ImagingTests.Sta(() =>
    {
        var window = new CaptureWindow(Image(1200, 800), new PixelRect(0, 0, 1200, 800), false);
        try
        {
            var content = (Grid)window.Content; content.Measure(new Size(1200, 800)); content.Arrange(new Rect(0, 0, 1200, 800)); content.UpdateLayout();
            window.SetSelection(new PixelRect(300, 80, 400, 220)); content.UpdateLayout();
            var actions = Descendants(content).OfType<FloatingToolbar>().Single(bar => bar.Name == "CaptureActions");
            var rect = actions.TransformToAncestor(content).TransformBounds(new Rect(new Point(), actions.RenderSize));
            Assert.Equal(300, rect.Left, 1);
            Assert.True(rect.Top >= 300);
            var buttons = Descendants(actions).OfType<Button>().ToArray();
            var dragIndex = Array.FindIndex(buttons, button => AutomationProperties.GetName(button) == "拖动截图面板");
            var cancelIndex = Array.FindIndex(buttons, button => AutomationProperties.GetName(button) == "取消");
            Assert.True(dragIndex >= 0 && cancelIndex > dragIndex);
            var recordBar = Descendants(content).OfType<FloatingToolbar>().Single(bar => bar.Name == "CaptureRecordActions");
            Assert.Contains(Descendants(recordBar).OfType<Button>(), button => AutomationProperties.GetName(button) == "拖动录制面板");
        }
        finally { window.Close(); }
    });

    [Fact] public void RecordingStartHandsOffToTransitionWithoutClosingSelectionFirst() => ImagingTests.Sta(() =>
    {
        var window = new CaptureWindow(Image(800, 500), new PixelRect(0, 0, 800, 500), true);
        CaptureWindow? handedOff = null;
        try
        {
            var content = (Grid)window.Content; content.Measure(new Size(800, 500)); content.Arrange(new Rect(0, 0, 800, 500)); content.UpdateLayout();
            window.SetSelection(new PixelRect(40, 40, 400, 300)); content.UpdateLayout();
            window.RecordTransitionRequested = value => handedOff = value;
            var start = Descendants(content).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "开始录制");
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(window, handedOff);
            Assert.Equal(CaptureAction.Record, window.ActionResult);
        }
        finally { window.Close(); }
    });

    [Fact] public void LongScreenshotStartHandsOffWithoutClosingOrRebuildingAnnotations() => ImagingTests.Sta(() =>
    {
        var window = new CaptureWindow(Image(800, 500), new PixelRect(0, 0, 800, 500), false);
        CaptureWindow? handedOff = null;
        try
        {
            var content = (Grid)window.Content; content.Measure(new Size(800, 500)); content.Arrange(new Rect(0, 0, 800, 500)); content.UpdateLayout();
            window.SetSelection(new PixelRect(40, 40, 400, 300)); content.UpdateLayout();
            window.BeginInlineEdit(AnnotationKind.Rectangle);
            window.InlineCanvas!.Document.Add(new Annotation(AnnotationKind.Rectangle, new[] { new PixelPoint(10, 10), new PixelPoint(40, 40) }, 0xff0000ff, 3));
            var document = window.InlineDocument;
            window.LongScreenshotTransitionRequested = value => handedOff = value;
            var start = Descendants(content).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "长截图");
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(window, handedOff); Assert.Equal(CaptureAction.LongScreenshot, window.ActionResult); Assert.Same(document, window.InlineDocument);
            window.Show(); window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            var handle = new WindowInteropHelper(window).Handle;
            window.HideForLongScreenshotTransition(); Assert.True(window.IsVisible); Assert.False(Native.IsWindowVisible(handle));
            window.ResumeFromLongScreenshot(); Assert.True(window.IsVisible); Assert.True(Native.IsWindowVisible(handle)); Assert.Same(document, window.InlineDocument);
        }
        finally { window.Close(); }
    });

    [Fact] public void LongScreenshotNativeHidingKeepsModalCompletionAndCancellationValid() => ImagingTests.Sta(() =>
    {
        var completed = new CaptureWindow(Image(800, 500), new PixelRect(0, 0, 800, 500), false);
        completed.Loaded += (_, _) =>
        {
            var handle = new WindowInteropHelper(completed).Handle;
            completed.HideForLongScreenshotTransition();
            Assert.True(completed.IsVisible); Assert.False(Native.IsWindowVisible(handle));
            completed.FinishLongScreenshotTransition();
        };
        Assert.True(completed.ShowDialog());
        var cancelled = new CaptureWindow(Image(800, 500), new PixelRect(0, 0, 800, 500), false);
        cancelled.Loaded += (_, _) =>
        {
            var handle = new WindowInteropHelper(cancelled).Handle;
            cancelled.HideForLongScreenshotTransition(); cancelled.ResumeFromLongScreenshot();
            Assert.True(cancelled.IsVisible); Assert.True(Native.IsWindowVisible(handle));
            cancelled.CancelLongScreenshotTransition();
        };
        Assert.False(cancelled.ShowDialog());
    });

    [Fact] public void AnnotationListMovesLeftAndMarksOverflowWithoutScrollbar() => ImagingTests.Sta(() =>
    {
        var window = new CaptureWindow(Image(1200, 800), new PixelRect(0, 0, 1200, 800), false);
        try
        {
            var content = (Grid)window.Content; content.Measure(new Size(1200, 800)); content.Arrange(new Rect(0, 0, 1200, 800)); content.UpdateLayout();
            window.SetSelection(new PixelRect(300, 80, 400, 220)); content.UpdateLayout();
            window.BeginInlineEdit(AnnotationKind.Rectangle); content.UpdateLayout();
            for (var index = 0; index < 40; index++) window.InlineCanvas!.Document.Add(new Annotation(AnnotationKind.Rectangle, new[] { new PixelPoint(10, 10), new PixelPoint(40, 40) }, 0xff0000ff, 3));
            window.InlineCanvas!.Refresh(); content.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            var list = Descendants(content).OfType<AnnotationListBar>().Single();
            var listRect = list.TransformToAncestor(content).TransformBounds(new Rect(new Point(), list.RenderSize));
            Assert.True(listRect.Right <= 300, $"list={listRect}");
            Assert.Equal(80, listRect.Top, 1);
            Assert.True(listRect.Bottom <= 792, $"list={listRect}");
            var scroll = Descendants(list).OfType<ScrollViewer>().Single();
            Assert.Equal(ScrollBarVisibility.Hidden, scroll.VerticalScrollBarVisibility);
            Assert.Contains(Descendants(list).OfType<TextBlock>(), text => text.Text == "…" && text.Visibility == Visibility.Visible);
        }
        finally { window.Close(); }
    });

    [Fact] public void InlineCanvasRebuildKeepsAnnotationsAtScreenPositionsWhenSelectionResized() => ImagingTests.Sta(() =>
    {
        var window = new CaptureWindow(Image(1200, 800), new PixelRect(0, 0, 1200, 800), false);
        try
        {
            var content = (Grid)window.Content; content.Measure(new Size(1200, 800)); content.Arrange(new Rect(0, 0, 1200, 800)); content.UpdateLayout();
            window.SetSelection(new PixelRect(40, 40, 200, 150)); content.UpdateLayout();
            window.BeginInlineEdit(AnnotationKind.Rectangle); content.UpdateLayout();
            window.InlineCanvas!.Document.Add(new Annotation(AnnotationKind.Rectangle, new[] { new PixelPoint(10, 10), new PixelPoint(40, 30) }, 0xffff0000, 3));
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            typeof(CaptureWindow).GetProperty("Selection", flags)!.SetValue(window, new PixelRect(60, 50, 200, 150));
            typeof(CaptureWindow).GetMethod("RebuildInlineForSelection", flags)!.Invoke(window, new object[] { new PixelRect(40, 40, 200, 150) });
            content.UpdateLayout();
            var item = window.InlineCanvas!.Document.Items.Single();
            Assert.Equal(new PixelPoint(-10, 0), item.Points[0]);
            Assert.Equal(new PixelPoint(20, 20), item.Points[1]);
        }
        finally { window.Close(); }
    });

    [Fact] public void InlineResizeOnlyStartsForSelectionEdges()
    {
        Assert.False(CaptureWindow.IsEdgeHandle(SelectionHandle.None));
        Assert.False(CaptureWindow.IsEdgeHandle(SelectionHandle.Move));
        Assert.True(CaptureWindow.IsEdgeHandle(SelectionHandle.Left));
        Assert.True(CaptureWindow.IsEdgeHandle(SelectionHandle.Right | SelectionHandle.Bottom));
    }

    [Fact] public void CaptureSceneShowsCompactToolsAndTheSelectedToolOpensInEditor() => ImagingTests.Sta(() =>
    {
        var pixels = new byte[1220 * 720 * 4]; for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 54; pixels[i + 1] = 40; pixels[i + 2] = 36; pixels[i + 3] = 255; }
        var desktop = BitmapSource.Create(1220, 720, 96, 96, PixelFormats.Bgra32, null, pixels, 1220 * 4); desktop.Freeze();
        var window = new CaptureWindow(desktop, new PixelRect(0, 0, 1220, 720), false, recent: new[] { new PixelRect(100, 80, 500, 300) });
        var content = (Grid)window.Content; content.Measure(new Size(1220, 720)); content.Arrange(new Rect(0, 0, 1220, 720)); content.UpdateLayout(); window.SetSelection(new PixelRect(36, 46, 1120, 590)); content.UpdateLayout();
        var info = Descendants(content).OfType<SelectionInfoBar>().Single(); Assert.True(info.ActualHeight < 35);
        var toolbar = Descendants(content).OfType<FloatingToolbar>().Single(bar => bar.Name == "CaptureActions"); Assert.True(toolbar.ActualHeight < 50);
        var directory = Environment.GetEnvironmentVariable("DESK_SNIP_TEST_PREVIEW_DIR"); if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory); var image = new RenderTargetBitmap(1220, 720, 96, 96, PixelFormats.Pbgra32); image.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var output = File.Create(Path.Combine(directory, "capture-scene.png"))) encoder.Save(output);
        }
        var kind = CaptureWindow.AnnotationTool(CaptureAction.EditText); Assert.Equal(AnnotationKind.Text, kind); var editor = new EditorWindow(Image(200, 120), _ => { }, _ => null!, initialTool: kind);
        Assert.Equal(AnnotationKind.Text, Descendants((DependencyObject)editor.Content).OfType<AnnotationCanvas>().Single().Kind); editor.Close(); window.Close();
    });

    [Fact] public void CaptureToolbarSwitchesToRecordingAndExposesDirectTools() => ImagingTests.Sta(() =>
    {
        var window = new CaptureWindow(Image(400, 300), new PixelRect(0, 0, 400, 300), false);
        try
        {
            var content = (Grid)window.Content; content.Measure(new Size(400, 300)); content.Arrange(new Rect(0, 0, 400, 300)); content.UpdateLayout();
            window.SetSelection(new PixelRect(20, 20, 200, 150)); content.UpdateLayout();
            var buttons = Descendants(content).OfType<Button>().ToArray();
            var shape = buttons.Single(button => (AutomationProperties.GetName(button) ?? "").StartsWith("形状标注"));
            Assert.IsType<Action>(shape.Tag);
            Assert.Contains(buttons, button => AutomationProperties.GetName(button) == "序号标注");
            var number = buttons.Single(button => AutomationProperties.GetName(button) == "序号标注");
            number.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(window.InlineEditing); Assert.Equal(AnnotationKind.Number, window.InlineCanvas!.Kind);
        }
        finally { window.Close(); }
    });

    [Fact] public void CaptureShapeGroupUsesItsPrimaryToolAndCameraSwitchesRecordingToolbar() => ImagingTests.Sta(() =>
    {
        var window = new CaptureWindow(Image(400, 300), new PixelRect(0, 0, 400, 300), false);
        try
        {
            var content = (Grid)window.Content; content.Measure(new Size(400, 300)); content.Arrange(new Rect(0, 0, 400, 300)); content.UpdateLayout();
            window.SetSelection(new PixelRect(20, 20, 200, 150)); content.UpdateLayout();
            var shape = Descendants(content).OfType<Button>().Single(button => (AutomationProperties.GetName(button) ?? "").StartsWith("形状标注"));
            ((Action)shape.Tag!)(); Assert.True(window.InlineEditing); Assert.Equal(AnnotationKind.Rectangle, window.InlineCanvas!.Kind);
        }
        finally { window.Close(); }
        var second = new CaptureWindow(Image(400, 300), new PixelRect(0, 0, 400, 300), false);
        try
        {
            var content = (Grid)second.Content; content.Measure(new Size(400, 300)); content.Arrange(new Rect(0, 0, 400, 300)); content.UpdateLayout();
            second.SetSelection(new PixelRect(20, 20, 200, 150)); content.UpdateLayout();
            var camera = Descendants(content).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "切换到录屏");
            camera.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); content.UpdateLayout();
            Assert.Equal("DeskSnip · 选择录制区域", second.Title);
            Assert.Equal(Visibility.Collapsed, Descendants(content).OfType<FloatingToolbar>().Single(toolbar => toolbar.Name == "CaptureActions").Visibility);
            Assert.Equal(Visibility.Visible, Descendants(content).OfType<FloatingToolbar>().Single(toolbar => toolbar.Name == "CaptureRecordActions").Visibility);
            Assert.Contains(Descendants(content).OfType<Button>(), button => AutomationProperties.GetName(button) == "开始录制");
            var recordBar = Descendants(content).OfType<FloatingToolbar>().Single(toolbar => toolbar.Name == "CaptureRecordActions");
            var recordStart = recordBar.Items.Children.OfType<Button>().Single(button => AutomationProperties.GetName(button) == "开始录制");
            Assert.InRange(recordBar.Items.Children.IndexOf(recordStart), 0, 1);
            Preview(content, 400, 300, "record-toolbar.png");
            var mode = Descendants(content).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "录制模式");
            var format = Descendants(content).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "录制格式");
            var gif = format.ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "GIF"));
            gif.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Assert.Equal("GIF", second.Plan.Format);
            var quick = mode.ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "快速录制"));
            quick.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(second.Plan.Quick); Assert.Equal("MP4", second.Plan.Format); Assert.False(format.IsEnabled);
            var normal = mode.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "普通录制"));
            normal.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Assert.Equal("GIF", second.Plan.Format); Assert.True(format.IsEnabled);
            Assert.Contains(Descendants(content).OfType<Button>(), button => AutomationProperties.GetName(button) == "录制帧率");
            var mic = Descendants(content).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "麦克风：未开启");
            mic.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.True(second.Plan.Microphone);
            Assert.Contains(Descendants(content).OfType<Button>(), button => AutomationProperties.GetName(button) == "麦克风：已开启");
        }
        finally { second.Close(); }
    });

    [Fact] public void InlineEditingFlattensAnnotationsOntoSelectedPixels() => ImagingTests.Sta(() =>
    {
        var pixels = new byte[200 * 120 * 4]; for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 40; pixels[i + 1] = 30; pixels[i + 2] = 20; pixels[i + 3] = 255; }
        var desktop = BitmapSource.Create(200, 120, 96, 96, PixelFormats.Bgra32, null, pixels, 200 * 4); desktop.Freeze();
        var window = new CaptureWindow(desktop, new PixelRect(0, 0, 200, 120), false);
        try
        {
            var content = (Grid)window.Content; content.Measure(new Size(200, 120)); content.Arrange(new Rect(0, 0, 200, 120)); content.UpdateLayout();
            window.SetSelection(new PixelRect(20, 10, 100, 60)); content.UpdateLayout();
            window.BeginInlineEdit(AnnotationKind.Rectangle);
            Assert.True(window.InlineEditing); Assert.NotNull(window.InlineCanvas);
            window.InlineCanvas!.Document.Add(new Annotation(AnnotationKind.Rectangle, new[] { new PixelPoint(5, 5), new PixelPoint(45, 35) }, 0xff0000ff, 3, appearance: new AnnotationAppearance(fill: true)));
            window.InlineCanvas.Refresh();
            var output = window.BuildInlineOutput()!;
            Assert.Equal(100, output.PixelWidth); Assert.Equal(60, output.PixelHeight);
            var pixel = new byte[4]; output.CopyPixels(new Int32Rect(20, 20, 1, 1), pixel, 4, 0);
            Assert.True(pixel[0] > 200); Assert.True(pixel[2] < 80);
        }
        finally { window.Close(); }
    });

    [Fact] public void ToolbarGroupUsesAndRemembersItsDefaultTool() => ImagingTests.Sta(() =>
    {
        AnnotationToolDefaults.Load("");
        try
        {
            var canvas = new AnnotationCanvas(Image(120, 80));
            var bar = new InlineAnnotationBar(canvas, new AppSettings(), withFinish: false, showShortcuts: false);
            bar.Measure(new Size(1200, 200)); bar.Arrange(new Rect(0, 0, 1200, 200)); bar.UpdateLayout();
            var group = bar.Items.Children.OfType<Button>().Single(button => (AutomationProperties.GetName(button) ?? "").StartsWith("形状工具"));
            ((Action)group.Tag!)(); Assert.Equal(AnnotationKind.Rectangle, canvas.Kind);
            var ellipse = group.ContextMenu!.Items.OfType<MenuItem>().Single(item => item.Tag is AnnotationKind kind && kind == AnnotationKind.Ellipse);
            ellipse.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(AnnotationKind.Ellipse, canvas.Kind); Assert.Equal(AnnotationKind.Ellipse, AnnotationToolDefaults.Get("Rectangle", AnnotationKind.Rectangle));
            canvas.SetTool(AnnotationKind.Rectangle); ((Action)group.Tag!)(); Assert.Equal(AnnotationKind.Ellipse, canvas.Kind);
            Assert.Contains("Rectangle=Ellipse", AnnotationToolDefaults.Serialize());
        }
        finally { AnnotationToolDefaults.Load(""); }
    });

    private static void Preview(Grid content, int width, int height, string name)
    {
        var directory = Environment.GetEnvironmentVariable("DESK_SNIP_TEST_PREVIEW_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
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
}
