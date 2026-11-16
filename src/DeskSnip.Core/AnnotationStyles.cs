using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DeskSnip.Core;

/// <summary>子操作栏里可跨启动保存的工具样式；绘制参数（线连接、文字换行等）仍来自设置页。</summary>
public sealed class AnnotationStyle
{
    public AnnotationStyle(uint argb, double thickness, bool fill, AnnotationDash dash, AnnotationArrow arrow, double opacity, double effect, bool textBackground,
        double cornerRadius, string fontFamily, double fontSize, bool bold, bool italic)
    {
        if (double.IsNaN(thickness) || thickness < 1 || thickness > 80) throw new ArgumentException(TextCatalog.Get("标注样式参数无效。"));
        if (string.IsNullOrWhiteSpace(fontFamily) || fontFamily.Length > 256) throw new ArgumentException(TextCatalog.Get("字体名称无效。"));
        if (double.IsNaN(fontSize) || fontSize < 1 || fontSize > 480) throw new ArgumentException(TextCatalog.Get("标注样式参数无效。"));
        _ = new AnnotationAppearance(fill, dash, opacity, arrow, effect, textBackground, cornerRadius: cornerRadius);
        Argb = argb; Thickness = thickness; Fill = fill; Dash = dash; Arrow = arrow; Opacity = opacity; Effect = effect; TextBackground = textBackground;
        CornerRadius = cornerRadius; FontFamily = fontFamily; FontSize = fontSize; Bold = bold; Italic = italic;
    }
    public uint Argb { get; }
    public double Thickness { get; }
    public bool Fill { get; }
    public AnnotationDash Dash { get; }
    public AnnotationArrow Arrow { get; }
    public double Opacity { get; }
    public double Effect { get; }
    public bool TextBackground { get; }
    public double CornerRadius { get; }
    public string FontFamily { get; }
    public double FontSize { get; }
    public bool Bold { get; }
    public bool Italic { get; }
    public bool SameAs(AnnotationStyle? other)
        => other != null && Argb == other.Argb && Thickness.Equals(other.Thickness) && Fill == other.Fill && Dash == other.Dash && Arrow == other.Arrow &&
           Opacity.Equals(other.Opacity) && Effect.Equals(other.Effect) && TextBackground == other.TextBackground && CornerRadius.Equals(other.CornerRadius) &&
           string.Equals(FontFamily, other.FontFamily, StringComparison.Ordinal) && FontSize.Equals(other.FontSize) && Bold == other.Bold && Italic == other.Italic;
}

public sealed class AnnotationStyleSet
{
    internal AnnotationStyleSet(IReadOnlyDictionary<string, AnnotationStyle> styles, uint? sharedArgb) { Styles = styles; SharedArgb = sharedArgb; }
    public IReadOnlyDictionary<string, AnnotationStyle> Styles { get; }
    public uint? SharedArgb { get; }
}

public static class AnnotationStyleRules
{
    public static string GroupOf(AnnotationKind kind) => kind switch
    {
        AnnotationKind.Rectangle or AnnotationKind.Ellipse or AnnotationKind.RoundedRectangle => "Rectangle",
        AnnotationKind.Arrow or AnnotationKind.Line or AnnotationKind.Polyline => "Arrow",
        AnnotationKind.Pen or AnnotationKind.Highlight => "Pen",
        AnnotationKind.Text or AnnotationKind.Watermark => "Text",
        AnnotationKind.Number => "Number",
        AnnotationKind.Mosaic or AnnotationKind.Blur or AnnotationKind.MosaicPen => "Mosaic",
        AnnotationKind.Cover or AnnotationKind.CoverPen => "Cover",
        AnnotationKind.Eraser or AnnotationKind.EraserPen => "Eraser",
        AnnotationKind.Spotlight or AnnotationKind.Magnifier => "Spotlight",
        _ => throw new ArgumentException(TextCatalog.Get("工具栏分组无效。"))
    };
    public static string Serialize(IReadOnlyDictionary<string, AnnotationStyle> styles, uint? sharedArgb)
    {
        var records = new List<string>();
        if (sharedArgb.HasValue) records.Add("@" + sharedArgb.Value.ToString("X8", CultureInfo.InvariantCulture));
        foreach (var group in AnnotationPreferenceRules.ToolbarGroups)
        {
            if (!styles.TryGetValue(group, out var style)) continue;
            records.Add(string.Join("|", group, style.Argb.ToString("X8", CultureInfo.InvariantCulture), Number(style.Thickness), style.Fill ? "1" : "0",
                ((int)style.Dash).ToString(CultureInfo.InvariantCulture), ((int)style.Arrow).ToString(CultureInfo.InvariantCulture), Number(style.Opacity), Number(style.Effect),
                style.TextBackground ? "1" : "0", Number(style.CornerRadius), Uri.EscapeDataString(style.FontFamily), Number(style.FontSize), style.Bold ? "1" : "0", style.Italic ? "1" : "0"));
        }
        return string.Join(";", records);
    }
    public static AnnotationStyleSet Parse(string? value)
    {
        var styles = new Dictionary<string, AnnotationStyle>(StringComparer.Ordinal);
        uint? shared = null;
        if (string.IsNullOrWhiteSpace(value)) return new AnnotationStyleSet(styles, shared);
        var text = value!;
        if (text.Length > 8192) throw new ArgumentException(TextCatalog.Get("工具子操作栏样式配置无效。"));
        foreach (var record in text.Split(';'))
        {
            if (record.Length == 0) continue;
            if (record[0] == '@')
            {
                if (shared.HasValue || record.Length != 9 || !uint.TryParse(record.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
                    throw new ArgumentException(TextCatalog.Get("工具子操作栏样式配置无效。"));
                shared = argb; continue;
            }
            var parts = record.Split('|');
            if (parts.Length != 14 || !AnnotationPreferenceRules.ToolbarGroups.Contains(parts[0]) || styles.ContainsKey(parts[0]))
                throw new ArgumentException(TextCatalog.Get("工具子操作栏样式配置无效。"));
            try
            {
                var style = new AnnotationStyle(
                    ParseArgb(parts[1]), ParseNumber(parts[2]), parts[3] == "1", (AnnotationDash)ParseInt(parts[4]), (AnnotationArrow)ParseInt(parts[5]),
                    ParseNumber(parts[6]), ParseNumber(parts[7]), parts[8] == "1", ParseNumber(parts[9]), Uri.UnescapeDataString(parts[10]), ParseNumber(parts[11]), parts[12] == "1", parts[13] == "1");
                if (parts[3] != "0" && parts[3] != "1" || parts[8] != "0" && parts[8] != "1" || parts[12] != "0" && parts[12] != "1" || parts[13] != "0" && parts[13] != "1")
                    throw new ArgumentException(TextCatalog.Get("工具子操作栏样式配置无效。"));
                styles[parts[0]] = style;
            }
            catch (Exception error) when (error is FormatException || error is OverflowException || error is UriFormatException || error is ArgumentException)
            { throw new ArgumentException(TextCatalog.Get("工具子操作栏样式配置无效。")); }
        }
        return new AnnotationStyleSet(styles, shared);
    }
    public static void Validate(string? value) => Parse(value);
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static uint ParseArgb(string value)
        => value.Length == 8 && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb) ? argb : throw new FormatException();
    private static double ParseNumber(string value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : throw new FormatException();
    private static int ParseInt(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : throw new FormatException();
}
