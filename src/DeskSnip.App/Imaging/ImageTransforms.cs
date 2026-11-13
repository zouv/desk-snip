using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskSnip.App.Imaging;

public enum ImageTransform { RotateClockwise, FlipHorizontal, FlipVertical }

public static class ImageTransforms
{
    public static BitmapSource Apply(BitmapSource source, ImageTransform transform)
    {
        if (!Enum.IsDefined(typeof(ImageTransform), transform)) throw new ArgumentOutOfRangeException(nameof(transform));
        var image = ImageFiles.Normalize(source); var sw = image.PixelWidth; var sh = image.PixelHeight;
        var width = transform == ImageTransform.RotateClockwise ? sh : sw;
        var height = transform == ImageTransform.RotateClockwise ? sw : sh;
        var input = new byte[checked(sw * sh * 4)]; image.CopyPixels(input, sw * 4, 0);
        var output = new byte[input.Length];
        for (var y = 0; y < sh; y++) for (var x = 0; x < sw; x++)
        {
            var dx = transform == ImageTransform.RotateClockwise ? sh - 1 - y : transform == ImageTransform.FlipHorizontal ? sw - 1 - x : x;
            var dy = transform == ImageTransform.RotateClockwise ? x : transform == ImageTransform.FlipVertical ? sh - 1 - y : y;
            Buffer.BlockCopy(input, (y * sw + x) * 4, output, (dy * width + dx) * 4, 4);
        }
        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, output, width * 4); result.Freeze(); return result;
    }
}
