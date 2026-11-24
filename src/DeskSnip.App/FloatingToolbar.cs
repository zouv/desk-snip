using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace DeskSnip.App;

internal enum ToolGlyph
{
    Rectangle, Ellipse, Polygon, Brush, Highlight, Line, Arrow, Text, Number, Mosaic, Cover, Eraser,
    Spotlight, Zoom, Select, Window, History, Restart, Copy, Save, Pin, Ocr, Cloud, Scroll,
    Close, Check, More, Undo, Redo, Styles, Chevron, Grip, Stop, Pause, Play, Sound, Muted, Microphone, Camera, Cursor, Qr,
    Palette, Keyboard, Transfer, Info, Mouse, GlobalMouse,
    LineSolid, LineDash, LineDot, Width, CornerRadius, Opacity,
    RotateClockwise, FlipHorizontal, FlipVertical, Grayscale, Invert, BrightnessUp, BrightnessDown,
    ZoomOut, OriginalSize, FitScreen, OpacityUp, OpacityDown, OpacityFull, Lock, Record, Crop
}

/// <summary>截图和录屏共用的自绘工具；图标不依赖外部字体或图片资源。</summary>
internal static class FloatingTools
{
    internal static readonly Brush Blue = Frozen("#3489F5");
    internal static readonly Brush Ink = Frozen("#333840");
    internal static readonly Brush MutedInk = Frozen("#73808E");
    internal static readonly Brush Red = Frozen("#E34B4B");
    internal static readonly Brush Hover = Frozen("#F0F3F7");
    internal static readonly Brush Selected = Frozen("#E8F2FF");
    internal static readonly Brush Edge = Frozen("#E2E6EC");
    internal const double RecordDotSize = 8;
    internal const double RecordTimeFontSize = 12;
    internal const double RecordTimeMinWidth = 40;
    internal const double RecordFpsFontSize = 11;
    internal static readonly Thickness RecordFpsMargin = new Thickness(6, 0, 8, 0);
    [ThreadStatic] private static Style? buttonStyle;
    private static readonly DependencyProperty shortcutProperty = DependencyProperty.RegisterAttached("OriginalShortcut", typeof(string), typeof(FloatingTools), new PropertyMetadata(""));
    private static readonly DependencyProperty widthProperty = DependencyProperty.RegisterAttached("OriginalWidth", typeof(double), typeof(FloatingTools), new PropertyMetadata(double.NaN));

