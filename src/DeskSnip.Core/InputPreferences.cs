using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace DeskSnip.Core;

public sealed partial class AppSettings
{
    [DataMember] public bool MarkSharedColor { get; set; } = true;
    [DataMember] public string MarkColorPalette { get; set; } = "#ED4F57,#00A886,#318BF1,#FFCC38,#FFFFFF,#111111";
    [DataMember] public bool MarkSecondaryEditing { get; set; } = true;
    [DataMember] public bool MarkQuickSettings { get; set; } = true;
    [DataMember] public string MarkPencilCursor { get; set; } = "CrossAndOutline";
    [DataMember] public int MarkPencilOutlineMinWidth { get; set; } = 50;
    [DataMember] public bool MarkRememberPinTool { get; set; }
    [DataMember] public bool MarkRememberScreenshotTool { get; set; }
    [DataMember] public string MarkLastPinTool { get; set; } = "Arrow";
    [DataMember] public string MarkLastScreenshotTool { get; set; } = "Arrow";
    [DataMember] public bool MarkLimitToSelection { get; set; } = true;
    // 旧设置：工具栏默认工具现在始终按组记忆并持久化到 MarkToolDefaults。
    [DataMember] public bool MarkRememberToolVariants { get; set; }
    [DataMember] public string MarkToolDefaults { get; set; } = "";
    [DataMember] public string MarkToolStyles { get; set; } = "";
    [DataMember] public bool MarkDisableWheel { get; set; }
    [DataMember] public int MarkQuickSettingsPadding { get; set; }
    [DataMember] public string MarkRoundedControls { get; set; } = "Hover";
    [DataMember] public bool MarkRenumberAfterDelete { get; set; } = true;
    [DataMember] public bool MarkSerialStartAtZero { get; set; }
    [DataMember] public bool MarkGlobalSerial { get; set; }
    [DataMember] public bool MarkSerialDefaultColor { get; set; } = true;
    [DataMember] public string MarkSerialBackground { get; set; } = "#ED4F57";
    [DataMember] public string MarkSerialTextSize { get; set; } = "Medium";
    [DataMember] public string MarkPencilShiftMode { get; set; } = "FreeAngle";
    [DataMember] public bool MarkSmoothPencil { get; set; } = true;
    [DataMember] public string MarkPolylineJoin { get; set; } = "Round";
    [DataMember] public string MarkPolylineCap { get; set; } = "Round";
    [DataMember] public bool MarkArrowDoubleClickDisable { get; set; }
    [DataMember] public bool MarkHideArrowControls { get; set; }
    [DataMember] public string MarkHighlighterMode { get; set; } = "FreeAngle";
    [DataMember] public string MarkTextWrap { get; set; } = "WordOrCharacter";
    [DataMember] public bool MarkEraserSecondaryEditing { get; set; }
    [DataMember] public bool MarkAutoPrivacyProcessing { get; set; } = true;
    [DataMember] public bool MarkAutomaticWatermark { get; set; }
    [DataMember] public string MarkWatermarkText { get; set; } = "DeskSnip {time}";
    [DataMember] public string MarkToolbarTools { get; set; } = "Rectangle,Arrow,Pen,Text,Number,Mosaic,Cover,Eraser,Spotlight";
    [DataMember] public bool BuiltinMergeNumberKeys { get; set; } = true;
    [DataMember] public bool ShortcutDisableInFullscreen { get; set; }
    [DataMember] public string ShortcutIgnoredProcesses { get; set; } = "";
    [DataMember] public bool GlobalMouseShowMask { get; set; }
    [DataMember] public string GlobalMouseIgnoredProcesses { get; set; } = "";
    [DataMember] public string ClipboardImageFileFormat { get; set; } = "Auto";
    [DataMember] public bool OcrDialogOnScreenshot { get; set; } = true;
    [DataMember] public bool OcrDialogOnCopy { get; set; } = true;
    [DataMember] public bool OcrDialogOnPin { get; set; } = true;
    [DataMember(EmitDefaultValue = false)] public List<ShortcutBinding>? BuiltinShortcuts { get; set; }
    [DataMember(EmitDefaultValue = false)] public List<MouseBinding>? PinMouseBindings { get; set; }
    [DataMember(EmitDefaultValue = false)] public List<MouseBinding>? GlobalMouseBindings { get; set; }
    [DataMember(EmitDefaultValue = false)] public List<string>? HiddenDefaultActions { get; set; }

