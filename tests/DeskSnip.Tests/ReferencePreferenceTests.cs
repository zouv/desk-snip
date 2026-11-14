using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public sealed class ReferencePreferenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "DeskSnip-reference-settings-" + Guid.NewGuid().ToString("N"));
    [Fact] public void NewPreferencesPersistAndPortableImportKeepsMachineProxyAndDirectories()
    {
        var settings = new AppSettings { ScreenshotDetectionPadding = -12, SelectionBorderWidth = 0, SelectionAnchorDisplay = "Four", MagnifierZoom = 27, MagnifierGridColor = "#802468AC", ColorFormatOrder = "Hsv,Rgb,HashHex", ScreenshotDoubleClickAction = "Save", ScreenshotMiddleClickAction = "QuickSave", ScreenshotCopyDpiLimit = 150, ScreenshotAreaHistoryLimit = 0,
            PinZoomStep = 35, PinFineZoomStep = 3, PinOpacityStep = 17, PinWheelAnchor = "BottomRight", PinMiddleAnchor = "Center", PinDefaultOpacity = 85, PinDisplayPosition = "MouseScreenCenter", PinHistoryLimit = 8, PinOutputImage = "Original",
            ManualSaveNameTemplate = "manual-{width}", ManualSaveFormat = "TIFF", AutomaticSaveNameTemplate = "archive-{kind}", AutomaticSaveFormat = "JPEG", QuickRecordingNameTemplate = "video-{time}", NetworkProxyMode = "Http", NetworkProxyHost = "proxy.example.com", NetworkProxyPort = 3128, QuickRecordingDirectory = root };
        foreach (var property in typeof(AppSettings).GetProperties().Where(property => property.PropertyType == typeof(bool))) property.SetValue(settings, !(bool)property.GetValue(settings)!);
        settings.Validate(); new LocalStore(root).SaveSettings(settings); var restored = new LocalStore(root).Settings;
        foreach (var property in typeof(AppSettings).GetProperties().Where(property => property.Name != nameof(AppSettings.ActionBindings))) Assert.Equal(property.GetValue(settings), property.GetValue(restored));
        var exported = PreferenceTransfer.Export(settings); Assert.Equal("35", exported.Values[nameof(AppSettings.PinZoomStep)]); Assert.False(exported.Values.ContainsKey(nameof(AppSettings.NetworkProxyHost))); Assert.False(exported.Values.ContainsKey(nameof(AppSettings.NetworkProxyMode))); Assert.False(exported.Values.ContainsKey(nameof(AppSettings.QuickRecordingDirectory)));
        var imported = PreferenceTransfer.Import(new AppSettings { NetworkProxyMode = "None", QuickRecordingDirectory = root }, exported); Assert.Equal("None", imported.NetworkProxyMode); Assert.Equal(root, imported.QuickRecordingDirectory); Assert.Equal(settings.ColorFormatOrder, imported.ColorFormatOrder); Assert.Equal(settings.ManualSaveFormat, imported.ManualSaveFormat); Assert.Equal(settings.PinClickThroughToolbar, imported.PinClickThroughToolbar);
    }
    [Theory]
    [InlineData("SelectionBorderWidth", "NaN")]
    [InlineData("MagnifierZoom", "0")]
    [InlineData("PinOpacityStep", "101")]
    [InlineData("PinHistoryLimit", "-1")]
    [InlineData("ScreenshotCopyDpiLimit", "125")]
    [InlineData("ColorFormatOrder", "Rgb,Rgb")]
    [InlineData("TraySingleClickAction", "bad")]
    [InlineData("AutomaticSaveNameTemplate", "{missing}")]
    [InlineData("MagnifierGridColor", "transparent")]
    public void InvalidReferencePreferenceIsRejectedWithoutChangingCurrent(string name, string text)
    {
        var current = new AppSettings(); var document = new PreferenceDocument(); document.Values.Add(name, text); Assert.Throws<ArgumentException>(() => PreferenceTransfer.Import(current, document)); Assert.Equal(11, current.MagnifierZoom); Assert.Equal(100, current.PinHistoryLimit);
    }
    [Fact] public void DetectionPaddingAndOutsideExpansionRespectPhysicalDesktopAndTinyTargets()
    {
        var desktop = new PixelRect(-100, -50, 200, 100);
        Assert.Equal(new PixelRect(-95, -35, 50, 50), CapturePreferenceRules.DetectionBounds(new PixelRect(-90, -30, 40, 40), 5, desktop));
        var tiny = CapturePreferenceRules.DetectionBounds(new PixelRect(-90, -30, 2, 3), -50, desktop); Assert.True(tiny.Width >= 1 && tiny.Height >= 1);
        Assert.Equal(new PixelRect(-100, -50, 11, 11), CapturePreferenceRules.DetectionBounds(new PixelRect(-100, -50, 1, 1), 10, desktop));
        Assert.Equal(new PixelRect(-80, -20, 111, 50), CapturePreferenceRules.ExpandToPoint(new PixelRect(-80, -20, 50, 50), new PixelPoint(30, 0), desktop));
        Assert.Equal(new PixelRect(-100, -50, 70, 80), CapturePreferenceRules.ExpandToPoint(new PixelRect(-80, -20, 50, 50), new PixelPoint(-500, -200), desktop));
        Assert.Equal(new[] { PixelColorFormat.Hsv, PixelColorFormat.Rgb }, CapturePreferenceRules.ColorFormats("Hsv,Rgb"));
    }
    [Fact] public void DpiCopyOnlyShrinksAndPinStepsRespectConfiguredPercentages()
    {
        Assert.Equal(.75, CapturePreferenceRules.CopyScale(150, 2)); Assert.Equal(1, CapturePreferenceRules.CopyScale(200, 1)); Assert.Equal(1, CapturePreferenceRules.CopyScale(0, 2));
        var pin = new PinPresentation(); pin.Zoom(120, false, 35, 3); Assert.Equal(1.35, pin.Scale, 8); pin.Zoom(120, true, 35, 3); Assert.Equal(1.38, pin.Scale, 8); pin.Fade(-120, 17); Assert.Equal(.83, pin.Opacity, 8);
        pin.Locked = true; pin.Zoom(120, false, 35, 3); Assert.Equal(1.38, pin.Scale, 8); Assert.Throws<ArgumentOutOfRangeException>(() => pin.Fade(1, 101));
    }
    [Fact] public void LockRestrictionsAndResizeAnchorsAreIndependent()
    {
        var settings = new AppSettings { PinLockMovement = false, PinLockKeyClose = false, PinLockMouseClose = true };
        Assert.True(PinPreferenceRules.MovementAllowed(settings, true)); Assert.True(PinPreferenceRules.CloseAllowed(settings, true, true)); Assert.False(PinPreferenceRules.CloseAllowed(settings, true, false));
        var position = new PixelPoint(-100, -50); var mouse = new PixelPoint(-75, -25);
        Assert.Equal(new PixelPoint(-150, -100), PinPreferenceRules.ResizePosition(position, 100, 100, 200, 200, "Center", mouse));
        Assert.Equal(new PixelPoint(-125, -75), PinPreferenceRules.ResizePosition(position, 100, 100, 200, 200, "Mouse", mouse));
        Assert.Equal(new PixelPoint(-200, -150), PinPreferenceRules.ResizePosition(position, 100, 100, 200, 200, "BottomRight", mouse));
    }
    [Fact] public void AutomaticAgeCleanupCanBeDisabledWhileCapacityAndCollisionRulesRemainActive()
    {
        var archive = new AutomaticArchive(root); var now = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var first = archive.Save(now, "image", ".png", path => File.WriteAllBytes(path, new byte[8]), 7, 24, false);
        var second = archive.Save(now.AddDays(60), "image", ".png", path => File.WriteAllBytes(path, new byte[8]), 7, 24, false);
        Assert.True(File.Exists(first)); Assert.EndsWith("image (1).png", second);
        var overwritten = archive.Save(now.AddDays(61), "image", ".png", path => File.WriteAllBytes(path, new byte[9]), 7, 24, false, true); Assert.Equal(first, overwritten);
        var largest = archive.Save(now.AddDays(62), "large", ".png", path => File.WriteAllBytes(path, new byte[30]), 7, 24, false); Assert.True(File.Exists(largest)); Assert.False(File.Exists(first)); Assert.False(File.Exists(second));
        var index = AtomicFile.ReadJson<System.Collections.Generic.List<AutoArchiveEntry>>(Path.Combine(root, ".desksnip-auto.json")); Assert.Single(index);
    }
    [Fact] public void OldPinPreferencesMigrateWithoutRestoringDisabledPinsOrEvictingExistingHistory()
    {
        var store = new LocalStore(root); store.SaveSettings(new AppSettings());
        var settingsPath = Path.Combine(root, "settings.json"); var legacyJson = Regex.Replace(File.ReadAllText(settingsPath), ",?\"Pin(?:RestoreOnStart|HistoryLimit)\":[^,}]+", ""); File.WriteAllText(settingsPath, legacyJson);
        var workspace = new PinWorkspace(root); workspace.Configure(false, 200);
        for (var i = 0; i < 102; i++) { var record = workspace.Add(1, 1, stream => stream.WriteByte(1)); workspace.Close(record.Id); }
        var loaded = new LocalStore(root); var migrated = loaded.Settings.Clone(); Assert.True(migrated.AdoptLegacyPinPreferences(workspace.RestoreOnStart, workspace.Snapshot.ClosedLimit)); loaded.SaveSettings(migrated);
        workspace.Configure(migrated.PinRestoreOnStart, migrated.PinHistoryLimit); Assert.Equal(102, workspace.Records.Count); Assert.False(workspace.RestoreOnStart);
        var reopened = new LocalStore(root).Settings; Assert.Equal(200, reopened.PinHistoryLimit); Assert.False(reopened.PinRestoreOnStart); Assert.False(reopened.AdoptLegacyPinPreferences(true, 1)); Assert.Equal(200, reopened.PinHistoryLimit);
        var explicitSettings = new AppSettings { PinRestoreOnStart = false, PinHistoryLimit = 8 }; Assert.False(explicitSettings.AdoptLegacyPinPreferences(true, 200)); Assert.False(explicitSettings.PinRestoreOnStart); Assert.Equal(8, explicitSettings.PinHistoryLimit);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
