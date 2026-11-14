using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DeskSnip.Core;

namespace DeskSnip.App.Windows;

internal static class ForegroundInput
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    internal static bool Allowed(string ignoredProcesses, bool disableFullscreen)
    {
        var window = GetForegroundWindow(); if (window == IntPtr.Zero) return true;
        GetWindowThreadProcessId(window, out var id);
        try { using (var process = Process.GetProcessById((int)id)) if (InputBindingRules.Ignored(ignoredProcesses, process.ProcessName)) return false; }
        catch (ArgumentException) { return false; } catch (System.ComponentModel.Win32Exception) { return false; } catch (InvalidOperationException) { return false; }
        if (!disableFullscreen || !Native.GetWindowRect(window, out var rect)) return true;
        var monitor = System.Windows.Forms.Screen.FromHandle(window).Bounds;
        return rect.Left > monitor.Left || rect.Top > monitor.Top || rect.Right < monitor.Right || rect.Bottom < monitor.Bottom;
    }
}
