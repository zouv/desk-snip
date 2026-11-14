using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.App.Recognition;
using DeskSnip.Core;
using Microsoft.Win32;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace DeskSnip.App;

internal sealed class OcrWindow : ProductWindow
{
    private readonly BitmapSource source;
    private readonly TextBox text = new TextBox { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 18, Padding = new Thickness(12), IsUndoEnabled = true };
    private readonly Canvas picture = new Canvas();
    private readonly List<Rectangle> boxes = new List<Rectangle>();
    private readonly TextBlock status = Ui.Text(L.Get("准备识别…"), 12, Ui.Subtle);
    private readonly ComboBox language = new ComboBox { Width = 125, Margin = new Thickness(5) };
    private readonly ComboBox layout = new ComboBox { Width = 115, ItemsSource = new[] { L.Get("保留换行"), L.Get("合并换行"), L.Get("智能分段") }, SelectedIndex = 0, Margin = new Thickness(5) };
    private readonly ComboBox punctuation = new ComboBox { Width = 115, ItemsSource = new[] { L.Get("原始标点"), L.Get("英文标点"), L.Get("中文标点") }, SelectedIndex = 0, Margin = new Thickness(5) };
    private readonly CheckBox spaces = new CheckBox { Content = L.Get("移除空格"), Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox closeAll = new CheckBox { Content = L.Get("复制全部后关闭"), IsChecked = true, Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
    private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
    private IReadOnlyList<RecognizedLine> lines = Array.Empty<RecognizedLine>();
    private bool recognizing;
    internal string ResultText => text.Text;
    internal OcrWindow(BitmapSource source, string preferredLanguage = "", bool closeAfterCopy = true, AppSettings? settings = null)
    {
        closeAll.IsChecked = closeAfterCopy;
        if (settings != null) { layout.SelectedIndex = (int)settings.OcrTextLayout; punctuation.SelectedIndex = (int)settings.OcrPunctuation; spaces.IsChecked = settings.OcrRemoveSpaces; }
        this.source = source; Title = L.Get("DeskSnip · 本地文字识别"); Width = 1040; Height = 700; MinWidth = 760; MinHeight = 460; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
        var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(Ui.Text(L.Get("本地文字识别"), 22)); top.Children.Add(language); top.Children.Add(layout); top.Children.Add(punctuation); top.Children.Add(spaces);
        top.Children.Add(Ui.Button(L.Get("重新识别"), () => { _ = Recognize(); }));
        var bottom = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        bottom.Children.Add(Ui.Button(L.Get("复制文本"), Copy, true)); bottom.Children.Add(Ui.Button(L.Get("保存文本…"), Save)); bottom.Children.Add(closeAll); bottom.Children.Add(status);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); columns.ColumnDefinitions.Add(new ColumnDefinition()); root.Children.Add(columns);
        picture.Width = source.PixelWidth; picture.Height = source.PixelHeight; picture.Background = Brushes.White;
        picture.Children.Add(new Image { Source = source, Width = source.PixelWidth, Height = source.PixelHeight, IsHitTestVisible = false });
        var view = new Viewbox { Child = picture, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
        var original = new ScrollViewer { Content = view, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromRgb(229, 235, 239)) }; columns.Children.Add(original);
        Grid.SetColumn(text, 2); columns.Children.Add(text);
        layout.SelectionChanged += (_, _) => Reformat(); punctuation.SelectionChanged += (_, _) => Reformat(); spaces.Click += (_, _) => Reformat();
        text.SelectionChanged += (_, _) => Highlight();
        Loaded += async (_, _) =>
        {
            try { var installed = new[] { L.Get("跟随系统") }.Concat(LocalOcr.Languages).ToArray(); language.ItemsSource = installed; language.SelectedIndex = Math.Max(0, Array.IndexOf(installed, preferredLanguage)); await Recognize(); }
            catch (Exception) { status.Text = L.Get("本地识别引擎不可用，请检查 Windows 语言组件。原图保留。"); }
        };
        Closed += (_, _) => { lifetime.Cancel(); lifetime.Dispose(); };
        language.SelectionChanged += async (_, _) => { if (IsLoaded && language.SelectedIndex >= 0 && lines.Count > 0) await Recognize(); };
    }
    private async Task Recognize()
    {
        if (recognizing || lifetime.IsCancellationRequested) return;
        recognizing = true; language.IsEnabled = false; status.Text = L.Get("正在本机识别…");
        try
        {
            var selected = language.SelectedIndex > 0 ? (string)language.SelectedItem : "";
            var result = await LocalOcr.RecognizeAsync(source, selected, lifetime.Token);
            if (lifetime.IsCancellationRequested) return;
            lines = result; Reformat(); RebuildBoxes(); status.Text = lines.Count == 0 ? L.Get("未检测到文本；原图保留，可切换语言重试。") : L.Get("识别完成；请对照原图校正。");
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException error) { status.Text = error.Message; }
        catch (Exception) { status.Text = L.Get("识别未完成；已有结果和原图保留，可重试。"); }
        finally { recognizing = false; if (!lifetime.IsCancellationRequested) language.IsEnabled = true; }
    }
    private void Reformat()
    {
        if (lines.Count == 0) return;
        var formatted = RecognitionText.Format(lines, (TextLineLayout)layout.SelectedIndex, spaces.IsChecked == true, (PunctuationStyle)punctuation.SelectedIndex);
        if (text.Text == formatted) return;
        text.BeginChange(); try { text.SelectAll(); text.SelectedText = formatted; text.CaretIndex = 0; } finally { text.EndChange(); }
    }
    private void RebuildBoxes()
    {
        foreach (var box in boxes) picture.Children.Remove(box); boxes.Clear();
        foreach (var word in lines.SelectMany(l => l.Words))
        {
            var box = new Rectangle { Width = word.Bounds.Width, Height = word.Bounds.Height, Fill = Brushes.Transparent, StrokeThickness = 1, ToolTip = word.Text, Cursor = Cursors.IBeam, Tag = word };
            Canvas.SetLeft(box, word.Bounds.X); Canvas.SetTop(box, word.Bounds.Y); picture.Children.Add(box); boxes.Add(box);
            box.MouseLeftButtonDown += (_, e) =>
            {
                var index = text.Text.IndexOf(word.Text, StringComparison.Ordinal);
                if (index >= 0) { text.Focus(); text.Select(index, word.Text.Length); }
                else { Clipboard.SetText(word.Text); status.Text = L.Get("已复制原始识别词。右侧保留校对结果。"); }
                e.Handled = true;
            };
        }
        Highlight();
    }
    private void Highlight()
    {
        var selected = RecognitionText.SelectedWords(lines, text.Text, text.SelectionStart, text.SelectionLength);
        foreach (var box in boxes)
        {
            var active = selected.Contains((RecognizedWord)box.Tag);
            box.Fill = active ? new SolidColorBrush(Color.FromArgb(80, 0, 137, 120)) : Brushes.Transparent; box.Stroke = active ? Ui.Accent : Brushes.Transparent;
        }
    }
    private void Copy()
    {
        var all = text.SelectionLength == 0; var value = all ? text.Text : text.SelectedText;
        if (string.IsNullOrEmpty(value)) { status.Text = L.Get("当前没有可以复制的文本。"); return; }
        try { Clipboard.SetText(value); status.Text = L.Get("文本已复制。"); if (all && closeAll.IsChecked == true) Close(); }
        catch (Exception error) { Ui.Error(this, error, L.Get("复制文本")); }
    }
    private void Save()
    {
        var dialog = new SaveFileDialog { Filter = L.Get("UTF-8 文本|*.txt"), FileName = "DeskSnip-text.txt", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try { AtomicFile.Write(dialog.FileName, output => { using (var writer = new StreamWriter(output, new UTF8Encoding(false), 4096, true)) writer.Write(text.Text); }); status.Text = L.Get("文本已保存。"); }
        catch (Exception error) { Ui.Error(this, error, L.Get("保存文本")); }
    }
}
