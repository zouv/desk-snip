using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DeskSnip.Core;

public enum AnnotationKind { Rectangle, Ellipse, Line, Arrow, Pen, Highlight, Text, Number, Mosaic, Cover, RoundedRectangle, Polyline, Spotlight, Magnifier, Blur, MosaicPen, CoverPen, Eraser, EraserPen, Watermark }

public sealed class Annotation
{
    public Annotation(AnnotationKind kind, IEnumerable<PixelPoint> points, uint argb, double thickness, string text = "", string fontFamily = "Microsoft YaHei UI", double? fontSize = null, AnnotationAppearance? appearance = null, bool bold = false, bool italic = false)
    {
        if (!Enum.IsDefined(typeof(AnnotationKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (thickness < 1 || thickness > 80 || double.IsNaN(thickness)) throw new ArgumentOutOfRangeException(nameof(thickness));
        if (string.IsNullOrWhiteSpace(fontFamily) || fontFamily.Length > 256) throw new ArgumentException(TextCatalog.Get("字体名称无效。"), nameof(fontFamily));
        if (fontSize.HasValue && (fontSize.Value < 1 || fontSize.Value > 480 || double.IsNaN(fontSize.Value))) throw new ArgumentOutOfRangeException(nameof(fontSize));
        var copy = points?.ToArray() ?? throw new ArgumentNullException(nameof(points));
        if (copy.Length == 0) throw new ArgumentException(TextCatalog.Get("标注至少需要一个点。"), nameof(points));
        Kind = kind; Points = Array.AsReadOnly(copy); Argb = argb; Thickness = thickness; Text = text ?? "";
        FontFamily = fontFamily; FontSize = fontSize ?? Math.Max(14, thickness * 6);
        Appearance = appearance ?? new AnnotationAppearance(); Bold = bold; Italic = italic;
    }
    public AnnotationKind Kind { get; }
    public ReadOnlyCollection<PixelPoint> Points { get; }
    public uint Argb { get; }
    public double Thickness { get; }
    public string Text { get; }
    public string FontFamily { get; }
    public double FontSize { get; }
    public AnnotationAppearance Appearance { get; }
    public bool Bold { get; }
    public bool Italic { get; }
    public Annotation WithPoints(IEnumerable<PixelPoint> points) => new Annotation(Kind, points, Argb, Thickness, Text, FontFamily, FontSize, Appearance, Bold, Italic);
    public Annotation WithText(string text) => new Annotation(Kind, Points, Argb, Thickness, text, FontFamily, FontSize, Appearance, Bold, Italic);
    public Annotation WithStyle(uint argb, double thickness, string? fontFamily = null, double? fontSize = null, AnnotationAppearance? appearance = null, bool? bold = null, bool? italic = null)
        => new Annotation(Kind, Points, argb, thickness, Text, fontFamily ?? FontFamily, fontSize ?? FontSize, appearance ?? Appearance, bold ?? Bold, italic ?? Italic);
}

/// <summary>保存不可变快照；新编辑切断 redo，清空也可以撤销。</summary>
public sealed class AnnotationDocument
{
    private readonly List<Annotation[]> states = new List<Annotation[]> { Array.Empty<Annotation>() };
    private int index;
    public AnnotationDocument(int width, int height, bool limitToCanvas = true)
    {
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width; Height = height;
        LimitToCanvas = limitToCanvas;
    }
    public int Width { get; }
    public int Height { get; }
    public bool LimitToCanvas { get; }
    public IReadOnlyList<Annotation> Items => Array.AsReadOnly(states[index]);
    public bool CanUndo => index > 0;
    public bool CanRedo => index < states.Count - 1;
    public int Revision { get; private set; }
    /// <summary>保留当前对象与撤销栈的独立副本；Annotation 本身不可变。</summary>
    public AnnotationDocument Clone()
    {
        var copy = new AnnotationDocument(Width, Height, LimitToCanvas);
        copy.states.Clear(); copy.states.AddRange(states.Select(state => (Annotation[])state.Clone()));
        copy.index = index; copy.Revision = Revision; return copy;
    }
    public void Add(Annotation item)
    {
        Validate(item);
        Commit(states[index].Concat(new[] { item }).ToArray());
    }
    public void Replace(int itemIndex, Annotation item)
    {
        CheckIndex(itemIndex); Validate(item);
        if (ReferenceEquals(states[index][itemIndex], item)) return;
        var updated = (Annotation[])states[index].Clone(); updated[itemIndex] = item; Commit(updated);
    }
    public void Remove(int itemIndex)
    {
        CheckIndex(itemIndex);
        Commit(states[index].Where((_, i) => i != itemIndex).ToArray());
    }
    public void RemoveAndRenumber(int itemIndex, bool startAtZero)
    {
        CheckIndex(itemIndex); var number = startAtZero ? 0 : 1;
        var next = states[index].Where((_, i) => i != itemIndex).Select(item => item.Kind == AnnotationKind.Number ? item.WithText((number++).ToString(System.Globalization.CultureInfo.InvariantCulture)) : item).ToArray(); Commit(next);
    }
    public Annotation PreviewMove(int itemIndex, PixelPoint delta, bool axisLocked = false)
    {
        CheckIndex(itemIndex); var item = states[index][itemIndex];
        var dx = delta.X; var dy = delta.Y;
        if (axisLocked) { if (Math.Abs(dx) >= Math.Abs(dy)) dy = 0; else dx = 0; }
        if (LimitToCanvas)
        {
            dx = Math.Max(-item.Points.Min(p => p.X), Math.Min(Width - item.Points.Max(p => p.X), dx));
            dy = Math.Max(-item.Points.Min(p => p.Y), Math.Min(Height - item.Points.Max(p => p.Y), dy));
        }
        return dx == 0 && dy == 0 ? item : item.WithPoints(item.Points.Select(p => new PixelPoint(p.X + dx, p.Y + dy)));
    }
    public int Move(int itemIndex, PixelPoint delta, bool copy = false, bool axisLocked = false)
    {
        var moved = PreviewMove(itemIndex, delta, axisLocked);
        if (copy) { Add(moved.WithPoints(moved.Points)); return states[index].Length - 1; }
        Replace(itemIndex, moved); return itemIndex;
    }
    private void Validate(Annotation item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        if (LimitToCanvas && item.Points.Any(p => p.X < 0 || p.Y < 0 || p.X > Width || p.Y > Height)) throw new ArgumentOutOfRangeException(nameof(item));
    }
    private void CheckIndex(int itemIndex)
    {
        if (itemIndex < 0 || itemIndex >= states[index].Length) throw new ArgumentOutOfRangeException(nameof(itemIndex));
    }
    public void Clear() { if (states[index].Length > 0) Commit(Array.Empty<Annotation>()); }
    public void Undo() { if (CanUndo) { index--; Revision++; } }
    public void Redo() { if (CanRedo) { index++; Revision++; } }
    private void Commit(Annotation[] items)
    {
        if (CanRedo) states.RemoveRange(index + 1, states.Count - index - 1);
        states.Add(items);
        if (states.Count > 256) states.RemoveAt(0);
        index = states.Count - 1;
        Revision++;
    }
}
