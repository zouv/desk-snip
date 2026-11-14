using System;

namespace DeskSnip.Core;

public enum AnnotationDash { Solid, Dash, Dot }
public enum AnnotationArrow { Filled, Outline, Double, Dimension }
public enum AnnotationLineJoin { Round, Miter, Bevel }
public enum AnnotationLineCap { Round, Flat, Square }
public enum AnnotationTextWrap { WordOrCharacter, Character, None }

public sealed class AnnotationAppearance
{
    public AnnotationAppearance(bool fill = false, AnnotationDash dash = AnnotationDash.Solid, double opacity = 1, AnnotationArrow arrow = AnnotationArrow.Filled, double effect = 2, bool textBackground = false,
        AnnotationLineJoin lineJoin = AnnotationLineJoin.Round, AnnotationLineCap lineCap = AnnotationLineCap.Round, bool smoothPencil = false, AnnotationTextWrap textWrap = AnnotationTextWrap.WordOrCharacter, double serialTextScale = 1.2, uint? serialBackground = null)
    {
        if (!Enum.IsDefined(typeof(AnnotationDash), dash) || !Enum.IsDefined(typeof(AnnotationArrow), arrow) || double.IsNaN(opacity) || opacity < .05 || opacity > 1 || double.IsNaN(effect) || effect < 1 || effect > 16) throw new ArgumentException(TextCatalog.Get("标注样式参数无效。"));
        Fill = fill; Dash = dash; Opacity = opacity; Arrow = arrow; Effect = effect; TextBackground = textBackground;
        if (!Enum.IsDefined(typeof(AnnotationLineJoin), lineJoin) || !Enum.IsDefined(typeof(AnnotationLineCap), lineCap) || !Enum.IsDefined(typeof(AnnotationTextWrap), textWrap) || double.IsNaN(serialTextScale) || serialTextScale < .5 || serialTextScale > 2) throw new ArgumentException(TextCatalog.Get("标注绘制参数无效。"));
        LineJoin = lineJoin; LineCap = lineCap; SmoothPencil = smoothPencil; TextWrap = textWrap; SerialTextScale = serialTextScale; SerialBackground = serialBackground;
    }
    public bool Fill { get; }
    public AnnotationDash Dash { get; }
    public double Opacity { get; }
    public AnnotationArrow Arrow { get; }
    public double Effect { get; }
    public bool TextBackground { get; }
    public AnnotationLineJoin LineJoin { get; }
    public AnnotationLineCap LineCap { get; }
    public bool SmoothPencil { get; }
    public AnnotationTextWrap TextWrap { get; }
    public double SerialTextScale { get; }
    public uint? SerialBackground { get; }
    public AnnotationAppearance WithEditorStyle(bool fill, AnnotationDash dash, double opacity, AnnotationArrow arrow, double effect, bool textBackground)
        => new AnnotationAppearance(fill, dash, opacity, arrow, effect, textBackground, LineJoin, LineCap, SmoothPencil, TextWrap, SerialTextScale, SerialBackground);
}
