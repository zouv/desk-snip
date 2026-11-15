using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DeskSnip.App.Imaging;

namespace DeskSnip.App;

internal sealed partial class PinWindow
{
    private void Menu()
    {
        var menu = BuildContextMenu();
        menu.PlacementTarget = this; menu.IsOpen = true;
    }
    internal ContextMenu BuildContextMenu()
    {
        var panel = Application.Current?.TryFindResource("PanelBrush") as Brush ?? Brushes.White;
        var edge = Application.Current?.TryFindResource("BorderBrush") as Brush ?? FloatingTools.Edge;
        var itemStyle = PinMenuItemStyle();
        var separatorStyle = PinSeparatorStyle(edge);
        var menu = new ContextMenu { Style = PinContextMenuStyle(panel, edge), FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 12, Foreground = Ui.Ink };
        void Add(string label, ToolGlyph glyph, Action action, string shortcut = "", bool? check = null)
            => menu.Items.Add(PinMenuItem(label, glyph, action, shortcut, check, itemStyle));
        void Separator() => menu.Items.Add(new Separator { Style = separatorStyle });
        Add(L.Get("复制图片"), ToolGlyph.Copy, Copy, "Ctrl+C");
        Add(L.Get("复制原始图片"), ToolGlyph.Copy, () => Copy(original));
        Add(L.Get("保存图片…"), ToolGlyph.Save, Save, "Ctrl+S");
        Add(L.Get("快速保存"), ToolGlyph.Save, () => QuickSaveRequested?.Invoke(OutputBitmap), "Ctrl+Shift+S");
        Separator();
        Add(L.Get("继续标注"), ToolGlyph.Brush, () => BeginInlineEdit(), "Space");
        Add(L.Get("本地文字识别"), ToolGlyph.Ocr, () => RecognizeRequested?.Invoke(Bitmap), "Shift+C");
        Add(L.Get("联网识别 / 翻译"), ToolGlyph.Cloud, () => ServiceRecognitionRequested?.Invoke(Bitmap), "Ctrl+Q");
        Separator();
        Add(L.Get("顺时针旋转 90°"), ToolGlyph.RotateClockwise, () => Transform(ImageTransform.RotateClockwise), "Ctrl+Right");
        Add(L.Get("水平翻转"), ToolGlyph.FlipHorizontal, () => Transform(ImageTransform.FlipHorizontal), "Ctrl+H");
        Add(L.Get("垂直翻转"), ToolGlyph.FlipVertical, () => Transform(ImageTransform.FlipVertical), "Ctrl+J");
        Add(L.Get("灰度"), ToolGlyph.Grayscale, () => Transform(ImageTransform.Grayscale));
        Add(L.Get("反色"), ToolGlyph.Invert, () => Transform(ImageTransform.Invert));
        Add(L.Get("增加亮度"), ToolGlyph.BrightnessUp, () => Transform(ImageTransform.Brighter));
        Add(L.Get("降低亮度"), ToolGlyph.BrightnessDown, () => Transform(ImageTransform.Darker));
        Separator();
        Add(L.Get("放大"), ToolGlyph.Zoom, () => MouseAction("Zoom", 1, Settings.PinWheelAnchor));
        Add(L.Get("缩小"), ToolGlyph.ZoomOut, () => MouseAction("Zoom", -1, Settings.PinWheelAnchor));
        Add(L.Get("原始大小 / 切回"), ToolGlyph.OriginalSize, () => MouseAction("Original", 1, Settings.PinMiddleAnchor), "O");
        Add(L.Get("适合屏幕"), ToolGlyph.FitScreen, () => { FitToScreen(); ResizeImage(); Rescue(); });
        Add(L.Get("增加透明度"), ToolGlyph.OpacityDown, () => MouseAction("Opacity", -1));
        Add(L.Get("恢复不透明"), ToolGlyph.OpacityFull, () => { while (Presentation.Opacity < 1) Presentation.Fade(1); ResizeImage(); });
        Separator();
        Add(L.Get("窗口置顶"), ToolGlyph.Pin, () => { Presentation.Topmost = !Presentation.Topmost; SetInteraction(); }, "T", Presentation.Topmost);
        Add(L.Get("锁定位置和尺寸"), ToolGlyph.Lock, () => { Presentation.Locked = !Presentation.Locked; SetInteraction(); }, "L", Presentation.Locked);
        Add(L.Get("鼠标穿透（控制台 / 托盘可解除）"), ToolGlyph.Cursor, () => { Presentation.ClickThrough = !Presentation.ClickThrough; SetInteraction(); }, "", Presentation.ClickThrough);
        Add(L.Get("移回可见屏幕"), ToolGlyph.Window, Rescue);
        Add(L.Get("修改标题"), ToolGlyph.Text, () => { var prompt = new TextPrompt(Title) { Title = L.Get("钉图标题"), Owner = this }; if (prompt.ShowDialog() == true) Rename(prompt.Value); });
        Separator();
        Add(L.Get("关闭（可在控制台恢复）"), ToolGlyph.Close, () => RequestCloseFromInput(false), "Esc");
        return menu;
    }
    private MenuItem PinMenuItem(string label, ToolGlyph glyph, Action action, string shortcut, bool? check, Style style)
    {
        var item = new MenuItem
        {
            Header = label, Icon = new ToolIcon(glyph) { Width = 16, Height = 16, Color = Ui.Ink }, Style = style,
            InputGestureText = Settings.PinShowMenuShortcuts ? shortcut : ""
        };
        if (check.HasValue) { item.IsCheckable = true; item.IsChecked = check.Value; }
        item.Click += (_, _) => action();
        AutomationProperties.SetName(item, label);
        return item;
    }
    private static Style PinMenuItemStyle()
    {
        var accent = ((SolidColorBrush)Ui.Accent).Color;
        var frame = new FrameworkElementFactory(typeof(Border)); frame.Name = "Frame";
        frame.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        frame.SetValue(Border.PaddingProperty, new Thickness(8, 5, 8, 5));
        var grid = new FrameworkElementFactory(typeof(Grid));
        AddColumn(grid, new GridLength(20)); AddColumn(grid, new GridLength(1, GridUnitType.Star)); AddColumn(grid, GridLength.Auto);
        var icon = new FrameworkElementFactory(typeof(ContentPresenter));
        icon.SetValue(Grid.ColumnProperty, 0); icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
        icon.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(MenuItem.IconProperty));
        icon.SetValue(UIElement.IsHitTestVisibleProperty, false);
        grid.AppendChild(icon);
        var header = new FrameworkElementFactory(typeof(ContentPresenter)); header.Name = "Header";
        header.SetValue(Grid.ColumnProperty, 1); header.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        header.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(MenuItem.HeaderProperty));
        header.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        grid.AppendChild(header);
        var gesture = new FrameworkElementFactory(typeof(TextBlock)); gesture.Name = "Gesture";
        gesture.SetValue(Grid.ColumnProperty, 2); gesture.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        gesture.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        gesture.SetValue(FrameworkElement.MarginProperty, new Thickness(28, 0, 2, 0));
        gesture.SetValue(TextBlock.TextProperty, new TemplateBindingExtension(MenuItem.InputGestureTextProperty));
        gesture.SetValue(TextBlock.FontSizeProperty, 11d); gesture.SetValue(TextBlock.ForegroundProperty, Ui.Subtle);
        grid.AppendChild(gesture);
        frame.AppendChild(grid);
        var template = new ControlTemplate(typeof(MenuItem)) { VisualTree = frame };
        var hover = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, AccentTint(accent, 30), "Frame"));
        template.Triggers.Add(hover);
        var check = new Trigger { Property = MenuItem.IsCheckedProperty, Value = true };
        check.Setters.Add(new Setter(Border.BackgroundProperty, AccentTint(accent, 52), "Frame"));
        check.Setters.Add(new Setter(TextElement.ForegroundProperty, Ui.Accent, "Header"));
        template.Triggers.Add(check);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .45, "Frame"));
        template.Triggers.Add(disabled);
        var style = new Style(typeof(MenuItem));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Ui.Ink));
        style.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Microsoft YaHei UI")));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 12d));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        return style;
    }
    private static Style PinContextMenuStyle(Brush panel, Brush edge)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, panel);
        border.SetValue(Border.BorderBrushProperty, edge);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.PaddingProperty, new Thickness(5));
        border.SetValue(Border.EffectProperty, new DropShadowEffect { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 2, Opacity = .18 });
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        scroll.SetValue(FrameworkElement.MaxHeightProperty, Math.Max(240, SystemParameters.WorkArea.Height - 60));
        scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        border.AppendChild(scroll);
        var template = new ControlTemplate(typeof(ContextMenu)) { VisualTree = border };
        var style = new Style(typeof(ContextMenu));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.BackgroundProperty, panel));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, edge));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Ui.Ink));
        style.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Microsoft YaHei UI")));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 12d));
        return style;
    }
    private static Style PinSeparatorStyle(Brush edge)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.HeightProperty, 1d);
        border.SetValue(Border.BackgroundProperty, edge);
        border.SetValue(Border.MarginProperty, new Thickness(10, 4, 10, 4));
        var template = new ControlTemplate(typeof(Separator)) { VisualTree = border };
        var style = new Style(typeof(Separator));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }
    private static void AddColumn(FrameworkElementFactory grid, GridLength width)
    {
        var column = new FrameworkElementFactory(typeof(ColumnDefinition));
        column.SetValue(ColumnDefinition.WidthProperty, width);
        grid.AppendChild(column);
    }
    private static Brush AccentTint(Color color, byte alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)); brush.Freeze(); return brush;
    }
}
