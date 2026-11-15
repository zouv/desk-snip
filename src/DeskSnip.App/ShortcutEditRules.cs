using System.Windows.Input;
using DeskSnip.App.Windows;

namespace DeskSnip.App;

internal enum ShortcutEditAction { Ignore, Cancel, Clear, Set }

/// <summary>快捷键输入框的按键判定；Esc / Backspace 取消，Delete 清空，其余组合直接完成设置。</summary>
internal static class ShortcutEditRules
{
    internal static ShortcutEditAction Resolve(Key key, ModifierKeys modifiers, bool global, out string value)
    {
        value = "";
        if (key == Key.Escape || key == Key.Back) return ShortcutEditAction.Cancel;
        if (key == Key.Delete) return ShortcutEditAction.Clear;
        if (key == Key.None || key == Key.ImeProcessed) return ShortcutEditAction.Ignore;
        if (global && ShortcutKeys.IsModifier(key)) return ShortcutEditAction.Ignore;
        value = ShortcutKeys.Display(ShortcutKeys.Format(key, modifiers));
        return ShortcutEditAction.Set;
    }
}
