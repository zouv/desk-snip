using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace DeskSnip.Core;

public static class DocumentExport
{
    private static readonly XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace relation = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace picture = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private static readonly XNamespace position = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    public static void SaveDocx(string path, RecognitionResponse result)
    {
        result.Validate(); var blocks = MarkdownContent.Parse(result.Markdown);
        AtomicFile.Write(path, stream =>
        {
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                OpenXmlPackage.ContentTypes(archive, new Dictionary<string, string> { { "/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" } }, true);
                OpenXmlPackage.Relationships(archive, "_rels/.rels", ("rId1", "officeDocument", "word/document.xml"));
                var assets = result.Assets.Select((asset, index) => (Asset: asset, Id: "rId" + (index + 1))).ToArray();
                OpenXmlPackage.Relationships(archive, "word/_rels/document.xml.rels", assets.Select(a => (a.Id, "image", "media/" + a.Asset.Name)).ToArray());
                foreach (var item in assets) using (var output = archive.CreateEntry("word/media/" + item.Asset.Name).Open()) { var bytes = RecognitionImages.Decode(item.Asset.Base64); output.Write(bytes, 0, bytes.Length); }
                var body = new XElement(word + "body");
                foreach (var block in blocks)
                {
                    if (block.Kind == "table")
                    {
                        var table = new XElement(word + "tbl", new XElement(word + "tblPr", new XElement(word + "tblW", new XAttribute(word + "w", 0), new XAttribute(word + "type", "auto")), new XElement(word + "tblBorders", new[] { "top", "left", "bottom", "right", "insideH", "insideV" }.Select(side => new XElement(word + side, new XAttribute(word + "val", "single"), new XAttribute(word + "sz", 4))))));
                        table.Add(new XElement(word + "tblGrid", Enumerable.Range(0, block.Rows[0].Count).Select(_ => new XElement(word + "gridCol", new XAttribute(word + "w", 9000 / block.Rows[0].Count)))));
                        foreach (var row in block.Rows) table.Add(new XElement(word + "tr", row.Select(cell => new XElement(word + "tc", Paragraph(cell))))); body.Add(table); continue;
                    }
                    if (block.Kind == "image")
                    {
                        var asset = assets.FirstOrDefault(a => a.Asset.Name == block.AssetName);
                        if (asset.Asset != null) { body.Add(Image(asset.Asset, asset.Id, assets.ToList().IndexOf(asset) + 1)); if (block.Text.Length > 0) body.Add(Paragraph(block.Text)); continue; }
                    }
                    body.Add(Paragraph(block.Kind == "formula" ? "$$" + block.Text + "$$" : block.Kind == "bullet" ? "• " + block.Text : block.Text, block.Kind == "heading" ? block.Level : 0, block.Kind == "code" || block.Kind == "formula"));
                }
                body.Add(new XElement(word + "sectPr", new XElement(word + "pgSz", new XAttribute(word + "w", 11906), new XAttribute(word + "h", 16838)), new XElement(word + "pgMar", new XAttribute(word + "top", 1134), new XAttribute(word + "right", 1134), new XAttribute(word + "bottom", 1134), new XAttribute(word + "left", 1134))));
                OpenXmlPackage.Xml(archive, "word/document.xml", new XElement(word + "document", new XAttribute(XNamespace.Xmlns + "w", word), new XAttribute(XNamespace.Xmlns + "r", relation), body));
            }
        });
    }
    private static XElement Paragraph(string text, int heading = 0, bool code = false)
    {
        var properties = new XElement(word + "rPr", new XElement(word + "rFonts", new XAttribute(word + "ascii", code ? "Consolas" : "Calibri"), new XAttribute(word + "eastAsia", "Microsoft YaHei")), new XElement(word + "sz", new XAttribute(word + "val", heading == 0 ? 22 : Math.Max(24, 42 - heading * 4))));
        if (heading > 0) properties.Element(word + "sz")!.AddBeforeSelf(new XElement(word + "b"));
        var run = new XElement(word + "r", properties); var lines = OpenXmlPackage.Text(text).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++) { if (i > 0) run.Add(new XElement(word + "br")); run.Add(new XElement(word + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), lines[i])); }
        return new XElement(word + "p", new XElement(word + "pPr", new XElement(word + "spacing", new XAttribute(word + "after", heading > 0 ? 200 : 120))), run);
    }
    private static XElement Image(RecognitionAsset asset, string id, int index)
    {
        var size = RecognitionImages.Dimensions(RecognitionImages.Decode(asset.Base64)); var ratio = Math.Min(1, 600d / size.Width); var width = (long)(size.Width * ratio * 9525); var height = (long)(size.Height * ratio * 9525);
        return new XElement(word + "p", new XElement(word + "r", new XElement(word + "drawing", new XElement(position + "inline", new XAttribute("distT", 0), new XAttribute("distB", 0), new XAttribute("distL", 0), new XAttribute("distR", 0),
            new XElement(position + "extent", new XAttribute("cx", width), new XAttribute("cy", height)), new XElement(position + "docPr", new XAttribute("id", index), new XAttribute("name", asset.Name)),
            new XElement(drawing + "graphic", new XElement(drawing + "graphicData", new XAttribute("uri", picture.NamespaceName), new XElement(picture + "pic", new XElement(picture + "nvPicPr", new XElement(picture + "cNvPr", new XAttribute("id", index), new XAttribute("name", asset.Name)), new XElement(picture + "cNvPicPr")),
                new XElement(picture + "blipFill", new XElement(drawing + "blip", new XAttribute(relation + "embed", id)), new XElement(drawing + "stretch", new XElement(drawing + "fillRect"))),
                new XElement(picture + "spPr", new XElement(drawing + "xfrm", new XElement(drawing + "off", new XAttribute("x", 0), new XAttribute("y", 0)), new XElement(drawing + "ext", new XAttribute("cx", width), new XAttribute("cy", height))), new XElement(drawing + "prstGeom", new XAttribute("prst", "rect"), new XElement(drawing + "avLst"))))))))));
    }
}
