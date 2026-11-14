using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.App.Imaging;
using DeskSnip.App.Recognition;
using DeskSnip.Core;
using Xunit;
using ZXing;
using ZXing.Common;

namespace DeskSnip.Tests;

public sealed class AdvancedImagingTests
{
    private static BitmapSource Image()
    {
        var bytes = new byte[100 * 80 * 4]; for (var i = 0; i < bytes.Length; i += 4) { bytes[i] = 40; bytes[i + 1] = 80; bytes[i + 2] = 120; bytes[i + 3] = 255; }
        var image = BitmapSource.Create(100, 80, 96, 96, PixelFormats.Bgra32, null, bytes, 400); image.Freeze(); return image;
    }
    private static byte[] Pixel(BitmapSource image, int x, int y) { var value = new byte[4]; image.CopyPixels(new Int32Rect(x, y, 1, 1), value, 4, 0); return value; }
    [Fact] public void IrregularCaptureExportsTransparentOutsideShape() => ImagingTests.Sta(() =>
    {
        var image = Image(); var ellipse = CaptureMasks.Apply(image, CaptureShape.Ellipse); Assert.Equal(0, Pixel(ellipse, 0, 0)[3]); Assert.Equal(255, Pixel(ellipse, 50, 40)[3]);
        var triangle = CaptureMasks.Apply(image, CaptureShape.Polygon, new[] { new PixelPoint(0, 0), new PixelPoint(99, 0), new PixelPoint(0, 79) }); Assert.Equal(0, Pixel(triangle, 99, 79)[3]); Assert.Equal(255, Pixel(triangle, 10, 10)[3]);
    });
    [Fact] public void FillAndEraserModifyOnlyTheirAreaAndUndoRestoresAnnotation() => ImagingTests.Sta(() =>
    {
        var image = Image(); var document = new AnnotationDocument(100, 80);
        var rectangle = new Annotation(AnnotationKind.Rectangle, new[] { new PixelPoint(10, 10), new PixelPoint(80, 60) }, 0xffff0000, 2, appearance: new AnnotationAppearance(fill: true)); document.Add(rectangle);
        var eraser = new Annotation(AnnotationKind.Eraser, new[] { new PixelPoint(20, 20), new PixelPoint(40, 40) }, 0xffffffff, 2); document.Add(eraser);
        var output = AnnotationRenderer.Flatten(image, document.Items); Assert.Equal(Pixel(image, 30, 30), Pixel(output, 30, 30)); Assert.Equal(255, Pixel(output, 50, 30)[2]);
        Assert.Equal(-1, AnnotationRenderer.HitTest(image, new[] { eraser }, new PixelPoint(90, 70))); Assert.Equal(0, AnnotationRenderer.HitTest(image, new[] { eraser }, new PixelPoint(30, 30)));
        document.Undo(); Assert.Equal(255, Pixel(AnnotationRenderer.Flatten(image, document.Items), 30, 30)[2]);
    });
    [Fact] public void SpotlightKeepsMultipleRegionsBrightAndStylesSurviveMoves() => ImagingTests.Sta(() =>
    {
        var image = Image(); var style = new AnnotationAppearance(true, AnnotationDash.Dash, .5, AnnotationArrow.Double, 3, true);
        var a = new Annotation(AnnotationKind.Spotlight, new[] { new PixelPoint(10, 10), new PixelPoint(30, 30) }, 0xffffffff, 2, appearance: style);
        var b = new Annotation(AnnotationKind.Spotlight, new[] { new PixelPoint(60, 10), new PixelPoint(80, 30) }, 0xffffffff, 2);
        var output = AnnotationRenderer.Flatten(image, new[] { a, b }); Assert.Equal(Pixel(image, 20, 20), Pixel(output, 20, 20)); Assert.Equal(Pixel(image, 70, 20), Pixel(output, 70, 20)); Assert.True(Pixel(output, 50, 60)[2] < Pixel(image, 50, 60)[2]);
        Assert.Same(style, a.WithPoints(new[] { new PixelPoint(11, 11), new PixelPoint(31, 31) }).Appearance);
    });
    [Fact] public void BlurIsBoundedAndPrivacyMatchesAcrossWordFragments()
    {
        var pixels = new byte[3 * 3 * 4]; for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255; pixels[4 * 4 + 2] = 255;
        var blurred = PixelEffects.Blur(new PixelBuffer(3, 3, pixels), 1).CopyPixels(); Assert.InRange(blurred[4 * 4 + 2], (byte)1, (byte)100); Assert.Equal(255, blurred[4 * 4 + 3]);
        var lines = new[] { new RecognizedLine(new[] { new RecognizedWord("138", new PixelRect(10, 10, 20, 20)), new RecognizedWord("00000000", new PixelRect(30, 10, 80, 20)), new RecognizedWord("demo@example.com", new PixelRect(10, 40, 160, 20)) }) };
        Assert.Equal(new[] { new PixelRect(10, 10, 100, 20), new PixelRect(10, 40, 160, 20) }, PrivacyDetector.Find(lines));
    }
    [Fact] public void QrReaderDecodesSyntheticCodeWithoutOpeningItsContent() => ImagingTests.Sta(() =>
    {
        var writer = new BarcodeWriterPixelData { Format = BarcodeFormat.QR_CODE, Options = new EncodingOptions { Width = 200, Height = 200, Margin = 4 } };
        var pixels = writer.Write("DeskSnip synthetic QR"); var image = BitmapSource.Create(pixels.Width, pixels.Height, 96, 96, PixelFormats.Bgra32, null, pixels.Pixels, pixels.Width * 4); image.Freeze();
        Assert.Equal(new[] { "DeskSnip synthetic QR" }, QrRecognition.Read(image));
    });
}
