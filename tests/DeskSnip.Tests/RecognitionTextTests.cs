using System;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public sealed class RecognitionTextTests
{
    [Fact] public void FormattingPreservesEnglishSpacesAndSupportsChinesePunctuation()
    {
        var lines = new[] { new RecognizedLine(new[] { new RecognizedWord("Hello,", default), new RecognizedWord("world!", default) }), new RecognizedLine(new[] { new RecognizedWord("Next", default) }) };
        Assert.Equal("Hello, world!" + Environment.NewLine + "Next", RecognitionText.Format(lines));
        Assert.Equal("Hello，world！Next", RecognitionText.Format(lines, TextLineLayout.MergeLines, true, PunctuationStyle.Chinese));
        Assert.Equal("Hello, world!" + Environment.NewLine + Environment.NewLine + "Next", RecognitionText.Format(lines, TextLineLayout.Paragraphs));
    }
    [Fact] public void HighlightUsesCurrentEditedTextWithoutInventingMatches()
    {
        var first = new RecognizedWord("one", new PixelRect(0, 0, 30, 20)); var second = new RecognizedWord("two", new PixelRect(40, 0, 30, 20));
        var lines = new[] { new RecognizedLine(new[] { first, second }) };
        Assert.Equal(new[] { second }, RecognitionText.SelectedWords(lines, "one two", 4, 3));
        Assert.Empty(RecognitionText.SelectedWords(lines, "corrected", 0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecognitionText.SelectedWords(lines, "one", 2, 2));
    }
}
