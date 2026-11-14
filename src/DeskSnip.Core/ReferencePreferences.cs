using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace DeskSnip.Core;

/// <summary>参考设置页的可用偏好；未开放的功能不生成可保存的假配置。</summary>
public sealed partial class AppSettings
{
    [DataMember] public bool StartWithWindows { get; set; }
    [DataMember] public string ExplorerContextMenu { get; set; } = "Disabled";
    [DataMember] public string TraySingleClickAction { get; set; } = "Capture";
    [DataMember] public string TrayDoubleClickAction { get; set; } = "Show";
    [DataMember] public string TrayMiddleClickAction { get; set; } = "Pin";
    [DataMember] public string TrayIconMode { get; set; } = "Auto";
    [DataMember] public string TrayIconColor { get; set; } = "#3489F5";
    [DataMember] public TextLineLayout OcrTextLayout { get; set; } = TextLineLayout.Paragraphs;
    [DataMember] public PunctuationStyle OcrPunctuation { get; set; }
    [DataMember] public bool OcrRemoveSpaces { get; set; }
    [DataMember] public string NetworkProxyMode { get; set; } = "System";
    [DataMember] public string NetworkProxyHost { get; set; } = "";
    [DataMember] public int NetworkProxyPort { get; set; } = 8080;
    [DataMember] public string LockedPinColor { get; set; } = "#FF9800";
    [DataMember] public string CaptureMaskColor { get; set; } = "#7D000000";
    [DataMember] public string ClickThroughColor { get; set; } = "#8095C89A";
    [DataMember] public string ToolbarIconSize { get; set; } = "Medium";
    [DataMember] public double SelectionBorderWidth { get; set; } = 2;
    [DataMember] public bool SelectionBorderFollowsTheme { get; set; } = true;
    [DataMember] public string SelectionBorderColor { get; set; } = "#3489F5";
    [DataMember] public string SelectionAnchorDisplay { get; set; } = "Eight";
    [DataMember] public string ScreenshotUiDetection { get; set; } = "Elements";
    [DataMember] public int ScreenshotDetectionPadding { get; set; }
    [DataMember] public bool ScreenshotExpandOutside { get; set; } = true;
    [DataMember] public bool ScreenshotCursorVisible { get; set; }
    [DataMember] public string ScreenshotGuideVisibility { get; set; } = "Alt";
    [DataMember] public bool GuideMouseLines { get; set; } = true;
    [DataMember] public bool GuideSelectionCenter { get; set; } = true;
    [DataMember] public bool GuideSelectionBorder { get; set; } = true;
    [DataMember] public bool GuideMagnifier { get; set; } = true;
    [DataMember] public bool GuideRuleOfThirds { get; set; }
    [DataMember] public bool ScreenshotShowInfoBar { get; set; } = true;
    [DataMember] public bool ScreenshotShowMagnifier { get; set; } = true;
    [DataMember] public int MagnifierZoom { get; set; } = 11;
    [DataMember] public bool MagnifierGrid { get; set; } = true;
    [DataMember] public bool MagnifierCrosshair { get; set; } = true;
    [DataMember] public bool MagnifierCenter { get; set; } = true;
    [DataMember] public bool MagnifierCoordinates { get; set; } = true;
    [DataMember] public bool MagnifierShowShortcuts { get; set; } = true;
    [DataMember] public bool MagnifierSmooth { get; set; }
    [DataMember] public string MagnifierGridColor { get; set; } = "#8CAAAAAA";
    [DataMember] public string ColorFormatOrder { get; set; } = "HashHex,Rgb";
    [DataMember] public bool ScreenshotCloseConfirmation { get; set; } = true;
    [DataMember] public bool ScreenshotShowShortcuts { get; set; } = true;
    [DataMember] public int ScreenshotAreaHistoryLimit { get; set; } = 100;
    [DataMember] public string ScreenshotDoubleClickAction { get; set; } = "Copy";
    [DataMember] public string ScreenshotMiddleClickAction { get; set; } = "Pin";
    [DataMember] public bool ScreenshotWheelTargets { get; set; } = true;
    [DataMember] public bool ScreenshotWheelResize { get; set; } = true;
    [DataMember] public int ScreenshotCopyDpiLimit { get; set; }
    [DataMember] public bool ScreenshotSecondaryButtons { get; set; } = true;
    [DataMember] public bool SoundCaptureStart { get; set; }
    [DataMember] public bool SoundCapturePin { get; set; }
    [DataMember] public bool SoundCaptureCopy { get; set; }
    [DataMember] public bool SoundSaveSuccess { get; set; }
    [DataMember] public bool SoundQuickSave { get; set; }
    [DataMember] public string LongScreenshotStart { get; set; } = "Manual";
    [DataMember] public bool LongScreenshotShortcuts { get; set; } = true;
    [DataMember] public bool RecordingCloseAfterExport { get; set; }
    [DataMember] public bool RecordingExcludeToolbar { get; set; } = true;
    [DataMember] public string RecordingToolbarDisplayMode { get; set; } = "Ask";
    [DataMember] public bool PinImageEnabled { get; set; } = true;
    private bool pinRestoreOnStart = true;
    private int pinHistoryLimit = 100;
    private bool pinRestoreSpecified = true;
    private bool pinHistorySpecified = true;
    [DataMember] public bool PinRestoreOnStart { get => pinRestoreOnStart; set { pinRestoreOnStart = value; pinRestoreSpecified = true; } }
    [DataMember] public int PinHistoryLimit { get => pinHistoryLimit; set { pinHistoryLimit = value; pinHistorySpecified = true; } }
    [DataMember] public string PinDisplayPosition { get; set; } = "Screenshot";
    [DataMember] public string PinRestoreOrder { get; set; } = "Closed";
    [DataMember] public bool PinLockMovement { get; set; } = true;
    [DataMember] public bool PinLockMouseClose { get; set; } = true;
    [DataMember] public bool PinLockKeyClose { get; set; } = true;
    [DataMember] public bool PinLockDrop { get; set; } = true;
    [DataMember] public bool PinShowLockHint { get; set; } = true;
    [DataMember] public bool PinShowMenuShortcuts { get; set; } = true;
    [DataMember] public bool PinConfirmKeyClose { get; set; } = true;
    [DataMember] public bool PinConfirmMouseClose { get; set; } = true;
    [DataMember] public bool PinShowShadow { get; set; } = true;
    [DataMember] public int PinDefaultOpacity { get; set; } = 100;
    [DataMember] public int PinClickThroughOpacity { get; set; } = 50;
    [DataMember] public int PinZoomStep { get; set; } = 10;
    [DataMember] public int PinFineZoomStep { get; set; } = 1;
    [DataMember] public int PinOpacityStep { get; set; } = 5;
    [DataMember] public string PinWheelAnchor { get; set; } = "TopLeft";
    [DataMember] public string PinMiddleAnchor { get; set; } = "Mouse";
    [DataMember] public bool PinResizeFromBorder { get; set; } = true;
    [DataMember] public bool PinClickThroughToolbar { get; set; } = true;
    [DataMember] public bool PinDefaultTopmost { get; set; } = true;
    [DataMember] public bool PinAutoFit { get; set; } = true;
    [DataMember] public bool PinRescueOnShow { get; set; } = true;
    [DataMember] public bool PinKeepOthersHidden { get; set; }
    [DataMember] public bool PinTaskbarWhenUnpinned { get; set; } = true;
    [DataMember] public bool PinSmoothScaling { get; set; } = true;
    [DataMember] public string PinTransparentBackground { get; set; } = "Checkerboard";
    [DataMember] public string PinOutputImage { get; set; } = "Current";
    [DataMember] public bool SaveCopyFile { get; set; }
    [DataMember] public bool SaveCopyPath { get; set; }
    [DataMember] public bool SaveExif { get; set; }
    [DataMember] public string ManualSaveNameTemplate { get; set; } = "DeskSnip-{date}-{time}";
    [DataMember] public string ManualSaveFormat { get; set; } = "PNG";
    [DataMember] public bool RememberSaveExtension { get; set; } = true;
    [DataMember] public bool QuickSaveNotification { get; set; } = true;
    [DataMember] public string QuickSaveNotificationAction { get; set; } = "Locate";
    [DataMember] public bool QuickRecordingEnabled { get; set; }
    [DataMember] public string QuickRecordingDirectory { get; set; } = "";
    [DataMember] public string QuickRecordingNameTemplate { get; set; } = "DeskSnip-{date}-{time}";
    [DataMember] public bool QuickRecordingOverwrite { get; set; }
    [DataMember] public string AutomaticSaveNameTemplate { get; set; } = "DeskSnip-{date}-{time}-{width}x{height}";
    [DataMember] public bool AutomaticSaveCleanup { get; set; } = true;
    [DataMember] public bool AutomaticSaveEnabled { get; set; } = true;
    [DataMember] public string AutomaticSaveFormat { get; set; } = "PNG";
    [DataMember] public bool AutomaticSaveOverwrite { get; set; }
    [DataMember] public bool AutoSaveOcr { get; set; }
    [DataMember] public bool AutoSaveTranslation { get; set; }
    [DataMember] public bool AutoSaveLongScreenshot { get; set; }

