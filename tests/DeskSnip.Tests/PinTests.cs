using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using DeskSnip.App;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public class PinTests
{
    private static BitmapSource Image()
    {
        var pixels = new byte[40 * 30 * 4]; for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var image = BitmapSource.Create(40, 30, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 160); image.Freeze(); return image;
    }
    private static void PreviewGlow(PinWindow pin, string name)
    {
        var directory = Environment.GetEnvironmentVariable("DESK_SNIP_TEST_PREVIEW_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        System.IO.Directory.CreateDirectory(directory);
        var root = (FrameworkElement)pin.Content; var width = pin.Width; var height = pin.Height;
        root.Measure(new Size(width, height)); root.Arrange(new Rect(new Point(), new Size(width, height))); root.UpdateLayout();
        const double scale = 10;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var output = System.IO.File.Create(System.IO.Path.Combine(directory, name)); encoder.Save(output);
    }
    private static void Preview(FrameworkElement element, string name)
    {
        var directory = Environment.GetEnvironmentVariable("DESK_SNIP_TEST_PREVIEW_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        System.IO.Directory.CreateDirectory(directory);
        element.Measure(new Size(420, 900)); element.Arrange(new Rect(new Point(), element.DesiredSize)); element.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth)); var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(element);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var output = System.IO.File.Create(System.IO.Path.Combine(directory, name)); encoder.Save(output);
    }
    [Fact] public void PinContextMenuUsesIconsAndShortcutHintsAndSelectionGlowToggles() => ImagingTests.Sta(() =>
    {
        var pin = new PinWindow(Image());
        try
        {
            var menu = pin.BuildContextMenu();
            var items = menu.Items.OfType<MenuItem>().ToArray();
            Assert.NotEmpty(items);
            Assert.All(items, item => Assert.NotNull(item.Icon));
            Assert.All(items, item => Assert.NotNull(item.Style));
            var copy = items.Single(item => Equals(item.Header, "复制图片")); Assert.Equal("Ctrl+C", copy.InputGestureText);
            var topmost = items.Single(item => Equals(item.Header, "窗口置顶")); Assert.True(topmost.IsCheckable); Assert.Equal("T", topmost.InputGestureText);
            var close = items.Single(item => Equals(item.Header, "关闭（可在控制台恢复）")); Assert.Equal("Esc", close.InputGestureText);
            Preview(menu, "pin-context-menu.png");
            Assert.Equal(Visibility.Collapsed, pin.SelectionGlow.Visibility);
            pin.RefreshAppearance();
            pin.Selected = true;
            Assert.True(pin.SelectionBorderVisible); Assert.Equal(Visibility.Visible, pin.SelectionGlow.Visibility);
            PreviewGlow(pin, "pin-selection-glow.png");
            pin.ToggleSelectionBorder(); Assert.False(pin.SelectionBorderVisible); Assert.Equal(Visibility.Collapsed, pin.SelectionGlow.Visibility);
            pin.ToggleSelectionBorder(); Assert.True(pin.SelectionBorderVisible); Assert.Equal(Visibility.Visible, pin.SelectionGlow.Visibility);
            pin.Selected = false; Assert.Equal(Visibility.Collapsed, pin.SelectionGlow.Visibility);
        }
        finally { pin.Close(); }
    });
    [Fact] public void LockedPinCannotBeResized()
    {
        var pin = new PinPresentation(); pin.Zoom(1); var scale = pin.Scale;
        pin.Locked = true; pin.Zoom(-1); pin.ResetScale(); Assert.Equal(scale, pin.Scale);
    }
    [Fact] public void ScaleAndOpacityAreBounded()
    {
        var pin = new PinPresentation(); for (var i = 0; i < 100; i++) { pin.Zoom(1); pin.Fade(-1); }
        Assert.Equal(8, pin.Scale); Assert.Equal(.1, pin.Opacity);
        for (var i = 0; i < 100; i++) { pin.Zoom(-1); pin.Fade(1); }
        Assert.Equal(.01, pin.Scale); Assert.Equal(1, pin.Opacity);
    }
    [Fact] public void RecoveryAlwaysRestoresInteraction()
    {
        var pin = new PinPresentation { Locked = true, ClickThrough = true }; pin.Fade(-1); pin.RestoreInteraction();
        Assert.False(pin.Locked); Assert.False(pin.ClickThrough); Assert.Equal(1, pin.Opacity);
    }
    [Fact] public void MiddleClickRestoresOriginalThenPreviousView()
    {
        var pin = new PinPresentation(); pin.SetScale(.5); pin.SetOpacity(.35); pin.ToggleOriginal();
        Assert.Equal(1, pin.Scale); Assert.Equal(1, pin.Opacity); pin.ToggleOriginal();
        Assert.Equal(.5, pin.Scale); Assert.Equal(.35, pin.Opacity);
        pin.Locked = true; pin.ToggleOriginal(); Assert.Equal(.5, pin.Scale);
    }
    [Fact] public void FineZoomAndFadeUseIndependentSteps()
    {
        var pin = new PinPresentation(); pin.Zoom(1, true); Assert.Equal(1.01, pin.Scale, 5);
        pin.Fade(-1); Assert.Equal(.95, pin.Opacity);
    }
}
