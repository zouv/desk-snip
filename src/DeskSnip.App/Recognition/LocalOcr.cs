using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using DeskSnip.App.Imaging;
using DeskSnip.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace DeskSnip.App.Recognition;

internal static class LocalOcr
{
    internal static IReadOnlyList<string> Languages => OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToArray();
    internal static async Task<IReadOnlyList<RecognizedLine>> RecognizeAsync(BitmapSource source, string language, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var engine = string.IsNullOrEmpty(language) ? OcrEngine.TryCreateFromUserProfileLanguages() : OcrEngine.TryCreateFromLanguage(new Language(language));
        if (engine == null) throw new InvalidOperationException(L.Get("没有可用的本地识别语言。请在 Windows 设置的“语言和区域”中安装相应语言及基本输入功能后重试。"));
        var image = ImageFiles.Normalize(source); var factor = Math.Min(1, OcrEngine.MaxImageDimension / (double)Math.Max(image.PixelWidth, image.PixelHeight));
        if (factor < 1) { var scaled = new TransformedBitmap(image, new System.Windows.Media.ScaleTransform(factor, factor)); scaled.Freeze(); image = ImageFiles.Normalize(scaled); }
        var pixels = new byte[checked(image.PixelWidth * image.PixelHeight * 4)]; image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        using (var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, image.PixelWidth, image.PixelHeight, BitmapAlphaMode.Ignore))
        {
            bitmap.CopyFromBuffer(pixels.AsBuffer());
            var result = await engine.RecognizeAsync(bitmap).AsTask(cancellation);
            return result.Lines.Select(line => new RecognizedLine(line.Words.Select(word => new RecognizedWord(word.Text,
                new PixelRect((int)Math.Floor(word.BoundingRect.X / factor), (int)Math.Floor(word.BoundingRect.Y / factor),
                    Math.Max(1, (int)Math.Ceiling(word.BoundingRect.Width / factor)), Math.Max(1, (int)Math.Ceiling(word.BoundingRect.Height / factor))))))).ToArray();
        }
    }
}
