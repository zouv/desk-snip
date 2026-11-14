using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DeskSnip.Core;

public static class PrivacyDetector
{
    private static readonly Regex sensitive = new Regex(@"(?<!\d)1[3-9]\d{9}(?!\d)|[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant);
    public static IReadOnlyList<PixelRect> Find(IReadOnlyList<RecognizedLine> lines)
    {
        var result = new List<PixelRect>();
        foreach (var line in lines)
        {
            var text = ""; var positions = new List<(RecognizedWord Word, int Start, int End)>();
            foreach (var word in line.Words) { var start = text.Length; text += Regex.Replace(word.Text, @"\s+", ""); positions.Add((word, start, text.Length)); }
            foreach (Match match in sensitive.Matches(text))
            {
                var words = positions.Where(p => p.Start < match.Index + match.Length && p.End > match.Index).Select(p => p.Word.Bounds).Where(b => !b.IsEmpty).ToArray();
                if (words.Length == 0) continue;
                var left = words.Min(b => b.X); var top = words.Min(b => b.Y); result.Add(new PixelRect(left, top, words.Max(b => b.Right) - left, words.Max(b => b.Bottom) - top));
            }
        }
        return result.Distinct().ToArray();
    }
}
