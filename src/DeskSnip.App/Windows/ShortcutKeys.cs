using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using DeskSnip.Core;

namespace DeskSnip.App.Windows;

internal static class ShortcutKeys
{
    private static readonly Dictionary<string, string> KeyLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Escape"] = "Esc", ["Return"] = "Enter", ["OemPlus"] = "=", ["OemMinus"] = "-", ["OemComma"] = ",", ["OemPeriod"] = ".", ["Oem1"] = ";", ["Oem2"] = "/", ["Oem3"] = "`", ["Oem4"] = "[", ["Oem5"] = "\\", ["Oem6"] = "]", ["Oem7"] = "'", ["Oem102"] = "反斜杠", ["Back"] = "Backspace",
        ["Add"] = "小键盘 +", ["Subtract"] = "小键盘 -", ["Multiply"] = "小键盘 *", ["Divide"] = "小键盘 /", ["Decimal"] = "小键盘 ."
    };
    internal static string Display(string value)
    {
        if (value.Length == 0) return ""; var split = value.LastIndexOf('+'); var main = value.Substring(split + 1);
        var label = KeyLabels.TryGetValue(main, out var found) ? L.Get(found) : main.Length == 2 && main[0] == 'D' && char.IsDigit(main[1]) ? main.Substring(1) : main.StartsWith("NumPad", StringComparison.Ordinal) ? L.Join(L.Get("小键盘 "),main.Substring(6)) : main;
        return L.Join(value.Substring(0, split + 1), label);
    }
    internal static string FromDisplay(string value)
    {
        if (value.Length == 0) return "";
        var prefixes = new[] { "小键盘 ", "Numpad " };
        var prefix = prefixes.FirstOrDefault(p => value.IndexOf(p, StringComparison.Ordinal) >= 0);
        var keypad = prefix == null ? -1 : value.IndexOf(prefix, StringComparison.Ordinal);
        var split = keypad >= 0 ? keypad - 1 : value.LastIndexOf('+'); var main = value.Substring(split + 1);
        var known = KeyLabels.FirstOrDefault(p => p.Value == main || TextCatalog.Get(p.Value, CultureInfo.GetCultureInfo("en-US")) == main);
        var stored = main == "Enter" ? "Enter" : known.Key != null ? known.Key : main.Length == 1 && char.IsDigit(main[0]) ? "D" + main : prefix != null && main.StartsWith(prefix, StringComparison.Ordinal) ? "NumPad" + main.Substring(prefix.Length) : main;
        return value.Substring(0, split + 1) + stored;
    }
    internal static bool IsModifier(Key key) => key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin;
    internal static string Format(Key key, ModifierKeys modifiers)
    {
        if (IsModifier(key)) return key == Key.LeftAlt || key == Key.RightAlt ? "Alt" : key == Key.LeftCtrl || key == Key.RightCtrl ? "Ctrl" : key == Key.LeftShift || key == Key.RightShift ? "Shift" : "Win";
        var parts = new List<string>(); if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl"); if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt"); if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift"); if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win"); parts.Add(key.ToString()); return string.Join("+", parts);
    }
    internal static string Modifiers(ModifierKeys modifiers) => string.Join("+", new[] { (ModifierKeys.Control, "Ctrl"), (ModifierKeys.Alt, "Alt"), (ModifierKeys.Shift, "Shift"), (ModifierKeys.Windows, "Win") }.Where(p => (modifiers & p.Item1) != 0).Select(p => p.Item2));
    internal static void Validate(string value)
    {
        if (value == "Ctrl" || value == "Alt" || value == "Shift" || value == "Win") return;
        InputBindingRules.CanonicalShortcut(value); var last = value.Split('+').Last(); if (!Enum.TryParse(last, true, out Key key) || key == Key.None || IsModifier(key) || KeyInterop.VirtualKeyFromKey(key) == 0) throw new ArgumentException(L.Get("快捷键主键无效。"));
    }
    private static string Canonical(string value, bool merge)
        => InputBindingRules.CanonicalShortcut(value, merge);
    internal static string? Match(AppSettings settings, string context, Key key, ModifierKeys modifiers)
    {
        if (key == Key.None || key == Key.ImeProcessed || key == Key.System || key == Key.DeadCharProcessed) return null;
        var actual = Canonical(Format(key, modifiers), settings.BuiltinMergeNumberKeys);
        foreach (var action in InputBindingCatalog.Actions(context).Where(a => a.Supported))
        {
            var value = InputBindingRules.Shortcut(settings, context, action.Name); if (value.Length > 0 && Canonical(value, settings.BuiltinMergeNumberKeys) == actual) return action.Name;
        }
        return null;
    }
    internal static string? Match(AppSettings settings, string context, KeyEventArgs e) => Match(settings, context, e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers);
}
