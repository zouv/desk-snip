using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.App.Imaging;
using DeskSnip.App.Windows;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public class ImagingTests
{
    internal static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    [Fact] public void ImportedDpiDoesNotChangeOutputPixels() => Sta(() =>
    {
        var pixels = new byte[120 * 80 * 4]; for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var source = BitmapSource.Create(120, 80, 300, 300, PixelFormats.Bgra32, null, pixels, 120 * 4);
        var normalized = ImageFiles.Normalize(source); var output = AnnotationRenderer.Flatten(normalized, Array.Empty<Annotation>());
        Assert.Equal(120, output.PixelWidth); Assert.Equal(80, output.PixelHeight); Assert.Equal(96, output.DpiX);
    });
    [Fact] public void FlattenDrawsAnnotationIntoExport() => Sta(() =>
    {
        var pixels = new byte[40 * 40 * 4]; for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var source = BitmapSource.Create(40, 40, 96, 96, PixelFormats.Bgra32, null, pixels, 160);
        var shape = new Annotation(AnnotationKind.Line, new[] { new PixelPoint(5, 20), new PixelPoint(35, 20) }, 0xffff0000, 4);
        var output = AnnotationRenderer.Flatten(source, new[] { shape }); var pixel = new byte[4]; output.CopyPixels(new System.Windows.Int32Rect(20, 20, 1, 1), pixel, 4, 0);
        Assert.True(pixel[2] > 240); Assert.Equal(0, pixel[1]);
    });
    [Fact] public void PhysicalCropWithNegativeOriginHasExactPixels() => Sta(() =>
    {
        var source = BitmapSource.Create(200, 100, 96, 96, PixelFormats.Bgra32, null, new byte[200 * 100 * 4], 800);
        var crop = ScreenCapture.Crop(source, new PixelRect(-100, -50, 200, 100), new PixelRect(-80, -10, 30, 20));
        Assert.Equal(30, crop.PixelWidth); Assert.Equal(20, crop.PixelHeight);
    });
    [Fact] public void RotationAndMirroringPreservePixelMapping() => Sta(() =>
    {
        var pixels = new byte[] { 1, 0, 0, 255, 2, 0, 0, 255, 3, 0, 0, 255, 4, 0, 0, 255, 5, 0, 0, 255, 6, 0, 0, 255 };
        var source = BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 12);
        var rotated = ImageTransforms.Apply(source, ImageTransform.RotateClockwise); var output = new byte[pixels.Length]; rotated.CopyPixels(output, 8, 0);
        Assert.Equal(2, rotated.PixelWidth); Assert.Equal(3, rotated.PixelHeight);
        Assert.Equal(new byte[] { 4, 1, 5, 2, 6, 3 }, System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(output, (_, i) => i % 4 == 0)));
        var flip = ImageTransforms.Apply(source, ImageTransform.FlipHorizontal); flip.CopyPixels(output, 12, 0);
        Assert.Equal(new byte[] { 3, 2, 1, 6, 5, 4 }, System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(output, (_, i) => i % 4 == 0)));
    });
    [Fact] public void SolidCoverRemovesPixelsInOutput() => Sta(() =>
    {
        var pixels = new byte[30 * 30 * 4]; for (var i = 0; i < pixels.Length; i++) pixels[i] = 255;
        var source = BitmapSource.Create(30, 30, 96, 96, PixelFormats.Bgra32, null, pixels, 120);
        var cover = new Annotation(AnnotationKind.Cover, new[] { new PixelPoint(5, 5), new PixelPoint(25, 25) }, 0xff000000, 3);
        var output = AnnotationRenderer.Flatten(source, new[] { cover }); var pixel = new byte[4]; output.CopyPixels(new System.Windows.Int32Rect(15, 15, 1, 1), pixel, 4, 0);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, pixel);
    });
}
