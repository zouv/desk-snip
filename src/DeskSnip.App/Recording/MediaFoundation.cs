using System;
using System.Runtime.InteropServices;

namespace DeskSnip.App.Recording;

/// <summary>仅封装使用到的 COM 槽；顺序依据 Windows SDK mfobjects.h / mfreadwrite.h。</summary>
internal static class Mf
{
    [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFShutdown();
    [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFCreateMediaType(out IntPtr mediaType);
    [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFCreateAttributes(out IntPtr attributes, uint initialSize);
    [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFCreateSample(out IntPtr sample);
    [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFCreateMemoryBuffer(int size, out IntPtr buffer);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] internal static extern int MFCreateSinkWriterFromURL(string url, IntPtr stream, IntPtr attributes, out IntPtr sink);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] internal static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IntPtr reader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int Simple(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetUint(IntPtr self, ref Guid key, uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetUlong(IntPtr self, ref Guid key, ulong value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetGuid(IntPtr self, ref Guid key, ref Guid value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetLong(IntPtr self, long value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetLength(IntPtr self, int length);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetPointer(IntPtr self, IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int AddStream(IntPtr self, IntPtr type, out uint index);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetInput(IntPtr self, uint index, IntPtr type, IntPtr attributes);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int WriteSample(IntPtr self, uint index, IntPtr sample);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int LockBuffer(IntPtr self, out IntPtr data, out int capacity, out int length);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int GetUint(IntPtr self, ref Guid key, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int GetUlong(IntPtr self, ref Guid key, out ulong value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int GetLong(IntPtr self, out long value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int GetPointer(IntPtr self, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int GetMediaType(IntPtr self, uint index, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetType(IntPtr self, uint index, IntPtr reserved, IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SelectStream(IntPtr self, uint index, int selected);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int ReadSample(IntPtr self, uint index, uint control, out uint actual, out uint flags, out long time, out IntPtr sample);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int Lock2D(IntPtr self, out IntPtr scanline0, out int pitch);
    internal static T Method<T>(IntPtr pointer, int slot) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(pointer), slot * IntPtr.Size));
    internal static void Check(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }
    internal static void Release(ref IntPtr pointer) { if (pointer != IntPtr.Zero) Marshal.Release(pointer); pointer = IntPtr.Zero; }
    internal static void Uint(IntPtr attributes, string key, uint value)
    { var guid = new Guid(key); Check(Method<SetUint>(attributes, 21)(attributes, ref guid, value)); }
    internal static void Ulong(IntPtr attributes, string key, ulong value)
    { var guid = new Guid(key); Check(Method<SetUlong>(attributes, 22)(attributes, ref guid, value)); }
    internal static void GuidValue(IntPtr attributes, string key, string value)
    { var guid = new Guid(key); var data = new Guid(value); Check(Method<SetGuid>(attributes, 24)(attributes, ref guid, ref data)); }
}
