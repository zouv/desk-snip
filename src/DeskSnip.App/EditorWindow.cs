using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskSnip.App.Imaging;
using DeskSnip.App.Windows;
using DeskSnip.Core;
using Microsoft.Win32;

namespace DeskSnip.App;

internal sealed partial class EditorWindow : ProductWindow
{
    private readonly AnnotationCanvas canvas;
    private readonly TextBlock status = Ui.Text("", 12, Ui.Subtle);
    private readonly Action<BitmapSource> pin;
    private readonly Func<BitmapSource, HistoryEntry> remember;
    private readonly Action<BitmapSource>? quickSave;
    private readonly Action<BitmapSource, string>? outputPerformed;
    private readonly int jpegQuality;
    private readonly AppSettings settings;
    private readonly Func<BitmapSource, Window, string?>? saveImage;
    private readonly Action<BitmapSource>? ocrRequested;
    private readonly Action<BitmapSource>? serviceRequested;
    private int lastSaved = -1;
    private readonly Button undo;
    private readonly Button redo;
    private readonly MenuItem remove;
    private readonly MenuItem duplicate;
    private readonly MenuItem editText;
    private readonly List<(Button Button, AnnotationKind?[] Tools)> toolButtons = new List<(Button, AnnotationKind?[])>();
    internal static readonly string[] ToolNames = { L.Get("矩形"), L.Get("椭圆"), L.Get("直线"), L.Get("箭头"), L.Get("画笔"), L.Get("高亮"), L.Get("文字"), L.Get("序号"), L.Get("马赛克"), L.Get("遮挡"), L.Get("圆角矩形"), L.Get("折线"), L.Get("聚光灯"), L.Get("局部放大"), L.Get("模糊"), L.Get("画笔马赛克"), L.Get("画笔遮挡"), L.Get("矩形橡皮擦"), L.Get("画笔橡皮擦"), L.Get("水印") };
    private ComboBox colors = null!;
    private ComboBox thickness = null!;
    private ComboBox fonts = null!;
    private ComboBox sizes = null!;
    private CheckBox fill = null!;
    private CheckBox textBackground = null!;
    private CheckBox bold = null!;
    private CheckBox italic = null!;
    private ComboBox dash = null!;
    private ComboBox arrow = null!;
    private ComboBox effect = null!;
    private ComboBox opacity = null!;
    private bool synchronizingStyle;
    private readonly List<string> colorNames = new List<string> { L.Get("红色"), L.Get("绿色"), L.Get("蓝色"), L.Get("黄色"), L.Get("白色"), L.Get("黑色") };
    private readonly List<uint> colorValues = new List<uint> { 0xffed4f57, 0xff00a886, 0xff318bf1, 0xffffcc38, 0xffffffff, 0xff111111 };
    internal EditorWindow(BitmapSource image, Action<BitmapSource> pin, Func<BitmapSource, HistoryEntry> remember, Action<BitmapSource>? ocr = null, Action<BitmapSource>? quickSave = null, Action<BitmapSource, string>? outputPerformed = null, AppSettings? settings = null, Action<BitmapSource>? service = null, AnnotationKind? initialTool = AnnotationKind.Arrow, Func<BitmapSource, Window, string?>? saveImage = null)
    {
        this.pin = pin; this.remember = remember;
        this.quickSave = quickSave; this.outputPerformed = outputPerformed; jpegQuality = settings?.JpegQuality ?? 95;
        this.settings = settings?.Clone() ?? new AppSettings(); this.saveImage = saveImage; ocrRequested = ocr; serviceRequested = service;
        Title = L.Get("DeskSnip · 图像编辑"); Width = 1060; Height = 760; MinWidth = 820; MinHeight = 460; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        canvas = new AnnotationCanvas(image, this.settings);
        colorNames.Clear(); colorValues.Clear(); foreach (var color in this.settings.MarkColorPalette.Split(',')) { colorNames.Add(color); colorValues.Add(0xff000000u | uint.Parse(color.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); }
        var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
        var header = new DockPanel { Margin = new Thickness(2, 0, 2, 10) }; DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var outputs = new FloatingToolbar { Name = "EditorOutputs", HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(outputs, Dock.Right); header.Children.Add(outputs);
        var title = Ui.Text(L.Get("图像编辑"), 16); title.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(title);
        if (ocr != null) outputs.Items.Children.Add(FloatingTools.Button(ToolGlyph.Ocr, L.Get("文字识别"), () => ocr(Output())));
        if (service != null) outputs.Items.Children.Add(FloatingTools.Button(ToolGlyph.Cloud, L.Get("联网识别"), () => service(Output())));
        outputs.Items.Children.Add(FloatingTools.Button(ToolGlyph.Pin, L.Get("钉到桌面"), Pin, "Ctrl+P"));
        var save = FloatingTools.Menu(); save.Items.Add(FloatingTools.Item(L.Get("保存图片"), Save, "Ctrl+S")); if (quickSave != null) save.Items.Add(FloatingTools.Item(L.Get("快速保存"), () => quickSave(Output()), "Ctrl+Shift+S")); outputs.Items.Children.Add(FloatingTools.MenuButton(ToolGlyph.Save, L.Get("保存"), save));
        outputs.Items.Children.Add(FloatingTools.Divider()); outputs.Items.Children.Add(FloatingTools.Button(ToolGlyph.Copy, L.Get("复制图片"), Copy, "Ctrl+C", true));
        status.Margin = new Thickness(5, 8, 5, 0); DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var toolbar = new FloatingToolbar { Name = "AnnotationTools", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(toolbar, Dock.Bottom); root.Children.Add(toolbar);
        foreach (var group in this.settings.MarkToolbarTools.Split(',')) AddConfiguredToolGroup(toolbar, group);
        toolbar.Items.Children.Add(FloatingTools.Divider());
        var tools = new WrapPanel();
        var styles = new Border { Name = "AnnotationStyles", Background = Brushes.White, BorderBrush = FloatingTools.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(8), Margin = new Thickness(0, 8 + this.settings.MarkQuickSettingsPadding, 0, 0), Child = tools, Visibility = Visibility.Collapsed }; DockPanel.SetDock(styles, Dock.Bottom); root.Children.Add(styles);
        if (Application.Current != null) styles.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        colors = new ComboBox { Width = 85, Margin = new Thickness(10, 3, 3, 3), VerticalContentAlignment = VerticalAlignment.Center, SelectedIndex = 0 };
        foreach (var name in colorNames) colors.Items.Add(name); colors.SelectedIndex = 0;
        colors.SelectionChanged += (_, _) => { if (!synchronizingStyle && colors.SelectedIndex >= 0) canvas.ChangeStyle(argb: colorValues[colors.SelectedIndex]); }; tools.Children.Add(colors);
        thickness = new ComboBox { Width = 60, Margin = new Thickness(3), ItemsSource = new[] { "2", "3", "5", "8", "12", "20" }, SelectedIndex = 1, VerticalContentAlignment = VerticalAlignment.Center };
        thickness.SelectionChanged += (_, _) => { if (!synchronizingStyle && thickness.SelectedItem is string value) canvas.ChangeStyle(thickness: double.Parse(value, CultureInfo.InvariantCulture)); }; tools.Children.Add(thickness);
        fonts = new ComboBox { Width = 150, Margin = new Thickness(10, 3, 3, 3), IsEditable = true, ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(f => f).ToArray(), Text = canvas.FontFamily };
        fonts.LostKeyboardFocus += (_, _) => ApplyFont(); fonts.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyFont(); e.Handled = true; } }; tools.Children.Add(fonts);
        sizes = new ComboBox { Width = 60, Margin = new Thickness(3), IsEditable = true, ItemsSource = new[] { "14", "18", "24", "28", "36", "48", "72" }, Text = canvas.FontSize.ToString(CultureInfo.InvariantCulture) };
        sizes.LostKeyboardFocus += (_, _) => ApplyFont(); sizes.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyFont(); e.Handled = true; } }; tools.Children.Add(sizes);
        fill = new CheckBox { Content = L.Get("填充"), Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center }; tools.Children.Add(fill);
        textBackground = new CheckBox { Content = L.Get("文字底色"), Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center }; tools.Children.Add(textBackground);
        bold = new CheckBox { Content = L.Get("粗体"), Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center }; italic = new CheckBox { Content = L.Get("斜体"), Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center }; tools.Children.Add(bold); tools.Children.Add(italic);
        dash = new ComboBox { Width = 75, ItemsSource = new[] { L.Get("实线"), L.Get("虚线"), L.Get("点线") }, SelectedIndex = 0, Margin = new Thickness(3) }; tools.Children.Add(dash);
        arrow = new ComboBox { Width = 95, ItemsSource = new[] { L.Get("实心箭头"), L.Get("轮廓箭头"), L.Get("双向箭头"), L.Get("尺寸线") }, SelectedIndex = 0, Margin = new Thickness(3) }; tools.Children.Add(arrow);
        effect = new ComboBox { Width = 65, ItemsSource = new[] { 1d, 2d, 3d, 4d, 8d, 16d }, SelectedItem = 2d, Margin = new Thickness(3), ToolTip = L.Get("放大倍率 / 效果强度") }; tools.Children.Add(effect);
        opacity = new ComboBox { Width = 70, ItemsSource = new[] { 1d, .75, .5, .25 }, SelectedItem = 1d, Margin = new Thickness(3), ToolTip = L.Get("标注不透明度") }; tools.Children.Add(opacity);
        void AppearanceChanged() { if (!synchronizingStyle) canvas.ChangeAppearance(canvas.Appearance.WithEditorStyle(fill.IsChecked == true, (AnnotationDash)dash.SelectedIndex, (double)opacity.SelectedItem, (AnnotationArrow)arrow.SelectedIndex, (double)effect.SelectedItem, textBackground.IsChecked == true)); }
        fill.Click += (_, _) => AppearanceChanged(); textBackground.Click += (_, _) => AppearanceChanged(); dash.SelectionChanged += (_, _) => AppearanceChanged(); arrow.SelectionChanged += (_, _) => AppearanceChanged(); effect.SelectionChanged += (_, _) => AppearanceChanged(); opacity.SelectionChanged += (_, _) => AppearanceChanged();
        bold.Click += (_, _) => { if (!synchronizingStyle) canvas.ChangeStyle(bold: bold.IsChecked == true); }; italic.Click += (_, _) => { if (!synchronizingStyle) canvas.ChangeStyle(italic: italic.IsChecked == true); };
        undo = FloatingTools.Button(ToolGlyph.Undo, L.Get("撤销"), () => { canvas.Document.Undo(); canvas.Refresh(); }, "Ctrl+Z"); redo = FloatingTools.Button(ToolGlyph.Redo, L.Get("重做"), () => { canvas.Document.Redo(); canvas.Refresh(); }, "Ctrl+Y"); toolbar.Items.Children.Add(undo); toolbar.Items.Children.Add(redo);
        var styleButton = FloatingTools.Button(ToolGlyph.Styles, L.Get("标注样式"), () => { styles.Visibility = styles.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; }); toolbar.Items.Children.Add(styleButton);
        ConfigureQuickSettings(toolbar);
        foreach (var pair in new[] { (colors, L.Get("颜色")), (thickness, L.Get("线宽（px）")), (fonts, L.Get("字体")), (sizes, L.Get("字号")), (dash, L.Get("线型")), (arrow, L.Get("箭头样式")), (effect, L.Get("效果强度 / 放大倍率")), (opacity, L.Get("不透明度")) }) { pair.Item1.ToolTip = pair.Item2; System.Windows.Automation.AutomationProperties.SetName(pair.Item1, pair.Item2); }
        var more = FloatingTools.Menu(); remove = FloatingTools.Item(L.Get("删除标注"), canvas.DeleteSelected, "Delete"); duplicate = FloatingTools.Item(L.Get("复制标注"), canvas.DuplicateSelected, "Ctrl+D"); editText = FloatingTools.Item(L.Get("修改文字"), EditSelectedText); more.Items.Add(remove); more.Items.Add(duplicate); more.Items.Add(editText);
        more.Items.Add(new Separator()); more.Items.Add(FloatingTools.Item(L.Get("清空标注"), () => { canvas.Document.Clear(); canvas.Refresh(); }, "Ctrl+Shift+Z")); more.Items.Add(FloatingTools.Item(L.Get("本地隐私遮挡"), () => { _ = DetectPrivacy(); }));
        more.Items.Add(FloatingTools.Item(L.Get("识别二维码"), () =>
        {
            try { var results = Recognition.QrRecognition.Read(Output()); if (results.Length == 0) { status.Text = L.Get("未检测到二维码。"); return; } var prompt = new TextPrompt(string.Join(Environment.NewLine, results)) { Title = L.Get("二维码内容（确认后复制）"), Owner = this }; if (prompt.ShowDialog() == true) Clipboard.SetText(prompt.Value); }
            catch (Exception error) { Ui.Error(this, error, L.Get("识别二维码")); }
        })); toolbar.Items.Children.Add(FloatingTools.MenuButton(ToolGlyph.More, L.Get("更多编辑操作"), more));
        var zoom = new ComboBox { Width = 85, Margin = new Thickness(10, 4, 3, 4), ItemsSource = new[] { "25%", "50%", "75%", "100%", "150%", "200%" }, SelectedIndex = image.PixelWidth > 1400 || image.PixelHeight > 850 ? 1 : 3, VerticalContentAlignment = VerticalAlignment.Center };
        zoom.SelectionChanged += (_, _) => canvas.LayoutTransform = new ScaleTransform(double.Parse(((string)zoom.SelectedItem).TrimEnd('%'), CultureInfo.InvariantCulture) / 100, double.Parse(((string)zoom.SelectedItem).TrimEnd('%'), CultureInfo.InvariantCulture) / 100);
        zoom.MinHeight = 28; zoom.VerticalAlignment = VerticalAlignment.Center; zoom.ToolTip = L.Get("显示缩放（导出保持原始尺寸）"); System.Windows.Automation.AutomationProperties.SetName(zoom, L.Get("显示缩放")); toolbar.Items.Children.Add(zoom);
        var scale = zoom.SelectedIndex == 1 ? .5 : 1;
        canvas.LayoutTransform = new ScaleTransform(scale, scale);
        var scroll = new ScrollViewer { Name = "AnnotationViewport", HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromRgb(232, 236, 242)), Padding = new Thickness(18), Content = canvas };
        root.Children.Add(scroll);
        canvas.SetTool(initialTool); canvas.Changed += UpdateStatus; PreviewKeyDown += Keys; UpdateStatus();
        if (this.settings.MarkAutomaticWatermark) canvas.AddAutomaticWatermark();
    }
    private void AddToolGroup(FloatingToolbar toolbar, string group, ToolGlyph glyph, string name)
    {
        var kinds = AnnotationPreferenceRules.GroupTools(group);
        var menu = FloatingTools.Menu();
        Button button = null!;
        foreach (var kind in kinds) { var tool = kind; var item = FloatingTools.Item(ToolNames[(int)tool], () => { AnnotationToolDefaults.Set(group, tool); canvas.SetTool(tool); UpdateGroupLabel(); }); item.Tag = tool; menu.Items.Add(item); }
        menu.Opened += (_, _) => { for (var i = 0; i < kinds.Length; i++) ((MenuItem)menu.Items[i]).IsChecked = canvas.Kind == kinds[i]; };
        button = FloatingTools.SplitMenuButton(glyph, name, () => canvas.SetTool(AnnotationToolDefaults.Get(group, kinds[0])), menu); toolbar.Items.Children.Add(button); toolButtons.Add((button, kinds.Select(kind => (AnnotationKind?)kind).ToArray()));
        void UpdateGroupLabel() { var label = AnnotationToolDefaults.Label(name, AnnotationToolDefaults.Get(group, kinds[0])); AutomationProperties.SetName(button, label); button.ToolTip = label; }
        UpdateGroupLabel();
    }
    private BitmapSource Output()
    {
        var output = AnnotationRenderer.Flatten(canvas.Image, canvas.Document.Items);
        if (lastSaved != canvas.Revision) { remember(output); lastSaved = canvas.Revision; }
        return output;
    }
    private void Copy()
    {
        try { var image = Output(); if (outputPerformed != null) outputPerformed(image, "Copy"); else ClipboardRetry.SetImage(image); status.Text = L.Get("已复制图片到剪贴板；历史已保存。"); }
        catch (System.Runtime.InteropServices.ExternalException) { status.Text = L.Get("剪贴板暂时不可用，请稍后重试。"); }
        catch (Exception e) { Ui.Error(this, e, L.Get("复制图片")); }
    }
    private void Save()
    {
        if (saveImage != null) { try { if (saveImage(Output(), this) != null) status.Text = L.Get("图片已保存；历史已更新。"); } catch (Exception error) { Ui.Error(this, error, L.Get("保存图片")); } return; }
        var dialog = new SaveFileDialog { Filter = L.Get("PNG 图片|*.png|JPEG 图片|*.jpg|BMP 图片|*.bmp|TIFF 图片|*.tiff"), FileName = "DeskSnip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try { var image = Output(); ImageFiles.Save(image, dialog.FileName, jpegQuality, settings.SaveExif); outputPerformed?.Invoke(image, "Save"); status.Text = L.Get("图片已保存；历史已更新。"); }
        catch (Exception e) { Ui.Error(this, e, L.Get("保存图片")); }
    }
    private void Pin()
    {
        try { pin(Output()); Close(); }
        catch (Exception e) { Ui.Error(this, e, L.Get("钉图")); }
    }
    private void UpdateStatus()
    {
        if (status == null) return;
        undo.IsEnabled = canvas.Document.CanUndo; redo.IsEnabled = canvas.Document.CanRedo;
        foreach (var tool in toolButtons) FloatingTools.SetSelected(tool.Button, tool.Tools.Contains(canvas.Kind));
        var item = canvas.Selected;
        remove.IsEnabled = duplicate.IsEnabled = item != null; editText.IsEnabled = item?.Kind == AnnotationKind.Text || item?.Kind == AnnotationKind.Watermark;
        if (colors != null)
        {
            synchronizingStyle = true;
            try
            {
                var argb = item?.Argb ?? canvas.Argb;
                var colorIndex = colorValues.IndexOf(argb);
                if (colorIndex < 0) { colorValues.Add(argb); colors.Items.Add("#" + argb.ToString("X8", CultureInfo.InvariantCulture)); colorIndex = colorValues.Count - 1; }
                colors.SelectedIndex = colorIndex;
                thickness.SelectedItem = (item?.Thickness ?? canvas.Thickness).ToString(CultureInfo.InvariantCulture);
                fonts.Text = item?.FontFamily ?? canvas.FontFamily; sizes.Text = (item?.FontSize ?? canvas.FontSize).ToString(CultureInfo.InvariantCulture);
                var appearance = item?.Appearance ?? canvas.Appearance;
                fill.IsChecked = appearance.Fill; textBackground.IsChecked = appearance.TextBackground; dash.SelectedIndex = (int)appearance.Dash; arrow.SelectedIndex = (int)appearance.Arrow; effect.SelectedItem = appearance.Effect; opacity.SelectedItem = appearance.Opacity;
            }
            finally { synchronizingStyle = false; }
        }
        bold.IsChecked = item?.Bold ?? canvas.Bold; italic.IsChecked = item?.Italic ?? canvas.Italic;
        status.Text = L.Join(L.Join(L.Format("{0} × {1} px · {2} 个标注 · ",canvas.Image.PixelWidth,canvas.Image.PixelHeight,canvas.Document.Items.Count),(item == null ? L.Get("点击同类标注重新编辑 · Alt 拖动复制 · Delete 删除") : L.Get("已选中：拖动移动 · Alt 拖动复制 · Delete 删除 · Esc 取消选择"))),L.Get(" · 显示缩放不改变导出尺寸"));
    }
    private void ApplyFont()
    {
        if (synchronizingStyle || fonts == null || sizes == null) return;
        if (string.IsNullOrWhiteSpace(fonts.Text) || !double.TryParse(sizes.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) || size < 1 || size > 480)
        { status.Text = L.Get("请输入字体与 1–480 的字号。"); return; }
        try { canvas.ChangeStyle(fontFamily: fonts.Text, fontSize: size); }
        catch (ArgumentException) { status.Text = L.Get("字体或字号无效。"); }
    }
    private void EditSelectedText() => canvas.EditSelectedText();
    private async System.Threading.Tasks.Task DetectPrivacy()
    {
        status.Text = L.Get("正在本机检测手机号与邮箱…");
        try
        {
            var lines = await Recognition.LocalOcr.RecognizeAsync(canvas.Image, "", System.Threading.CancellationToken.None); var regions = PrivacyDetector.Find(lines);
            if (!settings.MarkAutoPrivacyProcessing && regions.Count > 0 && MessageBox.Show(this, L.Format("检测到 {0} 处隐私区域，添加马赛克？",regions.Count), L.Get("隐私检测"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) { status.Text = L.Get("已保留检测前的图像。"); return; }
            foreach (var area in regions)
            {
                var left = Math.Max(0, area.X - 3); var top = Math.Max(0, area.Y - 3); var right = Math.Min(canvas.Image.PixelWidth, area.Right + 3); var bottom = Math.Min(canvas.Image.PixelHeight, area.Bottom + 3);
                canvas.Document.Add(new Annotation(AnnotationKind.Mosaic, new[] { new PixelPoint(left, top), new PixelPoint(right, bottom) }, 0xff000000, 3));
            }
            canvas.Refresh(); status.Text = L.Format("遮挡 {0} 处手机号/邮箱；请检查结果，可撤销或补画遮挡。",regions.Count);
        }
        catch (Exception) { status.Text = L.Get("隐私检测未完成，原图和标注保留；可以手动遮挡。"); }
    }
    private void Keys(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox) return;
        if (HandleConfiguredShortcut(e)) return;
        if (e.Key == Key.Delete) { canvas.DeleteSelected(); e.Handled = true; return; }
        if (e.Key == Key.Escape) { canvas.ClearSelection(); e.Handled = true; return; }
        if (e.Key == Key.Enter && Keyboard.FocusedElement is Button) return;
        if (e.Key == Key.Enter && (canvas.Selected?.Kind == AnnotationKind.Text || canvas.Selected?.Kind == AnnotationKind.Watermark)) { EditSelectedText(); e.Handled = true; return; }
        var dx = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0;
        var dy = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
        if ((dx != 0 || dy != 0) && canvas.Selected != null) { canvas.NudgeSelected(dx, dy); e.Handled = true; return; }
        if (settings.BuiltinShortcuts != null || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        switch (e.Key)
        {
            case Key.C: Copy(); break; case Key.S: if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && quickSave != null) quickSave(Output()); else Save(); break; case Key.P: Pin(); break;
            case Key.Z: if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) canvas.Document.Clear(); else canvas.Document.Undo(); canvas.Refresh(); break;
            case Key.Y: canvas.Document.Redo(); canvas.Refresh(); break;
            case Key.D: canvas.DuplicateSelected(); break;
            default: return;
        }
        e.Handled = true;
    }
}