    private void ValidateInputPreferences()
    {
        void One(string value, params string[] choices) { if (!choices.Contains(value)) throw new ArgumentException(TextCatalog.Get("标注设置选项无效。")); }
        if (!AnnotationPreferenceRules.ValidPalette(MarkColorPalette) || !Regex.IsMatch(MarkSerialBackground ?? "", "^#[0-9a-fA-F]{6}$")) throw new ArgumentException(TextCatalog.Get("调色板需要 1–24 个 #RRGGBB 颜色，用逗号分隔。"));
        if (MarkPencilOutlineMinWidth < 0 || MarkPencilOutlineMinWidth > 400 || MarkQuickSettingsPadding < 0 || MarkQuickSettingsPadding > 100) throw new ArgumentException(TextCatalog.Get("画笔轮廓宽度或快捷面板边距无效。"));
        One(MarkPencilCursor, "CrossAndOutline", "Cross", "Outline", "Arrow"); One(MarkRoundedControls, "Hover", "Always", "Never");
        One(MarkSerialTextSize, "Small", "Medium", "Large"); One(MarkPencilShiftMode, "FreeAngle", "Snap45", "Axis");
        One(MarkPolylineJoin, "Round", "Miter", "Bevel"); One(MarkPolylineCap, "Round", "Flat", "Square");
        One(MarkHighlighterMode, "FreeAngle", "Horizontal", "Vertical"); One(MarkTextWrap, "WordOrCharacter", "Character", "None");
        One(ClipboardImageFileFormat, "Auto", "PNG", "JPEG", "BMP");
        foreach (var tool in new[] { MarkLastPinTool, MarkLastScreenshotTool }) if (!Enum.TryParse(tool, out AnnotationKind parsed) || !Enum.IsDefined(typeof(AnnotationKind), parsed)) throw new ArgumentException(TextCatalog.Get("记忆标注工具无效。"));
        if (string.IsNullOrWhiteSpace(MarkWatermarkText) || MarkWatermarkText.Length > 256) throw new ArgumentException(TextCatalog.Get("水印文字需要 1–256 字符。"));
        var toolbar = (MarkToolbarTools ?? "").Split(','); if (toolbar.Length < 1 || toolbar.Length > 9 || toolbar.Distinct().Count() != toolbar.Length || toolbar.Any(t => !AnnotationPreferenceRules.ToolbarGroups.Contains(t))) throw new ArgumentException(TextCatalog.Get("标注工具栏配置无效。"));
        InputBindingRules.ValidateShortcuts(BuiltinShortcuts, BuiltinMergeNumberKeys);
        InputBindingRules.ValidateMouse(PinMouseBindings, false); InputBindingRules.ValidateMouse(GlobalMouseBindings, true);
        AnnotationPreferenceRules.ParseToolDefaults(MarkToolDefaults);
        AnnotationStyleRules.Validate(MarkToolStyles);
        InputBindingRules.ProcessNames(ShortcutIgnoredProcesses); InputBindingRules.ProcessNames(GlobalMouseIgnoredProcesses);
        if (HiddenDefaultActions != null && (HiddenDefaultActions.Count > 16 || HiddenDefaultActions.Distinct().Count() != HiddenDefaultActions.Count || HiddenDefaultActions.Any(a => !DesktopActions.DefaultNames.Contains(a)))) throw new ArgumentException(TextCatalog.Get("默认动作配置无效。"));
    }
}

public static class AnnotationPreferenceRules
{
    public static readonly string[] ToolbarGroups = { "Rectangle", "Arrow", "Pen", "Text", "Number", "Mosaic", "Cover", "Eraser", "Spotlight" };
    public static AnnotationKind[] GroupTools(string group) => group switch
    {
        "Rectangle" => new[] { AnnotationKind.Rectangle, AnnotationKind.Ellipse, AnnotationKind.RoundedRectangle },
        "Arrow" => new[] { AnnotationKind.Arrow, AnnotationKind.Line, AnnotationKind.Polyline },
        "Pen" => new[] { AnnotationKind.Pen, AnnotationKind.Highlight },
        "Text" => new[] { AnnotationKind.Text, AnnotationKind.Watermark },
        "Number" => new[] { AnnotationKind.Number },
        "Mosaic" => new[] { AnnotationKind.Mosaic, AnnotationKind.Blur, AnnotationKind.MosaicPen },
        "Cover" => new[] { AnnotationKind.Cover, AnnotationKind.CoverPen },
        "Eraser" => new[] { AnnotationKind.Eraser, AnnotationKind.EraserPen },
        "Spotlight" => new[] { AnnotationKind.Spotlight, AnnotationKind.Magnifier },
        _ => throw new ArgumentException(TextCatalog.Get("工具栏分组无效。"))
    };
    public static IReadOnlyDictionary<string, AnnotationKind> ParseToolDefaults(string value)
    {
        var result = new Dictionary<string, AnnotationKind>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(value)) return result;
        if (value.Length > 512) throw new ArgumentException(TextCatalog.Get("工具栏默认工具配置无效。"));
        foreach (var pair in value.Split(','))
        {
            var parts = pair.Split('=');
            if (parts.Length != 2 || !ToolbarGroups.Contains(parts[0]) || !Enum.TryParse(parts[1], out AnnotationKind kind) ||
                !GroupTools(parts[0]).Contains(kind) || result.ContainsKey(parts[0])) throw new ArgumentException(TextCatalog.Get("工具栏默认工具配置无效。"));
            result[parts[0]] = kind;
        }
        return result;
    }
    public static bool ValidPalette(string value) => value != null && value.Split(',').Length <= 24 && value.Split(',').All(c => Regex.IsMatch(c, "^#[0-9a-fA-F]{6}$"));
    public static PixelPoint ConstrainStroke(PixelPoint start, PixelPoint end, string mode)
    {
        var dx = end.X - start.X; var dy = end.Y - start.Y;
        if (mode == "Axis") return Math.Abs(dx) >= Math.Abs(dy) ? new PixelPoint(end.X, start.Y) : new PixelPoint(start.X, end.Y);
        if (mode != "Snap45") return end;
        var length = Math.Sqrt(dx * dx + dy * dy); var angle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * Math.PI / 4;
        return new PixelPoint(start.X + Math.Cos(angle) * length, start.Y + Math.Sin(angle) * length);
    }
    public static double SerialTextScale(string size) => size == "Small" ? .95 : size == "Large" ? 1.5 : 1.2;
}
