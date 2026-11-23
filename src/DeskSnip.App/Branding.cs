using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace DeskSnip.App;

internal static class Branding
{
    private static readonly Lazy<byte[]> data = new Lazy<byte[]>(Load);
    private static readonly Lazy<BitmapSource> image = new Lazy<BitmapSource>(LoadImage);
    private static readonly Dictionary<int, Icon> nativeIcons = new Dictionary<int, Icon>();
    public static BitmapSource Image => image.Value;
    internal const string ApplicationId = "DeskSnip.Desktop";
    private static byte[] Load()
    {
        using (var stream = typeof(Branding).Assembly.GetManifestResourceStream("DeskSnip.App.BrandIcon"))
        {
            if (stream == null) throw new InvalidOperationException("The application icon resource is missing.");
            using (var memory = new MemoryStream()) { stream.CopyTo(memory); return memory.ToArray(); }
        }
    }
    private static BitmapSource LoadImage()
    {
        using (var stream = new MemoryStream(data.Value, false))
        {
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.OrderByDescending(item => item.PixelWidth).First();
            var result = new WriteableBitmap(frame);
            result.Freeze(); return result;
        }
    }
    internal static Icon CreateTrayIcon(string? tint = null)
    {
        using (var stream = new MemoryStream(data.Value, false))
        using (var icon = new Icon(stream, 32, 32))
        {
            if (tint == null) return (Icon)icon.Clone();
            using (var bitmap = icon.ToBitmap())
            {
                var accent = ColorTranslator.FromHtml(tint);
                for (var y = 0; y < bitmap.Height; y++)
                    for (var x = 0; x < bitmap.Width; x++)
                    {
                        var pixel = bitmap.GetPixel(x, y);
                        if (pixel.B > pixel.R + 35) bitmap.SetPixel(x, y, Color.FromArgb(pixel.A, accent.R, accent.G, accent.B));
                    }
                var handle = bitmap.GetHicon();
                try { using (var temporary = Icon.FromHandle(handle)) return (Icon)temporary.Clone(); }
                finally { Windows.Native.DestroyIcon(handle); }
            }
        }
    }
    internal static byte[] IconBytes => (byte[])data.Value.Clone();
    internal static void Initialize() => Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(ApplicationId));
    internal static void ApplyWindowIcon(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle; if (handle == IntPtr.Zero) return;
        var dpi = GetDpiForWindow(handle); if (dpi == 0) dpi = 96;
        IntPtr IconFor(int baseSize)
        {
            var size = Math.Max(16, (int)Math.Ceiling(baseSize * dpi / 96d));
            lock (nativeIcons)
            {
                if (!nativeIcons.TryGetValue(size, out var icon))
                {
                    using (var stream = new MemoryStream(data.Value, false))
                    using (var source = new Icon(stream, size, size)) icon = (Icon)source.Clone();
                    nativeIcons.Add(size, icon);
                }
                return icon.Handle;
            }
        }
        SendMessage(handle, 0x0080, new IntPtr(0), IconFor(16));
        SendMessage(handle, 0x0080, new IntPtr(1), IconFor(32));
    }
    internal static void DisposeNativeIcons()
    {
        lock (nativeIcons)
        {
            foreach (var icon in nativeIcons.Values) icon.Dispose();
            nativeIcons.Clear();
        }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
