using System.Globalization;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public class ColorTests
{
    [Theory]
    [InlineData(PixelColorFormat.HashHex, "#0485F7")]
    [InlineData(PixelColorFormat.Hex, "0485F7")]
    [InlineData(PixelColorFormat.Rgb, "rgb(4, 133, 247)")]
    [InlineData(PixelColorFormat.Bgr, "bgr(247, 133, 4)")]
    [InlineData(PixelColorFormat.Hsv, "hsv(208, 98%, 97%)")]
    [InlineData(PixelColorFormat.Hsl, "hsl(208, 97%, 49%)")]
    public void FormatsMatchReferenceColor(PixelColorFormat format, string expected)
        => Assert.Equal(expected, new PixelColor(4, 133, 247).Format(format));

    [Theory]
    [InlineData(0, 0, 0, "hsv(0, 0%, 0%)", "hsl(0, 0%, 0%)")]
    [InlineData(255, 255, 255, "hsv(0, 0%, 100%)", "hsl(0, 0%, 100%)")]
    [InlineData(128, 128, 128, "hsv(0, 0%, 50%)", "hsl(0, 0%, 50%)")]
    [InlineData(255, 0, 0, "hsv(0, 100%, 100%)", "hsl(0, 100%, 50%)")]
    [InlineData(255, 0, 255, "hsv(300, 100%, 100%)", "hsl(300, 100%, 50%)")]
    public void NeutralColorsAndHueWrapRemainDefined(byte r, byte g, byte b, string hsv, string hsl)
    {
        Assert.Equal(hsv, new PixelColor(r, g, b).Format(PixelColorFormat.Hsv));
        Assert.Equal(hsl, new PixelColor(r, g, b).Format(PixelColorFormat.Hsl));
    }

    [Fact] public void FormatsAreIndependentOfLocale()
    {
        var original = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); Assert.Equal("rgb(4, 133, 247)", new PixelColor(4, 133, 247).Format(PixelColorFormat.Rgb)); }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact] public void CoordinatesCanBeGlobalOrSelectionRelative()
    {
        var point = new PixelPoint(-98, -47); var selection = new PixelRect(-100, -50, 20, 20);
        Assert.Equal("-98, -47", PixelColor.Coordinates(point, selection, false));
        Assert.Equal("2, 3", PixelColor.Coordinates(point, selection, true));
        Assert.Equal("-98, -47", PixelColor.Coordinates(point, default, true));
    }
}