    private void InitializeReferencePreferences()
    {
        var defaults = new AppSettings();
        foreach (var property in typeof(AppSettings).GetProperties().Where(p => p.DeclaringType == typeof(AppSettings) && ReferencePreferenceNames.Contains(p.Name))) property.SetValue(this, property.GetValue(defaults));
        pinRestoreSpecified = pinHistorySpecified = false;
    }
    /// <summary>旧配置未包含统一的钉图偏好时，沿用原工作区值，避免启动时降低历史上限或意外恢复。</summary>
    public bool AdoptLegacyPinPreferences(bool restoreOnStart, int closedLimit)
    {
        if (closedLimit < 0 || closedLimit > 1000) throw new ArgumentOutOfRangeException(nameof(closedLimit));
        var changed = !pinRestoreSpecified || !pinHistorySpecified;
        if (!pinRestoreSpecified) PinRestoreOnStart = restoreOnStart;
        if (!pinHistorySpecified) PinHistoryLimit = closedLimit;
        return changed;
    }
    internal static readonly string[] ReferencePreferenceNames = typeof(AppSettings).GetProperties().Where(p => p.GetCustomAttributes(typeof(DataMemberAttribute), false).Length > 0)
        .Select(p => p.Name).Except(new[] { "CaptureHotkey", "PinHotkey", "RecordHotkey", "FramesPerSecond", "HistoryLimit", "CaptureCursor", "MinimizeToTray", "RecordingFormat", "OutputDirectory", "FfmpegPath", "RecordSystemAudio", "RecordMicrophone", "VideoQuality", "VideoEncoder", "RecordingDelay", "QuickSaveDirectory", "QuickSaveFormat", "SaveNameTemplate", "QuickSaveOverwrite", "JpegQuality", "AutomaticSaveDirectory", "AutoSaveCapture", "AutoSaveCopy", "AutoSavePin", "AutoSaveManual", "AutomaticSaveDays", "AutomaticSaveMegabytes", "CopyFileWithImage", "PinAlsoCopy", "Theme", "AccentColor", "InterfaceLanguage", "InterfaceFont", "PinWheelAction", "PinControlWheelAction", "PinMiddleAction", "PinDoubleClickAction", "LocalOcrLanguage", "OcrDirectCopy", "OcrCloseAfterCopyAll", "ActionBindings", "RecognitionServiceEndpoint", "RecognitionKeyEnvironment", "RecognitionTimeoutSeconds", "RecognitionSourceLanguage", "RecognitionTargetLanguage" }).ToArray();
    private void ValidateReferencePreferences()
    {
        void One(string value, params string[] choices) { if (!choices.Contains(value)) throw new ArgumentException(TextCatalog.Join(TextCatalog.Get("设置选项无效："),value)); }
        void Range(double value, double min, double max) { if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max) throw new ArgumentException(TextCatalog.Get("设置数值超出允许范围。")); }
        foreach (var color in new[] { TrayIconColor, LockedPinColor, CaptureMaskColor, ClickThroughColor, SelectionBorderColor, MagnifierGridColor }) if (color == null || !Regex.IsMatch(color, "^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")) throw new ArgumentException(TextCatalog.Get("颜色需要 #RRGGBB 或 #AARRGGBB。"));
        One(ExplorerContextMenu, "Disabled", "Images"); One(TrayIconMode, "Auto", "Color", "Custom", "Hidden");
        foreach (var action in new[] { TraySingleClickAction, TrayDoubleClickAction, TrayMiddleClickAction }) One(action, "None", "Capture", "Pin", "Settings", "ToggleHotkeys", "TogglePins", "Show");
        One(NetworkProxyMode, "System", "None", "Http"); Range(NetworkProxyPort, 1, 65535); if (NetworkProxyMode == "Http" && (string.IsNullOrWhiteSpace(NetworkProxyHost) || NetworkProxyHost.Contains("/") || NetworkProxyHost.Length > 255)) throw new ArgumentException(TextCatalog.Get("代理主机需要有效主机名或 IP 地址。"));
        if (!Enum.IsDefined(typeof(TextLineLayout), OcrTextLayout) || !Enum.IsDefined(typeof(PunctuationStyle), OcrPunctuation)) throw new ArgumentException(TextCatalog.Get("文字识别排版无效。"));
        One(ToolbarIconSize, "Small", "Medium", "Large"); Range(SelectionBorderWidth, 0, 20); One(SelectionAnchorDisplay, "None", "Four", "Eight");
        One(ScreenshotUiDetection, "None", "Windows", "Elements"); Range(ScreenshotDetectionPadding, -50, 999); One(ScreenshotGuideVisibility, "Never", "Alt", "Always");
        Range(MagnifierZoom, 1, 100); Range(ScreenshotAreaHistoryLimit, 0, 99999);
        var formats = (ColorFormatOrder ?? "").Split(','); if (formats.Length == 0 || formats.Length > 6 || formats.Distinct().Count() != formats.Length || formats.Any(f => !Enum.TryParse(f, out PixelColorFormat parsed) || !Enum.IsDefined(typeof(PixelColorFormat), parsed))) throw new ArgumentException(TextCatalog.Get("请至少启用一种有效颜色格式，不能重复。"));
        foreach (var action in new[] { ScreenshotDoubleClickAction, ScreenshotMiddleClickAction }) One(action, "None", "Copy", "Ocr", "QuickSave", "Save", "Pin", "Close");
        Range(ScreenshotCopyDpiLimit, 0, 300); if (!new[] { 0, 100, 150, 200, 250, 300 }.Contains(ScreenshotCopyDpiLimit)) throw new ArgumentException(TextCatalog.Get("复制 DPI 限制无效。"));
        One(LongScreenshotStart, "Manual", "Vertical", "Horizontal", "Buttons"); Range(PinHistoryLimit, 0, 1000);
        One(RecordingToolbarDisplayMode, "Ask", "Always", "Hidden");
        One(PinDisplayPosition, "Screenshot", "Mouse", "PrimaryCenter", "MouseScreenCenter"); One(PinRestoreOrder, "Closed", "Created");
        Range(PinDefaultOpacity, 10, 100); Range(PinClickThroughOpacity, 10, 100); Range(PinZoomStep, 1, 100); Range(PinFineZoomStep, 1, 100); Range(PinOpacityStep, 1, 100);
        foreach (var anchor in new[] { PinWheelAnchor, PinMiddleAnchor }) One(anchor, "TopLeft", "Mouse", "Center", "TopRight", "BottomLeft", "BottomRight", "Disabled");
        One(PinTransparentBackground, "Checkerboard", "Transparent", "Light", "Dark"); One(PinOutputImage, "Current", "Original");
        One(ManualSaveFormat, "PNG", "JPEG", "BMP", "TIFF"); One(AutomaticSaveFormat, "PNG", "JPEG", "BMP", "TIFF"); One(QuickSaveNotificationAction, "Locate", "CopyFile", "CopyPath", "None");
        foreach (var template in new[] { ManualSaveNameTemplate, QuickRecordingNameTemplate, AutomaticSaveNameTemplate }) SaveNaming.Expand(template, new DateTime(2000, 1, 1), 100, 100, "Image");
        if (!string.IsNullOrEmpty(QuickRecordingDirectory) && !System.IO.Path.IsPathRooted(QuickRecordingDirectory)) throw new ArgumentException(TextCatalog.Get("快速录制目录需要完整路径。"));
    }
}
