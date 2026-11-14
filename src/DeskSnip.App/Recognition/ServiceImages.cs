using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.App.Imaging;
using DeskSnip.Core;
using WpfMath.Parsers;
using WpfMath.Rendering;

namespace DeskSnip.App.Recognition;

internal static class ServiceImages
{
    internal static string Encode(BitmapSource source)
    {
        using (var stream = new MemoryStream()) { ImageFiles.WritePng(source, stream); var result = Convert.ToBase64String(stream.ToArray()); RecognitionImages.DecodePng(result); return result; }
    }
    internal static BitmapSource Decode(string encoded)
    {
        var bytes = RecognitionImages.Decode(encoded); RecognitionImages.Dimensions(bytes);
        try
        {
            using (var stream = new MemoryStream(bytes))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                return ImageFiles.Normalize(decoder.Frames[0]);
            }
        }
        catch (Exception error) when (error is IOException || error is NotSupportedException || error is ArgumentException) { throw new ArgumentException(L.Get("图片无法解码。")); }
    }
}

internal static class FormulaImages
{
    internal static string Source(string text)
    {
        text = text.Trim();
        foreach (var pair in new[] { ("$$", "$$"), ("\\[", "\\]"), ("\\(", "\\)"), ("$", "$") })
            if (text.Length >= pair.Item1.Length + pair.Item2.Length && text.StartsWith(pair.Item1, StringComparison.Ordinal) && text.EndsWith(pair.Item2, StringComparison.Ordinal)) return text.Substring(pair.Item1.Length, text.Length - pair.Item1.Length - pair.Item2.Length).Trim();
        return text;
    }
    internal static BitmapSource Render(string text)
    {
        text = Source(text); if (string.IsNullOrWhiteSpace(text) || text.Length > 4096) throw new ArgumentException(L.Get("预览需要 1–4096 字符的 LaTeX 公式。"));
        var depth = 0; foreach (var c in text) { if (c == '{' && ++depth > 32) throw new ArgumentException(L.Get("公式嵌套过深，源码仍可导出。")); if (c == '}') depth--; }
        var formula = WpfTeXFormulaParser.Instance.Parse(text); var environment = WpfTeXEnvironment.Create(foreground: Brushes.Black, background: Brushes.Transparent);
        var bounds = formula.RenderToGeometry(environment, 28).Bounds;
        if (bounds.Width > 6000 || bounds.Height > 6000 || bounds.Width * bounds.Height > 16_000_000) throw new ArgumentException(L.Get("公式预览过大，源码仍可导出。"));
        var image = formula.RenderToBitmap(environment, 28); image.Freeze(); return image;
    }
}
