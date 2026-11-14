using System;
using System.Diagnostics;
using Microsoft.Win32;
using DeskSnip.Core;

namespace DeskSnip.App.Windows;

/// <summary>仅在用户保存相关偏好时修改当前账号的应用专属入口。</summary>
internal sealed class SystemIntegrationChange : IDisposable
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ShellPath = @"Software\Classes\SystemFileAssociations\image\shell\DeskSnip";
    private readonly bool startupChanged;
    private readonly bool menuChanged;
    private readonly string? oldRun;
    private readonly string? oldLabel;
    private readonly string? oldIcon;
    private readonly string? oldCommand;
    private readonly bool menuExisted;
    private bool committed;
    internal SystemIntegrationChange(AppSettings before, AppSettings after)
    {
        startupChanged = before.StartWithWindows != after.StartWithWindows;
        menuChanged = before.ExplorerContextMenu != after.ExplorerContextMenu;
        var executable = Process.GetCurrentProcess().MainModule!.FileName;
        if (!startupChanged && !menuChanged) return;
        using (var run = Registry.CurrentUser.OpenSubKey(RunPath)) oldRun = run?.GetValue("DeskSnip") as string;
        using (var menu = Registry.CurrentUser.OpenSubKey(ShellPath))
        { menuExisted = menu != null; oldLabel = menu?.GetValue("") as string; oldIcon = menu?.GetValue("Icon") as string; using (var command = menu?.OpenSubKey("command")) oldCommand = command?.GetValue("") as string; }
        try
        {
            if (startupChanged) WriteRun(after.StartWithWindows ? "\"" + executable + "\" --background" : null);
            if (menuChanged) WriteMenu(after.ExplorerContextMenu == "Images", L.Get("用 DeskSnip 钉住图片"), "\"" + executable + "\"", "\"" + executable + "\" --pin-file \"%1\"");
        }
        catch { Dispose(); throw; }
    }
    private static void WriteRun(string? command)
    { using (var run = Registry.CurrentUser.CreateSubKey(RunPath)) { if (command == null) run.DeleteValue("DeskSnip", false); else run.SetValue("DeskSnip", command, RegistryValueKind.String); } }
    private static void WriteMenu(bool enabled, string? label, string? icon, string? command)
    {
        if (!enabled) { Registry.CurrentUser.DeleteSubKeyTree(ShellPath, false); return; }
        using (var menu = Registry.CurrentUser.CreateSubKey(ShellPath))
        {
            if (label != null) menu.SetValue("", label); else menu.DeleteValue("", false);
            if (icon != null) menu.SetValue("Icon", icon); else menu.DeleteValue("Icon", false);
            using (var action = menu.CreateSubKey("command")) { if (command != null) action.SetValue("", command); else action.DeleteValue("", false); }
        }
    }
    internal void Commit() => committed = true;
    public void Dispose()
    {
        if (committed) return; committed = true;
        if (startupChanged) WriteRun(oldRun);
        if (menuChanged) WriteMenu(menuExisted, oldLabel, oldIcon, oldCommand);
    }
}
