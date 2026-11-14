using System;
using System.IO;
using System.Globalization;
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
            if ((long)frame.PixelWidth * frame.PixelHeight > 100_000_000) throw new ArgumentException(L.Get("图片过大，请使用小于一亿像素的图片。"));
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
    internal static void Save(BitmapSource source, string path, int quality = 95, bool includeExif = false)
    {
        if (quality < 0 || quality > 100) throw new ArgumentOutOfRangeException(nameof(quality));
        BitmapEncoder encoder;
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".jpg": case ".jpeg": encoder = new JpegBitmapEncoder { QualityLevel = Math.Max(1, quality) }; break;
            case ".bmp": encoder = new BmpBitmapEncoder(); break;
            case ".png": encoder = new PngBitmapEncoder(); break;
            case ".tif": case ".tiff": encoder = new TiffBitmapEncoder { Compression = TiffCompressOption.Zip }; break;
            default: throw new ArgumentException(L.Get("请选择 PNG、JPEG、BMP 或 TIFF 文件。"));
        }
        BitmapMetadata? metadata = null;
        if (includeExif && (encoder is JpegBitmapEncoder || encoder is TiffBitmapEncoder))
        {
            metadata = new BitmapMetadata(encoder is JpegBitmapEncoder ? "jpg" : "tiff");
            var prefix = encoder is JpegBitmapEncoder ? "/app1/ifd/" : "/ifd/";
            metadata.SetQuery(prefix + "{ushort=305}", "DeskSnip");
            metadata.SetQuery(prefix + "{ushort=306}", DateTime.Now.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture));
        }
        encoder.Frames.Add(BitmapFrame.Create(source, null, metadata, null));
        AtomicFile.Write(path, encoder.Save);
    }
}
