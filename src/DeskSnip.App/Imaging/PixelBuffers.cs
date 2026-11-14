using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.Core;

namespace DeskSnip.App.Imaging;

internal static class PixelBuffers
{
    internal static PixelBuffer FromBitmap(BitmapSource source)
    {
        var image = ImageFiles.Normalize(source); var pixels = new byte[checked(image.PixelWidth * image.PixelHeight * 4)]; image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        return new PixelBuffer(image.PixelWidth, image.PixelHeight, pixels);
    }
    internal static BitmapSource ToBitmap(PixelBuffer pixels)
    {
        var image = BitmapSource.Create(pixels.Width, pixels.Height, 96, 96, PixelFormats.Bgra32, null, pixels.CopyPixels(), pixels.Width * 4); image.Freeze(); return image;
    }
}
