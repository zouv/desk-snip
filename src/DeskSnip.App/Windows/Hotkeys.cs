using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;
using DeskSnip.Core;

namespace DeskSnip.App.Windows;

internal sealed class Hotkeys : IDisposable
{
    private readonly HwndSource source;
    private readonly Dictionary<int, Action> actions = new Dictionary<int, Action>();
    private readonly IntPtr handle;
    private AppSettings preferences = new AppSettings();
    private readonly HashSet<int> alwaysEnabled = new HashSet<int>();
    internal Hotkeys(IntPtr handle)
    {
        this.handle = handle;
        source = HwndSource.FromHwnd(handle);
        source.AddHook(Hook);
    }
    internal static (uint Modifiers, uint Key) Parse(string value)
    {
        var parts = value.Split('+').Select(p => p.Trim()).ToArray();
        if (parts.Length < 2 || parts.Any(string.IsNullOrEmpty)) throw new ArgumentException(L.Get("快捷键需要修饰键，例如 Ctrl+Alt+A。"));
        uint modifiers = 0;
        foreach (var part in parts.Take(parts.Length - 1))
        {
            var bit = part.ToLowerInvariant() switch { "ctrl" => 2u, "alt" => 1u, "shift" => 4u, "win" => 8u, _ => 0u };
            if (bit == 0 || (modifiers & bit) != 0) throw new ArgumentException(L.Get("快捷键的修饰键无效或重复。"));
            modifiers |= bit;
        }
        if (!Enum.TryParse<Key>(parts[parts.Length - 1], true, out var key) || key == Key.None || key >= Key.LeftShift && key <= Key.RightAlt)
            throw new ArgumentException(L.Get("快捷键的主键无效。"));
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0) throw new ArgumentException(L.Get("快捷键的主键无效。"));
        return (modifiers | 0x4000, (uint)virtualKey);
    }
    internal string Apply(AppSettings settings, Action capture, Action pin, Action record, IReadOnlyDictionary<string, Action>? extra = null)
    {
        var baseBindings = new[] { (settings.CaptureHotkey, capture), (settings.PinHotkey, pin), (settings.RecordHotkey, record) }.Where(b => !string.IsNullOrWhiteSpace(b.Item1)).ToArray();
        var bindings = settings.ActionBindings.Where(b => !string.IsNullOrWhiteSpace(b.Hotkey) && extra != null && extra.ContainsKey(b.Action)).ToArray();
        var keys = baseBindings.Select(b => b.Item1).Concat(bindings.Select(b => b.Hotkey)).ToArray();
        var parsed = keys.Select(Parse).ToArray();
        if (parsed.Distinct().Count() != parsed.Length) throw new ArgumentException(L.Get("全局动作快捷键不能相同。"));
        foreach (var id in actions.Keys) Native.UnregisterHotKey(handle, id);
        actions.Clear(); alwaysEnabled.Clear(); preferences = settings.Clone();
        var handlers = baseBindings.Select(b => b.Item2).Concat(bindings.Select(b => extra![b.Action])).ToArray();
        var failed = new List<string>();
        for (var i = 0; i < keys.Length; i++)
        {
            if (Native.RegisterHotKey(handle, i + 1, parsed[i].Modifiers, parsed[i].Key)) { actions[i + 1] = handlers[i]; if (i >= baseBindings.Length && bindings[i - baseBindings.Length].Action == "ToggleHotkeys") alwaysEnabled.Add(i + 1); }
            else failed.Add(keys[i]);
        }
        return failed.Count == 0 ? "" : L.Join(L.Join(L.Get("快捷键被占用："),string.Join("、", failed)),L.Get("。请在设置中修改，也可使用窗口或托盘入口。"));
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == Native.WmHotkey && actions.TryGetValue(wParam.ToInt32(), out var action)) { handled = true; if (alwaysEnabled.Contains(wParam.ToInt32()) || ForegroundInput.Allowed(preferences.ShortcutIgnoredProcesses, preferences.ShortcutDisableInFullscreen)) action(); }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        Clear(); source.RemoveHook(Hook);
    }
    internal void Clear()
    {
        foreach (var id in actions.Keys) Native.UnregisterHotKey(handle, id);
        actions.Clear();
    }
}
