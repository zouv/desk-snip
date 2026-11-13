using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DeskSnip.Core;

public enum AnnotationKind { Rectangle, Ellipse, Line, Arrow, Pen, Highlight, Text, Number, Mosaic, Cover }

public sealed class Annotation
{
    public Annotation(AnnotationKind kind, IEnumerable<PixelPoint> points, uint argb, double thickness, string text = "")
    {
        if (!Enum.IsDefined(typeof(AnnotationKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (thickness < 1 || thickness > 80 || double.IsNaN(thickness)) throw new ArgumentOutOfRangeException(nameof(thickness));
        var copy = points?.ToArray() ?? throw new ArgumentNullException(nameof(points));
        if (copy.Length == 0) throw new ArgumentException("An annotation needs a point.", nameof(points));
        Kind = kind; Points = Array.AsReadOnly(copy); Argb = argb; Thickness = thickness; Text = text ?? "";
    }
    public AnnotationKind Kind { get; }
    public ReadOnlyCollection<PixelPoint> Points { get; }
    public uint Argb { get; }
    public double Thickness { get; }
    public string Text { get; }
}

/// <summary>保存不可变快照；新编辑切断 redo，清空也可以撤销。</summary>
public sealed class AnnotationDocument
{
    private readonly List<Annotation[]> states = new List<Annotation[]> { Array.Empty<Annotation>() };
    private int index;
    public AnnotationDocument(int width, int height)
    {
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width; Height = height;
    }
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<Annotation> Items => Array.AsReadOnly(states[index]);
    public bool CanUndo => index > 0;
    public bool CanRedo => index < states.Count - 1;
    public void Add(Annotation item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        if (item.Points.Any(p => p.X < 0 || p.Y < 0 || p.X > Width || p.Y > Height))
            throw new ArgumentOutOfRangeException(nameof(item));
        Commit(states[index].Concat(new[] { item }).ToArray());
    }
    public void Clear() { if (states[index].Length > 0) Commit(Array.Empty<Annotation>()); }
    public void Undo() { if (CanUndo) index--; }
    public void Redo() { if (CanRedo) index++; }
    private void Commit(Annotation[] items)
    {
        if (CanRedo) states.RemoveRange(index + 1, states.Count - index - 1);
        states.Add(items);
        if (states.Count > 256) states.RemoveAt(0);
        index = states.Count - 1;
    }
}
