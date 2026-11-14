using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DeskSnip.Core;

[DataContract]
public sealed class PreferenceDocument
{
    [DataMember] public int Schema { get; set; } = 1;
    [DataMember] public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
    [DataMember(EmitDefaultValue = false)] public List<ActionHotkey>? ActionBindings { get; set; }
    [DataMember(EmitDefaultValue = false)] public List<ShortcutBinding>? BuiltinShortcuts { get; set; }
    [DataMember(EmitDefaultValue = false)] public List<MouseBinding>? PinMouseBindings { get; set; }
    [DataMember(EmitDefaultValue = false)] public List<MouseBinding>? GlobalMouseBindings { get; set; }
    [DataMember(EmitDefaultValue = false)] public List<string>? HiddenDefaultActions { get; set; }
}

public static class PreferenceTransfer
{
    // 使用明确白名单，新增本机路径或凭据字段不会自动进入可移植配置。
    private static readonly string[] names = { "CaptureHotkey", "PinHotkey", "RecordHotkey", "FramesPerSecond", "HistoryLimit", "CaptureCursor", "MinimizeToTray", "RecordingFormat",
        "RecordSystemAudio", "RecordMicrophone", "VideoQuality", "VideoEncoder", "RecordingDelay", "QuickSaveFormat", "SaveNameTemplate", "QuickSaveOverwrite", "JpegQuality", "AutoSaveCapture", "AutoSaveCopy", "AutoSavePin", "AutoSaveManual",
        "AutomaticSaveDays", "AutomaticSaveMegabytes", "CopyFileWithImage", "PinAlsoCopy", "Theme", "AccentColor", "InterfaceFont", "PinWheelAction", "PinControlWheelAction", "PinMiddleAction", "PinDoubleClickAction", "LocalOcrLanguage", "OcrDirectCopy", "OcrCloseAfterCopyAll",
        "RecognitionTimeoutSeconds", "RecognitionSourceLanguage", "RecognitionTargetLanguage", "InterfaceLanguage",
        "StartWithWindows", "ExplorerContextMenu", "TraySingleClickAction", "TrayDoubleClickAction", "TrayMiddleClickAction", "TrayIconMode", "TrayIconColor", "OcrTextLayout", "OcrPunctuation", "OcrRemoveSpaces",
        "LockedPinColor", "CaptureMaskColor", "ClickThroughColor", "ToolbarIconSize", "SelectionBorderWidth", "SelectionBorderFollowsTheme", "SelectionBorderColor", "SelectionAnchorDisplay",
        "ScreenshotUiDetection", "ScreenshotDetectionPadding", "ScreenshotExpandOutside", "ScreenshotCursorVisible", "ScreenshotGuideVisibility", "GuideMouseLines", "GuideSelectionCenter", "GuideSelectionBorder", "GuideMagnifier", "GuideRuleOfThirds",
        "ScreenshotShowInfoBar", "ScreenshotShowMagnifier", "MagnifierZoom", "MagnifierGrid", "MagnifierCrosshair", "MagnifierCenter", "MagnifierCoordinates", "MagnifierShowShortcuts", "MagnifierSmooth", "MagnifierGridColor", "ColorFormatOrder",
        "ScreenshotCloseConfirmation", "ScreenshotShowShortcuts", "ScreenshotAreaHistoryLimit", "ScreenshotDoubleClickAction", "ScreenshotMiddleClickAction", "ScreenshotWheelTargets", "ScreenshotWheelResize", "ScreenshotCopyDpiLimit", "ScreenshotSecondaryButtons",
        "SoundCaptureStart", "SoundCapturePin", "SoundCaptureCopy", "SoundSaveSuccess", "SoundQuickSave", "LongScreenshotStart", "LongScreenshotShortcuts", "RecordingCloseAfterExport", "RecordingExcludeToolbar", "RecordingToolbarDisplayMode",
        "PinImageEnabled", "PinRestoreOnStart", "PinHistoryLimit", "PinDisplayPosition", "PinRestoreOrder", "PinLockMovement", "PinLockMouseClose", "PinLockKeyClose", "PinLockDrop", "PinShowLockHint", "PinShowMenuShortcuts", "PinConfirmKeyClose", "PinConfirmMouseClose",
        "PinShowShadow", "PinDefaultOpacity", "PinClickThroughOpacity", "PinZoomStep", "PinFineZoomStep", "PinOpacityStep", "PinWheelAnchor", "PinMiddleAnchor", "PinResizeFromBorder", "PinClickThroughToolbar", "PinDefaultTopmost", "PinAutoFit", "PinRescueOnShow", "PinKeepOthersHidden", "PinTaskbarWhenUnpinned", "PinSmoothScaling", "PinTransparentBackground", "PinOutputImage",
        "SaveCopyFile", "SaveCopyPath", "SaveExif", "ManualSaveNameTemplate", "ManualSaveFormat", "RememberSaveExtension", "QuickSaveNotification", "QuickSaveNotificationAction", "QuickRecordingEnabled", "QuickRecordingNameTemplate", "QuickRecordingOverwrite",
        "AutomaticSaveNameTemplate", "AutomaticSaveCleanup", "AutomaticSaveEnabled", "AutomaticSaveFormat", "AutomaticSaveOverwrite", "AutoSaveOcr", "AutoSaveTranslation", "AutoSaveLongScreenshot",
        "MarkSharedColor", "MarkColorPalette", "MarkSecondaryEditing", "MarkQuickSettings", "MarkPencilCursor", "MarkPencilOutlineMinWidth", "MarkRememberPinTool", "MarkRememberScreenshotTool", "MarkLimitToSelection", "MarkRememberToolVariants", "MarkDisableWheel", "MarkQuickSettingsPadding", "MarkRoundedControls", "MarkRenumberAfterDelete", "MarkSerialStartAtZero", "MarkGlobalSerial", "MarkSerialDefaultColor", "MarkSerialBackground", "MarkSerialTextSize", "MarkPencilShiftMode", "MarkSmoothPencil", "MarkPolylineJoin", "MarkPolylineCap", "MarkArrowDoubleClickDisable", "MarkHideArrowControls", "MarkHighlighterMode", "MarkTextWrap", "MarkEraserSecondaryEditing", "MarkAutoPrivacyProcessing", "MarkAutomaticWatermark", "MarkWatermarkText", "MarkToolbarTools", "BuiltinMergeNumberKeys", "ShortcutDisableInFullscreen", "GlobalMouseShowMask", "ClipboardImageFileFormat", "OcrDialogOnScreenshot", "OcrDialogOnCopy", "OcrDialogOnPin" };
    public static PreferenceDocument Export(AppSettings settings)
    {
        settings.Validate(); var defaults = new AppSettings(); var values = new Dictionary<string, string>();
        foreach (var name in names) { var property = Property(name); var value = property.GetValue(settings); if (!Equals(value, property.GetValue(defaults))) values.Add(name, Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""); }
        return new PreferenceDocument { Values = values, ActionBindings = settings.ActionBindings.Count == 0 ? null : settings.ActionBindings.Select(a => a.Clone()).ToList(),
            BuiltinShortcuts = settings.BuiltinShortcuts?.Select(a => a.Clone()).ToList(), PinMouseBindings = settings.PinMouseBindings?.Select(a => a.Clone()).ToList(), GlobalMouseBindings = settings.GlobalMouseBindings?.Select(a => a.Clone()).ToList(), HiddenDefaultActions = settings.HiddenDefaultActions?.ToList() };
    }
    public static AppSettings Import(AppSettings current, PreferenceDocument document)
    {
        if (document.Schema != 1 || document.Values == null || document.Values.Count > names.Length) throw new ArgumentException(TextCatalog.Get("配置格式或版本无效。"));
        var next = current.Clone();
        foreach (var pair in document.Values)
        {
            if (!names.Contains(pair.Key) || pair.Value == null || pair.Value.Length > 1000) throw new ArgumentException(TextCatalog.Get("配置包含不支持的字段或本机数据。"));
            var property = Property(pair.Key); object value;
            try { value = property.PropertyType.IsEnum ? Enum.Parse(property.PropertyType, pair.Value, false) : Convert.ChangeType(pair.Value, property.PropertyType, CultureInfo.InvariantCulture); }
            catch (Exception error) when (error is FormatException || error is OverflowException || error is ArgumentException) { throw new ArgumentException(TextCatalog.Join(TextCatalog.Get("配置字段值无效："),pair.Key)); }
            property.SetValue(next, value);
        }
        if (document.ActionBindings != null) next.ActionBindings = document.ActionBindings.Select(a => a?.Clone() ?? throw new ArgumentException(TextCatalog.Get("全局动作配置无效。"))).ToList();
        if (document.BuiltinShortcuts != null) next.BuiltinShortcuts = document.BuiltinShortcuts.Select(a => a?.Clone() ?? throw new ArgumentException(TextCatalog.Get("内置快捷键配置无效。"))).ToList();
        if (document.PinMouseBindings != null) next.PinMouseBindings = document.PinMouseBindings.Select(a => a?.Clone() ?? throw new ArgumentException(TextCatalog.Get("鼠标配置无效。"))).ToList();
        if (document.GlobalMouseBindings != null) next.GlobalMouseBindings = document.GlobalMouseBindings.Select(a => a?.Clone() ?? throw new ArgumentException(TextCatalog.Get("鼠标配置无效。"))).ToList();
        if (document.HiddenDefaultActions != null) next.HiddenDefaultActions = document.HiddenDefaultActions.ToList();
        next.Validate(); return next;
    }
    private static PropertyInfo Property(string name) => typeof(AppSettings).GetProperty(name) ?? throw new InvalidOperationException(TextCatalog.Get("偏好配置结构已过期。"));
    public static void Write(string path, AppSettings settings) => AtomicFile.Write(path, stream => new DataContractJsonSerializer(typeof(PreferenceDocument), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }).WriteObject(stream, Export(settings)));
    public static PreferenceDocument Read(string path)
    {
        using (var stream = File.OpenRead(path))
        {
            if (stream.Length > 128 * 1024) throw new ArgumentException(TextCatalog.Get("配置文件过大。"));
            return (PreferenceDocument)(new DataContractJsonSerializer(typeof(PreferenceDocument), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }).ReadObject(stream) ?? throw new ArgumentException(TextCatalog.Get("配置文件为空。")));
        }
    }
}

