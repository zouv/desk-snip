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
