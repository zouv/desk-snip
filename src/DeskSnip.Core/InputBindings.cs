using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace DeskSnip.Core;

[DataContract]
public sealed class ShortcutBinding
{
    [DataMember] public string Context { get; set; } = "Common";
    [DataMember] public string Action { get; set; } = "Copy";
    [DataMember] public string Hotkey { get; set; } = "";
    public ShortcutBinding Clone() => (ShortcutBinding)MemberwiseClone();
}

[DataContract]
public sealed class MouseBinding
{
    [DataMember] public string Action { get; set; } = "Scale";
    [DataMember] public string Modifiers { get; set; } = "";
    [DataMember] public string Gesture { get; set; } = "None";
    public MouseBinding Clone() => (MouseBinding)MemberwiseClone();
}

public sealed class InputAction
{
    private readonly string label;
    public InputAction(string name, string label, string hotkey = "", bool supported = true) { Name = name; this.label = label; DefaultHotkey = hotkey; Supported = supported; }
    public string Name { get; }
    public string Label => TextCatalog.Get(label);
    public string DefaultHotkey { get; }
    public bool Supported { get; }
}

public static class InputBindingCatalog
{
    public static readonly string[] Contexts = { "Common", "Screenshot", "LongScreenshot", "Pin", "ImagePin", "DynamicPin", "TextPin", "WindowPin" };
    public static readonly string[] ContextLabels = { TextCatalog.Get("截图与钉图"), TextCatalog.Get("截图"), TextCatalog.Get("长截图"), TextCatalog.Get("钉图窗口"), TextCatalog.Get("图像钉图"), TextCatalog.Get("动态钉图"), TextCatalog.Get("文字钉图"), TextCatalog.Get("窗口钉图") };
    public static readonly InputAction[] Common = {
        new InputAction("Cancel", TextCatalog.Get("取消 / 退出"), "Escape"), new InputAction("Close", TextCatalog.Get("关闭窗口"), "Ctrl+W"), new InputAction("Copy", TextCatalog.Get("复制"), "Ctrl+C"),
        new InputAction("Save", TextCatalog.Get("保存到文件"), "Ctrl+S"), new InputAction("QuickSave", TextCatalog.Get("快速保存"), "Ctrl+Shift+S"), new InputAction("Pin", TextCatalog.Get("钉图"), "Ctrl+T"),
        new InputAction("Ocr", TextCatalog.Get("识别文字"), "Shift+C"), new InputAction("Undo", TextCatalog.Get("撤销标注"), "Ctrl+Z"), new InputAction("Redo", TextCatalog.Get("重做标注"), "Ctrl+Y"), new InputAction("Clear", TextCatalog.Get("清空标注"), "Ctrl+Shift+Z"),
        new InputAction("SecondaryEditing", TextCatalog.Get("启用 / 禁用二次编辑"), "Ctrl+E"), new InputAction("Guides", TextCatalog.Get("显示放大镜 / 辅助线"), "Alt"),
        new InputAction("CopyColor", TextCatalog.Get("复制颜色"), "C"), new InputAction("CopyCoordinates", TextCatalog.Get("复制鼠标坐标"), "Alt+C"), new InputAction("NextColor", TextCatalog.Get("切换颜色格式"), "Shift"),
        new InputAction("Service", TextCatalog.Get("翻译"), "Ctrl+Q"), new InputAction("Document", TextCatalog.Get("文档识别"), "Shift+L"), new InputAction("Table", TextCatalog.Get("表格识别"), "Shift+Q"), new InputAction("Formula", TextCatalog.Get("公式识别"), "Shift+F"),
        new InputAction("CursorUp", TextCatalog.Get("鼠标向上移动"), "W"), new InputAction("CursorDown", TextCatalog.Get("鼠标向下移动"), "S"), new InputAction("CursorLeft", TextCatalog.Get("鼠标向左移动"), "A"), new InputAction("CursorRight", TextCatalog.Get("鼠标向右移动"), "D"),
        new InputAction("ToolRectangle", TextCatalog.Get("设置标注工具：几何形状"), "Shift+D1"), new InputAction("ToolPolyline", TextCatalog.Get("设置标注工具：折线"), "Shift+D2"), new InputAction("ToolArrow", TextCatalog.Get("设置标注工具：箭头"), "Shift+D3"),
        new InputAction("ToolNumber", TextCatalog.Get("设置标注工具：序号"), "Shift+D4"), new InputAction("ToolPen", TextCatalog.Get("设置标注工具：画笔"), "Shift+D5"), new InputAction("ToolCover", TextCatalog.Get("设置标注工具：遮挡"), "Shift+D6"),
        new InputAction("ToolMosaic", TextCatalog.Get("设置标注工具：马赛克"), "Shift+D7"), new InputAction("ToolText", TextCatalog.Get("设置标注工具：文字"), "Shift+D8"), new InputAction("ToolEraser", TextCatalog.Get("设置标注工具：橡皮擦"), "Shift+D9"),
        new InputAction("ToolHighlight", TextCatalog.Get("设置标注工具：高亮"), "Shift+D0"), new InputAction("ToolWatermark", TextCatalog.Get("设置标注工具：水印"), "Shift+OemPlus"), new InputAction("ToolSpotlight", TextCatalog.Get("设置标注工具：聚光灯"), "Shift+Oem1"),
        new InputAction("ToolMagnifier", TextCatalog.Get("设置标注工具：放大镜"), "Shift+OemMinus"), new InputAction("AutoMosaic", TextCatalog.Get("自动隐私马赛克"), "Shift+Oem5"),
        new InputAction("WheelUp", TextCatalog.Get("模拟滚轮向上"), "OemPlus"), new InputAction("WheelDown", TextCatalog.Get("模拟滚轮向下"), "OemMinus") };
    public static InputAction[] Actions(string context) => context switch
    {
        "Common" => Common,
        "Screenshot" => new[] { new InputAction("Confirm", TextCatalog.Get("完成截图"), "Enter"), new InputAction("SelectAll", TextCatalog.Get("选择所在屏幕 / 全部桌面"), "Ctrl+A"), new InputAction("Toolbar", TextCatalog.Get("显示 / 隐藏工具栏"), "Space"), new InputAction("Detection", TextCatalog.Get("切换 UI 检测"), "Tab"), new InputAction("Cursor", TextCatalog.Get("显示 / 隐藏光标"), "Oem3"), new InputAction("Shortcuts", TextCatalog.Get("显示 / 隐藏快捷键提示"), "F1"), new InputAction("PreviousArea", TextCatalog.Get("上一历史区域"), "PageUp"), new InputAction("NextArea", TextCatalog.Get("下一历史区域"), "PageDown"), new InputAction("LongScreenshot", TextCatalog.Get("进入长截图"), "L"), new InputAction("Coordinates", TextCatalog.Get("相对 / 绝对坐标"), "P"), new InputAction("MagnifierLock", TextCatalog.Get("固定 / 释放放大镜"), "M"), new InputAction("Record", TextCatalog.Get("切换到录屏"), "G") },
        "LongScreenshot" => new[] { new InputAction("Capture", TextCatalog.Get("采集下一帧"), "Space"), new InputAction("Undo", TextCatalog.Get("撤回上一帧"), "Ctrl+Z"), new InputAction("Finish", TextCatalog.Get("完成长截图"), "Enter"), new InputAction("Cancel", TextCatalog.Get("取消"), "Escape") },
        "Pin" => new[] { new InputAction("Border", TextCatalog.Get("显示 / 隐藏选中边框"), "F"), new InputAction("Lock", TextCatalog.Get("锁定 / 解锁"), "L"), new InputAction("Topmost", TextCatalog.Get("置顶 / 取消置顶"), "T"), new InputAction("Hide", TextCatalog.Get("隐藏钉图"), "B"), new InputAction("Paste", TextCatalog.Get("替换剪贴板图片"), "Ctrl+V"), new InputAction("MoveLeft", TextCatalog.Get("向左移动"), "Left"), new InputAction("MoveRight", TextCatalog.Get("向右移动"), "Right"), new InputAction("MoveUp", TextCatalog.Get("向上移动"), "Up"), new InputAction("MoveDown", TextCatalog.Get("向下移动"), "Down") },
        "ImagePin" => new[] { new InputAction("Original", TextCatalog.Get("原始大小 / 切回"), "O"), new InputAction("RotateLeft", TextCatalog.Get("向左旋转"), "Ctrl+Left"), new InputAction("RotateRight", TextCatalog.Get("向右旋转"), "Ctrl+Right"), new InputAction("Mirror", TextCatalog.Get("水平镜像"), "Ctrl+H"), new InputAction("Flip", TextCatalog.Get("垂直翻转"), "Ctrl+J"), new InputAction("Edit", TextCatalog.Get("打开标注编辑器"), "E") },
        "DynamicPin" => new[] { new InputAction("Play", TextCatalog.Get("播放 / 暂停"), "Space", false), new InputAction("First", TextCatalog.Get("第一帧"), "Home", false), new InputAction("Last", TextCatalog.Get("最后一帧"), "End", false) },
        "TextPin" => new[] { new InputAction("SelectAll", TextCatalog.Get("选择全部文字"), "Ctrl+A", false), new InputAction("Copy", TextCatalog.Get("复制文字"), "Ctrl+C", false), new InputAction("Edit", TextCatalog.Get("编辑文字"), "F2", false) },
        "WindowPin" => new[] { new InputAction("Refresh", TextCatalog.Get("刷新窗口画面"), "F5", false), new InputAction("Interact", TextCatalog.Get("切换交互"), "Space", false) },
        _ => throw new ArgumentException(TextCatalog.Get("快捷键场景无效。"))
    };
    public static readonly InputAction[] PinMouse = {
        new InputAction("Scale", TextCatalog.Get("缩放")), new InputAction("FineScale", TextCatalog.Get("精细缩放")), new InputAction("Opacity", TextCatalog.Get("调整透明度")), new InputAction("Close", TextCatalog.Get("关闭")),
        new InputAction("CopyClose", TextCatalog.Get("复制内容并关闭")), new InputAction("Destroy", TextCatalog.Get("销毁")), new InputAction("Reset", TextCatalog.Get("还原")),
        new InputAction("ThumbnailRegion", TextCatalog.Get("选择缩略图区域"), supported: false), new InputAction("Thumbnail", TextCatalog.Get("缩略图"), supported: false), new InputAction("ThumbnailMove", TextCatalog.Get("移动缩略图区域"), supported: false),
        new InputAction("Copy", TextCatalog.Get("复制内容")), new InputAction("CopyText", TextCatalog.Get("复制文字")), new InputAction("Menu", TextCatalog.Get("菜单")), new InputAction("Markup", TextCatalog.Get("标注工具栏")),
        new InputAction("Move", TextCatalog.Get("移动 / 调整")), new InputAction("MoveWithoutOcr", TextCatalog.Get("忽略文字识别调整"), supported: false), new InputAction("DragContent", TextCatalog.Get("拖出内容")), new InputAction("MultiSelect", TextCatalog.Get("多选")),
        new InputAction("Hide", TextCatalog.Get("隐藏钉图")), new InputAction("Lock", TextCatalog.Get("锁定 / 解锁")) };
    public static readonly InputAction[] GlobalMouse = {
        new InputAction("Pin", TextCatalog.Get("截图并钉图")), new InputAction("Copy", TextCatalog.Get("截图并复制")), new InputAction("Ocr", TextCatalog.Get("截图并复制文字")), new InputAction("QuickSave", TextCatalog.Get("截图并快速保存")),
        new InputAction("Translate", TextCatalog.Get("截图并翻译")), new InputAction("Edit", TextCatalog.Get("截图并编辑")), new InputAction("Save", TextCatalog.Get("截图并保存")), new InputAction("LongScreenshot", TextCatalog.Get("长截图")),
        new InputAction("Gif", TextCatalog.Get("GIF 录制")), new InputAction("Formula", TextCatalog.Get("公式识别")) };
    public static List<MouseBinding> DefaultPin(AppSettings settings)
    {
        var result = PinMouse.Select(a => new MouseBinding { Action = a.Name }).ToList();
        void Set(string action, string gesture, string modifiers = "") { var b = result.Single(x => x.Action == action); b.Gesture = gesture; b.Modifiers = modifiers; }
        string Legacy(string action) => action == "Zoom" ? "Scale" : action == "Original" ? "Reset" : action;
        void Apply(string action, string gesture, string modifiers = "") { if (action != "None" && result.Any(b => b.Action == Legacy(action))) Set(Legacy(action), gesture, modifiers); }
        Apply(settings.PinWheelAction, "Wheel"); Apply(settings.PinControlWheelAction, "Wheel", "Ctrl"); Apply(settings.PinMiddleAction, "MiddleClick"); Apply(settings.PinDoubleClickAction, "LeftDoubleClick");
        Set("FineScale", "Wheel", "Alt"); Set("Menu", "RightClick"); Set("Move", "LeftDrag"); Set("CopyText", "RightClick", "Shift"); Set("DragContent", "LeftDrag", "Ctrl"); Set("MultiSelect", "LeftClick", "Ctrl");
        return result;
    }
    public static List<MouseBinding> DefaultGlobal() => GlobalMouse.Select(a => new MouseBinding { Action = a.Name }).ToList();
}

