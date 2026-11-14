using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskSnip.App;
using Xunit;

namespace DeskSnip.Tests;

[Collection("Interface language")]
public sealed class BrandingTests
{
    [Fact] public void EmbeddedIconHasClearBlueFramesForCommonDpiSizes() => ImagingTests.Sta(() =>
    {
        using (var stream = new MemoryStream(Branding.IconBytes))
        {
            var decoder = new IconBitmapDecoder(stream, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert.Equal(new[] { 16, 20, 24, 28, 32, 40, 48, 64, 128, 256 }, decoder.Frames.Select(frame => frame.PixelWidth).OrderBy(width => width));
            foreach (var frame in decoder.Frames) { Assert.Equal(frame.PixelWidth, frame.PixelHeight); Assert.True(frame.PixelWidth >= 16); }
            Assert.True(Branding.Image.IsFrozen); Assert.Equal(256, Branding.Image.PixelWidth);
        }
        using (var icon = Branding.CreateTrayIcon()) using (var bitmap = icon.ToBitmap()) AssertBlue(bitmap);
        using (var icon = Icon.ExtractAssociatedIcon(typeof(ProductWindow).Assembly.Location)) using (var bitmap = icon!.ToBitmap()) AssertBlue(bitmap);
    });

    [Fact] public void EveryProductWindowGetsSmallAndLargeNativeIconsAcrossTaskbarToggles() => ImagingTests.Sta(() =>
    {
        var window = new ProductWindow { ShowInTaskbar = false, ShowActivated = false };
        try
        {
            var handle = new WindowInteropHelper(window).EnsureHandle(); AssertNativeIcons(handle);
            foreach (var visible in new[] { true, false, true })
            {
                window.ShowInTaskbar = visible; Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle);
                AssertNativeIcons(new WindowInteropHelper(window).Handle);
            }
        }
        finally { window.Close(); }
    });

    [Fact] public void ApplicationIdentityIsStableWithinTheProcess()
    {
        Branding.Initialize(); Assert.Equal(0, GetCurrentProcessExplicitAppUserModelID(out var pointer));
        try { Assert.Equal("DeskSnip.Desktop", Marshal.PtrToStringUni(pointer)); }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    private static void AssertNativeIcons(IntPtr handle)
    {
        foreach (var kind in new[] { 0, 1 })
        {
            var native = SendMessage(handle, 0x007F, new IntPtr(kind), IntPtr.Zero); Assert.NotEqual(IntPtr.Zero, native);
            using (var icon = Icon.FromHandle(native)) using (var bitmap = icon.ToBitmap()) AssertBlue(bitmap);
        }
    }
    private static void AssertBlue(Bitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++) for (var x = 0; x < bitmap.Width; x++) { var pixel = bitmap.GetPixel(x, y); if (pixel.A > 0 && pixel.B > pixel.R + 35) count++; }
        Assert.True(count > bitmap.Width * bitmap.Height / 3, "The icon must remain blue and recognizable at small sizes.");
    }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("shell32.dll")] private static extern int GetCurrentProcessExplicitAppUserModelID(out IntPtr pointer);
}
