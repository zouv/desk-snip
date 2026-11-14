using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.App.Recognition;
using DeskSnip.Core;
using Microsoft.Win32;

namespace DeskSnip.App;

internal sealed partial class RecognitionServiceWindow
{
    private TableDocument? tableDocument;
    private readonly DataGrid tableGrid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserSortColumns = false, CanUserReorderColumns = false, SelectionUnit = DataGridSelectionUnit.Cell, SelectionMode = DataGridSelectionMode.Extended, EnableRowVirtualization = true, EnableColumnVirtualization = true, HeadersVisibility = DataGridHeadersVisibility.All };
    private readonly CheckBox tableHeader = new CheckBox { Content = L.Get("排序保留首行"), IsChecked = true, Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center };
    private readonly ListBox formulaList = new ListBox { MinHeight = 55, MaxHeight = 140, Margin = new Thickness(4) };
    private readonly TextBox formulaSource = Editor();
    private readonly ComboBox formulaDelimiter = new ComboBox { Width = 120, ItemsSource = new[] { L.Get("无定界符"), "$...$", "$$...$$", "\\[...\\]" }, SelectedIndex = 2, Margin = new Thickness(4) };
    private readonly Image formulaImage = new Image { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly FlowDocumentScrollViewer markdownPreview = new FlowDocumentScrollViewer { IsToolBarVisible = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock previewStatus = Ui.Text("", 12, Ui.Subtle);
    private int formulaIndex = -1;
    private bool editingHandlersAttached;
    private void AttachEditingHandlers()
    {
        if (editingHandlersAttached) return; editingHandlersAttached = true;
        tableGrid.LoadingRow += (_, e) => e.Row.Header = e.Row.GetIndex() + 1;
        tableGrid.CellEditEnding += (_, e) =>
        {
            if (e.EditAction != DataGridEditAction.Commit || tableDocument == null || !(e.Row.Item is TableRowView row) || !(e.EditingElement is TextBox input)) return;
            try
            {
                tableDocument.SetCell(row.Index, e.Column.DisplayIndex, input.Text); Changed();
                Dispatcher.BeginInvoke(new Action(() => { if (!closed && result?.Operation == "table") RefreshTable(); }));
            }
            catch (ArgumentException error) { e.Cancel = true; status.Text = error.Message; }
        };
        formulaSource.TextChanged += (_, _) => Changed();
        formulaList.SelectionChanged += (_, _) =>
        {
            if (loadingResult || result?.Operation != "formula") return;
            if (formulaIndex >= 0 && formulaIndex < result.Formulas.Count) result.Formulas[formulaIndex] = formulaSource.Text;
            formulaIndex = formulaList.SelectedIndex; loadingResult = true;
            formulaSource.Text = formulaIndex >= 0 ? result.Formulas[formulaIndex] : ""; loadingResult = false; RefreshPreview();
        };
    }
    private void BuildTable()
    {
        AttachEditingHandlers(); var panel = new DockPanel(); var bar = new WrapPanel(); DockPanel.SetDock(bar, Dock.Top); panel.Children.Add(bar);
        void Button(string label, Action action) => bar.Children.Add(Ui.Button(label, () => TableAction(action)));
        Button(L.Get("撤销"), () => tableDocument!.Undo()); Button(L.Get("重做"), () => tableDocument!.Redo());
        Button(L.Get("插入行"), () => tableDocument!.InsertRow(SelectedCell().Row)); Button(L.Get("删除行"), () => tableDocument!.DeleteRow(SelectedCell().Row));
        Button(L.Get("插入列"), () => tableDocument!.InsertColumn(SelectedCell().Column)); Button(L.Get("删除列"), () => tableDocument!.DeleteColumn(SelectedCell().Column));
        Button(L.Get("合并选区"), () => tableDocument!.Merge(SelectedArea())); Button(L.Get("拆分选区"), () => tableDocument!.Split(SelectedArea()));
        Button(L.Get("升序"), () => tableDocument!.Sort(SelectedCell().Column, false, tableHeader.IsChecked == true)); Button(L.Get("降序"), () => tableDocument!.Sort(SelectedCell().Column, true, tableHeader.IsChecked == true)); bar.Children.Add(tableHeader);
        bar.Children.Add(Ui.Button(L.Get("复制选区"), () => TableAction(() => Clipboard.SetText(TableExport.Delimited(tableDocument!.Snapshot, '\t', SelectedArea())))));
        bar.Children.Add(Ui.Button(L.Get("导出选区…"), ExportTableSelection));
        var hint = Ui.Text(L.Get("Ctrl / Shift 选择单元格；合并显示左上格，其余内容在拆分后恢复。插删行列会拆分合并。"), 12, Ui.Subtle); DockPanel.SetDock(hint, Dock.Bottom); panel.Children.Add(hint);
        panel.Children.Add(tableGrid); resultPanel.Content = panel; RefreshTable();
    }
    private sealed class TableRowView
    {
        public int Index { get; set; }
        public string[] Values { get; set; } = Array.Empty<string>();
    }
    private void RefreshTable()
    {
        if (tableDocument == null) return; var value = tableDocument.Snapshot;
        if (tableGrid.Columns.Count != value.Rows[0].Count)
        {
            tableGrid.Columns.Clear();
            for (var index = 0; index < value.Rows[0].Count; index++) tableGrid.Columns.Add(new DataGridTextColumn { Header = (index + 1).ToString(), Binding = new Binding($"Values[{index}]") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.Explicit }, Width = 115, MinWidth = 45 });
        }
        tableGrid.ItemsSource = value.Rows.Select((row, index) => new TableRowView { Index = index, Values = row.Select((cell, column) => value.Cell(index, column)).ToArray() }).ToArray();
    }
    private void CommitTableEdit()
    {
        if (tableDocument == null) return;
        if (!tableGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !tableGrid.CommitEdit(DataGridEditingUnit.Row, true)) throw new ArgumentException(L.Get("请先修正正在编辑的单元格。"));
    }
    private (int Row, int Column) SelectedCell()
    {
        var cell = tableGrid.CurrentCell;
        if (cell.IsValid && cell.Item is TableRowView row) return (row.Index, cell.Column.DisplayIndex);
        return (0, 0);
    }
    private PixelRect SelectedArea()
    {
        var cells = tableGrid.SelectedCells.Where(c => c.IsValid).ToArray(); if (cells.Length == 0) { var current = SelectedCell(); return new PixelRect(current.Column, current.Row, 1, 1); }
        var rows = cells.Select(c => ((TableRowView)c.Item).Index).ToArray(); var columns = cells.Select(c => c.Column.DisplayIndex).ToArray(); var area = new PixelRect(columns.Min(), rows.Min(), columns.Max() - columns.Min() + 1, rows.Max() - rows.Min() + 1);
        if (area.Width * area.Height != cells.Length) throw new ArgumentException(L.Get("请选择连续矩形单元格。")); return area;
    }
    private void TableAction(Action action)
    {
        try { CommitTableEdit(); action(); RefreshTable(); Changed(); }
        catch (Exception error) { status.Text = error.Message; }
    }
    private void ExportTableSelection()
    {
        try
        {
            CommitTableEdit(); var area = SelectedArea(); var value = tableDocument!.Snapshot;
            var dialog = new SaveFileDialog { Filter = L.Get("Excel 表格|*.xlsx|CSV 表格|*.csv"), FileName = "DeskSnip-table-selection", AddExtension = true }; if (dialog.ShowDialog(this) != true) return;
            if (dialog.FilterIndex == 1) TableExport.SaveXlsx(dialog.FileName, value, area); else TableExport.SaveDelimited(dialog.FileName, value, ',', area); status.Text = L.Get("表格选区已导出。");
        }
        catch (Exception error) { Ui.Error(this, error, L.Get("导出表格选区")); }
    }
    private void BuildFormula()
    {
        AttachEditingHandlers(); formulaIndex = result!.Formulas.Count > 0 ? 0 : -1; formulaList.ItemsSource = result.Formulas.Select((_, index) => L.Join(L.Get("公式 "),(index + 1))).ToArray(); formulaList.SelectedIndex = formulaIndex; formulaSource.Text = formulaIndex < 0 ? "" : result.Formulas[formulaIndex];
        var panel = new DockPanel(); var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top); top.Children.Add(formulaList);
        var options = new WrapPanel(); options.Children.Add(Ui.Text(L.Get("复制 / 源码导出定界符"))); options.Children.Add(formulaDelimiter); options.Children.Add(Ui.Text(L.Get("PNG 导出当前选中公式"), 12, Ui.Subtle)); top.Children.Add(options);
        DockPanel.SetDock(previewStatus, Dock.Bottom); panel.Children.Add(previewStatus);
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition()); grid.Children.Add(formulaSource);
        var preview = new ScrollViewer { Content = formulaImage, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.White, Padding = new Thickness(10) }; Grid.SetRow(preview, 1); grid.Children.Add(preview); panel.Children.Add(grid); resultPanel.Content = panel;
    }
    private string FormulaText(RecognitionResponse value)
    {
        string Wrap(string text)
        {
            text = FormulaImages.Source(text); return formulaDelimiter.SelectedIndex == 1 ? "$" + text + "$" : formulaDelimiter.SelectedIndex == 2 ? "$$\n" + text + "\n$$" : formulaDelimiter.SelectedIndex == 3 ? "\\[\n" + text + "\n\\]" : text;
        }
        return string.Join("\n\n", value.Formulas.Select(Wrap));
    }
    private void BuildMarkdown()
    {
        var tabs = new TabControl(); tabs.Items.Add(new TabItem { Header = L.Get("Markdown 编辑"), Content = resultText }); tabs.Items.Add(new TabItem { Header = L.Get("文档预览"), Content = markdownPreview });
        var panel = new DockPanel(); var note = Ui.Text(L.Get("预览支持标题、列表、表格、公式和服务配图；Word 中公式保留 LaTeX 源码。"), 12, Ui.Subtle); DockPanel.SetDock(note, Dock.Bottom); panel.Children.Add(note); panel.Children.Add(tabs); resultPanel.Content = panel;
    }
    private void RefreshPreview()
    {
        if (result?.Operation == "formula")
        {
            try { formulaImage.Source = FormulaImages.Render(formulaSource.Text); previewStatus.Text = L.Get("LaTeX 预览已更新。"); }
            catch (Exception error) { formulaImage.Source = null; previewStatus.Text = L.Join(L.Get("预览未完成，源码保留："),error.Message); }
        }
        else if (result?.Operation == "document")
        {
            try { markdownPreview.Document = MarkdownPreview(resultText.Text, result.Assets); }
            catch (Exception error) { status.Text = L.Join(L.Get("文档预览未完成，源码保留："),error.Message); }
        }
    }
    internal static FlowDocument MarkdownPreview(string markdown, IReadOnlyList<RecognitionAsset> assets)
    {
        var document = new FlowDocument { PagePadding = new Thickness(15), FontSize = 16, Background = Brushes.White, Foreground = Brushes.Black };
        var limited = markdown.Length > 200000 ? markdown.Substring(0, 200000) : markdown;
        var blocks = MarkdownContent.Parse(limited);
        foreach (var block in blocks.Take(2000))
        {
            if (block.Kind == "image")
            {
                var asset = assets.FirstOrDefault(a => a.Name == block.AssetName);
                if (asset != null) { document.Blocks.Add(new BlockUIContainer(new Image { Source = ServiceImages.Decode(asset.Base64), MaxWidth = 450, Stretch = Stretch.Uniform })); continue; }
            }
            if (block.Kind == "formula")
            {
                try { document.Blocks.Add(new BlockUIContainer(new Image { Source = FormulaImages.Render(block.Text), Stretch = Stretch.None })); continue; }
                catch (Exception) { }
            }
            if (block.Kind == "table")
            {
                var table = new Table { CellSpacing = 0 }; var group = new TableRowGroup(); table.RowGroups.Add(group);
                foreach (var row in block.Rows) { var item = new TableRow(); foreach (var cell in row) item.Cells.Add(new TableCell(new Paragraph(new Run(cell))) { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(.5), Padding = new Thickness(5) }); group.Rows.Add(item); } document.Blocks.Add(table); continue;
            }
            document.Blocks.Add(new Paragraph(new Run(block.Kind == "bullet" ? "• " + block.Text : block.Text)) { FontSize = block.Kind == "heading" ? Math.Max(18, 30 - block.Level * 2) : 16, FontWeight = block.Kind == "heading" ? FontWeights.Bold : FontWeights.Normal, FontFamily = new FontFamily(block.Kind == "code" || block.Kind == "formula" ? "Consolas" : "Microsoft YaHei UI"), Margin = new Thickness(0, 4, 0, 8) });
        }
        if (limited.Length != markdown.Length || blocks.Count > 2000) document.Blocks.Add(new Paragraph(new Run(L.Get("长文预览只显示前 20 万字符 / 2000 段，源码及导出仍保留完整内容。"))));
        return document;
    }
}
