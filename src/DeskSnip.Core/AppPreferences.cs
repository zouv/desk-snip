using System;
using System.IO;
using System.Runtime.Serialization;
using System.Collections.Generic;
using System.Linq;

namespace DeskSnip.Core;

public sealed partial class AppSettings
{
    [DataMember] public string FfmpegPath { get; set; } = "";
    [DataMember] public bool RecordSystemAudio { get; set; }
    [DataMember] public bool RecordMicrophone { get; set; }
    [DataMember] public int VideoQuality { get; set; } = 80;
    [DataMember] public VideoEncoder VideoEncoder { get; set; } = VideoEncoder.Software;
    [DataMember] public int RecordingDelay { get; set; }
    [DataMember] public string QuickSaveDirectory { get; set; } = "";
    [DataMember] public string QuickSaveFormat { get; set; } = "PNG";
    [DataMember] public string SaveNameTemplate { get; set; } = "DeskSnip-{date}-{time}-{width}x{height}";
    [DataMember] public bool QuickSaveOverwrite { get; set; }
    [DataMember] public int JpegQuality { get; set; } = 95;
    [DataMember] public string AutomaticSaveDirectory { get; set; } = "";
    [DataMember] public bool AutoSaveCapture { get; set; }
    [DataMember] public bool AutoSaveCopy { get; set; }
    [DataMember] public bool AutoSavePin { get; set; }
    [DataMember] public bool AutoSaveManual { get; set; }
    [DataMember] public int AutomaticSaveDays { get; set; } = 30;
    [DataMember] public int AutomaticSaveMegabytes { get; set; } = 1024;
    [DataMember] public bool CopyFileWithImage { get; set; }
    [DataMember] public bool PinAlsoCopy { get; set; }
    [DataMember] public string Theme { get; set; } = "Light";
    [DataMember] public string AccentColor { get; set; } = "#3489F5";
    private string interfaceLanguage = "zh-CN";
    private bool hasStoredInterfaceLanguage;
    [DataMember] public string InterfaceLanguage
    {
        get => interfaceLanguage;
        set { interfaceLanguage = value; hasStoredInterfaceLanguage = true; }
    }
    [DataMember] public string InterfaceFont { get; set; } = "Microsoft YaHei UI";
    [DataMember] public string PinWheelAction { get; set; } = "Zoom";
    [DataMember] public string PinControlWheelAction { get; set; } = "Opacity";
    [DataMember] public string PinMiddleAction { get; set; } = "Original";
    [DataMember] public string PinDoubleClickAction { get; set; } = "Close";
    [DataMember] public string LocalOcrLanguage { get; set; } = "";
    [DataMember] public bool OcrDirectCopy { get; set; }
    [DataMember] public bool OcrCloseAfterCopyAll { get; set; } = true;
    [DataMember] public List<ActionHotkey> ActionBindings { get; set; } = new List<ActionHotkey>();
    public AppSettings Clone()
    {
        var value = (AppSettings)MemberwiseClone(); value.ActionBindings = ActionBindings.Select(a => a.Clone()).ToList();
        value.BuiltinShortcuts = BuiltinShortcuts?.Select(a => a.Clone()).ToList(); value.PinMouseBindings = PinMouseBindings?.Select(a => a.Clone()).ToList();
        value.GlobalMouseBindings = GlobalMouseBindings?.Select(a => a.Clone()).ToList(); value.HiddenDefaultActions = HiddenDefaultActions?.ToList(); return value;
    }
    [OnDeserializing] private void LoadingPreferences(StreamingContext context)
    {
        FfmpegPath = ""; VideoQuality = 80; VideoEncoder = VideoEncoder.Software; QuickSaveDirectory = AutomaticSaveDirectory = ""; QuickSaveFormat = "PNG";
        SaveNameTemplate = "DeskSnip-{date}-{time}-{width}x{height}"; JpegQuality = 95; AutomaticSaveDays = 30; AutomaticSaveMegabytes = 1024;
        Theme = "Light"; AccentColor = "#3489F5"; interfaceLanguage = "zh-CN"; hasStoredInterfaceLanguage = false; InterfaceFont = "Microsoft YaHei UI"; PinWheelAction = "Zoom"; PinControlWheelAction = "Opacity"; PinMiddleAction = "Original"; PinDoubleClickAction = "Close";
        LocalOcrLanguage = ""; OcrCloseAfterCopyAll = true;
        ActionBindings = new List<ActionHotkey>();
        InitializeServicePreferences();
        InitializeReferencePreferences();
    }
    [OnDeserialized] private void PreferencesLoaded(StreamingContext context)
    {
        if (!hasStoredInterfaceLanguage && string.Equals(AccentColor, "#008978", StringComparison.OrdinalIgnoreCase)) AccentColor = "#3489F5";
    }
    private void ValidatePreferences()
    {
        InterfaceLanguageRules.Validate(InterfaceLanguage);
        ValidateServicePreferences();
        ValidateReferencePreferences();
        ValidateInputPreferences();
        if (VideoQuality < 1 || VideoQuality > 100 || RecordingDelay < 0 || RecordingDelay > 30 || !Enum.IsDefined(typeof(VideoEncoder), VideoEncoder)) throw new ArgumentException(TextCatalog.Get("录制画质、倒计时或编码器无效。"));
        if (!string.IsNullOrEmpty(FfmpegPath) && (!Path.IsPathRooted(FfmpegPath) || !string.Equals(Path.GetExtension(FfmpegPath), ".exe", StringComparison.OrdinalIgnoreCase))) throw new ArgumentException(TextCatalog.Get("FFmpeg 路径需要为完整的 exe 文件路径。"));
        if (QuickSaveFormat != "PNG" && QuickSaveFormat != "JPEG" && QuickSaveFormat != "BMP" && QuickSaveFormat != "TIFF") throw new ArgumentException(TextCatalog.Get("快速保存格式无效。"));
        SaveNaming.Expand(SaveNameTemplate, new DateTime(2000, 1, 1), 100, 100, "Image");
        if (JpegQuality < 0 || JpegQuality > 100 || AutomaticSaveDays < 1 || AutomaticSaveDays > 36500 || AutomaticSaveMegabytes < 1 || AutomaticSaveMegabytes > 1000000) throw new ArgumentException(TextCatalog.Get("保存质量或自动清理上限无效。"));
        foreach (var path in new[] { QuickSaveDirectory, AutomaticSaveDirectory }) if (!string.IsNullOrEmpty(path) && !Path.IsPathRooted(path)) throw new ArgumentException(TextCatalog.Get("保存目录需要完整路径。"));
        if (Theme != "Light" && Theme != "Dark" && Theme != "System" || AccentColor == null || !System.Text.RegularExpressions.Regex.IsMatch(AccentColor, "^#[0-9A-Fa-f]{6}$") || string.IsNullOrWhiteSpace(InterfaceFont) || InterfaceFont.Length > 128) throw new ArgumentException(TextCatalog.Get("主题、强调色或界面字体无效。"));
        var actions = new[] { "Zoom", "Opacity", "Original", "Close", "Hide", "Copy", "Lock", "None" };
        if (Array.IndexOf(actions, PinWheelAction) < 0 || Array.IndexOf(actions, PinControlWheelAction) < 0 || Array.IndexOf(actions, PinMiddleAction) < 0 || Array.IndexOf(actions, PinDoubleClickAction) < 0) throw new ArgumentException(TextCatalog.Get("钉图鼠标动作无效。"));
        if (ActionBindings == null || ActionBindings.Count > 64 || ActionBindings.Any(a => a == null || !DesktopActions.Names.Contains(a.Action) || a.Hotkey == null || a.Hotkey.Length > 80 || a.Title == null || a.Title.Length > 80)) throw new ArgumentException(TextCatalog.Get("全局动作配置无效。"));
    }
}
