using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace DeskSnip.App.Windows;

/// <summary>录屏选区阶段的 Esc：本进程窗口在前台时交给 WPF 正常处理（菜单/文字框先响应），其它应用在前台时拦截并取消选区。</summary>
internal sealed class RecordingEscapeHook : IDisposable
{
    private delegate IntPtr KeyboardHook(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardData { public uint VirtualKey; public uint ScanCode; public uint Flags; public uint Time; public UIntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, KeyboardHook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    private readonly KeyboardHook callback;
    private readonly Action cancelled;
    private readonly Dispatcher dispatcher;
    private readonly uint processId;
    private IntPtr hook;
    private int fired;
    internal RecordingEscapeHook(Dispatcher dispatcher, Action cancelled)
    {
        this.dispatcher = dispatcher; this.cancelled = cancelled; callback = Hook;
        processId = (uint)Process.GetCurrentProcess().Id;
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
    }
    private IntPtr Hook(int code, IntPtr message, IntPtr data)
    {
        if (code < 0 || hook == IntPtr.Zero) return CallNextHookEx(hook, code, message, data);
        var kind = message.ToInt32();
        if (kind != 0x0100 && kind != 0x0104) return CallNextHookEx(hook, code, message, data);
        if (Marshal.PtrToStructure<KeyboardData>(data).VirtualKey != 0x1B) return CallNextHookEx(hook, code, message, data);
        var foreground = Native.GetForegroundWindow();
        if (foreground != IntPtr.Zero)
        {
            Native.GetWindowThreadProcessId(foreground, out var owner);
            if (owner == processId) return CallNextHookEx(hook, code, message, data);
        }
        if (Interlocked.Exchange(ref fired, 1) == 0) { try { dispatcher.BeginInvoke(cancelled); } catch (InvalidOperationException) { } }
        return new IntPtr(1);
    }
    public void Dispose()
    {
        var previous = hook; hook = IntPtr.Zero;
        if (previous != IntPtr.Zero) UnhookWindowsHookEx(previous);
    }
}