public static class InputBindingRules
{
    private static readonly HashSet<string> MainKeys = new HashSet<string>(("Cancel,Back,Tab,LineFeed,Clear,Enter,Pause,CapsLock,KanaMode,JunjaMode,FinalMode,HanjaMode,Escape,ImeConvert,ImeNonConvert,ImeAccept,ImeModeChange,Space,PageUp,PageDown,End,Home,Left,Up,Right,Down,Select,Print,Execute,PrintScreen,Insert,Delete,Help,Apps,Sleep,Multiply,Add,Separator,Subtract,Decimal,Divide,NumLock,Scroll,BrowserBack,BrowserForward,BrowserRefresh,BrowserStop,BrowserSearch,BrowserFavorites,BrowserHome,VolumeMute,VolumeDown,VolumeUp,MediaNextTrack,MediaPreviousTrack,MediaStop,MediaPlayPause,LaunchMail,SelectMedia,LaunchApplication1,LaunchApplication2,Oem1,OemPlus,OemComma,OemMinus,OemPeriod,Oem2,Oem3,AbntC1,AbntC2,Oem4,Oem5,Oem6,Oem7,Oem8,Oem102,OemAttn,OemFinish,OemCopy,OemAuto,OemEnlw,OemBackTab,Attn,CrSel,ExSel,EraseEof,Play,Zoom,NoName,Pa1,OemClear").Split(','), StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> KeyAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Return"] = "Enter", ["Prior"] = "PageUp", ["Next"] = "PageDown", ["Capital"] = "CapsLock", ["HangulMode"] = "KanaMode", ["KanjiMode"] = "HanjaMode", ["Snapshot"] = "PrintScreen",
        ["OemSemicolon"] = "Oem1", ["OemQuestion"] = "Oem2", ["OemTilde"] = "Oem3", ["OemOpenBrackets"] = "Oem4", ["OemPipe"] = "Oem5", ["OemCloseBrackets"] = "Oem6", ["OemQuotes"] = "Oem7", ["OemBackslash"] = "Oem102",
        ["DbeAlphanumeric"] = "OemAttn", ["DbeKatakana"] = "OemFinish", ["DbeHiragana"] = "OemCopy", ["DbeSbcsChar"] = "OemAuto", ["DbeDbcsChar"] = "OemEnlw", ["DbeRoman"] = "OemBackTab", ["DbeNoRoman"] = "Attn", ["DbeEnterWordRegisterMode"] = "CrSel", ["DbeEnterImeConfigureMode"] = "ExSel", ["DbeFlushString"] = "EraseEof", ["DbeCodeInput"] = "Play", ["DbeNoCodeInput"] = "Zoom", ["DbeDetermineString"] = "NoName", ["DbeEnterDialogConversionMode"] = "Pa1"
    };
    public static readonly string[] Modifiers = { "", "Ctrl", "Alt", "Shift", "Win", "Ctrl+Alt", "Ctrl+Shift", "Alt+Shift", "Ctrl+Alt+Shift", "Win+Ctrl", "Win+Alt", "Win+Shift" };
    public static readonly string[] Gestures = { "None", "Wheel", "LeftClick", "LeftDoubleClick", "LeftDrag", "RightClick", "RightDoubleClick", "RightDrag", "MiddleClick", "MiddleDoubleClick", "MiddleDrag", "X1Click", "X2Click" };
    public static string[] ProcessNames(string value)
    {
        if (value == null || value.Length > 4000) throw new ArgumentException(TextCatalog.Get("忽略进程名单过长。"));
        var names = value.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
        if (names.Length > 100 || names.Any(n => !Regex.IsMatch(n, "^[^\\\\/:*?\"<>|\x00-\x1f]{1,128}(?:\\.exe)?$"))) throw new ArgumentException(TextCatalog.Get("忽略进程只填写进程名，例如 example.exe，不填路径。"));
        return names.Select(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n.Substring(0, n.Length - 4) : n).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static bool Ignored(string list, string processName) => ProcessNames(list).Contains(processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName.Substring(0, processName.Length - 4) : processName, StringComparer.OrdinalIgnoreCase);
    public static void ValidateMouse(List<MouseBinding>? bindings, bool global)
    {
        if (bindings == null) return;
        var catalog = global ? InputBindingCatalog.GlobalMouse : InputBindingCatalog.PinMouse;
        if (bindings.Count > catalog.Length || bindings.Any(b => b == null || !catalog.Any(a => a.Name == b.Action) || !Modifiers.Contains(b.Modifiers) || !Gestures.Contains(b.Gesture)) || bindings.Select(b => b.Action).Distinct().Count() != bindings.Count) throw new ArgumentException(TextCatalog.Get("鼠标动作配置无效。"));
        var enabled = bindings.Where(b => b.Gesture != "None").ToArray();
        if (enabled.Any(b => !catalog.Single(a => a.Name == b.Action).Supported || global && (!b.Gesture.EndsWith("Drag", StringComparison.Ordinal) || b.Modifiers.Length == 0))) throw new ArgumentException(TextCatalog.Get("全局鼠标需要修饰键与拖动组合；未开放的动作不能启用。"));
        if (!global && enabled.Any(b => new[] { "Scale", "FineScale", "Opacity" }.Contains(b.Action) && b.Gesture != "Wheel" || new[] { "Move", "DragContent" }.Contains(b.Action) && !b.Gesture.EndsWith("Drag", StringComparison.Ordinal))) throw new ArgumentException(TextCatalog.Get("缩放与透明度使用滚轮，移动和拖出内容使用拖动组合。"));
        if (enabled.Select(b => NormalizeModifiers(b.Modifiers) + ":" + b.Gesture).Distinct().Count() != enabled.Length) throw new ArgumentException(TextCatalog.Get("鼠标组合重复，请为每个动作设置不同组合。"));
    }
    public static string NormalizeModifiers(string value) => string.Join("+", value.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries).OrderBy(s => s, StringComparer.Ordinal));
    public static string? MouseAction(IEnumerable<MouseBinding> bindings, string modifiers, string gesture) => bindings.FirstOrDefault(b => b.Gesture == gesture && NormalizeModifiers(b.Modifiers) == NormalizeModifiers(modifiers))?.Action;
    public static void ValidateShortcuts(List<ShortcutBinding>? bindings, bool mergeNumberKeys = true)
    {
        if (bindings == null) return;
        if (bindings.Count > 160 || bindings.Any(b => b == null || !InputBindingCatalog.Contexts.Contains(b.Context) || !InputBindingCatalog.Actions(b.Context).Any(a => a.Name == b.Action && a.Supported) || b.Hotkey == null || b.Hotkey.Length > 80) || bindings.Select(b => b.Context + ":" + b.Action).Distinct().Count() != bindings.Count) throw new ArgumentException(TextCatalog.Get("内置快捷键配置无效。"));
        foreach (var context in InputBindingCatalog.Contexts)
        {
            var effective = InputBindingCatalog.Actions(context).Where(a => a.Supported).Select(a => bindings.FirstOrDefault(b => b.Context == context && b.Action == a.Name)?.Hotkey ?? a.DefaultHotkey).Where(k => k.Length > 0).Select(k => CanonicalShortcut(k, mergeNumberKeys)).ToArray();
            if (effective.Distinct(StringComparer.OrdinalIgnoreCase).Count() != effective.Length) throw new ArgumentException(TextCatalog.Get("同一场景的内置快捷键不能重复。"));
        }
    }
    public static string CanonicalShortcut(string value, bool mergeNumberKeys = false)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80) throw new ArgumentException(TextCatalog.Get("快捷键格式无效。"));
        var parts = value.Split('+').Select(p => p.Trim()).ToArray(); if (parts.Any(string.IsNullOrEmpty)) throw new ArgumentException(TextCatalog.Get("快捷键格式无效。"));
        var modifiers = parts.Take(parts.Length - 1).ToArray(); if (modifiers.Any(p => !new[] { "Ctrl", "Alt", "Shift", "Win" }.Contains(p, StringComparer.OrdinalIgnoreCase)) || modifiers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != modifiers.Length) throw new ArgumentException(TextCatalog.Get("快捷键修饰键无效或重复。"));
        var main = parts.Last(); if (KeyAliases.TryGetValue(main, out var alias)) main = alias; main = main.ToUpperInvariant();
        if (!MainKeys.Contains(main) && !Regex.IsMatch(main, "^(?:[A-Z]|D[0-9]|NUMPAD[0-9]|F(?:[1-9]|1[0-9]|2[0-4]))$") && !(parts.Length == 1 && new[] { "CTRL", "ALT", "SHIFT", "WIN" }.Contains(main))) throw new ArgumentException(TextCatalog.Get("快捷键主键无效。"));
        if (mergeNumberKeys) { if (Regex.IsMatch(main, "^NUMPAD[0-9]$")) main = "D" + main.Substring(6); else if (main == "ADD") main = "OEMPLUS"; else if (main == "SUBTRACT") main = "OEMMINUS"; }
        return string.Join("+", modifiers.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Concat(new[] { main })).ToUpperInvariant();
    }
    public static string Shortcut(AppSettings settings, string context, string action) => settings.BuiltinShortcuts?.FirstOrDefault(b => b.Context == context && b.Action == action)?.Hotkey ?? InputBindingCatalog.Actions(context).Single(a => a.Name == action).DefaultHotkey;
}
