using System;
using System.Globalization;

namespace DeskSnip.Core;

public enum PixelColorFormat { HashHex, Rgb, Bgr, Hex, Hsv, Hsl }

public readonly struct PixelColor
{
    public PixelColor(byte red, byte green, byte blue) { Red = red; Green = green; Blue = blue; }
    public byte Red { get; }
    public byte Green { get; }
    public byte Blue { get; }

    public string Format(PixelColorFormat format)
    {
        var hex = Red.ToString("X2", CultureInfo.InvariantCulture) + Green.ToString("X2", CultureInfo.InvariantCulture) + Blue.ToString("X2", CultureInfo.InvariantCulture);
        switch (format)
        {
            case PixelColorFormat.HashHex: return "#" + hex;
            case PixelColorFormat.Hex: return hex;
            case PixelColorFormat.Rgb: return string.Format(CultureInfo.InvariantCulture, "rgb({0}, {1}, {2})", Red, Green, Blue);
            case PixelColorFormat.Bgr: return string.Format(CultureInfo.InvariantCulture, "bgr({0}, {1}, {2})", Blue, Green, Red);
            case PixelColorFormat.Hsv:
            case PixelColorFormat.Hsl:
                var r = Red / 255d; var g = Green / 255d; var b = Blue / 255d;
                var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var difference = max - min;
                var hue = difference == 0 ? 0 : max == r ? 60 * ((g - b) / difference % 6) : max == g ? 60 * ((b - r) / difference + 2) : 60 * ((r - g) / difference + 4);
                if (hue < 0) hue += 360;
                var lightness = (max + min) / 2;
                var saturation = format == PixelColorFormat.Hsv ? (max == 0 ? 0 : difference / max) :
                    (difference == 0 ? 0 : difference / (1 - Math.Abs(2 * lightness - 1)));
                return string.Format(CultureInfo.InvariantCulture, "{0}({1}, {2}%, {3}%)", format == PixelColorFormat.Hsv ? "hsv" : "hsl",
                    Round(hue) % 360, Round(saturation * 100), Round((format == PixelColorFormat.Hsv ? max : lightness) * 100));
            default: throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    public static string Coordinates(PixelPoint point, PixelRect selection, bool relative)
        => string.Format(CultureInfo.InvariantCulture, "{0}, {1}", (long)point.X - (relative && !selection.IsEmpty ? selection.X : 0),
            (long)point.Y - (relative && !selection.IsEmpty ? selection.Y : 0));

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
