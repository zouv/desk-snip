using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DeskSnip.Core;

namespace DeskSnip.App.Windows;

internal static class CaptureTargetReader
{
    private delegate bool WindowVisitor(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowVisitor callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, WindowVisitor callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Native.Rect value, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int GetCloaked(IntPtr window, int attribute, out int value, int size);
    internal static IReadOnlyList<CaptureTarget> Snapshot(PixelRect desktop)
    {
        var result = new List<CaptureTarget>(); var own = (uint)Process.GetCurrentProcess().Id;
        EnumWindows((window, _) =>
        {
            if (result.Count >= 5000) return false;
            GetWindowThreadProcessId(window, out var process);
            if (process == own || !IsWindowVisible(window) || IsIconic(window) || GetCloaked(window, 14, out var cloaked, 4) >= 0 && cloaked != 0) return true;
            if (DwmGetWindowAttribute(window, 9, out var rect, Marshal.SizeOf<Native.Rect>()) < 0 && !Native.GetWindowRect(window, out rect)) return true;
            if (rect.Right <= rect.Left || rect.Bottom <= rect.Top) return true;
            var bounds = new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top).Intersect(desktop);
            if (bounds.IsEmpty) return true; var group = window.ToInt64().ToString(); result.Add(new CaptureTarget(group, bounds, 0));
            EnumChildWindows(window, (child, __) =>
            {
                if (result.Count >= 5000) return false;
                if (!IsWindowVisible(child) || !Native.GetWindowRect(child, out var area) || area.Right <= area.Left || area.Bottom <= area.Top) return true;
                var clipped = new PixelRect(area.Left, area.Top, area.Right - area.Left, area.Bottom - area.Top).Intersect(bounds); if (!clipped.IsEmpty) result.Add(new CaptureTarget(group, clipped, 1)); return true;
            }, IntPtr.Zero); return true;
        }, IntPtr.Zero); return result;
    }
}
