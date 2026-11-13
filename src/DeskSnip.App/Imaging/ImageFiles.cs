using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.Core;

namespace DeskSnip.App.Imaging;

internal static class ImageFiles
{
    internal static BitmapSource Load(string path)
    {
        using (var stream = File.OpenRead(path))
        {
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
            if ((long)frame.PixelWidth * frame.PixelHeight > 100_000_000) throw new ArgumentException("图片过大，请使用小于一亿像素的图片。");
            return Normalize(frame);
        }
    }
    internal static BitmapSource Normalize(BitmapSource source)
    {
        var converted = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var buffer = new byte[checked(converted.PixelWidth * converted.PixelHeight * 4)];
        converted.CopyPixels(buffer, converted.PixelWidth * 4, 0);
        var result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Bgra32, null, buffer, converted.PixelWidth * 4);
        result.Freeze(); return result;
    }
    internal static void WritePng(BitmapSource source, Stream stream)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source)); encoder.Save(stream);
    }
    internal static void Save(BitmapSource source, string path)
    {
        BitmapEncoder encoder;
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".jpg": case ".jpeg": encoder = new JpegBitmapEncoder { QualityLevel = 95 }; break;
            case ".bmp": encoder = new BmpBitmapEncoder(); break;
            case ".png": encoder = new PngBitmapEncoder(); break;
            default: throw new ArgumentException("请选择 PNG、JPEG 或 BMP 文件。");
        }
        encoder.Frames.Add(BitmapFrame.Create(source));
        AtomicFile.Write(path, encoder.Save);
    }
}