[DataContract]
public sealed class ActionHotkey
{
    [DataMember] public string Action { get; set; } = "QuickCapture";
    [DataMember] public string Hotkey { get; set; } = "";
    [DataMember] public bool TrayMenu { get; set; } = true;
    [DataMember] public string Title { get; set; } = "";
    [OnDeserializing] private void Loading(StreamingContext context) { Action = "QuickCapture"; Hotkey = Title = ""; TrayMenu = true; }
    public ActionHotkey Clone() => (ActionHotkey)MemberwiseClone();
}

public static class DesktopActions
{
    public static readonly string[] DefaultNames = { "Capture", "CustomCapture", "CopyCapture", "Pin", "RestorePin", "SwitchPinGroup", "ClosePins", "TogglePins", "ToggleOtherPins", "PinFile", "TogglePenetration", "ToggleHotkeys", "Record" };
    public static readonly string[] Names = DefaultNames.Concat(new[] { "QuickCapture", "LongScreenshot", "OcrClipboard", "RecoverPins", "Show" }).ToArray();
    public static string Label(string name) => name switch
    {
        "Capture" => TextCatalog.Get("截图"), "CustomCapture" => TextCatalog.Get("自定义截图"), "CopyCapture" => TextCatalog.Get("截图并复制"), "Pin" => TextCatalog.Get("钉图"), "RestorePin" => TextCatalog.Get("恢复最近关闭的钉图"), "SwitchPinGroup" => TextCatalog.Get("切换钉图分组"), "ClosePins" => TextCatalog.Get("关闭所有钉图"), "TogglePins" => TextCatalog.Get("隐藏 / 显示所有钉图"), "ToggleOtherPins" => TextCatalog.Get("隐藏 / 显示所选以外的钉图"), "PinFile" => TextCatalog.Get("钉住图像文件"), "TogglePenetration" => TextCatalog.Get("开启 / 关闭鼠标穿透"), "ToggleHotkeys" => TextCatalog.Get("禁用 / 恢复其他快捷键"), "Record" => TextCatalog.Get("开始录屏 / 暂停继续"), "QuickCapture" => TextCatalog.Get("快速截图保存"), "LongScreenshot" => TextCatalog.Get("长截图"), "OcrClipboard" => TextCatalog.Get("识别剪贴板"), "RecoverPins" => TextCatalog.Get("解除全部穿透 / 锁定"), "Show" => TextCatalog.Get("打开控制台"), _ => name
    };
}
