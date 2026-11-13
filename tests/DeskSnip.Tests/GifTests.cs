using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public class GifTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(23, 15)]
    [InlineData(320, 200)]
    public void StreamDecodesAcrossCodeWidthAndDictionaryResets(int width, int height)
    {
        var pixels = new byte[width * height * 4]; new Random(17).NextBytes(pixels);
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        using (var stream = new MemoryStream())
        {
            using (var gif = new GifWriter(stream, width, height, true)) { gif.WriteFrame(pixels, TimeSpan.FromMilliseconds(120)); gif.WriteFrame(pixels, TimeSpan.FromMilliseconds(230)); }
            stream.Position = 0;
            var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Assert.Equal(2, decoder.Frames.Count);
            Assert.Equal((ushort)12, ((BitmapMetadata)decoder.Frames[0].Metadata).GetQuery("/grctlext/Delay"));
            Assert.Equal((ushort)23, ((BitmapMetadata)decoder.Frames[1].Metadata).GetQuery("/grctlext/Delay"));
            var decoded = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            var actual = new byte[pixels.Length]; decoded.CopyPixels(actual, width * 4, 0);
            for (var i = 0; i < pixels.Length; i += 4)
            {
                Assert.Equal((pixels[i] >> 6) * 255 / 3, actual[i]);
                Assert.Equal((pixels[i + 1] >> 5) * 255 / 7, actual[i + 1]);
                Assert.Equal((pixels[i + 2] >> 5) * 255 / 7, actual[i + 2]);
                Assert.Equal(255, actual[i + 3]);
            }
        }
    }
    [Fact] public void LongStillFrameSplitsWithoutLosingDuration()
    {
        using (var stream = new MemoryStream())
        {
            using (var gif = new GifWriter(stream, 1, 1, true)) gif.WriteFrame(new byte[] { 0, 0, 255, 255 }, TimeSpan.FromSeconds(700));
            stream.Position = 0; var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Assert.Equal(2, decoder.Frames.Count); var duration = 0;
            foreach (var frame in decoder.Frames) duration += (ushort)((BitmapMetadata)frame.Metadata).GetQuery("/grctlext/Delay");
            Assert.Equal(70000, duration);
        }
    }
}
