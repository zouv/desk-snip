using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace DeskSnip.Core;

public static class TableExport
{
    private static readonly XNamespace sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static string Delimited(StructuredTable table, char separator, PixelRect? selection = null)
    {
        if (separator != ',' && separator != '\t') throw new ArgumentException(TextCatalog.Get("不支持此分隔符。"));
        table.Validate(); var area = Area(table, selection); var lines = new List<string>();
        for (var row = area.Y; row < area.Bottom; row++)
        {
            var cells = new List<string>();
            for (var column = area.X; column < area.Right; column++)
            {
                var value = table.Cell(row, column);
                var significant = value.TrimStart(' ', '\t', '\r', '\n');
                if (significant.Length > 0 && "=+-@".Contains(significant[0]) && !decimal.TryParse(significant, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) value = "'" + value;
                if (value.IndexOfAny(new[] { separator, '"', '\r', '\n' }) >= 0) value = "\"" + value.Replace("\"", "\"\"") + "\"";
                cells.Add(value);
            }
            lines.Add(string.Join(separator.ToString(), cells));
        }
        return string.Join("\r\n", lines);
    }
    public static void SaveDelimited(string path, StructuredTable table, char separator, PixelRect? selection = null)
        => AtomicFile.Write(path, stream => { using (var writer = new StreamWriter(stream, new UTF8Encoding(true), 4096, true)) writer.Write(Delimited(table, separator, selection)); });
    public static void SaveXlsx(string path, StructuredTable table, PixelRect? selection = null)
    {
        table.Validate(); var area = Area(table, selection);
        if (table.Rows.Skip(area.Y).Take(area.Height).Any(row => row.Skip(area.X).Take(area.Width).Any(value => value.Length > 32767))) throw new ArgumentException(TextCatalog.Get("XLSX 单元格最多 32767 字符，请缩短内容或使用 CSV。"));
        AtomicFile.Write(path, stream =>
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                OpenXmlPackage.ContentTypes(archive, new Dictionary<string, string> { { "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml" }, { "/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml" }, { "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml" } });
                OpenXmlPackage.Relationships(archive, "_rels/.rels", ("rId1", "officeDocument", "xl/workbook.xml"));
                OpenXmlPackage.Relationships(archive, "xl/_rels/workbook.xml.rels", ("rId1", "worksheet", "worksheets/sheet1.xml"), ("rId2", "styles", "styles.xml"));
                OpenXmlPackage.Xml(archive, "xl/workbook.xml", new XElement(sheet + "workbook", new XAttribute(XNamespace.Xmlns + "r", relationships), new XElement(sheet + "sheets", new XElement(sheet + "sheet", new XAttribute("name", "识别表格"), new XAttribute("sheetId", "1"), new XAttribute(relationships + "id", "rId1")))));
                var rows = new XElement(sheet + "sheetData");
                for (var row = area.Y; row < area.Bottom; row++)
                {
                    var element = new XElement(sheet + "row", new XAttribute("r", row - area.Y + 1));
                    for (var column = area.X; column < area.Right; column++) element.Add(new XElement(sheet + "c", new XAttribute("r", Address(column - area.X, row - area.Y)), new XAttribute("t", "inlineStr"), new XAttribute("s", row == area.Y ? 1 : 0), new XElement(sheet + "is", new XElement(sheet + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), OpenXmlPackage.Text(table.Cell(row, column))))));
                    rows.Add(element);
                }
                var document = new XElement(sheet + "worksheet", new XElement(sheet + "cols", new XElement(sheet + "col", new XAttribute("min", "1"), new XAttribute("max", area.Width), new XAttribute("width", "24"), new XAttribute("customWidth", "1"))), rows);
                var merges = table.Merges.Where(m => area.Intersect(m.Bounds).Equals(m.Bounds)).Select(m => new XElement(sheet + "mergeCell", new XAttribute("ref", Address(m.Column - area.X, m.Row - area.Y) + ":" + Address(m.Column + m.Columns - area.X - 1, m.Row + m.Rows - area.Y - 1)))).ToArray();
                if (merges.Length > 0) document.Add(new XElement(sheet + "mergeCells", new XAttribute("count", merges.Length), merges));
                OpenXmlPackage.Xml(archive, "xl/worksheets/sheet1.xml", document);
                OpenXmlPackage.Xml(archive, "xl/styles.xml", Styles());
            }
        });
    }
    private static XElement Styles()
    {
        XElement X(string name, params object[] content) => new XElement(sheet + name, content);
        var line = new XAttribute("style", "thin");
        return X("styleSheet", X("fonts", new XAttribute("count", 2), X("font", X("sz", new XAttribute("val", 11)), X("name", new XAttribute("val", "Calibri"))), X("font", X("b"), X("sz", new XAttribute("val", 11)), X("name", new XAttribute("val", "Calibri")))),
            X("fills", new XAttribute("count", 2), X("fill", X("patternFill", new XAttribute("patternType", "none"))), X("fill", X("patternFill", new XAttribute("patternType", "gray125")))),
            X("borders", new XAttribute("count", 1), X("border", X("left", line), X("right", new XAttribute(line)), X("top", new XAttribute(line)), X("bottom", new XAttribute(line)), X("diagonal"))),
            X("cellStyleXfs", new XAttribute("count", 1), X("xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0))),
            X("cellXfs", new XAttribute("count", 2), Enumerable.Range(0, 2).Select(i => X("xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", i), new XAttribute("fillId", 0), new XAttribute("borderId", 0), new XAttribute("xfId", 0), new XAttribute("applyAlignment", 1), X("alignment", new XAttribute("horizontal", "left"), new XAttribute("vertical", "top"), new XAttribute("wrapText", 1))))),
            X("cellStyles", new XAttribute("count", 1), X("cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", 0), new XAttribute("builtinId", 0))));
    }
    private static string Address(int column, int row)
    {
        var letters = ""; for (var value = column + 1; value > 0; value = (value - 1) / 26) letters = (char)('A' + (value - 1) % 26) + letters;
        return letters + (row + 1).ToString(CultureInfo.InvariantCulture);
    }
    private static PixelRect Area(StructuredTable table, PixelRect? selection)
    {
        var bounds = new PixelRect(0, 0, table.Rows[0].Count, table.Rows.Count); var area = selection ?? bounds;
        if (area.IsEmpty || !area.Intersect(bounds).Equals(area)) throw new ArgumentException(TextCatalog.Get("表格导出范围无效。")); return area;
    }
}

