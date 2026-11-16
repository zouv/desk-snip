using System;
using System.Collections.Generic;
using System.Linq;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public class AnnotationStyleTests
{
    private static AnnotationStyle Style(uint argb, double thickness, AnnotationDash dash = AnnotationDash.Solid, AnnotationArrow arrow = AnnotationArrow.Filled,
        bool fill = false, double opacity = 1, double effect = 2, bool textBackground = false, double radius = 0, string font = "Microsoft YaHei UI", double size = 18, bool bold = false, bool italic = false)
        => new AnnotationStyle(argb, thickness, fill, dash, arrow, opacity, effect, textBackground, radius, font, size, bold, italic);
    [Fact] public void StylesRoundTripWithSharedColorAndEscapedFont()
    {
        var styles = new Dictionary<string, AnnotationStyle>
        {
            ["Rectangle"] = Style(0xff112233, 5, AnnotationDash.Dash, fill: true, opacity: .75, effect: 4, textBackground: true, radius: 12, font: "Segoe UI;|Test", size: 24, bold: true, italic: true),
            ["Arrow"] = Style(0xff445566, 8, AnnotationDash.Dot, AnnotationArrow.Outline)
        };
        var text = AnnotationStyleRules.Serialize(styles, 0xff3489f5);
        var parsed = AnnotationStyleRules.Parse(text);
        Assert.Equal(0xff3489f5u, parsed.SharedArgb);
        Assert.True(parsed.Styles["Rectangle"].SameAs(styles["Rectangle"]));
        Assert.True(parsed.Styles["Arrow"].SameAs(styles["Arrow"]));
        Assert.Equal("Segoe UI;|Test", parsed.Styles["Rectangle"].FontFamily);
    }
    [Fact] public void StyleGroupsCoverEveryAnnotationKind()
    {
        foreach (var kind in Enum.GetValues(typeof(AnnotationKind)).Cast<AnnotationKind>())
            Assert.Contains(AnnotationStyleRules.GroupOf(kind), AnnotationPreferenceRules.ToolbarGroups);
    }
    [Fact] public void InvalidStyleConfigurationIsRejected()
    {
        Assert.Throws<ArgumentException>(() => AnnotationStyleRules.Parse("Rectangle|bad"));
        Assert.Throws<ArgumentException>(() => AnnotationStyleRules.Parse("Unknown|FF112233|3|0|0|0|1|2|0|0|Arial|18|0|0"));
        Assert.Throws<ArgumentException>(() => AnnotationStyleRules.Parse("Rectangle|FF112233|0|0|0|0|1|2|0|0|Arial|18|0|0"));
        Assert.Throws<ArgumentException>(() => AnnotationStyleRules.Parse("@FF112233;@FF445566"));
        Assert.Throws<ArgumentException>(() => new AppSettings { MarkToolStyles = "Rectangle|broken" }.Validate());
    }
    [Fact] public void PortablePreferencesRoundTripLongStyleConfiguration()
    {
        var font = new string('A', 200);
        var styles = AnnotationPreferenceRules.ToolbarGroups.ToDictionary(group => group, group => Style(0xff123456, 3, font: font));
        var value = AnnotationStyleRules.Serialize(styles, 0xff123456);
        Assert.True(value.Length > 1000);
        var document = PreferenceTransfer.Export(new AppSettings { MarkToolStyles = value });
        var imported = PreferenceTransfer.Import(new AppSettings(), document);
        Assert.Equal(value, imported.MarkToolStyles);
    }
}
