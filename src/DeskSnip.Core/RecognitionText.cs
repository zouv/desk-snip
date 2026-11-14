using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DeskSnip.Core;

public sealed class RecognizedWord
{
    public RecognizedWord(string text, PixelRect bounds) { Text = text ?? throw new ArgumentNullException(nameof(text)); Bounds = bounds; }
    public string Text { get; }
    public PixelRect Bounds { get; }
}

public sealed class RecognizedLine
{
    public RecognizedLine(IEnumerable<RecognizedWord> words) { Words = words.ToArray(); Text = string.Join(" ", Words.Select(w => w.Text)); }
    public IReadOnlyList<RecognizedWord> Words { get; }
    public string Text { get; }
}

public enum TextLineLayout { PreserveLines, MergeLines, Paragraphs }
public enum PunctuationStyle { Original, English, Chinese }

public static class RecognitionText
{
    public static string Format(IReadOnlyList<RecognizedLine> lines, TextLineLayout layout = TextLineLayout.PreserveLines,
        bool removeSpaces = false, PunctuationStyle punctuation = PunctuationStyle.Original)
    {
        var text = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Text;
            if (removeSpaces) line = Regex.Replace(line, @"[^\S\r\n]+", "");
            if (i > 0)
            {
                if (layout == TextLineLayout.PreserveLines) text.Append(Environment.NewLine);
                else if (layout == TextLineLayout.Paragraphs && EndsSentence(lines[i - 1].Text)) text.Append(Environment.NewLine).Append(Environment.NewLine);
                else if (text.Length > 0 && line.Length > 0 && IsLatin(text[text.Length - 1]) && IsLatin(line[0]) && !removeSpaces) text.Append(' ');
            }
            text.Append(ConvertPunctuation(line, punctuation));
        }
        return text.ToString();
    }
    private static bool IsLatin(char value) => value < 128 && char.IsLetterOrDigit(value);
    private static bool EndsSentence(string value) => value.TrimEnd().EndsWith("。", StringComparison.Ordinal) || value.TrimEnd().EndsWith(".", StringComparison.Ordinal) ||
        value.TrimEnd().EndsWith("!", StringComparison.Ordinal) || value.TrimEnd().EndsWith("！", StringComparison.Ordinal) || value.TrimEnd().EndsWith("?", StringComparison.Ordinal) || value.TrimEnd().EndsWith("？", StringComparison.Ordinal);
    public static string ConvertPunctuation(string text, PunctuationStyle style)
    {
        if (style == PunctuationStyle.Original) return text;
        const string western = ",:;!?()"; const string eastern = "，：；！？（）";
        var from = style == PunctuationStyle.English ? eastern : western; var to = style == PunctuationStyle.English ? western : eastern;
        var characters = text.ToCharArray(); for (var i = 0; i < characters.Length; i++) { var index = from.IndexOf(characters[i]); if (index >= 0) characters[i] = to[index]; }
        return new string(characters);
    }
    /// <summary>校对改变文字后，只高亮仍能在当前文本中精确对应的识别词。</summary>
    public static IReadOnlyList<RecognizedWord> SelectedWords(IReadOnlyList<RecognizedLine> lines, string editedText, int start, int length)
    {
        if (start < 0 || length < 0 || start > editedText.Length - length) throw new ArgumentOutOfRangeException(nameof(start));
        var result = new List<RecognizedWord>(); var cursor = 0;
        foreach (var word in lines.SelectMany(l => l.Words))
        {
            if (word.Text.Length == 0) continue;
            var index = editedText.IndexOf(word.Text, cursor, StringComparison.Ordinal);
            if (index < 0) continue;
            if (length > 0 && index < start + length && index + word.Text.Length > start) result.Add(word);
            cursor = index + word.Text.Length;
        }
        return result;
    }
}