public static class RecognitionExport
{
    public static void SaveText(string path, string text) => AtomicFile.Write(path, stream => { using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true)) writer.Write(text); });
    public static void SaveMarkdownZip(string path, RecognitionResponse result)
    {
        result.Validate();
        AtomicFile.Write(path, stream =>
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("document.md").Open(), new UTF8Encoding(false))) writer.Write(result.Markdown);
                foreach (var asset in result.Assets) using (var output = archive.CreateEntry("images/" + asset.Name).Open()) { var bytes = RecognitionImages.Decode(asset.Base64); output.Write(bytes, 0, bytes.Length); }
            }
        });
    }
}

internal static class OpenXmlPackage
{
    internal static void Xml(ZipArchive archive, string name, XElement root)
    {
        using (var output = archive.CreateEntry(name).Open()) new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root).Save(output);
    }
    internal static void ContentTypes(ZipArchive archive, IDictionary<string, string> overrides, bool images = false)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        var types = new XElement(ns + "Types", new XElement(ns + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")), new XElement(ns + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")));
        if (images) { types.Add(new XElement(ns + "Default", new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png"))); foreach (var extension in new[] { "jpg", "jpeg" }) types.Add(new XElement(ns + "Default", new XAttribute("Extension", extension), new XAttribute("ContentType", "image/jpeg"))); }
        foreach (var pair in overrides) types.Add(new XElement(ns + "Override", new XAttribute("PartName", pair.Key), new XAttribute("ContentType", pair.Value))); Xml(archive, "[Content_Types].xml", types);
    }
    internal static void Relationships(ZipArchive archive, string name, params (string Id, string Type, string Target)[] items)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/relationships";
        Xml(archive, name, new XElement(ns + "Relationships", items.Select(i => new XElement(ns + "Relationship", new XAttribute("Id", i.Id), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/" + i.Type), new XAttribute("Target", i.Target)))));
    }
    internal static string Text(string value) => new string(value.Where(c => c >= ' ' || c == '\n' || c == '\r' || c == '\t').ToArray());
}
