using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.App;
using DeskSnip.App.Imaging;
using DeskSnip.App.Recognition;
using DeskSnip.App.Windows;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public class ReferenceBehaviorUiTests
{
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { foreach (var item in LogicalTreeHelper.GetChildren(root)) if (item is DependencyObject child) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static BitmapSource Image(int width = 180, int height = 120)
    { var data = new byte[width * height * 4]; for (var i = 0; i < data.Length; i += 4) { data[i] = 31; data[i + 1] = 63; data[i + 2] = 127; data[i + 3] = 255; } var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, data, width * 4); image.Freeze(); return image; }
    [Fact] public void LongCaptureAndPlaybackReviewUseInPlaceBorderlessWindows() => ImagingTests.Sta(() =>
    {
        var bar = new LongScreenshotBar(new PixelRect(100, 100, 200, 200), new AppSettings(), (_, _) => { }, () => { });
        Assert.Equal(WindowStyle.None, bar.WindowStyle); Assert.True(bar.Topmost); Assert.False(bar.ShowInTaskbar); Assert.Equal(300, bar.Width); Assert.True(bar.Height > 400);
        bar.Close();
        var overlay = new PlaybackWindow("missing.mp4", "MP4", inlineRegion: new PixelRect(100, 100, 200, 200));
        Assert.Equal(WindowStyle.None, overlay.WindowStyle); Assert.True(overlay.Topmost); Assert.False(overlay.ShowInTaskbar); Assert.Equal(WindowStartupLocation.Manual, overlay.WindowStartupLocation);
        var normal = new PlaybackWindow("missing.mp4", "MP4");
        Assert.NotEqual(WindowStyle.None, normal.WindowStyle); Assert.False(normal.Topmost);
    });

    [Fact] public void PinCopyCloseAndEscapeBypassCloseConfirmation() => ImagingTests.Sta(() =>
    {
        var pin = new PinWindow(Image(), settings: new AppSettings { PinShowShadow = false, PinConfirmKeyClose = true, PinConfirmMouseClose = true });
        var copied = false; pin.CopyRequested = _ => copied = true;
        typeof(PinWindow).GetMethod("CopyAndClose", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pin, null);
        Assert.True(copied);
        var escapePin = new PinWindow(Image(), settings: new AppSettings { PinShowShadow = false, PinConfirmKeyClose = true });
        try { typeof(PinWindow).GetMethod("CloseFromEscape", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(escapePin, null); }
        finally { escapePin.Close(); }
    });

    [Fact] public void PinInlineEditingCommitsAnnotationsAndRemembersTool() => ImagingTests.Sta(() =>
    {
        BitmapSource? persisted = null; AnnotationKind? remembered = null;
        var pin = new PinWindow(Image(), persist: value => persisted = value, settings: new AppSettings { PinShowShadow = false });
        try
        {
            pin.RememberToolRequested = kind => remembered = kind;
            pin.BeginInlineEdit(AnnotationKind.Rectangle);
            Assert.True(pin.InlineEditing); Assert.NotNull(pin.InlineCanvas);
            pin.InlineCanvas!.Document.Add(new Annotation(AnnotationKind.Rectangle, new[] { new PixelPoint(20, 20), new PixelPoint(80, 60) }, 0xff0000ff, 3, appearance: new AnnotationAppearance(fill: true)));
            pin.CommitInlineEdit();
            Assert.False(pin.InlineEditing); Assert.Same(persisted, pin.Bitmap); Assert.Equal(AnnotationKind.Rectangle, remembered);
            var pixel = new byte[4]; pin.Bitmap.CopyPixels(new Int32Rect(50, 40, 1, 1), pixel, 4, 0);
            Assert.True(pixel[0] > 200); Assert.True(pixel[2] < 80);
        }
        finally { pin.Close(); }
    });

    [Fact] public void CapturePreferencesChangeControlsAndFrozenCursorSource() => ImagingTests.Sta(() =>
    {
        var clean = Image(800, 600); var cursor = Image(800, 600); var settings = new AppSettings { ScreenshotUiDetection = "None", ScreenshotShowInfoBar = false, ScreenshotShowMagnifier = false, GuideMagnifier = false, ScreenshotGuideVisibility = "Never", ScreenshotShowShortcuts = false, SelectionBorderFollowsTheme = false, SelectionBorderColor = "#F08020", SelectionBorderWidth = 4, SelectionAnchorDisplay = "Four", ScreenshotSecondaryButtons = false, ToolbarIconSize = "Large", ScreenshotCursorVisible = true };
        var window = new CaptureWindow(clean, new PixelRect(-800, 0, 800, 600), false, settings: settings, desktopWithCursor: cursor); var root = (Grid)window.Content;
        root.Measure(new Size(800, 600)); root.Arrange(new Rect(0, 0, 800, 600)); window.SetSelection(new PixelRect(-700, 100, 400, 200)); root.UpdateLayout();
        Assert.Same(cursor, window.SelectedDesktop); Assert.Equal(Visibility.Collapsed, Descendants(root).OfType<SelectionInfoBar>().Single().Visibility); Assert.Equal(Visibility.Collapsed, Descendants(root).OfType<PixelMagnifier>().Single().Visibility);
        Assert.Equal(4, Descendants(root).OfType<System.Windows.Shapes.Ellipse>().Count(handle => handle.Visibility == Visibility.Visible));
        var outline = Descendants(root).OfType<System.Windows.Shapes.Rectangle>().Single(); Assert.Equal(4, outline.StrokeThickness); Assert.Equal(Color.FromRgb(240, 128, 32), ((SolidColorBrush)outline.Stroke).Color);
        var bar = Descendants(root).OfType<FloatingToolbar>().Single(toolbar => toolbar.Name == "CaptureActions"); Assert.DoesNotContain(bar.Items.Children.OfType<Button>(), button => AutomationProperties.GetName(button) == "联网识别"); Assert.Equal(38, bar.Items.Children.OfType<Button>().Last().Height);
        Assert.Equal(CaptureAction.QuickSave, CaptureWindow.MouseCaptureAction("QuickSave")); Assert.Null(CaptureWindow.MouseCaptureAction("None")); window.Close();
    });
    [Fact] public void PinMouseActionsConsumeStepsLocksAndOriginalOutput() => ImagingTests.Sta(() =>
    {
        var original = Image(); var settings = new AppSettings { PinZoomStep = 35, PinOpacityStep = 17, PinLockMovement = false, PinDefaultOpacity = 85, PinOutputImage = "Original", PinShowShadow = false };
        var pin = new PinWindow(original, settings: settings); pin.MouseAction("Zoom", 1); Assert.Equal(1.35, pin.Presentation.Scale, 8); pin.MouseAction("Opacity", -1); Assert.Equal(.68, pin.Presentation.Opacity, 8);
        pin.Presentation.Locked = true; Assert.True(pin.CanMove); pin.MouseAction("Zoom", 1); Assert.Equal(1.7, pin.Presentation.Scale, 8); Assert.True(pin.Presentation.Locked);
        pin.Replace(Image(100, 60)); Assert.Same(original, pin.OutputBitmap); var copied = (BitmapSource?)null; pin.CopyRequested = image => copied = image; pin.MouseAction("Copy", 1); Assert.Same(original, copied); pin.CloseForExit();
    });
    [Fact] public void ClickThroughToolbarFollowsVisibilityAndCannotBeRecreatedAfterPinCloses() => ImagingTests.Sta(() =>
    {
        var pin = new PinWindow(Image(), settings: new AppSettings { PinShowShadow = false, PinClickThroughToolbar = true });
        var field = typeof(PinWindow).GetField("rescueToolbar", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            pin.Show(); pin.Presentation.ClickThrough = true; pin.RefreshAppearance(); pin.UpdateLayout();
            var toolbar = (Window)field.GetValue(pin)!; Assert.True(toolbar.IsVisible);
            pin.Hide(); Assert.False(toolbar.IsVisible); pin.Show(); Assert.True(toolbar.IsVisible);
            var cancel = true; pin.Closing += (_, e) => e.Cancel = cancel; pin.CloseForExit(); Assert.True(pin.IsVisible); Assert.True(toolbar.IsVisible);
            cancel = false; pin.CloseForExit(); Assert.False(toolbar.IsVisible); Assert.Null(field.GetValue(pin));
            pin.RefreshAppearance(); Assert.Null(field.GetValue(pin));
        }
        finally { pin.CloseForExit(); }
    });
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PinClosingHonorsSeparateMouseAndKeyLockRestrictions(bool keyboard) => ImagingTests.Sta(() =>
    {
        var settings = new AppSettings { PinConfirmKeyClose = false, PinConfirmMouseClose = false, PinLockMouseClose = keyboard, PinLockKeyClose = !keyboard, PinShowShadow = false };
        var pin = new PinWindow(Image(), settings: settings); var closed = false; pin.Closed += (_, _) => closed = true;
        try
        {
            pin.Show(); pin.Presentation.Locked = true;
            var close = typeof(PinWindow).GetMethod("RequestCloseFromInput", BindingFlags.Instance | BindingFlags.NonPublic)!;
            close.Invoke(pin, new object[] { !keyboard }); Assert.False(closed);
            close.Invoke(pin, new object[] { keyboard }); Assert.True(closed);
        }
        finally { pin.CloseForExit(); }
    });
    [Theory]
    [InlineData(".jpg", "/app1/ifd/{ushort=305}")]
    [InlineData(".tiff", "/ifd/{ushort=305}")]
    public void ExifOptionWritesOnlyProductAndTimeMetadata(string extension, string query) => ImagingTests.Sta(() =>
    {
        var root = Path.Combine(Path.GetTempPath(), "DeskSnip-exif-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "sample" + extension); ImageFiles.Save(Image(), path, 0, true);
            using (var stream = File.OpenRead(path)) { var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); var metadata = (BitmapMetadata)frame.Metadata; Assert.Equal("DeskSnip", metadata.GetQuery(query)); Assert.Null(metadata.CameraManufacturer); Assert.Null(metadata.CameraModel); }
        }
        finally { Directory.Delete(root, true); }
    });
    [Fact] public void ProxyModeChangesHttpHandlerWithoutMakingNetworkRequests()
    {
        using (var none = ServiceClient.CreateHandler(new AppSettings { NetworkProxyMode = "None" })) Assert.False(none.UseProxy);
        using (var system = ServiceClient.CreateHandler(new AppSettings())) { Assert.True(system.UseProxy); Assert.Null(system.Proxy); }
        using (var custom = ServiceClient.CreateHandler(new AppSettings { NetworkProxyMode = "Http", NetworkProxyHost = "proxy.example.com", NetworkProxyPort = 3128 })) Assert.Equal(new Uri("http://proxy.example.com:3128"), custom.Proxy.GetProxy(new Uri("https://example.com")));
    }
    [Fact] public void ImageCommandReachesExistingInstanceChannelAndStopsCleanly()
    {
        var identity = "test-" + Guid.NewGuid().ToString("N"); string? received = null; using (var signal = new ManualResetEvent(false))
        using (var channel = new PinFileChannel(identity, path => { received = path; signal.Set(); }))
        { PinFileChannel.Send(identity, "example-image.png"); Assert.True(signal.WaitOne(3000)); Assert.Equal("example-image.png", received); }
    }
    [Fact] public void ImageChannelRejectsMalformedFramesAndAcceptsRepeatedCommandsWhileOldClientIsOpen()
    {
        var identity = "test-" + Guid.NewGuid().ToString("N"); string? received = null;
        using (var signal = new AutoResetEvent(false))
        using (var channel = new PinFileChannel(identity, path => { received = path; signal.Set(); }))
        {
            foreach (var invalid in new[] { 0, 32769 })
            using (var client = new NamedPipeClientStream(".", "DeskSnip.PinFile." + identity, PipeDirection.Out))
            { client.Connect(3000); using (var writer = new BinaryWriter(client, Encoding.UTF8, true)) { writer.Write(invalid); writer.Flush(); } }
            using (var oldClient = new NamedPipeClientStream(".", "DeskSnip.PinFile." + identity, PipeDirection.Out))
            {
                oldClient.Connect(3000); var bytes = Encoding.UTF8.GetBytes("旧连接.png"); using (var writer = new BinaryWriter(oldClient, Encoding.UTF8, true)) { writer.Write(bytes.Length); writer.Write(bytes); writer.Flush(); }
                Assert.True(signal.WaitOne(3000)); Assert.Equal("旧连接.png", received);
                for (var i = 0; i < 12; i++) { PinFileChannel.Send(identity, "参考图-" + i + ".png"); Assert.True(signal.WaitOne(3000)); Assert.Equal("参考图-" + i + ".png", received); }
            }
            Assert.Throws<ArgumentException>(() => PinFileChannel.Send(identity, ""));
        }
    }
    [Fact] public async Task ImageChannelTimesOutIncompleteInputAndStillHandlesNextCommand()
    {
        var identity = "test-" + Guid.NewGuid().ToString("N");
        using (var signal = new ManualResetEvent(false))
        using (var channel = new PinFileChannel(identity, _ => signal.Set()))
        using (var client = new NamedPipeClientStream(".", "DeskSnip.PinFile." + identity, PipeDirection.Out))
        {
            client.Connect(3000); using (var writer = new BinaryWriter(client, Encoding.UTF8, true)) { writer.Write(8); writer.Write((byte)1); writer.Flush(); }
            await Task.Delay(3400); Assert.False(signal.WaitOne(0)); PinFileChannel.Send(identity, "next.png"); Assert.True(signal.WaitOne(3000));
        }
    }
}
