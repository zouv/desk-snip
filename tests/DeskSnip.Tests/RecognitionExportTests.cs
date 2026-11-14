using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using DeskSnip.Core;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Xunit;

namespace DeskSnip.Tests;

public sealed class RecognitionExportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "DeskSnip-export-" + Guid.NewGuid().ToString("N"));
    private static StructuredTable Table() => new StructuredTable { Rows = new List<List<string>> { new List<string> { "名称", "数量" }, new List<string> { "甲,\"引号\"\n换行", "10" }, new List<string> { "乙", "2" } } };
    [Fact] public void TableEditsSortMergeAndUndoKeepCellContents()
    {
        var document = new TableDocument(Table()); document.Sort(1, false, true); Assert.Equal("2", document.Snapshot.Rows[1][1]);
        document.Merge(new PixelRect(0, 1, 2, 1)); Assert.Equal("", document.Snapshot.Cell(1, 1)); document.SetCell(1, 1, "edited"); Assert.Equal("edited", document.Snapshot.Cell(1, 0));
        document.Split(new PixelRect(0, 1, 2, 1)); Assert.Equal("2", document.Snapshot.Cell(1, 1)); document.Undo(); Assert.Single(document.Snapshot.Merges); document.Undo(); Assert.Equal("乙", document.Snapshot.Cell(1, 0));
        document.Redo(); document.InsertRow(2); Assert.Empty(document.Snapshot.Merges); Assert.Equal(4, document.Snapshot.Rows.Count); document.DeleteRow(2); document.InsertColumn(2); document.DeleteColumn(2);
        var previous = document.Snapshot; Assert.Throws<ArgumentException>(() => document.Merge(new PixelRect(0, 0, 999, 999))); Assert.Equal(previous.Rows[1], document.Snapshot.Rows[1]);
    }
    [Fact] public void CsvQuotesAndXlsxPreserveLiteralStringsAndMergeRanges()
    {
        var table = Table(); table.Rows[2][0] = "=SUM(1,2)"; Assert.Contains("\"甲,\"\"引号\"\"\n换行\"", TableExport.Delimited(table, ',')); Assert.Contains("'乙".Replace("乙", "=SUM(1,2)"), TableExport.Delimited(table, ','));
        table.Merges.Add(new TableMerge { Row = 0, Column = 0, Columns = 2, Rows = 1 }); var path = Path.Combine(root, "table.xlsx"); TableExport.SaveXlsx(path, table);
        using (var archive = ZipFile.OpenRead(path))
        {
            var sheet = Xml(archive, "xl/worksheets/sheet1.xml"); XNamespace ns = sheet.Root!.Name.Namespace;
            Assert.Contains(sheet.Descendants(ns + "t"), value => value.Value == "=SUM(1,2)"); Assert.Empty(sheet.Descendants(ns + "f")); Assert.Equal("A1:B1", sheet.Descendants(ns + "mergeCell").Single().Attribute("ref")!.Value);
            Assert.NotNull(archive.GetEntry("[Content_Types].xml")); Assert.NotNull(archive.GetEntry("xl/styles.xml"));
            Assert.All(sheet.Descendants(ns + "c"), cell => Assert.Equal("inlineStr", cell.Attribute("t")!.Value));
        }
        using (var document = SpreadsheetDocument.Open(path, false)) Assert.Empty(new OpenXmlValidator().Validate(document).Select(error => error.Description + " at " + error.Path?.XPath));
        Assert.Equal("数量\r\n10\r\n2", TableExport.Delimited(Table(), '\t', new PixelRect(1, 0, 1, 3)));
        var selected = Path.Combine(root, "selected.xlsx"); TableExport.SaveXlsx(selected, Table(), new PixelRect(1, 1, 1, 2));
        using (var archive = ZipFile.OpenRead(selected)) { var xml = Xml(archive, "xl/worksheets/sheet1.xml"); Assert.Contains(xml.Descendants(), value => value.Name.LocalName == "t" && value.Value == "10"); }
    }
    [Fact] public void MarkdownPackageAndWordIncludeEmbeddedAssetsAndEditableText()
    {
        var result = RecognitionServiceTests.Result("document"); var asset = new RecognitionAsset { Name = "sample.png", Base64 = RecognitionServiceTests.SyntheticImage() }; result.Assets.Add(asset);
        result.Markdown += "\n\n![示例图](images/sample.png)\n\n```\n<literal>\n```";
        var zip = Path.Combine(root, "document.zip"); RecognitionExport.SaveMarkdownZip(zip, result);
        using (var archive = ZipFile.OpenRead(zip)) { Assert.NotNull(archive.GetEntry("images/sample.png")); using (var reader = new StreamReader(archive.GetEntry("document.md")!.Open())) Assert.Equal(result.Markdown, reader.ReadToEnd()); }
        var word = Path.Combine(root, "document.docx"); DocumentExport.SaveDocx(word, result);
        using (var archive = ZipFile.OpenRead(word))
        {
            var document = Xml(archive, "word/document.xml"); Assert.NotNull(archive.GetEntry("word/media/sample.png")); Assert.Contains(document.Descendants(), element => element.Name.LocalName == "t" && element.Value == "<literal>");
            Assert.Contains(document.Descendants(), element => element.Name.LocalName == "tbl"); Assert.Contains(document.Descendants(), element => element.Name.LocalName == "drawing");
            var relations = Xml(archive, "word/_rels/document.xml.rels"); Assert.All(relations.Root!.Elements(), relation => Assert.Null(relation.Attribute("TargetMode")));
        }
        using (var document = WordprocessingDocument.Open(word, false))
        {
            var errors = new OpenXmlValidator().Validate(document).Select(error => error.Description + " at " + error.Path?.XPath).ToArray();
            Assert.True(errors.Length == 0, string.Join("\n", errors));
        }
        var before = File.ReadAllBytes(zip); result.Assets[0].Name = "../bad.png"; Assert.Throws<ArgumentException>(() => RecognitionExport.SaveMarkdownZip(zip, result)); Assert.Equal(before, File.ReadAllBytes(zip));
    }
    [Fact] public void MarkdownParserPreservesUntrustedLinksAsText()
    {
        var blocks = MarkdownContent.Parse("# Title\n\n![remote](https://example.com/a.png)\n\n<script>sample</script>\n\n- item\n\n$$\nx^2\n$$");
        Assert.Equal("heading", blocks[0].Kind); Assert.DoesNotContain(blocks, b => b.Kind == "image"); Assert.Contains(blocks, b => b.Text.Contains("<script>")); Assert.Contains(blocks, b => b.Kind == "formula" && b.Text == "x^2");
    }
    private static XDocument Xml(ZipArchive archive, string name) { using (var stream = archive.GetEntry(name)!.Open()) return XDocument.Load(stream); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
