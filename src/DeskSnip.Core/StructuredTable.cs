using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;

namespace DeskSnip.Core;

[DataContract]
public sealed class TableMerge
{
    [DataMember(Name = "row")] public int Row { get; set; }
    [DataMember(Name = "column")] public int Column { get; set; }
    [DataMember(Name = "rows")] public int Rows { get; set; } = 1;
    [DataMember(Name = "columns")] public int Columns { get; set; } = 1;
    public TableMerge Clone() => (TableMerge)MemberwiseClone();
    public PixelRect Bounds => new PixelRect(Column, Row, Columns, Rows);
}

[DataContract]
public sealed class StructuredTable
{
    [DataMember(Name = "rows")] public List<List<string>> Rows { get; set; } = new List<List<string>>();
    [DataMember(Name = "merges")] public List<TableMerge> Merges { get; set; } = new List<TableMerge>();
    [OnDeserializing] private void Loading(StreamingContext context) { Merges = new List<TableMerge>(); }
    public StructuredTable Clone() => new StructuredTable { Rows = Rows.Select(r => r.ToList()).ToList(), Merges = Merges.Select(m => m.Clone()).ToList() };
    public void Validate()
    {
        if (Rows == null || Rows.Count < 1 || Rows.Count > 2000 || Rows[0] == null || Rows[0].Count < 1 || Rows[0].Count > 200 || (long)Rows.Count * Rows[0].Count > 100000 ||
            Rows.Any(r => r == null || r.Count != Rows[0].Count || r.Any(c => c == null || c.Length > 50000)) || Rows.Sum(r => r.Sum(c => (long)c.Length)) > 2_000_000 || Merges == null || Merges.Count > 1000) throw new ArgumentException(TextCatalog.Get("表格尺寸、单元格或内容大小无效。"));
        foreach (var merge in Merges)
            if (merge == null || merge.Row < 0 || merge.Column < 0 || merge.Rows < 1 || merge.Columns < 1 || (long)merge.Row + merge.Rows > Rows.Count || (long)merge.Column + merge.Columns > Rows[0].Count) throw new ArgumentException(TextCatalog.Get("表格合并区域无效。"));
        for (var i = 0; i < Merges.Count; i++) for (var j = i + 1; j < Merges.Count; j++) if (!Merges[i].Bounds.Intersect(Merges[j].Bounds).IsEmpty) throw new ArgumentException(TextCatalog.Get("表格合并区域重叠。"));
    }
    public string Cell(int row, int column)
    {
        var merge = Merges.FirstOrDefault(m => row >= m.Row && row < m.Row + m.Rows && column >= m.Column && column < m.Column + m.Columns);
        return merge != null && (row != merge.Row || column != merge.Column) ? "" : Rows[row][column];
    }
}

public sealed class TableDocument
{
    private readonly List<StructuredTable> states = new List<StructuredTable>();
    private int index;
    public TableDocument(StructuredTable table) { table.Validate(); states.Add(table.Clone()); }
    public StructuredTable Snapshot => states[index].Clone();
    public bool CanUndo => index > 0;
    public bool CanRedo => index < states.Count - 1;
    public void Undo() { if (CanUndo) index--; }
    public void Redo() { if (CanRedo) index++; }
    public void SetCell(int row, int column, string text)
    {
        var next = Snapshot; CheckCell(next, row, column);
        var merge = next.Merges.FirstOrDefault(m => m.Bounds.Intersect(new PixelRect(column, row, 1, 1)).Width > 0);
        if (merge != null) { row = merge.Row; column = merge.Column; }
        if (next.Rows[row][column] == text) return;
        next.Rows[row][column] = text; Commit(next);
    }
    public void InsertRow(int row)
    {
        var next = Snapshot; if (row < 0 || row > next.Rows.Count) throw new ArgumentOutOfRangeException(nameof(row));
        next.Merges.Clear(); next.Rows.Insert(row, Enumerable.Repeat("", next.Rows[0].Count).ToList()); Commit(next);
    }
    public void DeleteRow(int row)
    {
        var next = Snapshot; CheckCell(next, row, 0); if (next.Rows.Count == 1) throw new ArgumentException(TextCatalog.Get("表格至少保留一行。")); next.Merges.Clear(); next.Rows.RemoveAt(row); Commit(next);
    }
    public void InsertColumn(int column)
    {
        var next = Snapshot; if (column < 0 || column > next.Rows[0].Count) throw new ArgumentOutOfRangeException(nameof(column)); next.Merges.Clear(); foreach (var row in next.Rows) row.Insert(column, ""); Commit(next);
    }
    public void DeleteColumn(int column)
    {
        var next = Snapshot; CheckCell(next, 0, column); if (next.Rows[0].Count == 1) throw new ArgumentException(TextCatalog.Get("表格至少保留一列。")); next.Merges.Clear(); foreach (var row in next.Rows) row.RemoveAt(column); Commit(next);
    }
    public void Merge(PixelRect selection)
    {
        var next = Snapshot; if (selection.IsEmpty || !selection.Intersect(new PixelRect(0, 0, next.Rows[0].Count, next.Rows.Count)).Equals(selection)) throw new ArgumentException(TextCatalog.Get("请选择连续矩形单元格。"));
        next.Merges.RemoveAll(m => !m.Bounds.Intersect(selection).IsEmpty); next.Merges.Add(new TableMerge { Row = selection.Y, Column = selection.X, Rows = selection.Height, Columns = selection.Width }); Commit(next);
    }
    public void Split(PixelRect selection) { var next = Snapshot; next.Merges.RemoveAll(m => !m.Bounds.Intersect(selection).IsEmpty); Commit(next); }
    public void Sort(int column, bool descending, bool header)
    {
        var next = Snapshot; CheckCell(next, 0, column); if (next.Merges.Any(m => m.Rows > 1)) throw new ArgumentException(TextCatalog.Get("跨行合并表格需要先拆分后排序。"));
        var first = header ? 1 : 0; var rows = next.Rows.Skip(first).Select((row, order) => (Row: row, Order: order)).ToList();
        rows.Sort((a, b) =>
        {
            var left = a.Row[column]; var right = b.Row[column]; int result;
            if (double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var lv) && double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var rv)) result = lv.CompareTo(rv);
            else result = string.Compare(left, right, StringComparison.CurrentCulture);
            return result == 0 ? a.Order.CompareTo(b.Order) : descending ? -result : result;
        });
        // 排序移动整行，并同步仅跨列的合并区域。
        var orderMap = rows.Select((row, newIndex) => (Old: row.Order + first, New: newIndex + first)).ToDictionary(p => p.Old, p => p.New);
        foreach (var merge in next.Merges.Where(m => m.Row >= first)) merge.Row = orderMap[merge.Row];
        next.Rows = next.Rows.Take(first).Concat(rows.Select(r => r.Row)).ToList(); Commit(next);
    }
    private static void CheckCell(StructuredTable table, int row, int column) { if (row < 0 || column < 0 || row >= table.Rows.Count || column >= table.Rows[0].Count) throw new ArgumentOutOfRangeException(nameof(row)); }
    private void Commit(StructuredTable next) { next.Validate(); if (CanRedo) states.RemoveRange(index + 1, states.Count - index - 1); states.Add(next); if (states.Count > 100) states.RemoveAt(0); index = states.Count - 1; }
}
