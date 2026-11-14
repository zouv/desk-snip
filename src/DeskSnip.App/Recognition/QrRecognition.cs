using System;
using System.Linq;
using System.Windows.Media.Imaging;
using DeskSnip.App.Imaging;
using ZXing;
using ZXing.Common;

namespace DeskSnip.App.Recognition;

internal static class QrRecognition
{
    internal static string[] Read(BitmapSource source)
    {
        var image = ImageFiles.Normalize(source); var bytes = new byte[checked(image.PixelWidth * image.PixelHeight * 4)]; image.CopyPixels(bytes, image.PixelWidth * 4, 0);
        var reader = new BarcodeReaderGeneric { AutoRotate = true, Options = new DecodingOptions { TryHarder = true, PossibleFormats = new[] { BarcodeFormat.QR_CODE } } };
        var result = reader.DecodeMultiple(new RGBLuminanceSource(bytes, image.PixelWidth, image.PixelHeight, RGBLuminanceSource.BitmapFormat.BGRA32));
        return result?.Select(r => r.Text).Distinct().ToArray() ?? Array.Empty<string>();
    }
}
