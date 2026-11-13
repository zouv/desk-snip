using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.App.Imaging;
using DeskSnip.Core;
using Microsoft.Win32;

namespace DeskSnip.App;

internal sealed class EditorWindow : Window
{
    private readonly AnnotationCanvas canvas;
    private readonly TextBlock status;
    private readonly Action<BitmapSource> pin;
    private readonly Func<BitmapSource, HistoryEntry> remember;
    private int lastSaved = -1;
    private readonly Button undo;
    private readonly Button redo;
    internal EditorWindow(BitmapSource image, Action<BitmapSource> pin, Func<BitmapSource, HistoryEntry> remember)
    {
        this.pin = pin; this.remember = remember;
        Title = "DeskSnip · 图像编辑"; Width = 1060; Height = 760; MinWidth = 820; MinHeight = 460; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        canvas = new AnnotationCanvas(image); canvas.Changed += UpdateStatus;
        var root = new DockPanel { Margin = new Thickness(16) }; Content = root;
        var header = new DockPanel { Margin = new Thickness(3, 0, 3, 10) }; DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var outputs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(outputs, Dock.Right); header.Children.Add(outputs);
        outputs.Children.Add(Ui.Button("复制 Ctrl+C", Copy)); outputs.Children.Add(Ui.Button("保存 Ctrl+S", Save)); outputs.Children.Add(Ui.Button("钉到桌面 Ctrl+P", Pin, true));
        header.Children.Add(Ui.Text("图像编辑", 22));
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(tools, Dock.Top); root.Children.Add(tools);
        var names = new[] { "矩形", "椭圆", "直线", "箭头", "画笔", "高亮", "文字", "序号", "马赛克", "遮挡" };
        for (var i = 0; i < names.Length; i++)
        {
            var kind = (AnnotationKind)i;
            var button = new RadioButton { Content = names[i], GroupName = "Tool", Margin = new Thickness(6, 8, 6, 8), IsChecked = kind == AnnotationKind.Arrow, VerticalAlignment = VerticalAlignment.Center };
            button.Checked += (_, _) => { canvas.Kind = kind; canvas.Cursor = kind == AnnotationKind.Text ? Cursors.IBeam : Cursors.Cross; }; tools.Children.Add(button);
        }
        var colors = new ComboBox { Width = 85, Margin = new Thickness(10, 3, 3, 3), VerticalContentAlignment = VerticalAlignment.Center, ItemsSource = new[] { "红色", "绿色", "蓝色", "黄色", "白色", "黑色" }, SelectedIndex = 0 };
        var values = new uint[] { 0xffed4f57, 0xff00a886, 0xff318bf1, 0xffffcc38, 0xffffffff, 0xff111111 };
        colors.SelectionChanged += (_, _) => canvas.Argb = values[Math.Max(0, colors.SelectedIndex)]; tools.Children.Add(colors);
        var thickness = new ComboBox { Width = 60, Margin = new Thickness(3), ItemsSource = new[] { "2", "3", "5", "8" }, SelectedIndex = 1, VerticalContentAlignment = VerticalAlignment.Center };
        thickness.SelectionChanged += (_, _) => canvas.Thickness = double.Parse((string)thickness.SelectedItem, CultureInfo.InvariantCulture); tools.Children.Add(thickness);
        var controls = new DockPanel { Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(controls, Dock.Top); root.Children.Add(controls);
        var editButtons = new StackPanel { Orientation = Orientation.Horizontal }; controls.Children.Add(editButtons);
        undo = Ui.Button("撤销 Ctrl+Z", () => { canvas.Document.Undo(); canvas.Refresh(); }); redo = Ui.Button("重做 Ctrl+Y", () => { canvas.Document.Redo(); canvas.Refresh(); });
        editButtons.Children.Add(undo); editButtons.Children.Add(redo); editButtons.Children.Add(Ui.Button("清空标注", () => { canvas.Document.Clear(); canvas.Refresh(); }));
        var zoom = new ComboBox { Width = 85, Margin = new Thickness(10, 4, 3, 4), ItemsSource = new[] { "25%", "50%", "75%", "100%", "150%", "200%" }, SelectedIndex = image.PixelWidth > 1400 || image.PixelHeight > 850 ? 1 : 3, VerticalContentAlignment = VerticalAlignment.Center };
        zoom.SelectionChanged += (_, _) => canvas.LayoutTransform = new ScaleTransform(double.Parse(((string)zoom.SelectedItem).TrimEnd('%'), CultureInfo.InvariantCulture) / 100, double.Parse(((string)zoom.SelectedItem).TrimEnd('%'), CultureInfo.InvariantCulture) / 100);
        editButtons.Children.Add(zoom);
        var scale = zoom.SelectedIndex == 1 ? .5 : 1;
        canvas.LayoutTransform = new ScaleTransform(scale, scale);
        status = Ui.Text("", 12, Ui.Subtle); status.Margin = new Thickness(5, 10, 5, 0); DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromRgb(220, 228, 232)), Padding = new Thickness(18), Content = canvas };
        root.Children.Add(scroll);
        PreviewKeyDown += Keys; UpdateStatus();
    }
    private BitmapSource Output()
    {
        var output = AnnotationRenderer.Flatten(canvas.Image, canvas.Document.Items);
        if (lastSaved != canvas.Revision) { remember(output); lastSaved = canvas.Revision; }
        return output;
    }
    private void Copy()
    {
        try { Clipboard.SetImage(Output()); status.Text = "已复制图片到剪贴板；历史已保存。"; }
        catch (Exception e) { Ui.Error(this, e, "复制图片"); }
    }
    private void Save()
    {
        var dialog = new SaveFileDialog { Filter = "PNG 图片|*.png|JPEG 图片|*.jpg|BMP 图片|*.bmp", FileName = "DeskSnip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try { ImageFiles.Save(Output(), dialog.FileName); status.Text = "图片已保存；历史已更新。"; }
        catch (Exception e) { Ui.Error(this, e, "保存图片"); }
    }
    private void Pin()
    {
        try { pin(Output()); Close(); }
        catch (Exception e) { Ui.Error(this, e, "钉图"); }
    }
    private void UpdateStatus()
    {
        if (status == null) return;
        undo.IsEnabled = canvas.Document.CanUndo; redo.IsEnabled = canvas.Document.CanRedo;
        status.Text = $"{canvas.Image.PixelWidth} × {canvas.Image.PixelHeight} px · {canvas.Document.Items.Count} 个标注 · 显示缩放不改变导出尺寸";
    }
    private void Keys(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        switch (e.Key)
        {
            case Key.C: Copy(); break; case Key.S: Save(); break; case Key.P: Pin(); break;
            case Key.Z: if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) canvas.Document.Redo(); else canvas.Document.Undo(); canvas.Refresh(); break;
            case Key.Y: canvas.Document.Redo(); canvas.Refresh(); break;
            default: return;
        }
        e.Handled = true;
    }
}

