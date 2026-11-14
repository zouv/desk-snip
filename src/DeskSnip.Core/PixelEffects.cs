using System;

namespace DeskSnip.Core;

public static class PixelEffects
{
    public static PixelBuffer Blur(PixelBuffer image, int radius)
    {
        if (radius < 1 || radius > 80) throw new ArgumentOutOfRangeException(nameof(radius));
        var input = image.Data; var horizontal = new byte[input.Length]; var output = new byte[input.Length];
        for (var y = 0; y < image.Height; y++) for (var channel = 0; channel < 4; channel++)
        {
            long sum = 0; var left = 0; var right = Math.Min(image.Width - 1, radius);
            for (var x = left; x <= right; x++) sum += input[(y * image.Width + x) * 4 + channel];
            for (var x = 0; x < image.Width; x++)
            {
                var nextLeft = Math.Max(0, x - radius); var nextRight = Math.Min(image.Width - 1, x + radius);
                while (left < nextLeft) sum -= input[(y * image.Width + left++) * 4 + channel];
                while (right < nextRight) sum += input[(y * image.Width + ++right) * 4 + channel];
                horizontal[(y * image.Width + x) * 4 + channel] = (byte)(sum / (right - left + 1));
            }
        }
        for (var x = 0; x < image.Width; x++) for (var channel = 0; channel < 4; channel++)
        {
            long sum = 0; var top = 0; var bottom = Math.Min(image.Height - 1, radius);
            for (var y = top; y <= bottom; y++) sum += horizontal[(y * image.Width + x) * 4 + channel];
            for (var y = 0; y < image.Height; y++)
            {
                var nextTop = Math.Max(0, y - radius); var nextBottom = Math.Min(image.Height - 1, y + radius);
                while (top < nextTop) sum -= horizontal[(top++ * image.Width + x) * 4 + channel];
                while (bottom < nextBottom) sum += horizontal[(++bottom * image.Width + x) * 4 + channel];
                output[(y * image.Width + x) * 4 + channel] = (byte)(sum / (bottom - top + 1));
            }
        }
        return new PixelBuffer(image.Width, image.Height, output);
    }
}
