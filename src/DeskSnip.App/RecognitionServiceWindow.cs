using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskSnip.App.Recognition;
using DeskSnip.Core;
using Microsoft.Win32;

namespace DeskSnip.App;

internal sealed partial class RecognitionServiceWindow : ProductWindow, IBackgroundWindow
{
    private readonly AppSettings settings;
    private readonly Action<BitmapSource, string>? archiveSource;
    private readonly string dataRoot;
    private readonly RecognitionResultStore store;
    private readonly ComboBox operation = new ComboBox { Width = 150, Margin = new Thickness(4) };
    private readonly TextBox sourceLanguage = ShortInput();
    private readonly TextBox targetLanguage = ShortInput();
    private readonly TextBox requestText = Editor();
    private readonly Image originalImage = new Image { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly CheckBox sendImage = new CheckBox { Content = L.Get("发送原图"), Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock status = Ui.Text(L.Get("选择操作，检查原图或文字，再请求服务。"), 12, Ui.Subtle);
    private readonly ContentControl resultPanel = new ContentControl();
    private readonly TextBox resultText = Editor();
    private readonly Button requestButton;
    private readonly Button cancelButton;
    private readonly DispatcherTimer persistTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
    private RecognitionResponse? result;
    private string? imageBase64;
    private CancellationTokenSource? cancellation;
    private Task? request;
    private bool loadingResult;
    private bool closeRequested;
    private bool closed;
    private Task? closeTask;
    internal RecognitionServiceWindow(BitmapSource? image, AppSettings settings, string dataRoot, bool restore = false, string text = "", Action<BitmapSource, string>? archiveSource = null)
    {
        this.settings = settings.Clone(); this.dataRoot = dataRoot; store = new RecognitionResultStore(dataRoot);
        this.archiveSource = archiveSource;
        Title = L.Get("DeskSnip · 联网识别与翻译"); Width = 1200; Height = 800; MinWidth = 930; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Margin = new Thickness(16) }; Content = root;
        var top = new WrapPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        operation.ItemsSource = new[] { L.Get("联网文字识别"), L.Get("图片 / 文本翻译"), L.Get("表格识别"), L.Get("公式识别"), L.Get("文档识别") }; operation.SelectedIndex = 0;
        sourceLanguage.Text = settings.RecognitionSourceLanguage; targetLanguage.Text = settings.RecognitionTargetLanguage;
        top.Children.Add(operation); top.Children.Add(Ui.Text(L.Get("源语言"))); top.Children.Add(sourceLanguage); top.Children.Add(Ui.Text(L.Get("目标语言"))); top.Children.Add(targetLanguage); top.Children.Add(sendImage);
        requestButton = Ui.Button(L.Get("请求服务"), () => { request = RunRequest(); }, true); cancelButton = Ui.Button(L.Get("取消请求"), () => cancellation?.Cancel()); cancelButton.IsEnabled = false;
        top.Children.Add(requestButton); top.Children.Add(cancelButton); top.Children.Add(Ui.Button(L.Get("恢复上次结果"), Restore));
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var outputs = new WrapPanel(); bottom.Children.Add(outputs); outputs.Children.Add(Ui.Button(L.Get("复制结果"), CopyResult)); outputs.Children.Add(Ui.Button(L.Get("导出…"), ExportResult)); outputs.Children.Add(Ui.Button(L.Get("保存本地结果"), () => Persist(true)));
        bottom.Children.Add(status);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); root.Children.Add(grid);
        var inputs = new TabControl(); grid.Children.Add(inputs);
        var imagePanel = new DockPanel(); var zoom = new Slider { Minimum = .1, Maximum = 4, Value = 1, Margin = new Thickness(8) }; DockPanel.SetDock(zoom, Dock.Bottom); imagePanel.Children.Add(zoom);
        zoom.ValueChanged += (_, _) => originalImage.LayoutTransform = new ScaleTransform(zoom.Value, zoom.Value);
        imagePanel.Children.Add(new ScrollViewer { Content = originalImage, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        inputs.Items.Add(new TabItem { Header = L.Get("原图"), Content = imagePanel }); inputs.Items.Add(new TabItem { Header = L.Get("原文 / 请求文字"), Content = requestText });
        Grid.SetColumn(resultPanel, 2); grid.Children.Add(resultPanel); resultPanel.Content = Ui.Text(L.Get("结果会显示在这里，可校对后复制或导出。"), 16);
        requestText.Text = text; if (image == null) inputs.SelectedIndex = 1; SetSource(image == null ? null : ServiceImages.Encode(image));
        resultText.TextChanged += (_, _) => Changed();
        persistTimer.Tick += (_, _) => { persistTimer.Stop(); Persist(false); };
        previewTimer.Tick += (_, _) => { previewTimer.Stop(); RefreshPreview(); };
        Closing += (_, e) => { if (closed) return; e.Cancel = true; _ = CloseSafelyAsync(); };
        Closed += (_, _) => { persistTimer.Stop(); previewTimer.Stop(); cancellation?.Dispose(); };
        if (restore) Restore();
        if (string.IsNullOrEmpty(settings.RecognitionServiceEndpoint)) status.Text = L.Get("请先在“设置 → 联网服务”配置支持 DeskSnip 协议的服务地址。");
    }
    private static TextBox ShortInput() => new TextBox { Width = 70, Margin = new Thickness(4), Padding = new Thickness(5), VerticalContentAlignment = VerticalAlignment.Center };
    internal void SelectOperation(string name) { var index = Array.IndexOf(RecognitionOperations.All, name); if (index < 0) throw new ArgumentException(L.Get("识别操作无效。")); operation.SelectedIndex = index; }
    private static TextBox Editor() => new TextBox { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 16, Padding = new Thickness(10), IsUndoEnabled = true };
    private void SetSource(string? encoded) { imageBase64 = encoded; originalImage.Source = encoded == null ? null : ServiceImages.Decode(encoded); sendImage.IsChecked = encoded != null; sendImage.IsEnabled = encoded != null; }
    private async Task RunRequest()
    {
        if (request != null && !request.IsCompleted || closeRequested) return;
        try
        {
            Persist(false);
            var payload = new RecognitionRequest { Operation = RecognitionOperations.All[operation.SelectedIndex], ImageBase64 = sendImage.IsChecked == true ? imageBase64 : null, Text = string.IsNullOrWhiteSpace(requestText.Text) ? null : requestText.Text, SourceLanguage = sourceLanguage.Text.Trim(), TargetLanguage = targetLanguage.Text.Trim() };
            payload.Validate(); cancellation = new CancellationTokenSource(); SetBusy(true); status.Text = L.Get("正在请求服务…原图和旧结果保留。");
            if (payload.ImageBase64 != null) archiveSource?.Invoke(ServiceImages.Decode(payload.ImageBase64), payload.Operation == "translation" ? "Translation" : "Ocr");
            using (var client = new ServiceClient(settings, dataRoot))
            {
                var next = await client.RecognizeAsync(payload, cancellation.Token); cancellation.Token.ThrowIfCancellationRequested();
                store.SaveSession(new StoredRecognitionSession { Result = next, ImageBase64 = imageBase64, RequestText = payload.Text });
                if (!closeRequested) { ApplyResult(next); status.Text = L.Get("请求成功，可编辑结果；已保存到本机。"); }
            }
        }
        catch (OperationCanceledException) { if (!closeRequested) status.Text = L.Get("请求已取消，原图和旧结果保留。"); }
        catch (Exception error) { if (!closeRequested) status.Text = error.Message; }
        finally { cancellation?.Dispose(); cancellation = null; SetBusy(false); }
    }
    private void SetBusy(bool busy)
    {
        requestButton.IsEnabled = !busy && !closeRequested; cancelButton.IsEnabled = busy; operation.IsEnabled = sourceLanguage.IsEnabled = targetLanguage.IsEnabled = !busy;
        requestText.IsReadOnly = busy; sendImage.IsEnabled = !busy && imageBase64 != null; resultPanel.IsEnabled = !busy;
    }
    internal void ApplyResult(RecognitionResponse value)
    {
        value.Validate(); loadingResult = true; persistTimer.Stop(); previewTimer.Stop(); resultPanel.Content = null;
        foreach (var control in new FrameworkElement[] { resultText, tableGrid, tableHeader, formulaList, formulaSource, formulaDelimiter, formulaImage, previewStatus, markdownPreview }) Detach(control);
        try
        {
            result = value.Clone(); tableDocument = result.Table == null ? null : new TableDocument(result.Table); operation.SelectedIndex = Array.IndexOf(RecognitionOperations.All, result.Operation);
            if (result.Operation == "table") BuildTable();
            else if (result.Operation == "formula") BuildFormula();
            else if (result.Operation == "document") { resultText.Text = result.Markdown; BuildMarkdown(); }
            else { resultText.Text = result.Operation == "translation" ? result.TranslatedText : result.Text; resultPanel.Content = resultText; }
            if (requestText.Text.Length == 0 && result.Text.Length > 0) requestText.Text = result.Text;
        }
        finally { loadingResult = false; RefreshPreview(); }
    }
    private static void Detach(FrameworkElement element)
    {
        var parent = LogicalTreeHelper.GetParent(element);
        if (parent is Panel panel) panel.Children.Remove(element);
        else if (parent is ContentControl content) content.Content = null;
        else if (parent is Decorator decorator) decorator.Child = null;
    }
    internal RecognitionResponse? Snapshot()
    {
        if (result == null) return null; CommitTableEdit(); var value = result.Clone();
        if (value.Operation == "table") value.Table = tableDocument!.Snapshot;
        else if (value.Operation == "document") value.Markdown = resultText.Text;
        else if (value.Operation == "translation") value.TranslatedText = resultText.Text;
        else if (value.Operation == "text") value.Text = resultText.Text;
        else if (formulaIndex >= 0 && formulaIndex < value.Formulas.Count) value.Formulas[formulaIndex] = formulaSource.Text;
        return value;
    }
    private void Changed() { if (loadingResult || closed || result == null) return; persistTimer.Stop(); persistTimer.Start(); previewTimer.Stop(); previewTimer.Start(); }
    private void Persist(bool notify)
    {
        if (result == null) { if (notify) status.Text = L.Get("尚无结果可保存。"); return; }
        try { var value = Snapshot()!; store.SaveSession(new StoredRecognitionSession { Result = value, ImageBase64 = imageBase64, RequestText = requestText.Text }); if (notify) status.Text = L.Get("编辑后的结果和原图已保存到本机。"); }
        catch (Exception error) { status.Text = L.Join(L.Get("本地保存未完成，旧结果保留："),error.Message); }
    }
    private void Restore()
    {
        if (request != null && !request.IsCompleted) return;
        try { var session = store.ReadSession(); if (session == null) { status.Text = L.Get("没有上次结果。"); return; } SetSource(session.ImageBase64); requestText.Text = session.RequestText ?? ""; ApplyResult(session.Result); status.Text = L.Get("已恢复上次结果和原图，可继续编辑，无需联网。"); }
        catch (Exception error) { status.Text = error.Message; }
    }
    private void CopyResult()
    {
        try
        {
            var value = Snapshot(); if (value == null) return;
            Clipboard.SetText(value.Operation == "table" ? TableExport.Delimited(value.Table!, '\t') : value.Operation == "formula" ? FormulaText(value) : value.Operation == "document" ? value.Markdown : value.Operation == "translation" ? value.TranslatedText : value.Text); status.Text = L.Get("结果已复制。");
        }
        catch (Exception error) { Ui.Error(this, error, L.Get("复制识别结果")); }
    }
    private void ExportResult()
    {
        try
        {
            var value = Snapshot(); if (value == null) return;
            var filter = value.Operation == "table" ? L.Get("Excel 表格|*.xlsx|CSV 表格|*.csv|TSV 表格|*.tsv") : value.Operation == "formula" ? L.Get("LaTeX 源码|*.tex|当前公式 PNG|*.png") : value.Operation == "document" ? L.Get("Markdown 与配图|*.zip|Word 文档|*.docx|Markdown 源码|*.md") : L.Get("文字|*.txt");
            var dialog = new SaveFileDialog { Filter = filter, FileName = "DeskSnip-result", AddExtension = true }; if (dialog.ShowDialog(this) != true) return;
            if (value.Operation == "table") { if (dialog.FilterIndex == 1) TableExport.SaveXlsx(dialog.FileName, value.Table!); else TableExport.SaveDelimited(dialog.FileName, value.Table!, dialog.FilterIndex == 2 ? ',' : '\t'); }
            else if (value.Operation == "formula" && dialog.FilterIndex == 2) Imaging.ImageFiles.Save(FormulaImages.Render(formulaSource.Text), dialog.FileName);
            else if (value.Operation == "document" && dialog.FilterIndex == 1) RecognitionExport.SaveMarkdownZip(dialog.FileName, value);
            else if (value.Operation == "document" && dialog.FilterIndex == 2) DocumentExport.SaveDocx(dialog.FileName, value);
            else RecognitionExport.SaveText(dialog.FileName, value.Operation == "formula" ? FormulaText(value) : value.Operation == "document" ? value.Markdown : value.Operation == "translation" ? value.TranslatedText : value.Text);
            Persist(false); status.Text = L.Get("导出完成。");
        }
        catch (Exception error) { Ui.Error(this, error, L.Get("导出识别结果")); }
    }
    public Task CloseSafelyAsync()
    {
        if (closeTask != null) return closeTask; closeRequested = true; cancellation?.Cancel(); closeTask = CloseCore(); return closeTask;
    }
    private async Task CloseCore()
    {
        await Task.Yield(); if (request != null) { try { await request; } catch (Exception) { } }
        persistTimer.Stop(); previewTimer.Stop(); Persist(false); closed = true; Close();
    }
}
