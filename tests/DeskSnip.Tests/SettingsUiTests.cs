using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskSnip.App;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public class SettingsUiTests
{
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var item in LogicalTreeHelper.GetChildren(root)) if (item is DependencyObject child) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static Control Preference(Window window, string name) => Descendants((DependencyObject)window.Content).OfType<Control>().Single(control => AutomationProperties.GetAutomationId(control) == name);
    private static AppSettings Read(SettingsWindow window) => (AppSettings)typeof(SettingsWindow).GetMethod("ReadValues", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
    private static void Arrange(FrameworkElement root, Size size) { root.Measure(size); root.Arrange(new Rect(new Point(), size)); root.UpdateLayout(); root.Dispatcher.Invoke(new Action(root.UpdateLayout), DispatcherPriority.ContextIdle); }
    [Theory]
    [InlineData(760, 540, "Light")]
    [InlineData(760, 540, "Dark")]
    [InlineData(1020, 780, "Light")]
    [InlineData(1020, 780, "Dark")]
    public void SidebarCardsAndFooterFitAcrossPagesAndThemes(int width, int height, string theme) => ImagingTests.Sta(() =>
    {
        var settings = new AppSettings { Theme = theme, AccentColor = "#3489F5" }; var window = new SettingsWindow(settings, _ => { });
        try
        {
        var root = (Grid)window.Content; var size = new Size(width, height); Arrange(root, size);
        var navigation = Descendants(root).OfType<ListBox>().Single(list => list.Name == "SettingsNavigation"); Assert.Equal(15, navigation.Items.Count); Assert.InRange(navigation.ActualWidth, 180, 205);
        foreach (var item in navigation.Items.OfType<ListBoxItem>())
        {
            var id = ((SettingsPage)item.Tag).Id; window.SelectPage(id); Arrange(root, size);
            var page = ((SettingsPage)item.Tag).View; Assert.True(page.ActualWidth >= 530); Assert.True(page.ActualHeight > 350); Assert.Equal(((SettingsPage)item.Tag).Title + "设置", AutomationProperties.GetName(page));
            var bounds = page.TransformToAncestor(root).TransformBounds(new Rect(new Point(), page.RenderSize)); Assert.True(new Rect(new Point(), size).Contains(bounds));
            var cancel = Descendants(root).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "取消"); var save = Descendants(root).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "保存设置");
            Assert.True(new Rect(new Point(), size).Contains(save.TransformToAncestor(root).TransformBounds(new Rect(new Point(), save.RenderSize)))); Assert.True(new Rect(new Point(), size).Contains(cancel.TransformToAncestor(root).TransformBounds(new Rect(new Point(), cancel.RenderSize))));
            foreach (var control in Descendants(page).OfType<Control>().Where(control => AutomationProperties.GetAutomationId(control).Length > 0 && page.IsAncestorOf(control)))
            {
                var local = control.TransformToAncestor(page).TransformBounds(new Rect(new Point(), control.RenderSize)); Assert.True(local.Left >= -1 && local.Right <= page.ActualWidth + 1, "Setting extends horizontally: " + AutomationProperties.GetAutomationId(control));
            }
            var brush = (SolidColorBrush)window.Resources["PanelBrush"]; Assert.Equal(theme == "Dark" ? Color.FromRgb(36, 45, 52) : Colors.White, brush.Color); Preview(root, width, height, "settings-" + id + "-" + theme + "-" + width);
            if (id == "shortcuts") Assert.All(Descendants(page).OfType<TextBox>().Where(box => AutomationProperties.GetAutomationId(box).StartsWith("Action_", StringComparison.Ordinal)), box => Assert.True(box.ActualWidth >= 110, "Shortcut input is too narrow to read."));
            if (page.ScrollableHeight > 0) { page.ScrollToEnd(); Arrange(root, size); Assert.True(Math.Abs(page.VerticalOffset - page.ScrollableHeight) < 1); Preview(root, width, height, "settings-" + id + "-" + theme + "-" + width + "-bottom"); }
        }
        }
        finally { window.Close(); }
    });
    [Fact] public void PageSwitchesKeepDraftValuesAndRestoringOnlyResetsCurrentPage() => ImagingTests.Sta(() =>
    {
        var window = new SettingsWindow(new AppSettings { Theme = "Dark", JpegQuality = 70, PinMiddleAction = "Copy" }, _ => { }); var root = (Grid)window.Content; Arrange(root, new Size(1020, 780));
        window.SelectPage("appearance"); Arrange(root, new Size(1020, 780)); ((ComboBox)Preference(window, nameof(AppSettings.Theme))).SelectedValue = "Light"; var font = (ComboBox)Preference(window, nameof(AppSettings.InterfaceFont)); ((TextBox)font.Template.FindName("PART_EditableTextBox", font)).Text = "Segoe UI";
        window.SelectPage("save"); ((TextBox)Preference(window, nameof(AppSettings.JpegQuality))).Text = "88"; ((TextBox)Preference(window, nameof(AppSettings.SaveNameTemplate))).Text = "Sample-{width}x{height}"; ((ComboBox)Preference(window, nameof(AppSettings.QuickSaveFormat))).SelectedValue = "JPEG";
        var preview = Descendants(root).OfType<TextBlock>().Single(text => text.Name == "SettingsSaveNamePreview"); Assert.Contains("Sample-1920x1080.jpg", preview.Text);
        window.SelectPage("pin"); ((ComboBox)Preference(window, nameof(AppSettings.PinMiddleAction))).SelectedValue = "Lock"; window.SelectPage("save"); Assert.Equal("88", ((TextBox)Preference(window, nameof(AppSettings.JpegQuality))).Text);
        var read = Read(window); Assert.Equal("Light", read.Theme); Assert.Equal("Segoe UI", read.InterfaceFont); Assert.Equal("Lock", read.PinMiddleAction); Assert.Equal(88, read.JpegQuality);
        Descendants(root).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "恢复本页默认").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); read = Read(window); Assert.Equal(95, read.JpegQuality); Assert.Equal("PNG", read.QuickSaveFormat); Assert.Equal("Light", read.Theme); Assert.Equal("Segoe UI", read.InterfaceFont); Assert.Equal("Lock", read.PinMiddleAction);
        window.Close();
    });
    [Fact] public void EveryExistingPreferenceRoundTripsThroughSidebarAndInvalidDraftDoesNotSave() => ImagingTests.Sta(() =>
    {
        var root = Path.Combine(Path.GetTempPath(), "DeskSnip-settings-roundtrip"); var expected = new AppSettings { CaptureHotkey = "Ctrl+Alt+F6", PinHotkey = "Ctrl+Alt+F7", RecordHotkey = "Ctrl+Alt+F8", FramesPerSecond = 24, HistoryLimit = 250, CaptureCursor = false, MinimizeToTray = false, RecordingFormat = "WebP", OutputDirectory = root, FfmpegPath = Path.Combine(root, "ffmpeg.exe"), RecordSystemAudio = true, RecordMicrophone = true, VideoQuality = 60, VideoEncoder = VideoEncoder.Intel, RecordingDelay = 3, QuickSaveDirectory = root, QuickSaveFormat = "TIFF", SaveNameTemplate = "Sample-{date}", QuickSaveOverwrite = true, JpegQuality = 80, AutomaticSaveDirectory = root, AutoSaveCapture = true, AutoSaveCopy = true, AutoSavePin = true, AutoSaveManual = true, AutomaticSaveDays = 45, AutomaticSaveMegabytes = 2500, CopyFileWithImage = true, PinAlsoCopy = true, Theme = "Dark", AccentColor = "#336699", InterfaceFont = "Segoe UI", PinWheelAction = "Opacity", PinControlWheelAction = "Zoom", PinMiddleAction = "Lock", PinDoubleClickAction = "Copy", LocalOcrLanguage = "en-US", OcrDirectCopy = true, OcrCloseAfterCopyAll = false, RecognitionServiceEndpoint = "https://example.com/recognize", RecognitionKeyEnvironment = "DESK_SNIP_TEST_ONLY", RecognitionTimeoutSeconds = 35, RecognitionSourceLanguage = "en", RecognitionTargetLanguage = "ja" };
        expected.ActionBindings.Add(new ActionHotkey { Action = "QuickCapture", Hotkey = "Ctrl+Alt+F9" }); var saves = 0; var window = new SettingsWindow(expected, _ => saves++); var actual = Read(window);
        foreach (var property in typeof(AppSettings).GetProperties().Where(property => property.Name != nameof(AppSettings.ActionBindings))) Assert.Equal(property.GetValue(expected), property.GetValue(actual)); Assert.Equal("Ctrl+Alt+F9", Assert.Single(actual.ActionBindings).Hotkey);
        window.SelectPage("recording"); ((TextBox)Preference(window, nameof(AppSettings.FramesPerSecond))).Text = "bad"; Assert.Throws<TargetInvocationException>(() => Read(window)); Assert.Equal(0, saves); Assert.Equal(24, expected.FramesPerSecond);
        window.SelectPage("save"); ((TextBox)Preference(window, nameof(AppSettings.SaveNameTemplate))).Text = "{unknown}"; Assert.Contains("不支持", Descendants((DependencyObject)window.Content).OfType<TextBlock>().Single(text => text.Name == "SettingsSaveNamePreview").Text); window.Close();
    });
    [Fact] public void ReferencePreferencesRoundTripNonDefaultValuesThroughAllPages() => ImagingTests.Sta(() =>
    {
        var expected = new AppSettings
        {
            ExplorerContextMenu = "Images", TraySingleClickAction = "Settings", TrayDoubleClickAction = "TogglePins", TrayMiddleClickAction = "ToggleHotkeys", TrayIconMode = "Custom", TrayIconColor = "#468ACE", OcrTextLayout = TextLineLayout.MergeLines, OcrPunctuation = PunctuationStyle.English,
            NetworkProxyMode = "Http", NetworkProxyHost = "proxy.example.com", NetworkProxyPort = 3128, LockedPinColor = "#112233", CaptureMaskColor = "#80223344", ClickThroughColor = "#60334455", ToolbarIconSize = "Large", SelectionBorderWidth = 0, SelectionBorderColor = "#778899", SelectionAnchorDisplay = "Four",
            ScreenshotUiDetection = "Windows", ScreenshotDetectionPadding = -12, ScreenshotGuideVisibility = "Always", MagnifierZoom = 23, MagnifierGridColor = "#80446688", ColorFormatOrder = "Hsv,Rgb,HashHex", ScreenshotAreaHistoryLimit = 0, ScreenshotDoubleClickAction = "Save", ScreenshotMiddleClickAction = "QuickSave", ScreenshotCopyDpiLimit = 150,
            LongScreenshotStart = "Horizontal", RecordingToolbarDisplayMode = "Hidden", PinHistoryLimit = 8, PinDisplayPosition = "MouseScreenCenter", PinRestoreOrder = "Created", PinDefaultOpacity = 85, PinClickThroughOpacity = 35, PinZoomStep = 27, PinFineZoomStep = 3, PinOpacityStep = 17, PinWheelAnchor = "BottomRight", PinMiddleAnchor = "Center", PinTransparentBackground = "Dark", PinOutputImage = "Original",
            ManualSaveNameTemplate = "manual-{width}", ManualSaveFormat = "TIFF", QuickSaveNotificationAction = "CopyPath", QuickRecordingDirectory = Path.GetTempPath(), QuickRecordingNameTemplate = "video-{kind}-{width}x{height}", AutomaticSaveNameTemplate = "archive-{kind}", AutomaticSaveFormat = "JPEG"
        };
        var defaults = new AppSettings(); foreach (var property in typeof(AppSettings).GetProperties().Where(property => property.PropertyType == typeof(bool))) property.SetValue(expected, !(bool)property.GetValue(defaults)!);
        var window = new SettingsWindow(expected, _ => { }); var actual = Read(window);
        foreach (var property in typeof(AppSettings).GetProperties().Where(property => property.Name != nameof(AppSettings.ActionBindings))) Assert.Equal(property.GetValue(expected), property.GetValue(actual));
        window.Close();
    });
    [Fact] public void PinAndScreenshotCopyCheckboxesShareDraftAndRestoreFromEitherPage() => ImagingTests.Sta(() =>
    {
        var window = new SettingsWindow(new AppSettings(), _ => { }); var root = (Grid)window.Content; var size = new Size(1020, 780);
        window.SelectPage("screenshot"); Arrange(root, size); var source = (CheckBox)Preference(window, nameof(AppSettings.PinAlsoCopy)); source.IsChecked = true;
        ((CheckBox)Preference(window, nameof(AppSettings.ScreenshotShowMagnifier))).IsChecked = false;
        window.SelectPage("pin"); Arrange(root, size); var mirror = (CheckBox)Preference(window, nameof(AppSettings.PinAlsoCopy)); Assert.True(mirror.IsChecked); Assert.Equal(FontWeights.Bold, mirror.FontWeight);
        mirror.IsChecked = false; window.SelectPage("screenshot"); Arrange(root, size); Assert.False(source.IsChecked); source.IsChecked = true;
        window.SelectPage("pin"); Arrange(root, size); typeof(SettingsWindow).GetMethod("RestoreCurrentPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null); Arrange(root, size);
        Assert.False(mirror.IsChecked); var actual = Read(window); Assert.False(actual.PinAlsoCopy); Assert.False(actual.ScreenshotShowMagnifier);
        window.SelectPage("screenshot"); Arrange(root, size); Assert.False(source.IsChecked); source.IsChecked = true; typeof(SettingsWindow).GetMethod("RestoreCurrentPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        window.SelectPage("pin"); Arrange(root, size); Assert.False(mirror.IsChecked); Assert.Equal(FontWeights.Normal, mirror.FontWeight); window.Close();
    });
    [Fact] public void SaveDependenciesPreserveDraftAndPreviewsMatchArchiveAndRecordingNames() => ImagingTests.Sta(() =>
    {
        var window = new SettingsWindow(new AppSettings { AutoSaveCapture = true, AutomaticSaveDays = 45 }, _ => { }); var root = (Grid)window.Content; var size = new Size(1020, 780); window.SelectPage("save"); Arrange(root, size);
        var enabled = (CheckBox)Preference(window, nameof(AppSettings.AutomaticSaveEnabled)); var cleanup = (CheckBox)Preference(window, nameof(AppSettings.AutomaticSaveCleanup)); var days = Preference(window, nameof(AppSettings.AutomaticSaveDays)); var capacity = Preference(window, nameof(AppSettings.AutomaticSaveMegabytes));
        Assert.True(days.IsEnabled); cleanup.IsChecked = false; Arrange(root, size); Assert.False(days.IsEnabled); Assert.True(capacity.IsEnabled);
        enabled.IsChecked = false; Arrange(root, size); Assert.False(cleanup.IsEnabled); Assert.False(capacity.IsEnabled); Assert.False(Preference(window, nameof(AppSettings.AutoSaveCapture)).IsEnabled);
        var directory = Preference(window, nameof(AppSettings.AutomaticSaveDirectory)); Assert.False(directory.IsEnabled); Assert.All(Descendants((DependencyObject)directory.Parent).OfType<Button>(), button => Assert.False(button.IsEnabled));
        var actual = Read(window); Assert.True(actual.AutoSaveCapture); Assert.Equal(45, actual.AutomaticSaveDays);
        enabled.IsChecked = true; cleanup.IsChecked = true; Arrange(root, size); Assert.True(days.IsEnabled); Assert.True(directory.IsEnabled);
        var quick = (CheckBox)Preference(window, nameof(AppSettings.QuickRecordingEnabled)); Assert.False(Preference(window, nameof(AppSettings.QuickRecordingNameTemplate)).IsEnabled); quick.IsChecked = true; Arrange(root, size); Assert.True(Preference(window, nameof(AppSettings.QuickRecordingDirectory)).IsEnabled);
        ((TextBox)Preference(window, nameof(AppSettings.AutomaticSaveNameTemplate))).Text = "sample-{width}x{height}"; ((ComboBox)Preference(window, nameof(AppSettings.AutomaticSaveFormat))).SelectedValue = "JPEG";
        ((TextBox)Preference(window, nameof(AppSettings.QuickRecordingNameTemplate))).Text = "sample-{kind}-{width}x{height}"; Arrange(root, size);
        ((TextBox)directory).Text = Path.GetTempPath(); ((TextBox)Preference(window, nameof(AppSettings.QuickRecordingDirectory))).Text = Path.GetTempPath(); Arrange(root, size);
        Assert.Equal("预览：" + Path.Combine(Path.GetTempPath(), "DeskSnip-auto-sample-1920x1080.jpg"), Descendants(root).OfType<TextBlock>().Single(text => AutomationProperties.GetAutomationId(text) == nameof(AppSettings.AutomaticSaveNameTemplate) + "Preview").Text);
        Assert.Equal("预览：" + Path.Combine(Path.GetTempPath(), "sample-Recording-1920x1080.mp4"), Descendants(root).OfType<TextBlock>().Single(text => AutomationProperties.GetAutomationId(text) == nameof(AppSettings.QuickRecordingNameTemplate) + "Preview").Text); window.Close();
    });
    private static void Preview(Visual visual, int width, int height, string name)
    {
        var directory = Environment.GetEnvironmentVariable("DESK_SNIP_TEST_PREVIEW_DIR"); if (string.IsNullOrEmpty(directory)) return; Directory.CreateDirectory(directory); var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
        var pixel = new byte[4]; image.CopyPixels(new Int32Rect(5, 5, 1, 1), pixel, 4, 0); if (pixel[3] == 0) throw new InvalidOperationException("WPF 返回空预览，请检查桌面会话；布局尺寸验证不能代替视觉验收。");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var output = File.Create(Path.Combine(directory, name + ".png"))) encoder.Save(output);
    }
}
