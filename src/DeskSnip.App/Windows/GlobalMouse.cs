using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DeskSnip.Core;

namespace DeskSnip.App.Windows;

internal sealed class GlobalMouse : IDisposable
{
    private readonly AppSettings settings;
    private readonly MouseGestureSession gesture;
    private readonly Dispatcher dispatcher;
    private readonly Action started;
    private readonly Action<PixelRect> moved;
    private readonly Action<string, PixelRect> finished;
    private readonly Action cancelled;
    private readonly Func<bool> canStart;
    private readonly MouseHook callback;
    private IntPtr hook;
    private string? swallowedButton;
    private PixelRect lastRegion;
    private bool moveQueued;
    private delegate IntPtr MouseHook(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public int X; public int Y; public uint Mouse; public uint Flags; public uint Time; public UIntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, MouseHook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    internal GlobalMouse(AppSettings settings, Dispatcher dispatcher, Action started, Action<PixelRect> moved, Action<string, PixelRect> finished, Action cancelled, Func<bool> canStart)
    {
        this.settings = settings.Clone(); this.dispatcher = dispatcher; this.started = started; this.moved = moved; this.finished = finished; this.cancelled = cancelled; this.canStart = canStart;
        gesture = new MouseGestureSession(settings.GlobalMouseBindings ?? InputBindingCatalog.DefaultGlobal()); callback = Hook;
        hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0); if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), L.Get("无法启用全局鼠标动作。"));
    }
    private static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    private static string Modifiers()
    {
        var modifiers = new System.Collections.Generic.List<string>(); if (Down(0x11)) modifiers.Add("Ctrl"); if (Down(0x12)) modifiers.Add("Alt"); if (Down(0x10)) modifiers.Add("Shift"); if (Down(0x5b) || Down(0x5c)) modifiers.Add("Win"); return string.Join("+", modifiers);
    }
    private IntPtr Hook(int code, IntPtr message, IntPtr data)
    {
        if (code < 0 || hook == IntPtr.Zero) return CallNextHookEx(hook, code, message, data);
        var input = Marshal.PtrToStructure<MouseData>(data); if ((input.Flags & 1) != 0) return CallNextHookEx(hook, code, message, data);
        var kind = message.ToInt32(); var button = kind == 0x201 || kind == 0x202 ? "Left" : kind == 0x204 || kind == 0x205 ? "Right" : kind == 0x207 || kind == 0x208 ? "Middle" : null;
        var down = kind == 0x201 || kind == 0x204 || kind == 0x207; var up = kind == 0x202 || kind == 0x205 || kind == 0x208;
        try
        {
            var point = new PixelPoint(input.X, input.Y);
            if (swallowedButton != null)
            {
                if (gesture.Active && Down(0x1b)) { gesture.Cancel(); dispatcher.BeginInvoke(cancelled); }
                if (kind == 0x200 && gesture.Active)
                {
                    gesture.Move(point); lastRegion = gesture.Region;
                    if (!moveQueued) { moveQueued = true; dispatcher.BeginInvoke(new Action(() => { moveQueued = false; if (gesture.Active) moved(lastRegion); })); }
                }
                if (up && button == swallowedButton)
                {
                    var result = gesture.Finish(button, point); swallowedButton = null;
                    if (result.HasValue) dispatcher.BeginInvoke(new Action(() => finished(result.Value.Action, result.Value.Region))); else dispatcher.BeginInvoke(cancelled);
                }
                return new IntPtr(1);
            }
            if (down && button != null && canStart() && gesture.Begin(button, Modifiers(), point, ForegroundInput.Allowed(settings.GlobalMouseIgnoredProcesses, false)))
            { swallowedButton = button; dispatcher.BeginInvoke(started); return new IntPtr(1); }
        }
        catch (Exception) { if (swallowedButton != null) { gesture.Cancel(); dispatcher.BeginInvoke(cancelled); return new IntPtr(1); } }
        return CallNextHookEx(hook, code, message, data);
    }
    public void Dispose()
    {
        var previous = hook; hook = IntPtr.Zero; if (previous != IntPtr.Zero) UnhookWindowsHookEx(previous); gesture.Cancel(); swallowedButton = null;
    }
}
