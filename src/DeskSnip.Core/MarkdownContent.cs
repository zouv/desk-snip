using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DeskSnip.Core;

public sealed class MarkdownBlock
{
    public string Kind { get; set; } = "paragraph";
    public string Text { get; set; } = "";
    public int Level { get; set; }
    public string AssetName { get; set; } = "";
    public List<List<string>> Rows { get; set; } = new List<List<string>>();
}

public static class MarkdownContent
{
    public static IReadOnlyList<MarkdownBlock> Parse(string markdown)
    {
        var blocks = new List<MarkdownBlock>(); var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index]; if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                var content = new List<string>(); while (++index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal)) content.Add(lines[index]);
                blocks.Add(new MarkdownBlock { Kind = "code", Text = string.Join("\n", content) }); continue;
            }
            if (line.Trim() == "$$")
            {
                var content = new List<string>(); while (++index < lines.Length && lines[index].Trim() != "$$") content.Add(lines[index]);
                blocks.Add(new MarkdownBlock { Kind = "formula", Text = string.Join("\n", content) }); continue;
            }
            if (line.Trim().StartsWith("$$", StringComparison.Ordinal) && line.Trim().EndsWith("$$", StringComparison.Ordinal) && line.Trim().Length > 4)
            { blocks.Add(new MarkdownBlock { Kind = "formula", Text = line.Trim().Substring(2, line.Trim().Length - 4) }); continue; }
            var image = Regex.Match(line.Trim(), @"^!\[(.*?)\]\((?:\./)?images/([A-Za-z0-9_-]{1,80}\.(?:png|jpg|jpeg))\)$");
            if (image.Success) { blocks.Add(new MarkdownBlock { Kind = "image", Text = image.Groups[1].Value, AssetName = image.Groups[2].Value }); continue; }
            var heading = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            if (heading.Success) { blocks.Add(new MarkdownBlock { Kind = "heading", Level = heading.Groups[1].Length, Text = heading.Groups[2].Value }); continue; }
            if (index + 1 < lines.Length && line.Contains("|") && IsTableSeparator(lines[index + 1]))
            {
                var rows = new List<List<string>> { TableRow(line) }; index++;
                while (index + 1 < lines.Length && lines[index + 1].Contains("|") && !string.IsNullOrWhiteSpace(lines[index + 1])) rows.Add(TableRow(lines[++index]));
                var width = rows.Max(r => r.Count); foreach (var row in rows) while (row.Count < width) row.Add("");
                blocks.Add(new MarkdownBlock { Kind = "table", Rows = rows }); continue;
            }
            var bullet = Regex.Match(line, @"^\s*(?:[-*+]\s+|\d+\.\s+)(.+)$");
            blocks.Add(new MarkdownBlock { Kind = bullet.Success ? "bullet" : "paragraph", Text = bullet.Success ? bullet.Groups[1].Value : line });
        }
        return blocks;
    }
    private static bool IsTableSeparator(string value) => Regex.IsMatch(value.Trim(), @"^\|?\s*:?-{3,}:?\s*(?:\|\s*:?-{3,}:?\s*)+\|?$");
    private static List<string> TableRow(string value) => value.Trim().Trim('|').Split('|').Select(s => s.Trim()).ToList();
}
