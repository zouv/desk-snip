using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.App.Recognition;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public sealed class LocalOcrTests
{
    [Fact] public void WindowsEngineRecognizesSyntheticTextOrReportsMissingLanguage() => ImagingTests.Sta(() =>
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 900, 120));
            drawing.DrawText(new FormattedText("DESKSNIP 12345", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Arial"), 56, Brushes.Black, 1), new Point(25, 24));
        }
        var image = new RenderTargetBitmap(900, 120, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze();
        var language = LocalOcr.Languages.FirstOrDefault(l => l.StartsWith("en", StringComparison.OrdinalIgnoreCase)) ?? LocalOcr.Languages.FirstOrDefault();
        if (language == null) { Assert.Throws<InvalidOperationException>(() => LocalOcr.RecognizeAsync(image, "", CancellationToken.None).GetAwaiter().GetResult()); return; }
        var lines = LocalOcr.RecognizeAsync(image, language, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Contains("12345", RecognitionText.Format(lines)); Assert.All(lines.SelectMany(l => l.Words), word => Assert.False(word.Bounds.IsEmpty));
    });
}