internal sealed partial class AnnotationCanvas : FrameworkElement
{
    private readonly List<PixelPoint> stroke = new List<PixelPoint>();
    private Annotation? preview;
    private int selectedIndex = -1;
    private Annotation? selectedReference;
    private PixelPoint dragStart;
    private Rect dragBounds;
    private bool draggingObject;
    private bool copyingObject;
    internal event Action? Changed;
    internal BitmapSource Image { get; }
    internal int Revision => Document.Revision;
    internal string PersistedStyles => preferences.MarkToolStyles;
    internal AnnotationDocument Document { get; }
    internal AnnotationKind? Kind { get; private set; } = AnnotationKind.Arrow;
    internal int SelectedIndex => selectedIndex;
    internal Annotation? Selected => selectedIndex >= 0 && selectedIndex < Document.Items.Count && ReferenceEquals(Document.Items[selectedIndex], selectedReference) ? selectedReference : null;
    internal uint Argb { get; set; } = 0xffed4f57;
    internal double Thickness { get; set; } = 3;
    internal string FontFamily { get; private set; } = "Microsoft YaHei UI";
    internal double FontSize { get; private set; } = 18;
    internal AnnotationAppearance Appearance { get; private set; } = new AnnotationAppearance();
    internal bool Bold { get; private set; }
    internal bool Italic { get; private set; }
    internal bool HideEditorChrome { get; set; }
    internal bool ExcludeTextPopupFromCapture { get; set; }
    private bool pathBuilding;
    private Popup? textPopup;
    private TextBox? textEditor;
    private Annotation? editingText;
    private int editingTextIndex = -1;
    private AnnotationKind textKind = AnnotationKind.Text;
    private PixelPoint textPoint;
    private bool closingText;
    private bool textFocused;
    private DateTime lastTextCommit;
    private bool draggingHandle;
    private int handleIndex = -1;
    private Rect handleBounds;
    private readonly Dictionary<string, AnnotationStyle> savedStyles = new Dictionary<string, AnnotationStyle>(StringComparer.Ordinal);
    private uint? sharedArgb;
    internal AnnotationCanvas(BitmapSource image, AppSettings? settings = null)
    {
        preferences = settings?.Clone() ?? new AppSettings(); secondaryEditing = preferences.MarkSecondaryEditing;
        Image = image; Document = new AnnotationDocument(image.PixelWidth, image.PixelHeight, preferences.MarkLimitToSelection);
        LoadSavedStyles();
        var initial = SavedStyle(AnnotationKind.Arrow);
        if (initial != null) ApplyStyle(initial, false); else Appearance = BuildAppearance(null);
        Width = image.PixelWidth; Height = image.PixelHeight; Cursor = Cursors.Cross; Focusable = true;
        MouseLeftButtonDown += Down; MouseMove += Move; MouseLeftButtonUp += Up;
        LostMouseCapture += (_, _) => CancelInteraction();
        MouseWheel += (_, e) => { if (preferences.MarkDisableWheel || !Kind.HasValue) return; ChangeStyle(thickness: Math.Max(1, Math.Min(80, Thickness + (e.Delta > 0 ? 1 : -1)))); e.Handled = true; };
        Unloaded += (_, _) => CloseTextEditor();
        PreviewKeyDown += (_, e) => { if (!pathBuilding) return; if (e.Key == Key.Enter) { FinishPath(); e.Handled = true; } else if (e.Key == Key.Escape) { CancelInteraction(); e.Handled = true; } };
    }
    internal void Refresh()
    {
        if (Selected == null) { selectedIndex = -1; selectedReference = null; }
        CancelInteraction(); InvalidateVisual(); Changed?.Invoke();
    }
    internal void SetTool(AnnotationKind? kind)
    {
        Kind = kind;
        if (kind.HasValue)
        {
            var style = SavedStyle(kind.Value);
            if (style != null) ApplyStyle(style, preferences.MarkSharedColor);
            LastTool = kind.Value;
        }
        Cursor = kind == null ? Cursors.Arrow : kind == AnnotationKind.Text ? Cursors.IBeam : preferences.MarkPencilCursor == "Arrow" && IsBrush(kind) ? Cursors.Arrow : Cursors.Cross;
        ClearSelection();
        ToolChanged?.Invoke();
    }
    private void LoadSavedStyles()
    {
        try
        {
            var set = AnnotationStyleRules.Parse(preferences.MarkToolStyles ?? "");
            foreach (var pair in set.Styles) savedStyles[pair.Key] = pair.Value;
            sharedArgb = set.SharedArgb;
        }
        catch (ArgumentException) { savedStyles.Clear(); sharedArgb = null; }
    }
    private AnnotationStyle? SavedStyle(AnnotationKind kind)
        => savedStyles.TryGetValue(AnnotationStyleRules.GroupOf(kind), out var style) ? style : null;
    private AnnotationAppearance BuildAppearance(AnnotationStyle? style)
    {
        var lineJoin = (AnnotationLineJoin)Enum.Parse(typeof(AnnotationLineJoin), preferences.MarkPolylineJoin);
        var lineCap = (AnnotationLineCap)Enum.Parse(typeof(AnnotationLineCap), preferences.MarkPolylineCap);
        var wrap = (AnnotationTextWrap)Enum.Parse(typeof(AnnotationTextWrap), preferences.MarkTextWrap);
        var serialScale = AnnotationPreferenceRules.SerialTextScale(preferences.MarkSerialTextSize);
        var serialBackground = preferences.MarkSerialDefaultColor ? (uint?)null : 0xff000000u | uint.Parse(preferences.MarkSerialBackground.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (style == null) return new AnnotationAppearance(lineJoin: lineJoin, lineCap: lineCap, smoothPencil: preferences.MarkSmoothPencil, textWrap: wrap, serialTextScale: serialScale, serialBackground: serialBackground);
        return new AnnotationAppearance(style.Fill, style.Dash, style.Opacity, style.Arrow, style.Effect, style.TextBackground, lineJoin, lineCap, preferences.MarkSmoothPencil, wrap, serialScale, serialBackground, style.CornerRadius);
    }
    private void ApplyStyle(AnnotationStyle style, bool preserveColor)
    {
        if (preferences.MarkSharedColor && sharedArgb.HasValue) Argb = sharedArgb.Value;
        else if (!preserveColor) Argb = style.Argb;
        Thickness = style.Thickness; FontFamily = style.FontFamily; FontSize = style.FontSize; Bold = style.Bold; Italic = style.Italic;
        Appearance = BuildAppearance(style);
    }
    private void SaveCurrentStyle()
    {
        var kind = Selected?.Kind ?? Kind ?? LastTool;
        var style = new AnnotationStyle(Argb, Thickness, Appearance.Fill, Appearance.Dash, Appearance.Arrow, Appearance.Opacity, Appearance.Effect, Appearance.TextBackground,
            Appearance.CornerRadius, FontFamily, FontSize, Bold, Italic);
        var group = AnnotationStyleRules.GroupOf(kind);
        var changed = !savedStyles.TryGetValue(group, out var current) || !current.SameAs(style);
        if (sharedArgb != style.Argb) { sharedArgb = style.Argb; changed = true; }
        if (!changed) return;
        savedStyles[group] = style;
        preferences.MarkToolStyles = AnnotationStyleRules.Serialize(savedStyles, sharedArgb);
        AnnotationStylePersistence.Publish(preferences.MarkToolStyles);
    }
    internal bool SelectAt(PixelPoint point)
    {
        if (!secondaryEditing && Kind.HasValue || (Kind == AnnotationKind.Eraser || Kind == AnnotationKind.EraserPen) && !preferences.MarkEraserSecondaryEditing) { Select(-1); return false; }
        var itemIndex = -1;
        if (!Kind.HasValue) itemIndex = AnnotationRenderer.HitTest(Image, Document.Items, point);
        else foreach (var candidate in SelectableKinds(Kind.Value)) { var hit = AnnotationRenderer.HitTest(Image, Document.Items, point, candidate); if (hit > itemIndex) itemIndex = hit; }
        Select(itemIndex); return itemIndex >= 0;
    }
    private static AnnotationKind[] SelectableKinds(AnnotationKind kind)
    {
        switch (kind)
        {
            case AnnotationKind.Rectangle: case AnnotationKind.Ellipse: case AnnotationKind.RoundedRectangle: return new[] { AnnotationKind.Rectangle, AnnotationKind.Ellipse, AnnotationKind.RoundedRectangle };
            case AnnotationKind.Arrow: case AnnotationKind.Line: case AnnotationKind.Polyline: return new[] { AnnotationKind.Arrow, AnnotationKind.Line, AnnotationKind.Polyline };
            case AnnotationKind.Pen: case AnnotationKind.Highlight: return new[] { AnnotationKind.Pen, AnnotationKind.Highlight };
            case AnnotationKind.Text: case AnnotationKind.Watermark: return new[] { AnnotationKind.Text, AnnotationKind.Watermark };
            case AnnotationKind.Mosaic: case AnnotationKind.Blur: case AnnotationKind.MosaicPen: return new[] { AnnotationKind.Mosaic, AnnotationKind.Blur, AnnotationKind.MosaicPen };
            case AnnotationKind.Cover: case AnnotationKind.CoverPen: return new[] { AnnotationKind.Cover, AnnotationKind.CoverPen };
            case AnnotationKind.Eraser: case AnnotationKind.EraserPen: return new[] { AnnotationKind.Eraser, AnnotationKind.EraserPen };
            case AnnotationKind.Spotlight: case AnnotationKind.Magnifier: return new[] { AnnotationKind.Spotlight, AnnotationKind.Magnifier };
            default: return new[] { kind };
        }
    }
    internal void ChangeCorner(bool rounded)
    {
        if (Selected == null || (Selected.Kind != AnnotationKind.Rectangle && Selected.Kind != AnnotationKind.RoundedRectangle)) return;
        var kind = rounded ? AnnotationKind.RoundedRectangle : AnnotationKind.Rectangle;
        var appearance = rounded ? Selected.Appearance : Selected.Appearance.WithEditorStyle(Selected.Appearance.Fill, Selected.Appearance.Dash, Selected.Appearance.Opacity, Selected.Appearance.Arrow, Selected.Appearance.Effect, Selected.Appearance.TextBackground, 0);
        if (Selected.Kind == kind && ReferenceEquals(appearance, Selected.Appearance)) return;
        var item = Selected; var current = selectedIndex;
        Document.Replace(current, new Annotation(kind, item.Points, item.Argb, item.Thickness, item.Text, item.FontFamily, item.FontSize, appearance, item.Bold, item.Italic));
        Select(current);
        SaveCurrentStyle();
    }
    private void Select(int itemIndex)
    {
        selectedIndex = itemIndex; selectedReference = itemIndex >= 0 ? Document.Items[itemIndex] : null;
        if (selectedReference != null)
        { Argb = selectedReference.Argb; Thickness = selectedReference.Thickness; FontFamily = selectedReference.FontFamily; FontSize = selectedReference.FontSize; Appearance = selectedReference.Appearance; Bold = selectedReference.Bold; Italic = selectedReference.Italic; }
        InvalidateVisual(); Changed?.Invoke();
    }
    internal void ClearSelection()
    {
        CancelInteraction(); selectedIndex = -1; selectedReference = null; InvalidateVisual(); Changed?.Invoke();
    }
    /// <summary>右侧标记列表的点击入口：直接选中指定标记并取得焦点。</summary>
    internal void SelectIndex(int index)
    {
        if (index < 0 || index >= Document.Items.Count) return;
        Select(index); Focus();
    }
    internal void DeleteSelected()
    {
        if (Selected == null) return;
        if (Selected.Kind == AnnotationKind.Number && preferences.MarkRenumberAfterDelete && !preferences.MarkGlobalSerial) Document.RemoveAndRenumber(selectedIndex, preferences.MarkSerialStartAtZero); else Document.Remove(selectedIndex); ClearSelection();
    }
    internal void DuplicateSelected()
    {
        if (Selected == null) return;
        var bounds = AnnotationRenderer.Bounds(Image, Selected);
        var dx = bounds.Right + 12 <= Width ? 12 : bounds.Left >= 12 ? -12 : 0;
        var dy = bounds.Bottom + 12 <= Height ? 12 : bounds.Top >= 12 ? -12 : 0;
        Select(Document.Move(selectedIndex, new PixelPoint(dx, dy), copy: true));
    }
    internal void NudgeSelected(int dx, int dy)
    {
        if (Selected == null) return;
        var delta = ClampObjectDelta(new PixelPoint(dx, dy), AnnotationRenderer.Bounds(Image, Selected));
        Document.Move(selectedIndex, delta); Select(selectedIndex);
    }
    internal void ChangeStyle(uint? argb = null, double? thickness = null, string? fontFamily = null, double? fontSize = null, bool? bold = null, bool? italic = null, AnnotationAppearance? appearance = null)
    {
        var item = Selected;
        var nextArgb = argb ?? Argb; var nextThickness = thickness ?? Thickness;
        var nextFamily = fontFamily ?? FontFamily; var nextSize = fontSize ?? FontSize;
        var nextBold = bold ?? Bold; var nextItalic = italic ?? Italic; var nextAppearance = appearance ?? Appearance;
        if (nextArgb == Argb && nextThickness == Thickness && nextFamily == FontFamily && nextSize == FontSize && nextBold == Bold && nextItalic == Italic && SameAppearance(nextAppearance, Appearance)) return;
        // 通过不可变模型先验证，失败时当前样式与文档都保持原值。
        var candidate = new Annotation(item?.Kind ?? Kind ?? AnnotationKind.Text, (IEnumerable<PixelPoint>?)item?.Points ?? new[] { new PixelPoint(0, 0) },
            nextArgb, nextThickness, item?.Text ?? "", nextFamily, nextSize, nextAppearance, nextBold, nextItalic);
        Argb = nextArgb; Thickness = nextThickness; FontFamily = nextFamily; FontSize = nextSize; Bold = nextBold; Italic = nextItalic; Appearance = nextAppearance;
        if (item != null) { Document.Replace(selectedIndex, candidate); Select(selectedIndex); }
        else { InvalidateVisual(); Changed?.Invoke(); }
        SaveCurrentStyle();
    }
    internal void ChangeAppearance(AnnotationAppearance value)
    {
        if (SameAppearance(Appearance, value)) return;
        Appearance = value;
        if (Selected != null) { Document.Replace(selectedIndex, Selected.WithStyle(Selected.Argb, Selected.Thickness, appearance: value)); Select(selectedIndex); }
        else { InvalidateVisual(); Changed?.Invoke(); }
        SaveCurrentStyle();
    }
    private static bool SameAppearance(AnnotationAppearance left, AnnotationAppearance right)
        => left.Fill == right.Fill && left.Dash == right.Dash && left.Opacity == right.Opacity && left.Arrow == right.Arrow && left.Effect == right.Effect &&
           left.TextBackground == right.TextBackground && left.LineJoin == right.LineJoin && left.LineCap == right.LineCap && left.SmoothPencil == right.SmoothPencil &&
           left.TextWrap == right.TextWrap && left.SerialTextScale == right.SerialTextScale && left.SerialBackground == right.SerialBackground && left.CornerRadius == right.CornerRadius;
    internal void ReplaceSelectedText(string text)
    {
        if (Selected?.Kind != AnnotationKind.Text) return;
        if (string.IsNullOrWhiteSpace(text)) { DeleteSelected(); return; }
        if (text == Selected.Text) return;
        Document.Replace(selectedIndex, Selected.WithText(text)); Select(selectedIndex);
    }
    internal void EditSelectedText()
    {
        if (Selected == null || (Selected.Kind != AnnotationKind.Text && Selected.Kind != AnnotationKind.Watermark)) return;
        editingText = Selected; editingTextIndex = selectedIndex; textKind = Selected.Kind; textPoint = Selected.Points[0]; OpenTextEditor(Selected.Text);
    }
    private void OpenTextEditor(string value)
    {
        CloseTextPopup();
        var existing = editingText;
        var color = existing?.Argb ?? Argb; var family = existing?.FontFamily ?? FontFamily; var size = existing?.FontSize ?? FontSize;
        var useBold = existing?.Bold ?? Bold; var useItalic = existing?.Italic ?? Italic;
        var scale = LayoutTransform is ScaleTransform transform && transform.ScaleX > 0 ? transform.ScaleX : 1;
        var brush = new SolidColorBrush(Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color));
        var input = new TextBox { Text = value, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Padding = new Thickness(2), Background = Brushes.Transparent, Foreground = brush, CaretBrush = brush, FontFamily = new FontFamily(family), FontSize = Math.Max(8, size * scale), FontWeight = useBold ? FontWeights.Bold : FontWeights.Normal, FontStyle = useItalic ? FontStyles.Italic : FontStyles.Normal, MinWidth = 120, MaxWidth = Math.Max(120, (Width - textPoint.X) * scale), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        textEditor = input;
        var border = new Border { Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)), BorderBrush = Ui.Accent, BorderThickness = new Thickness(1), Padding = new Thickness(2), Child = input };
        var popup = new Popup { PlacementTarget = this, Placement = PlacementMode.Absolute, AllowsTransparency = true, StaysOpen = true, Child = border };
        var screen = PointToScreen(new Point(textPoint.X, textPoint.Y)); popup.HorizontalOffset = screen.X; popup.VerticalOffset = screen.Y; textPopup = popup;
        input.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { CancelTextEditor(); e.Handled = true; } else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { CommitTextEditor(); e.Handled = true; } };
        input.PreviewMouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Left) TextEditorPointerDown?.Invoke(); };
        input.GotKeyboardFocus += (_, _) => textFocused = true;
        input.LostKeyboardFocus += (_, _) => { if (textFocused) { lastTextCommit = DateTime.UtcNow; CommitTextEditor(); } };
        popup.IsOpen = true;
        if (ExcludeTextPopupFromCapture && PresentationSource.FromVisual(input) is HwndSource source) Native.SetWindowDisplayAffinity(source.Handle, 0x11);
        Dispatcher.BeginInvoke(new Action(() => { if (textEditor == input) { input.Focus(); input.CaretIndex = input.Text.Length; } }), DispatcherPriority.Input);
    }
    private void CommitTextEditor()
    {
        if (closingText || textEditor == null) return;
        var value = textEditor.Text; var existing = editingText; var index = editingTextIndex; var kind = textKind; var point = textPoint;
        if (existing == null && kind == AnnotationKind.Watermark) value = value.Replace("{time}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        CloseTextPopup();
        if (string.IsNullOrWhiteSpace(value))
        {
            if (existing != null && index >= 0 && index < Document.Items.Count && ReferenceEquals(Document.Items[index], existing)) Document.Remove(index);
            else { InvalidateVisual(); Changed?.Invoke(); }
            editingText = null; editingTextIndex = -1; return;
        }
        if (existing != null && index >= 0 && index < Document.Items.Count && ReferenceEquals(Document.Items[index], existing)) { Document.Replace(index, existing.WithText(value)); Select(index); }
        else { Document.Add(new Annotation(kind, new[] { point }, Argb, Thickness, value, FontFamily, FontSize, Appearance, Bold, Italic)); Select(Document.Items.Count - 1); }
        editingText = null; editingTextIndex = -1; Refresh();
    }
    /// <summary>复制/关闭前提交尚未结束的文字输入，避免正在编辑的内容丢失。</summary>
    internal void CommitPendingText() { if (textEditor != null) CommitTextEditor(); }
    private void CancelTextEditor() { CloseTextPopup(); editingText = null; editingTextIndex = -1; }
    private void CloseTextPopup()
    {
        closingText = true;
        if (textPopup != null) { textPopup.IsOpen = false; textPopup = null; }
        textEditor = null; closingText = false; textFocused = false;
    }
    private void CloseTextEditor() { CloseTextPopup(); editingText = null; editingTextIndex = -1; }
    internal void BeginObjectDrag(PixelPoint point, bool copy)
    {
        if (Selected == null) return;
        CancelInteraction(); dragStart = point; dragBounds = AnnotationRenderer.Bounds(Image, Selected);
        draggingObject = true; copyingObject = copy;
    }
    internal void UpdateObjectDrag(PixelPoint point, bool axisLocked)
    {
        if (!draggingObject || Selected == null) return;
        var delta = new PixelPoint(point.X - dragStart.X, point.Y - dragStart.Y);
        if (axisLocked) delta = Math.Abs(delta.X) >= Math.Abs(delta.Y) ? new PixelPoint(delta.X, 0) : new PixelPoint(0, delta.Y);
        preview = Document.PreviewMove(selectedIndex, ClampObjectDelta(delta, dragBounds)); InvalidateVisual();
    }
    internal void EndObjectDrag()
    {
        if (draggingObject && Selected != null && preview != null && !ReferenceEquals(preview, Selected))
        {
            if (copyingObject) { Document.Add(preview); selectedIndex = Document.Items.Count - 1; }
            else Document.Replace(selectedIndex, preview);
            selectedReference = Document.Items[selectedIndex];
        }
        CancelInteraction(); InvalidateVisual(); Changed?.Invoke();
    }
    private PixelPoint ClampObjectDelta(PixelPoint delta, Rect visualBounds)
    {
        if (!preferences.MarkLimitToSelection || visualBounds.IsEmpty) return delta;
        return new PixelPoint(Math.Max(-Math.Max(0, visualBounds.Left), Math.Min(Width - Math.Min(Width, visualBounds.Right), delta.X)),
            Math.Max(-Math.Max(0, visualBounds.Top), Math.Min(Height - Math.Min(Height, visualBounds.Bottom), delta.Y)));
    }
    private void CancelInteraction()
    {
        stroke.Clear(); preview = null; draggingObject = false; copyingObject = false; pathBuilding = false; draggingHandle = false; handleIndex = -1;
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
    }
    internal bool TryBeginHandleDrag(PixelPoint point)
    {
        if (Selected == null) return false;
        var scale = LayoutTransform is ScaleTransform transform && transform.ScaleX > 0 ? transform.ScaleX : 1;
        var tolerance = 9 / scale;
        CancelInteraction();
        if (Selected.Kind == AnnotationKind.Line || Selected.Kind == AnnotationKind.Arrow)
        {
            var first = Selected.Points[0]; var last = Selected.Points[Selected.Points.Count - 1];
            if (Distance(point, first) <= tolerance) handleIndex = 0;
            else if (Distance(point, last) <= tolerance) handleIndex = Selected.Points.Count - 1;
            else return false;
            draggingHandle = true; return true;
        }
        if (!IsResizable(Selected.Kind)) return false;
        var bounds = AnnotationRenderer.Bounds(Image, Selected);
        var corners = new[] { bounds.TopLeft, bounds.BottomRight, bounds.BottomLeft, bounds.TopRight };
        for (var i = 0; i < corners.Length; i++) if (Distance(point, corners[i]) <= tolerance) { handleIndex = i; handleBounds = bounds; draggingHandle = true; return true; }
        return false;
    }
    private static bool IsResizable(AnnotationKind kind)
        => kind == AnnotationKind.Rectangle || kind == AnnotationKind.Ellipse || kind == AnnotationKind.RoundedRectangle || kind == AnnotationKind.Cover || kind == AnnotationKind.Mosaic || kind == AnnotationKind.Blur || kind == AnnotationKind.Spotlight || kind == AnnotationKind.Eraser || kind == AnnotationKind.Magnifier;
    private static double Distance(PixelPoint point, PixelPoint target) => Math.Sqrt((point.X - target.X) * (point.X - target.X) + (point.Y - target.Y) * (point.Y - target.Y));
    private static double Distance(PixelPoint point, Point target) => Math.Sqrt((point.X - target.X) * (point.X - target.X) + (point.Y - target.Y) * (point.Y - target.Y));
    internal void UpdateHandleDrag(PixelPoint point)
    {
        if (Selected == null) return;
        if (Selected.Kind == AnnotationKind.Line || Selected.Kind == AnnotationKind.Arrow)
        {
            if (handleIndex < 0 || handleIndex >= Selected.Points.Count) return;
            var points = Selected.Points.ToArray(); points[handleIndex] = point; preview = Selected.WithPoints(points); return;
        }
        var anchor = handleIndex switch { 0 => handleBounds.BottomRight, 1 => handleBounds.TopLeft, 2 => handleBounds.TopRight, _ => handleBounds.BottomLeft };
        var left = Math.Max(0, Math.Min(anchor.X, point.X)); var top = Math.Max(0, Math.Min(anchor.Y, point.Y));
        var right = Math.Min(Width, Math.Max(anchor.X, point.X)); var bottom = Math.Min(Height, Math.Max(anchor.Y, point.Y));
        if (right - left < 2 || bottom - top < 2) return;
        var sx = (right - left) / Math.Max(1, handleBounds.Width); var sy = (bottom - top) / Math.Max(1, handleBounds.Height);
        var scaled = Selected.Points.Select(p => new PixelPoint((int)Math.Round(left + (p.X - handleBounds.X) * sx), (int)Math.Round(top + (p.Y - handleBounds.Y) * sy))).ToArray();
        preview = Selected.WithPoints(scaled);
    }
    internal void EndHandleDrag()
    {
        if (Selected != null && preview != null && !ReferenceEquals(preview, Selected)) Document.Replace(selectedIndex, preview);
        Select(selectedIndex); CancelInteraction(); Refresh();
    }
    protected override void OnRender(DrawingContext dc)
    {
        IEnumerable<Annotation> items = Document.Items;
        if (preview != null)
            items = (draggingObject || draggingHandle) && !copyingObject ? Document.Items.Select((item, i) => i == selectedIndex ? preview! : item) : Document.Items.Concat(new[] { preview });
        AnnotationRenderer.Draw(dc, Image, items);
        if (!HideEditorChrome) DrawPencilOutline(dc);
        var selected = draggingObject && preview != null ? preview : Selected;
        if (!HideEditorChrome && selected != null)
        {
            var bounds = AnnotationRenderer.Bounds(Image, selected);
            if (!bounds.IsEmpty)
            {
                bounds.Inflate(4, 4);
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, Width, Height)));
                dc.DrawRectangle(null, new Pen(Ui.Accent, 1) { DashStyle = DashStyles.Dash }, bounds); dc.Pop();
                DrawControlPoints(dc, selected, bounds);
            }
        }
    }
    private PixelPoint Point(MouseEventArgs e)
    {
        var point = e.GetPosition(this); return preferences.MarkLimitToSelection ? new PixelPoint(Math.Max(0, Math.Min(Width - 1, point.X)), Math.Max(0, Math.Min(Height - 1, point.Y))) : new PixelPoint(point.X, point.Y);
    }
    private void Down(object sender, MouseButtonEventArgs e)
    {
        if (textEditor != null) { CommitTextEditor(); e.Handled = true; return; }
        if ((DateTime.UtcNow - lastTextCommit).TotalMilliseconds < 150) { e.Handled = true; return; }
        Focus(); var point = Point(e);
        if (Selected != null && TryBeginHandleDrag(point)) { CaptureMouse(); e.Handled = true; return; }
        if (Kind == AnnotationKind.Polyline && pathBuilding)
        {
            stroke.Add(point); preview = MakeAnnotation(stroke); if (e.ClickCount == 2) FinishPath(); InvalidateVisual(); e.Handled = true; return;
        }
        if (SelectAt(point))
        {
            if (e.ClickCount == 2 && Selected?.Kind == AnnotationKind.Arrow && preferences.MarkArrowDoubleClickDisable) { SetTool(null); e.Handled = true; return; }
            if (e.ClickCount == 2 && Selected?.Kind == AnnotationKind.Text && (Keyboard.Modifiers & ModifierKeys.Alt) == 0)
            { EditSelectedText(); e.Handled = true; return; }
            BeginObjectDrag(point, (Keyboard.Modifiers & ModifierKeys.Alt) != 0); CaptureMouse(); e.Handled = true; return;
        }
        if (!Kind.HasValue) { e.Handled = true; return; }
        if (Kind == AnnotationKind.Text || Kind == AnnotationKind.Watermark)
        {
            textKind = Kind.Value; textPoint = point; editingText = null; editingTextIndex = -1;
            OpenTextEditor(Kind == AnnotationKind.Watermark ? "DeskSnip {time}" : "");
            return;
        }
        if (Kind == AnnotationKind.Number)
        {
            var number = preferences.MarkGlobalSerial ? System.Threading.Interlocked.Increment(ref globalSerial) - (preferences.MarkSerialStartAtZero ? 1 : 0) : Document.Items.Where(a => a.Kind == AnnotationKind.Number).Select(a => int.TryParse(a.Text, out var n) ? n + 1 : 0).DefaultIfEmpty(preferences.MarkSerialStartAtZero ? 0 : 1).Max();
            Document.Add(new Annotation(Kind.Value, new[] { point }, Argb, Thickness, number.ToString(CultureInfo.InvariantCulture), FontFamily, FontSize, Appearance)); Refresh(); return;
        }
        if (Kind == AnnotationKind.Polyline) { stroke.Clear(); stroke.Add(point); pathBuilding = true; e.Handled = true; return; }
        stroke.Clear(); stroke.Add(point); CaptureMouse(); e.Handled = true;
    }
    private void Move(object sender, MouseEventArgs e)
    {
        pointer = Point(e); InvalidateVisual();
        if (draggingHandle && IsMouseCaptured) { UpdateHandleDrag(Point(e)); return; }
        if (draggingObject && IsMouseCaptured) { UpdateObjectDrag(Point(e), (Keyboard.Modifiers & ModifierKeys.Shift) != 0); return; }
        if (pathBuilding) { preview = MakeAnnotation(stroke.Concat(new[] { Point(e) })); InvalidateVisual(); return; }
        if (!IsMouseCaptured || stroke.Count == 0) return;
        var point = Point(e);
        if (IsBrush(Kind))
        {
            var last = stroke[stroke.Count - 1];
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 || Kind == AnnotationKind.Highlight && preferences.MarkHighlighterMode != "FreeAngle")
            {
                var origin = stroke[0]; var mode = Kind == AnnotationKind.Highlight && preferences.MarkHighlighterMode != "FreeAngle" ? "Axis" : preferences.MarkPencilShiftMode;
                point = Kind == AnnotationKind.Highlight && preferences.MarkHighlighterMode == "Horizontal" ? new PixelPoint(point.X, origin.Y) : Kind == AnnotationKind.Highlight && preferences.MarkHighlighterMode == "Vertical" ? new PixelPoint(origin.X, point.Y) : AnnotationPreferenceRules.ConstrainStroke(origin, point, mode);
                if (preferences.MarkLimitToSelection) point = new PixelPoint(Math.Max(0, Math.Min(Width - 1, point.X)), Math.Max(0, Math.Min(Height - 1, point.Y))); stroke.Clear(); stroke.Add(origin); stroke.Add(point);
            }
            else if (Math.Abs(last.X - point.X) + Math.Abs(last.Y - point.Y) > .8) stroke.Add(point);
        }
        else { if (stroke.Count > 1) stroke.RemoveAt(1); stroke.Add(point); }
        if (Kind.HasValue) preview = MakeAnnotation(stroke); InvalidateVisual();
    }
    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (draggingHandle) { EndHandleDrag(); e.Handled = true; return; }
        if (draggingObject) { UpdateObjectDrag(Point(e), (Keyboard.Modifiers & ModifierKeys.Shift) != 0); EndObjectDrag(); e.Handled = true; return; }
        if (pathBuilding) return;
        if (stroke.Count == 0) return;
        Move(sender, e);
        if (preview != null) Document.Add(preview);
        else if (IsBrush(Kind)) Document.Add(MakeAnnotation(stroke));
        stroke.Clear(); preview = null; ReleaseMouseCapture(); Refresh(); e.Handled = true;
    }
    private static bool IsBrush(AnnotationKind? kind) => kind == AnnotationKind.Pen || kind == AnnotationKind.Highlight || kind == AnnotationKind.MosaicPen || kind == AnnotationKind.CoverPen || kind == AnnotationKind.EraserPen;
    private Annotation MakeAnnotation(IEnumerable<PixelPoint> points) => new Annotation(Kind!.Value, points, Argb, Thickness, fontFamily: FontFamily, fontSize: FontSize, appearance: Appearance, bold: Bold, italic: Italic);
    private void FinishPath() { if (stroke.Count < 2) return; Document.Add(MakeAnnotation(stroke)); CancelInteraction(); Refresh(); }
}

internal sealed class TextPrompt : ProductWindow
{
    private readonly TextBox input = new TextBox { MinHeight = 75, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(8) };
    internal string Value => input.Text;
    internal TextPrompt(string value = "")
    {
        var editing = value.Length > 0; input.Text = value;
        Title = editing ? L.Get("修改文字") : L.Get("添加文字"); Width = 390; Height = 210; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Thickness(18) }; root.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var button = Ui.Button(editing ? L.Get("应用") : L.Get("添加"), () => DialogResult = true, true); button.IsDefault = true; buttons.Children.Add(button);
        var cancel = Ui.Button(L.Get("取消"), () => DialogResult = false); cancel.IsCancel = true; buttons.Children.Add(cancel); root.Children.Add(buttons); Content = root;
        Loaded += (_, _) => { input.Focus(); if (editing) input.SelectAll(); };
    }
}
