using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskSnip.App.Imaging;

public enum ImageTransform { RotateClockwise, FlipHorizontal, FlipVertical, Grayscale, Invert, Brighter, Darker }

public static class ImageTransforms
{
    public static BitmapSource Apply(BitmapSource source, ImageTransform transform)
    {
        if (!Enum.IsDefined(typeof(ImageTransform), transform)) throw new ArgumentOutOfRangeException(nameof(transform));
        var image = ImageFiles.Normalize(source); var sw = image.PixelWidth; var sh = image.PixelHeight;
        var width = transform == ImageTransform.RotateClockwise ? sh : sw;
        var height = transform == ImageTransform.RotateClockwise ? sw : sh;
        var input = new byte[checked(sw * sh * 4)]; image.CopyPixels(input, sw * 4, 0);
        if (transform >= ImageTransform.Grayscale)
        {
            for (var i = 0; i < input.Length; i += 4)
            {
                if (transform == ImageTransform.Grayscale)
                { var value = (byte)((input[i + 2] * 299 + input[i + 1] * 587 + input[i] * 114 + 500) / 1000); input[i] = input[i + 1] = input[i + 2] = value; }
                else for (var c = 0; c < 3; c++) input[i + c] = (byte)(transform == ImageTransform.Invert ? 255 - input[i + c] : Math.Max(0, Math.Min(255, input[i + c] + (transform == ImageTransform.Brighter ? 16 : -16))));
            }
            var adjusted = BitmapSource.Create(sw, sh, 96, 96, PixelFormats.Bgra32, null, input, sw * 4); adjusted.Freeze(); return adjusted;
        }
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