    private static Brush Frozen(string value)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value)!; brush.Freeze(); return brush;
    }
    private static Style CreateButtonStyle()
    {
        var frame = new FrameworkElementFactory(typeof(Border)); frame.Name = "Frame";
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        frame.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); frame.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = frame };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Border.BackgroundProperty, Hover, "Frame")); template.Triggers.Add(hover);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderBrushProperty, Blue, "Frame")); focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1), "Frame")); template.Triggers.Add(focus);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .3, "Frame")); template.Triggers.Add(disabled);
        var style = new Style(typeof(Button)); style.Setters.Add(new Setter(Control.TemplateProperty, template)); return style;
    }
    internal static Button Button(ToolGlyph glyph, string name, Action action, string shortcut = "", bool primary = false)
    {
        var button = new Button { Style = buttonStyle ?? (buttonStyle = CreateButtonStyle()), Width = 32, Height = 32, MinHeight = 0, MinWidth = 0, Padding = new Thickness(0), Margin = new Thickness(1), Background = primary ? Selected : Brushes.Transparent, Foreground = primary ? Blue : Ink, Cursor = Cursors.Hand };
        SetIcon(button, glyph, name, shortcut); button.Click += (_, _) => action(); return button;
    }
    internal static Ellipse RecordDot()
        => new Ellipse { Width = RecordDotSize, Height = RecordDotSize, Fill = Red, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    internal static TextBlock RecordTime()
        => new TextBlock { Text = "00:00", FontFamily = new FontFamily("Consolas"), FontSize = RecordTimeFontSize, Foreground = Ink, MinWidth = RecordTimeMinWidth, TextAlignment = TextAlignment.Left, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
    internal static TextBlock RecordFps(string text)
        => new TextBlock { Text = text, FontSize = RecordFpsFontSize, Foreground = MutedInk, Margin = RecordFpsMargin, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
    internal static Button RecordButton(string name, Action action, string shortcut = "")
    {
        var button = Button(ToolGlyph.Record, name, action, shortcut, true);
        button.Content = RecordDot();
        return button;
    }
    internal static void SetIcon(Button button, ToolGlyph glyph, string name, string shortcut = "")
    {
        if (button.Content is ToolIcon current) current.SetGlyph(glyph);
        else { var icon = new ToolIcon(glyph); icon.SetBinding(ToolIcon.ColorProperty, new System.Windows.Data.Binding("Foreground") { Source = button }); button.Content = icon; }
        SetName(button, name, shortcut);
    }
    internal static void SetMenuIcon(Button button, ToolGlyph glyph)
    {
        if (button.Content is StackPanel panel)
        {
            var icon = panel.Children.OfType<ToolIcon>().FirstOrDefault(item => item.Glyph != ToolGlyph.Chevron);
            if (icon != null) { icon.SetGlyph(glyph); return; }
        }
        SetIcon(button, glyph, AutomationProperties.GetName(button));
    }
    internal static void SetName(Button button, string name, string shortcut = "")
    {
        button.SetValue(shortcutProperty, shortcut);
        AutomationProperties.SetName(button, name); button.ToolTip = name + (shortcut.Length == 0 ? "" : "  " + shortcut);
        ToolTipService.SetInitialShowDelay(button, 350); ToolTipService.SetShowDuration(button, 6000);
    }
    internal static void SetSelected(Button button, bool selected)
    {
        button.Background = selected ? Selected : Brushes.Transparent; button.Foreground = selected ? Blue : Ink;
    }
    internal static ContextMenu Menu()
        => new ContextMenu { Background = Brushes.White, Foreground = Ink, BorderBrush = Edge, BorderThickness = new Thickness(1), Padding = new Thickness(4), FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 12 };
    internal static MenuItem Item(string name, Action action, string shortcut = "")
    {
        var item = new MenuItem { Header = name, InputGestureText = shortcut, Foreground = Ink, Padding = new Thickness(8, 6, 8, 6) };
        item.SetValue(shortcutProperty, shortcut);
        AutomationProperties.SetName(item, name); item.Click += (_, _) => action(); return item;
    }
    internal static Button MenuButton(ToolGlyph glyph, string name, ContextMenu menu)
    {
        Button button = null!; button = CreateMenuButton(glyph, name, () => Open(menu, button), menu); return button;
    }
    /// <summary>文字下拉按钮：与图标按钮共用浮动栏样式，避免默认 ComboBox 外观割裂。</summary>
    internal static Button TextMenuButton(string text, string name, ContextMenu menu, out TextBlock label, double width = 0)
    {
        label = new TextBlock { Text = text, Foreground = Ink, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
        content.Children.Add(label);
        content.Children.Add(new ToolIcon(ToolGlyph.Chevron) { Width = 9, Height = 12, Margin = new Thickness(4, 0, 0, 0), Color = MutedInk });
        Button button = null!;
        button = Button(ToolGlyph.More, name, () => Open(menu, button));
        button.Content = content; button.Width = width > 0 ? width : double.NaN; button.Height = 28; button.VerticalAlignment = VerticalAlignment.Center; button.ContextMenu = menu;
        return button;
    }
    internal static Button SplitMenuButton(ToolGlyph glyph, string name, Action primary, ContextMenu menu)
    {
        Button button = null!;
        button = CreateMenuButton(glyph, name, () =>
        {
            var point = Mouse.GetPosition(button);
            if (point.X >= button.ActualWidth - 14) Open(menu, button); else primary();
        }, menu);
        button.Tag = primary;
        button.MouseRightButtonUp += (_, e) => { Open(menu, button); e.Handled = true; };
        return button;
    }
    private static Button CreateMenuButton(ToolGlyph glyph, string name, Action click, ContextMenu menu)
    {
        var button = Button(glyph, name, click); button.ContextMenu = menu;
        var icon = (UIElement)button.Content; button.Content = null;
        var content = new StackPanel { Orientation = Orientation.Horizontal }; content.Children.Add(icon);
        content.Children.Add(new ToolIcon(ToolGlyph.Chevron) { Width = 9, Height = 12, Margin = new Thickness(2, 0, 0, 0), Color = MutedInk });
        button.Content = content; button.Width = 42; return button;
    }
    internal static void Open(ContextMenu menu, FrameworkElement target)
    {
        menu.PlacementTarget = target; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true;
    }
    internal static FrameworkElement Divider(bool verticalToolbar = false)
        => new Border { Width = verticalToolbar ? 23 : 1, Height = verticalToolbar ? 1 : 20, Background = Edge, Margin = verticalToolbar ? new Thickness(4, 5, 4, 5) : new Thickness(6, 7, 6, 7), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    internal static void Configure(Panel items, string size, bool showShortcuts)
    {
        var scale = size == "Small" ? .875 : size == "Large" ? 1.1875 : 1;
        foreach (var button in items.Children.OfType<Button>())
        {
            var width = (double)button.GetValue(widthProperty); if (double.IsNaN(width)) { width = button.Width; button.SetValue(widthProperty, width); }
            button.Width = width * scale; button.Height = 32 * scale;
            if (button.Content is ToolIcon icon) icon.Width = icon.Height = 18 * scale;
            else if (button.Content is StackPanel group) foreach (var glyph in group.Children.OfType<ToolIcon>()) if (glyph.Glyph != ToolGlyph.Chevron) glyph.Width = glyph.Height = 18 * scale;
            var shortcut = (string)button.GetValue(shortcutProperty); button.ToolTip = AutomationProperties.GetName(button) + (showShortcuts && shortcut.Length > 0 ? "  " + shortcut : "");
            if (button.ContextMenu != null) foreach (var menuItem in button.ContextMenu.Items.OfType<MenuItem>()) menuItem.InputGestureText = showShortcuts ? (string)menuItem.GetValue(shortcutProperty) : "";
        }
    }
}

internal class FloatingToolbar : Border
{
    internal WrapPanel Items { get; }
    internal FloatingToolbar(bool vertical = false)
    {
        Background = Brushes.White; BorderBrush = FloatingTools.Edge; BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(7); Padding = new Thickness(4);
        Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 10, ShadowDepth = 2, Opacity = .14 };
        Items = new WrapPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal }; Child = Items;
        SnapsToDevicePixels = true; UseLayoutRounding = true; Cursor = Cursors.Arrow;
    }
}

internal sealed class ToolIcon : FrameworkElement
{
    internal static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(Brush), typeof(ToolIcon), new FrameworkPropertyMetadata(FloatingTools.Ink, FrameworkPropertyMetadataOptions.AffectsRender));
    internal Brush Color { get => (Brush)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    internal ToolGlyph Glyph { get; private set; }
    internal ToolIcon(ToolGlyph glyph) { Glyph = glyph; Width = Height = 18; IsHitTestVisible = false; }
    internal void SetGlyph(ToolGlyph glyph) { if (Glyph != glyph) { Glyph = glyph; InvalidateVisual(); } }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); dc.PushTransform(new ScaleTransform(ActualWidth / 24, ActualHeight / 24));
        var pen = new Pen(Color, 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        void Path(string data, bool fill = false) => dc.DrawGeometry(fill ? Color : null, pen, Geometry.Parse(data));
        void Circle(double x, double y, double r, bool fill = false) => dc.DrawEllipse(fill ? Color : null, pen, new Point(x, y), r, r);
        switch (Glyph)
        {
            case ToolGlyph.Rectangle: dc.DrawRoundedRectangle(null, pen, new Rect(4, 5, 16, 14), 1, 1); break;
            case ToolGlyph.Ellipse: dc.DrawEllipse(null, pen, new Point(12, 12), 8, 7); break;
            case ToolGlyph.Polygon: Path("M 4,15 L 8,4 19,7 20,17 11,21 Z"); break;
            case ToolGlyph.Brush: Path("M 8,14 L 17,4 21,8 11,18 Z M 8,14 C 2,14 7,20 3,21 C 9,22 13,20 11,18"); break;
            case ToolGlyph.Highlight: Path("M 8,15 L 16,4 21,8 13,19 Z M 8,15 L 5,20 10,20 M 3,22 L 21,22"); break;
            case ToolGlyph.Line: Path("M 4,20 L 20,4"); break;
            case ToolGlyph.Arrow: Path("M 4,20 L 20,4 M 10,4 L 20,4 20,14"); break;
            case ToolGlyph.Text: Path("M 4,5 L 20,5 M 12,5 L 12,20 M 8,20 L 16,20"); break;
            case ToolGlyph.Number: Circle(12, 12, 8); Path("M 10,9 L 12,7 12,17 M 10,17 L 14,17"); break;
            case ToolGlyph.Mosaic: Path("M 4,4 L 20,4 20,20 4,20 Z M 4,9 L 20,9 M 4,15 L 20,15 M 9,4 L 9,20 M 15,4 L 15,20"); dc.DrawRectangle(Color, null, new Rect(4, 4, 5, 5)); dc.DrawRectangle(Color, null, new Rect(9, 9, 6, 6)); dc.DrawRectangle(Color, null, new Rect(15, 15, 5, 5)); break;
            case ToolGlyph.Cover: dc.DrawRoundedRectangle(Color, null, new Rect(4, 5, 16, 14), 1, 1); break;
            case ToolGlyph.Eraser: Path("M 3,14 L 13,4 21,12 13,20 9,20 Z M 7,10 L 15,18 M 13,20 L 22,20"); break;
            case ToolGlyph.Spotlight: Circle(12, 12, 7); Path("M 12,1 L 12,3 M 12,21 L 12,23 M 1,12 L 3,12 M 21,12 L 23,12"); break;
            case ToolGlyph.Zoom: Circle(10, 10, 6); Path("M 15,15 L 21,21 M 7,10 L 13,10 M 10,7 L 10,13"); break;
            case ToolGlyph.Select: Path("M 5,3 L 5,19 10,14 14,21 17,19 13,12 20,12 Z"); break;
            case ToolGlyph.Window: Path("M 3,5 L 21,5 21,20 3,20 Z M 3,9 L 21,9 M 6,7 L 7,7 M 10,7 L 11,7"); break;
            case ToolGlyph.History: Circle(12, 13, 8); Path("M 12,8 L 12,13 16,15 M 4,4 L 4,9 9,9"); break;
            case ToolGlyph.Restart: Path("M 5,8 C 11,-1 22,5 21,14 C 20,21 9,24 4,17 M 5,3 L 5,8 10,8"); break;
            case ToolGlyph.Copy: Path("M 8,8 L 21,8 21,21 8,21 Z M 5,16 L 3,16 3,3 16,3 16,5"); break;
            case ToolGlyph.Save: Path("M 12,3 L 12,16 M 7,11 L 12,16 17,11 M 4,17 L 4,21 20,21 20,17"); break;
            case ToolGlyph.Pin: Path("M 10,3 L 21,14 M 12,5 L 7,10 5,10 14,19 14,16 19,12 M 9,15 L 3,21"); break;
            case ToolGlyph.Ocr: Path("M 8,3 L 3,3 3,8 M 16,3 L 21,3 21,8 M 3,16 L 3,21 8,21 M 21,16 L 21,21 16,21 M 7,16 L 12,7 17,16 M 9,13 L 15,13"); break;
            case ToolGlyph.Cloud: Path("M 6,18 C -1,18 0,9 6,9 C 7,1 18,1 19,10 C 26,11 24,18 19,18 Z M 12,10 L 12,21 M 8,17 L 12,21 16,17"); break;
            case ToolGlyph.Scroll: Path("M 6,3 L 18,3 18,21 6,21 Z M 9,7 L 15,7 M 9,10 L 15,10 M 12,13 L 12,18 M 9,16 L 12,19 15,16"); break;
            case ToolGlyph.Close: Path("M 5,5 L 19,19 M 19,5 L 5,19"); break;
            case ToolGlyph.Check: Path("M 4,12 L 10,18 20,6"); break;
            case ToolGlyph.More: Circle(5, 12, 1, true); Circle(12, 12, 1, true); Circle(19, 12, 1, true); break;
            case ToolGlyph.Undo: Path("M 9,6 L 3,11 9,16 M 3,11 L 14,11 C 24,11 22,21 14,21"); break;
            case ToolGlyph.Redo: Path("M 15,6 L 21,11 15,16 M 21,11 L 10,11 C 0,11 2,21 10,21"); break;
            case ToolGlyph.Styles: Path("M 3,7 L 21,7 M 3,17 L 21,17 M 8,4 L 8,10 M 16,14 L 16,20"); break;
            case ToolGlyph.Chevron: Path("M 6,9 L 12,15 18,9"); break;
            case ToolGlyph.Grip: for (var y = 6; y <= 18; y += 6) { Circle(9, y, .6, true); Circle(15, y, .6, true); } break;
            case ToolGlyph.Stop: Circle(12, 12, 9); dc.DrawRoundedRectangle(Color, null, new Rect(8, 8, 8, 8), 1, 1); break;
            case ToolGlyph.Pause: dc.DrawRoundedRectangle(Color, null, new Rect(6, 4, 4, 16), 1, 1); dc.DrawRoundedRectangle(Color, null, new Rect(14, 4, 4, 16), 1, 1); break;
            case ToolGlyph.Play: Path("M 6,3 L 21,12 6,21 Z", true); break;
            case ToolGlyph.Sound: case ToolGlyph.Muted:
                Path("M 3,9 L 7,9 12,5 12,19 7,15 3,15 Z");
                if (Glyph == ToolGlyph.Sound) Path("M 16,7 C 20,10 20,14 16,17 M 19,4 C 26,10 26,14 19,20");
                else Path("M 16,9 L 22,15 M 22,9 L 16,15"); break;
            case ToolGlyph.Microphone: dc.DrawRoundedRectangle(null, pen, new Rect(9, 3, 6, 12), 3, 3); Path("M 5,11 C 5,22 19,22 19,11 M 12,19 L 12,23 M 8,23 L 16,23"); break;
            case ToolGlyph.Camera: dc.DrawRoundedRectangle(null, pen, new Rect(3, 7, 18, 12), 2, 2); dc.DrawEllipse(null, pen, new Point(12, 13), 4, 4); Path("M 8,7 L 9,4 15,4 16,7"); break;
            case ToolGlyph.Cursor: Path("M 6,3 L 6,20 11,15 18,15 Z"); break;
            case ToolGlyph.Qr: Path("M 3,3 L 9,3 9,9 3,9 Z M 15,3 L 21,3 21,9 15,9 Z M 3,15 L 9,15 9,21 3,21 Z M 15,15 L 18,15 18,18 21,18 21,21 15,21 Z"); break;
            case ToolGlyph.Palette: Path("M 12,3 C 1,3 -1,16 8,21 C 12,23 14,20 12,17 C 10,14 15,13 18,14 C 25,16 23,3 12,3 Z"); Circle(7,9,1,true); Circle(12,6,1,true); Circle(17,8,1,true); Circle(6,14,1,true); break;
            case ToolGlyph.Keyboard: dc.DrawRoundedRectangle(null, pen, new Rect(2,6,20,13), 2, 2); Path("M 6,10 L 7,10 M 11,10 L 12,10 M 16,10 L 17,10 M 6,14 L 7,14 M 11,14 L 12,14 M 16,14 L 17,14 M 8,17 L 16,17"); break;
            case ToolGlyph.Transfer: Path("M 3,7 L 20,7 M 15,2 L 20,7 15,12 M 21,17 L 4,17 M 9,12 L 4,17 9,22"); break;
            case ToolGlyph.Info: Circle(12,12,9); Circle(12,7,1,true); Path("M 12,11 L 12,17 M 10,17 L 14,17"); break;
            case ToolGlyph.Mouse: dc.DrawRoundedRectangle(null, pen, new Rect(6,2,12,20), 6, 6); Path("M 12,3 L 12,9"); break;
            case ToolGlyph.GlobalMouse: Path("M 3,9 L 3,3 9,3 M 15,3 L 21,3 21,9 M 3,15 L 3,21 9,21 M 11,10 L 11,21 15,17 21,17 Z"); break;
            case ToolGlyph.LineSolid: Path("M 4,12 L 20,12"); break;
            case ToolGlyph.LineDash: Path("M 4,12 L 8,12 M 11,12 L 15,12 M 18,12 L 20,12"); break;
            case ToolGlyph.LineDot: Circle(6,12,1,true); Circle(12,12,1,true); Circle(18,12,1,true); break;
            case ToolGlyph.Width:
                dc.DrawRectangle(Color, null, new Rect(4,7,16,1.2)); dc.DrawRectangle(Color, null, new Rect(4,11,16,2.4)); dc.DrawRectangle(Color, null, new Rect(4,16,16,3.6)); break;
            case ToolGlyph.CornerRadius:
                dc.DrawRoundedRectangle(null, new Pen(Color, 1.4) { DashStyle = DashStyles.Dash }, new Rect(4,4,16,16), 6, 6); break;
            case ToolGlyph.Opacity: Circle(12,12,8); Path("M 12,4 A 8,8 0 0 1 12,20 Z", true); break;
            case ToolGlyph.RotateClockwise: Path("M 6,7 C 10,1 20,4 21,12 C 21,18 14,22 8,19 M 6,2 L 6,7 11,7"); break;
            case ToolGlyph.FlipHorizontal: Path("M 12,3 L 12,21 M 8,6 L 3,10 8,14 Z M 16,6 L 21,10 16,14 Z"); break;
            case ToolGlyph.FlipVertical: Path("M 3,12 L 21,12 M 6,8 L 10,3 14,8 Z M 6,16 L 10,21 14,16 Z"); break;
            case ToolGlyph.Grayscale: dc.DrawEllipse(Color, null, new Point(9,12), 5, 5); dc.DrawEllipse(null, pen, new Point(15,12), 5, 5); break;
            case ToolGlyph.Invert: Circle(12,12,8); Path("M 12,4 A 8,8 0 0 1 12,20 Z", true); break;
            case ToolGlyph.BrightnessUp: Circle(12,12,8); Path("M 12,8 L 12,16 M 8,12 L 16,12"); break;
            case ToolGlyph.BrightnessDown: Circle(12,12,8); Path("M 8,12 L 16,12"); break;
            case ToolGlyph.ZoomOut: Circle(10,10,6); Path("M 15,15 L 21,21 M 7,10 L 13,10"); break;
            case ToolGlyph.OriginalSize: Path("M 4,9 L 4,4 9,4 M 15,4 L 20,4 20,9 M 20,15 L 20,20 15,20 M 9,20 L 4,20 4,15 M 8,8 L 16,8 16,16 8,16 Z"); break;
            case ToolGlyph.FitScreen: Path("M 4,9 L 4,4 9,4 M 15,4 L 20,4 20,9 M 20,15 L 20,20 15,20 M 9,20 L 4,20 4,15 M 9,9 L 15,9 15,15 9,15 Z"); break;
            case ToolGlyph.OpacityUp: Circle(12,12,8); Path("M 12,7 L 12,17 M 7,12 L 17,12"); break;
            case ToolGlyph.OpacityDown: Circle(12,12,8); Path("M 7,12 L 17,12"); break;
            case ToolGlyph.OpacityFull: dc.DrawEllipse(Color, null, new Point(12,12), 8, 8); break;
            case ToolGlyph.Lock: dc.DrawRoundedRectangle(null, pen, new Rect(5,10,14,11), 2, 2); Path("M 8,10 L 8,7 C 8,2 16,2 16,7 L 16,10"); break;
            case ToolGlyph.Record: dc.DrawEllipse(Color, null, new Point(12,12), 7, 7); break;
            case ToolGlyph.Crop: Path("M 7,3 L 7,17 21,17 M 3,7 L 17,7 17,21"); break;
        }
        dc.Pop();
    }
}

/// <summary>工具以选区为锚点，先放外侧；空间不足时回退到屏幕内，并避开其它工具。</summary>
internal static class FloatingLayout
{
    internal static Rect Clamp(Rect viewport, Point point, Size size)
    {
        var width = Math.Min(size.Width, viewport.Width); var height = Math.Min(size.Height, viewport.Height);
        var margin = Math.Min(8, Math.Max(0, Math.Min((viewport.Width - width) / 2, (viewport.Height - height) / 2)));
        return new Rect(Math.Max(viewport.Left + margin, Math.Min(point.X, viewport.Right - width - margin)), Math.Max(viewport.Top + margin, Math.Min(point.Y, viewport.Bottom - height - margin)), width, height);
    }
    internal static Rect Actions(Rect viewport, Rect selection, Size size)
    {
        var y = selection.Bottom + size.Height + 8 <= viewport.Bottom ? selection.Bottom + 8 : selection.Top - size.Height - 8 >= viewport.Top ? selection.Top - size.Height - 8 : viewport.Bottom - size.Height - 8;
        var x = Math.Max(viewport.Left + 8, Math.Min(selection.Left, viewport.Right - size.Width - 8));
        return Clamp(viewport, new Point(x, y), size);
    }
    /// <summary>标注工具条固定在选区左下方，截图工具条紧贴其右；整组避开屏幕边缘。</summary>
    internal static (Rect Inline, Rect Actions) InlineRow(Rect viewport, Rect selection, Size inlineSize, Size actionsSize)
    {
        const double gap = 6;
        var width = inlineSize.Width + gap + actionsSize.Width;
        var height = Math.Max(inlineSize.Height, actionsSize.Height);
        var x = selection.Left;
        if (x + width > viewport.Right - 8) x = viewport.Right - width - 8;
        if (x < viewport.Left + 8) x = viewport.Left + 8;
        var y = selection.Bottom + 8;
        if (y + height > viewport.Bottom - 8)
        {
            y = selection.Top - height - 8;
            if (y < viewport.Top + 8) y = viewport.Bottom - height - 8;
        }
        y = Math.Max(viewport.Top + 8, Math.Min(y, viewport.Bottom - height - 8));
        return (new Rect(x, y, inlineSize.Width, inlineSize.Height), new Rect(x + inlineSize.Width + gap, y, actionsSize.Width, actionsSize.Height));
    }
    /// <summary>标记列表优先放在选区左侧；左侧无屏幕空间时放右侧，最后贴选区内侧。</summary>
    internal static Rect AnnotationList(Rect viewport, Rect selection, Size size)
    {
        var x = selection.Left - size.Width - 8;
        if (x < viewport.Left + 8)
        {
            var right = selection.Right + 8;
            x = right + size.Width <= viewport.Right - 8 ? right : Math.Max(viewport.Left + 8, selection.Left + 8);
        }
        if (x + size.Width > viewport.Right - 8) x = viewport.Left + 8;
        var y = Math.Max(viewport.Top + 8, Math.Min(selection.Top, viewport.Bottom - size.Height - 8));
        return Clamp(viewport, new Point(x, y), size);
    }
    internal static Rect Info(Rect viewport, Rect selection, Size size, Rect actions)
        => Available(viewport, size, new[] { new Point(selection.Left, selection.Top - size.Height - 8), new Point(selection.Left + 8, selection.Top + 8), new Point(selection.Left, actions.Top - size.Height - 8), new Point(selection.Right - size.Width, actions.Bottom + 8), new Point(viewport.Left + 8, viewport.Top + 8) }, new[] { actions });
    internal static Rect Modes(Rect viewport, Rect selection, Size size, Rect info, Rect actions)
        => Available(viewport, size, new[] { new Point(selection.Right + 8, selection.Top), new Point(selection.Right - size.Width - 8, selection.Top + 8), new Point(selection.Left - size.Width - 8, selection.Top), new Point(info.Left - size.Width - 8, info.Top), new Point(viewport.Right - size.Width - 8, info.Bottom + 8), new Point(viewport.Right - size.Width - 8, viewport.Top + 8), new Point(viewport.Left + 8, viewport.Bottom - size.Height - 8) }, new[] { info, actions });
    private static Rect Available(Rect viewport, Size size, IEnumerable<Point> candidates, IReadOnlyList<Rect> obstacles)
    {
        var rectangles = candidates.Select(point => Clamp(viewport, point, size)).ToArray();
        foreach (var rect in rectangles) if (obstacles.All(obstacle => !rect.IntersectsWith(obstacle))) return rect;
        return rectangles.OrderBy(rect => obstacles.Sum(obstacle => { var overlap = Rect.Intersect(rect, obstacle); return overlap.IsEmpty ? 0 : overlap.Width * overlap.Height; })).First();
    }
}