internal sealed class AnnotationCanvas : FrameworkElement
{
    private readonly List<PixelPoint> stroke = new List<PixelPoint>();
    private Annotation? preview;
    internal event Action? Changed;
    internal BitmapSource Image { get; }
    internal int Revision { get; private set; }
    internal AnnotationDocument Document { get; }
    internal AnnotationKind Kind { get; set; } = AnnotationKind.Arrow;
    internal uint Argb { get; set; } = 0xffed4f57;
    internal double Thickness { get; set; } = 3;
    internal AnnotationCanvas(BitmapSource image)
    {
        Image = image; Document = new AnnotationDocument(image.PixelWidth, image.PixelHeight);
        Width = image.PixelWidth; Height = image.PixelHeight; Cursor = Cursors.Cross; Focusable = true;
        MouseLeftButtonDown += Down; MouseMove += Move; MouseLeftButtonUp += Up;
        LostMouseCapture += (_, _) => { if (stroke.Count > 0) { stroke.Clear(); preview = null; InvalidateVisual(); } };
    }
    internal void Refresh() { Revision++; InvalidateVisual(); Changed?.Invoke(); }
    protected override void OnRender(DrawingContext dc)
    {
        var items = preview == null ? Document.Items : Document.Items.Concat(new[] { preview });
        AnnotationRenderer.Draw(dc, Image, items);
    }
    private PixelPoint Point(MouseEventArgs e)
    {
        var point = e.GetPosition(this); return new PixelPoint(Math.Max(0, Math.Min(Width - 1, point.X)), Math.Max(0, Math.Min(Height - 1, point.Y)));
    }
    private void Down(object sender, MouseButtonEventArgs e)
    {
        Focus(); var point = Point(e);
        if (Kind == AnnotationKind.Text)
        {
            var prompt = new TextPrompt { Owner = Window.GetWindow(this) };
            if (prompt.ShowDialog() == true && !string.IsNullOrWhiteSpace(prompt.Value)) { Document.Add(new Annotation(Kind, new[] { point }, Argb, Thickness, prompt.Value)); Refresh(); }
            return;
        }
        if (Kind == AnnotationKind.Number)
        {
            Document.Add(new Annotation(Kind, new[] { point }, Argb, Thickness, (Document.Items.Count(a => a.Kind == AnnotationKind.Number) + 1).ToString(CultureInfo.InvariantCulture))); Refresh(); return;
        }
        stroke.Clear(); stroke.Add(point); CaptureMouse(); e.Handled = true;
    }
    private void Move(object sender, MouseEventArgs e)
    {
        if (!IsMouseCaptured || stroke.Count == 0) return;
        var point = Point(e);
        if (Kind == AnnotationKind.Pen || Kind == AnnotationKind.Highlight)
        {
            var last = stroke[stroke.Count - 1];
            if (Math.Abs(last.X - point.X) + Math.Abs(last.Y - point.Y) > .8) stroke.Add(point);
        }
        else { if (stroke.Count > 1) stroke.RemoveAt(1); stroke.Add(point); }
        preview = new Annotation(Kind, stroke, Argb, Thickness); InvalidateVisual();
    }
    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (stroke.Count == 0) return;
        Move(sender, e);
        if (preview != null) Document.Add(preview);
        else if (Kind == AnnotationKind.Pen || Kind == AnnotationKind.Highlight) Document.Add(new Annotation(Kind, stroke, Argb, Thickness));
        stroke.Clear(); preview = null; ReleaseMouseCapture(); Refresh(); e.Handled = true;
    }
}

internal sealed class TextPrompt : Window
{
    private readonly TextBox input = new TextBox { MinHeight = 75, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(8) };
    internal string Value => input.Text;
    internal TextPrompt()
    {
        Title = "添加文字"; Width = 390; Height = 210; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Thickness(18) }; root.Children.Add(input);
        var button = Ui.Button("添加", () => DialogResult = true, true); button.IsDefault = true; root.Children.Add(button); Content = root;
        Loaded += (_, _) => input.Focus();
    }
}
