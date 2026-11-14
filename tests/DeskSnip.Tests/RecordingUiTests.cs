using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.App;
using DeskSnip.App.Recording;
using DeskSnip.App.Windows;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public class RecordingUiTests
{
    private sealed class SyntheticSource : IFrameSource
    {
        public void Copy(byte[] pixels, bool cursor) { for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 37; pixels[i + 1] = 34; pixels[i + 2] = 31; pixels[i + 3] = 255; } }
        public void Dispose() { }
    }
    [Theory]
    [InlineData(100, 70, 600, 380, true)]
    [InlineData(600, 590, 300, 100, false)]
    [InlineData(980, 10, 16, 16, true)]
    [InlineData(0, 0, 1000, 700, false)]
    public void RecordingToolbarAnchorsToRegionAndStaysOnNegativeMonitor(int x, int y, int width, int height, bool below)
    {
        var monitor = new Rect(-1000, -100, 1000, 700); var selection = new Rect(x - 1000, y - 100, width, height);
        var placed = FloatingLayout.Actions(monitor, selection, new Size(478, 56)); Assert.True(monitor.Contains(placed));
        if (below) Assert.True(placed.Top >= selection.Bottom);
        else if (selection.Top > monitor.Top + 64) Assert.True(placed.Bottom <= selection.Top);
    }
    [Fact] public void CompactControlBarTracksPauseAndDisablesActionsWhileSaving() => RecognitionServiceUiTests.StaAsync(async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "DeskSnip-recording-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var session = new RecordingSession(Path.Combine(directory, "test.gif"), new PixelRect(100, 80, 32, 24), "GIF", 30, true, _ => new SyntheticSource());
        RecordingBar? bar = null;
        try
        {
            await session.StartAsync(); var requests = 0; bar = new RecordingBar(session, () => requests++, () => { }, () => { });
            bar.Show(); await Task.Delay(80); Assert.True(bar.IsVisible);
            var handle = new WindowInteropHelper(bar).Handle; Assert.True(Native.GetWindowRect(handle, out var rectangle)); var dpi = VisualTreeHelper.GetDpi(bar); Assert.InRange(rectangle.Right - rectangle.Left, (int)Math.Floor(bar.Width * dpi.DpiScaleX) - 1, (int)Math.Ceiling(bar.Width * dpi.DpiScaleX) + 1);
            var frame = (RecordingRegionWindow)typeof(RecordingBar).GetField("regionWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bar)!; var frameHandle = new WindowInteropHelper(frame).Handle; Assert.True(Native.GetWindowRect(frameHandle, out var frameRectangle)); Assert.Equal(session.Region.X - 2, frameRectangle.Left); Assert.Equal(session.Region.Y - 2, frameRectangle.Top); Assert.Equal(session.Region.Width + 4, frameRectangle.Right - frameRectangle.Left);
            Assert.NotEqual(0, Native.GetWindowLongPtr(frameHandle, Native.GwlExStyle).ToInt64() & Native.WsExTransparent); Assert.NotEqual(0, Native.GetWindowLongPtr(frameHandle, Native.GwlExStyle).ToInt64() & Native.WsExNoActivate);
            var root = (FloatingToolbar)bar.Content; var size = new Size(bar.Width, bar.Height); root.Measure(size); root.Arrange(new Rect(new Point(), size)); root.UpdateLayout();
            Assert.InRange(root.ActualHeight, 40, 60); Assert.True(root.ActualWidth < 520); Assert.Equal(WindowStyle.None, bar.WindowStyle);
            var buttons = Descendants(root).OfType<Button>().ToArray(); Assert.All(buttons, button => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button))));
            Assert.Contains(Descendants(root).OfType<TextBlock>(), text => text.Text == "15 FPS");
            var pause = buttons.Single(button => AutomationProperties.GetName(button) == "暂停录制"); pause.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(RecordingState.Paused, session.State); Assert.Equal("继续录制", AutomationProperties.GetName(pause)); Assert.Equal(ToolGlyph.Play, ((ToolIcon)pause.Content).Glyph);
            var paused = session.Elapsed; await Task.Delay(100); Assert.Equal(paused, session.Elapsed); Preview(root, "recording-paused");
            pause.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(RecordingState.Recording, session.State); Preview(root, "recording-controls");
            var stop = buttons.Single(button => AutomationProperties.GetName(button) == "停止并保存"); stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(1, requests);
            bar.ShowFinishing(); Assert.False(frame.IsVisible); Assert.False(pause.IsEnabled); Assert.False(stop.IsEnabled); Assert.False(buttons.Single(button => AutomationProperties.GetName(button) == "更多录制操作").IsEnabled); bar.TogglePause(); Assert.Equal(RecordingState.Recording, session.State); Preview(root, "recording-saving");
            bar.Close(); bar = new RecordingBar(session, () => requests++); bar.Close(); bar.Close(); Assert.Equal(2, requests);
        }
        finally { if (bar != null) { bar.Finishing = true; bar.Close(); } await session.StopAsync(); Directory.Delete(directory, true); }
    });
    [Fact] public void RecordingSceneUsesRealFrameAndToolbarViews() => ImagingTests.Sta(() =>
    {
        var region = new PixelRect(36, 46, 1120, 590); var session = new RecordingSession(Path.Combine(Path.GetTempPath(), "desksnip-ui-preview.gif"), region, "GIF", 16, false);
        var bar = new RecordingBar(session, () => { }, () => { }, () => { }); var frame = new RecordingRegionWindow(region);
        var surface = new Canvas { Background = new SolidColorBrush(Color.FromRgb(36, 40, 54)) }; var frameView = (Border)frame.Content; frame.Content = null; frameView.Width = region.Width; frameView.Height = region.Height; Canvas.SetLeft(frameView, region.X); Canvas.SetTop(frameView, region.Y); surface.Children.Add(frameView);
        var toolbar = (FloatingToolbar)bar.Content; bar.Content = null; Canvas.SetLeft(toolbar, region.Right - bar.Width); Canvas.SetTop(toolbar, region.Bottom + 7); surface.Children.Add(toolbar);
        surface.Measure(new Size(1220, 720)); surface.Arrange(new Rect(0, 0, 1220, 720)); surface.UpdateLayout(); Preview(surface, "recording-scene"); bar.Finishing = true; bar.Close(); frame.Close();
    });
    [Fact] public void RecordingSetupKeepsCommonChoicesAndStartVisibleWithAdvancedOptionsFolded() => ImagingTests.Sta(() =>
    {
        var window = new RecordingSetup(new PixelRect(100, 80, 1120, 590), new AppSettings(), format => Path.Combine(Path.GetTempPath(), "DeskSnip-preview." + format.ToLowerInvariant()), Path.GetTempPath());
        var root = (ScrollViewer)window.Content; var size = new Size(604, 400); root.Measure(size); root.Arrange(new Rect(new Point(), size)); root.UpdateLayout();
        var advanced = Descendants(root).OfType<Expander>().Single(); Assert.False(advanced.IsExpanded); Assert.True(root.ExtentHeight <= root.ViewportHeight);
        var start = Descendants(root).OfType<Button>().Single(button => Equals(button.Content, "开始录制")); Assert.True(new Rect(new Point(), size).Contains(start.TransformToAncestor(root).TransformBounds(new Rect(new Point(), start.RenderSize))));
        advanced.IsExpanded = true; root.UpdateLayout(); Assert.True(root.ExtentHeight > root.ViewportHeight); Assert.Equal(6, Descendants(root).OfType<ComboBox>().Count()); window.Close();
    });
    [Theory]
    [InlineData("Ask", "Auto", true, true)]
    [InlineData("Always", "Auto", true, false)]
    [InlineData("Hidden", "Auto", false, false)]
    [InlineData("Hidden", "Hidden", true, false)]
    [InlineData("Ask", "Hidden", true, false)]
    public void RecordingToolbarModeKeepsStopControlWhenTrayIsHidden(string mode, string tray, bool visible, bool editable) => ImagingTests.Sta(() =>
    {
        var settings = new AppSettings { RecordingToolbarDisplayMode = mode, TrayIconMode = tray };
        var window = new RecordingSetup(new PixelRect(0, 0, 64, 48), settings, format => Path.Combine(Path.GetTempPath(), "DeskSnip-toolbar-test." + format.ToLowerInvariant()), Path.GetTempPath());
        var checkbox = Descendants((DependencyObject)window.Content).OfType<CheckBox>().Single(control => AutomationProperties.GetName(control) == "显示录制工具栏");
        Assert.Equal(visible, window.ToolbarVisible); Assert.Equal(editable, checkbox.IsEnabled);
        if (editable) { checkbox.IsChecked = false; Assert.False(window.ToolbarVisible); }
        window.Close();
    });
    private static void Preview(FrameworkElement visual, string name)
    {
        var directory = Environment.GetEnvironmentVariable("DESK_SNIP_TEST_PREVIEW_DIR"); if (string.IsNullOrEmpty(directory)) return; Directory.CreateDirectory(directory);
        visual.UpdateLayout(); var image = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32); image.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var output = File.Create(Path.Combine(directory, name + ".png"))) encoder.Save(output);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var item in LogicalTreeHelper.GetChildren(root)) if (item is DependencyObject child) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
