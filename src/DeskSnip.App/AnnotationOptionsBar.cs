using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DeskSnip.Core;

namespace DeskSnip.App;

/// <summary>当前标注工具的子操作栏；只显示与该工具相关的样式控件，随工具切换重建。</summary>
internal sealed class AnnotationOptionsBar : FloatingToolbar
{
    private readonly AnnotationCanvas canvas;
    private readonly AppSettings settings;
    private readonly List<(Button Button, AnnotationKind Kind)> shapeButtons = new List<(Button, AnnotationKind)>();
    private readonly List<(Border Ring, uint Argb)> swatches = new List<(Border, uint)>();
    private readonly List<(MenuItem Item, AnnotationDash Value)> dashItems = new List<(MenuItem, AnnotationDash)>();
    private readonly List<(MenuItem Item, double Value)> widthItems = new List<(MenuItem, double)>();
    private readonly List<(MenuItem Item, AnnotationArrow Value)> arrowItems = new List<(MenuItem, AnnotationArrow)>();
    private readonly List<(MenuItem Item, double Value)> effectItems = new List<(MenuItem, double)>();
    private readonly List<(MenuItem Item, double Value)> opacityItems = new List<(MenuItem, double)>();
    private CheckBox? fill;
    private CheckBox? textBackground;
    private CheckBox? bold;
    private CheckBox? italic;
    private Button? dashButton;
    private Button? widthButton;
    private Button? arrowButton;
    private Button? effectButton;
    private Button? opacityButton;
    private ComboBox? fonts;
    private ComboBox? sizes;
    private ComboBox? radius;
    private bool synchronizing;

    internal AnnotationOptionsBar(AnnotationCanvas canvas, AppSettings settings)
    {
        this.canvas = canvas; this.settings = settings;
        Name = "AnnotationOptions";
        Padding = new Thickness(4);
        canvas.ToolChanged += Rebuild;
        canvas.Changed += RefreshState;
        Rebuild();
    }

    private void Rebuild()
    {
        Items.Children.Clear();
        shapeButtons.Clear(); swatches.Clear(); dashItems.Clear(); widthItems.Clear(); arrowItems.Clear(); effectItems.Clear(); opacityItems.Clear();
        fill = null; textBackground = null; bold = null; italic = null;
        dashButton = null; widthButton = null; arrowButton = null; effectButton = null; opacityButton = null;
        fonts = null; sizes = null; radius = null;
        var kind = canvas.Kind;
        if (!kind.HasValue) { Visibility = Visibility.Collapsed; return; }
        Visibility = Visibility.Visible;
        var value = kind.Value;
        if (IsShape(value))
        {
            AddShapeButtons();
            Items.Children.Add(FloatingTools.Divider());
            AddFill();
            AddDash();
            AddWidth();
            if (value != AnnotationKind.Ellipse) AddRadius();
            AddColors();
        }
        else if (value == AnnotationKind.Line || value == AnnotationKind.Arrow || value == AnnotationKind.Polyline)
        {
            AddDash();
            AddWidth();
            if (value == AnnotationKind.Arrow) AddArrow();
            AddColors();
        }
        else if (value == AnnotationKind.Pen || value == AnnotationKind.Highlight)
        {
            AddWidth();
            AddOpacity();
            AddColors();
        }
        else if (value == AnnotationKind.Text || value == AnnotationKind.Watermark)
        {
            AddFonts();
            AddSizes();
            AddBoldItalic();
            AddTextBackground();
            AddColors();
        }
        else if (value == AnnotationKind.Number)
        {
            AddWidth();
            AddColors();
        }
        else if (value == AnnotationKind.Mosaic || value == AnnotationKind.MosaicPen || value == AnnotationKind.Blur)
        {
            if (value == AnnotationKind.Blur) AddEffect(); else AddWidth();
        }
        else if (value == AnnotationKind.Cover || value == AnnotationKind.CoverPen)
        {
            if (value == AnnotationKind.CoverPen) AddWidth();
            AddColors();
        }
        else if (value == AnnotationKind.Eraser || value == AnnotationKind.EraserPen) AddWidth();
        else if (value == AnnotationKind.Spotlight || value == AnnotationKind.Magnifier)
        {
            AddWidth();
            if (value == AnnotationKind.Magnifier) AddEffect();
            AddColors();
        }
        RefreshState();
    }

    private static bool IsShape(AnnotationKind kind)
        => kind == AnnotationKind.Rectangle || kind == AnnotationKind.Ellipse || kind == AnnotationKind.RoundedRectangle;

    private void AddShapeButtons()
    {
        var presets = new[]
        {
            (Kind: AnnotationKind.Rectangle, Glyph: ToolGlyph.Rectangle, Name: L.Get("矩形")),
            (Kind: AnnotationKind.Ellipse, Glyph: ToolGlyph.Ellipse, Name: L.Get("椭圆")),
            (Kind: AnnotationKind.RoundedRectangle, Glyph: ToolGlyph.CornerRadius, Name: L.Get("圆角矩形"))
        };
        foreach (var preset in presets)
        {
            var kind = preset.Kind;
            var button = FloatingTools.Button(preset.Glyph, preset.Name, () =>
            {
                if (canvas.Kind == kind) return;
                AnnotationToolDefaults.Set("Rectangle", kind);
                canvas.SetTool(kind);
                FocusCanvas();
            });
            button.Width = 30; button.Height = 28;
            Items.Children.Add(button);
            shapeButtons.Add((button, kind));
        }
    }

    private void AddFill()
    {
        var box = new CheckBox { Content = L.Get("填充"), IsChecked = canvas.Appearance.Fill, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0), FontSize = 12, ToolTip = L.Get("填充") };
        AutomationProperties.SetName(box, L.Get("填充"));
        box.Click += (_, _) => { if (!synchronizing) ChangeAppearance(fill: box.IsChecked == true); };
        fill = box; Items.Children.Add(box);
    }

    private void AddDash()
    {
        var menu = FloatingTools.Menu();
        foreach (var preset in new[] { (L.Get("实线"), AnnotationDash.Solid), (L.Get("虚线"), AnnotationDash.Dash), (L.Get("点线"), AnnotationDash.Dot) })
        {
            var value = preset.Item2;
            var item = new MenuItem { Header = LinePreview(preset.Item1, value), Padding = new Thickness(8, 5, 8, 5), IsCheckable = true, Tag = value };
            item.Click += (_, _) => { ChangeAppearance(dash: value); };
            menu.Items.Add(item); dashItems.Add((item, value));
        }
        menu.Opened += (_, _) => { foreach (var pair in dashItems) pair.Item.IsChecked = canvas.Appearance.Dash == pair.Value; };
        dashButton = FloatingTools.MenuButton(ToolGlyph.LineSolid, L.Get("线型"), menu);
        dashButton.Width = 44; dashButton.Height = 28;
        Items.Children.Add(dashButton);
    }

    private static StackPanel LinePreview(string label, AnnotationDash dash)
    {
        var line = new Line
        {
            X1 = 0, X2 = 42, Y1 = 2, Y2 = 2, Stroke = FloatingTools.Ink, StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = 42, Height = 4, Stretch = Stretch.None,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0)
        };
        if (dash == AnnotationDash.Dash) line.StrokeDashArray = new DoubleCollection { 4, 2 };
        else if (dash == AnnotationDash.Dot) line.StrokeDashArray = new DoubleCollection { 1, 1.6 };
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { line, new TextBlock { Text = label, Foreground = FloatingTools.Ink, VerticalAlignment = VerticalAlignment.Center } } };
    }

    private void AddWidth()
    {
        var menu = FloatingTools.Menu();
        foreach (var width in new[] { 2d, 3d, 5d, 8d, 12d, 20d })
        {
            var value = width;
            var item = new MenuItem { Header = WidthPreview(value), Padding = new Thickness(8, 5, 8, 5), IsCheckable = true, Tag = value };
            item.Click += (_, _) => { canvas.ChangeStyle(thickness: value); FocusCanvas(); };
            menu.Items.Add(item); widthItems.Add((item, value));
        }
        menu.Opened += (_, _) => { foreach (var pair in widthItems) pair.Item.IsChecked = Math.Abs(canvas.Thickness - pair.Value) < .01; };
        widthButton = FloatingTools.MenuButton(ToolGlyph.Width, L.Get("线宽（px）"), menu);
        widthButton.Width = 44; widthButton.Height = 28;
        Items.Children.Add(widthButton);
    }

    private static StackPanel WidthPreview(double width)
    {
        var line = new Border
        {
            Width = 42, Height = Math.Min(8, Math.Max(1, width)), Background = FloatingTools.Ink, CornerRadius = new CornerRadius(Math.Min(4, width / 2)),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0)
        };
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { line, new TextBlock { Text = width.ToString(CultureInfo.InvariantCulture), Foreground = FloatingTools.Ink, VerticalAlignment = VerticalAlignment.Center } } };
    }

    private void AddRadius()
    {
        var values = new[] { "0", "2", "4", "6", "8", "12", "16", "24", "32", "48" };
        var box = new ComboBox
        {
            Width = 56, Height = 26, IsEditable = true, ItemsSource = values, FontSize = 11, VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 2, 0), ToolTip = L.Get("圆角半径")
        };
        AutomationProperties.SetName(box, L.Get("圆角半径"));
        box.LostKeyboardFocus += (_, _) => ApplyRadius();
        box.DropDownClosed += (_, _) => ApplyRadius();
        box.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyRadius(); e.Handled = true; } };
        var group = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) };
        group.Children.Add(new ToolIcon(ToolGlyph.CornerRadius) { Width = 15, Height = 15, Color = FloatingTools.MutedInk, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        group.Children.Add(box);
        radius = box; Items.Children.Add(group);
    }

    private void ApplyRadius()
    {
        if (synchronizing || radius == null) return;
        if (!double.TryParse(radius.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0 || value > 400) { RefreshState(); return; }
        ChangeAppearance(cornerRadius: value);
    }

    private void AddArrow()
    {
        var menu = FloatingTools.Menu();
        foreach (var preset in new[] { (L.Get("实心箭头"), AnnotationArrow.Filled), (L.Get("轮廓箭头"), AnnotationArrow.Outline), (L.Get("双向箭头"), AnnotationArrow.Double), (L.Get("尺寸线"), AnnotationArrow.Dimension) })
        {
            var value = preset.Item2;
            var item = FloatingTools.Item(preset.Item1, () => ChangeAppearance(arrow: value)); item.IsCheckable = true; item.Tag = value;
            menu.Items.Add(item); arrowItems.Add((item, value));
        }
        menu.Opened += (_, _) => { foreach (var pair in arrowItems) pair.Item.IsChecked = canvas.Appearance.Arrow == pair.Value; };
        arrowButton = FloatingTools.MenuButton(ToolGlyph.Arrow, L.Get("箭头样式"), menu);
        arrowButton.Width = 44; arrowButton.Height = 28;
        Items.Children.Add(arrowButton);
    }

    private void AddEffect()
    {
        var menu = FloatingTools.Menu();
        foreach (var effect in new[] { 1d, 2d, 3d, 4d, 8d, 16d })
        {
            var value = effect;
            var item = FloatingTools.Item(value.ToString(CultureInfo.InvariantCulture), () => ChangeAppearance(effect: value)); item.IsCheckable = true; item.Tag = value;
            menu.Items.Add(item); effectItems.Add((item, value));
        }
        menu.Opened += (_, _) => { foreach (var pair in effectItems) pair.Item.IsChecked = Math.Abs(canvas.Appearance.Effect - pair.Value) < .001; };
        effectButton = FloatingTools.MenuButton(ToolGlyph.Zoom, L.Get("效果强度 / 放大倍率"), menu);
        effectButton.Width = 44; effectButton.Height = 28;
        Items.Children.Add(effectButton);
    }

    private void AddOpacity()
    {
        var menu = FloatingTools.Menu();
        foreach (var preset in new[] { ("100%", 1d), ("75%", .75), ("50%", .5), ("25%", .25) })
        {
            var value = preset.Item2;
            var item = FloatingTools.Item(preset.Item1, () => ChangeAppearance(opacity: value)); item.IsCheckable = true; item.Tag = value;
            menu.Items.Add(item); opacityItems.Add((item, value));
        }
        menu.Opened += (_, _) => { foreach (var pair in opacityItems) pair.Item.IsChecked = Math.Abs(canvas.Appearance.Opacity - pair.Value) < .001; };
        opacityButton = FloatingTools.MenuButton(ToolGlyph.Opacity, L.Get("不透明度"), menu);
        opacityButton.Width = 44; opacityButton.Height = 28;
        Items.Children.Add(opacityButton);
    }

    private void AddFonts()
    {
        var families = new[] { "Microsoft YaHei UI", "SimSun", "Arial", "Segoe UI", "Consolas", "Times New Roman", "Calibri", "Verdana" };
        var box = new ComboBox { Width = 132, Height = 26, IsEditable = true, ItemsSource = families, Text = canvas.FontFamily, FontSize = 11, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), ToolTip = L.Get("字体") };
        AutomationProperties.SetName(box, L.Get("字体"));
        box.LostKeyboardFocus += (_, _) => ApplyFont();
        box.DropDownClosed += (_, _) => ApplyFont();
        box.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyFont(); e.Handled = true; } };
        fonts = box; Items.Children.Add(box);
    }

    private void AddSizes()
    {
        var box = new ComboBox { Width = 54, Height = 26, IsEditable = true, ItemsSource = new[] { "14", "18", "24", "28", "36", "48", "72" }, FontSize = 11, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), ToolTip = L.Get("字号") };
        AutomationProperties.SetName(box, L.Get("字号"));
        box.LostKeyboardFocus += (_, _) => ApplyFont();
        box.DropDownClosed += (_, _) => ApplyFont();
        box.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyFont(); e.Handled = true; } };
        sizes = box; Items.Children.Add(box);
    }

    private void ApplyFont()
    {
        if (synchronizing || fonts == null || sizes == null) return;
        if (string.IsNullOrWhiteSpace(fonts.Text)) return;
        if (!double.TryParse(sizes.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) || size < 1 || size > 480) { RefreshState(); return; }
        canvas.ChangeStyle(fontFamily: fonts.Text, fontSize: size);
        FocusCanvas();
    }

    private void AddBoldItalic()
    {
        var boldBox = new CheckBox { Content = L.Get("粗体"), IsChecked = canvas.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0), FontSize = 12 };
        var italicBox = new CheckBox { Content = L.Get("斜体"), IsChecked = canvas.Italic, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0), FontSize = 12 };
        boldBox.Click += (_, _) => { if (!synchronizing) { canvas.ChangeStyle(bold: boldBox.IsChecked == true); FocusCanvas(); } };
        italicBox.Click += (_, _) => { if (!synchronizing) { canvas.ChangeStyle(italic: italicBox.IsChecked == true); FocusCanvas(); } };
        bold = boldBox; italic = italicBox;
        Items.Children.Add(boldBox); Items.Children.Add(italicBox);
    }

    private void AddTextBackground()
    {
        var box = new CheckBox { Content = L.Get("文字底色"), IsChecked = canvas.Appearance.TextBackground, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0), FontSize = 12 };
        box.Click += (_, _) => { if (!synchronizing) ChangeAppearance(textBackground: box.IsChecked == true); };
        textBackground = box; Items.Children.Add(box);
    }

    private void AddColors()
    {
        Items.Children.Add(FloatingTools.Divider());
        foreach (var hex in settings.MarkColorPalette.Split(','))
        {
            if (!TryColor(hex, out var color)) continue;
            var argb = 0xff000000u | (uint)color.R << 16 | (uint)color.G << 8 | color.B;
            var inner = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(color), BorderBrush = FloatingTools.Edge, BorderThickness = new Thickness(1) };
            var ring = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Margin = new Thickness(2, 0, 2, 0), Child = inner, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, ToolTip = hex, Background = Brushes.Transparent };
            AutomationProperties.SetName(ring, L.Get("颜色") + " " + hex);
            var value = argb;
            ring.MouseLeftButtonUp += (_, e) => { canvas.ChangeStyle(argb: value); FocusCanvas(); e.Handled = true; };
            Items.Children.Add(ring);
            swatches.Add((ring, argb));
        }
    }

    private static bool TryColor(string hex, out Color color)
    {
        color = default;
        if (hex == null || hex.Length != 7 || hex[0] != '#') return false;
        if (!uint.TryParse(hex.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) return false;
        color = Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    private void ChangeAppearance(bool? fill = null, AnnotationDash? dash = null, AnnotationArrow? arrow = null, double? effect = null, double? opacity = null, bool? textBackground = null, double? cornerRadius = null)
    {
        var current = canvas.Appearance;
        var next = current.WithEditorStyle(fill ?? current.Fill, dash ?? current.Dash, opacity ?? current.Opacity, arrow ?? current.Arrow, effect ?? current.Effect, textBackground ?? current.TextBackground, cornerRadius);
        if (Same(current, next)) return;
        canvas.ChangeAppearance(next);
        FocusCanvas();
    }
    private static bool Same(AnnotationAppearance left, AnnotationAppearance right)
        => left.Fill == right.Fill && left.Dash == right.Dash && left.Opacity == right.Opacity && left.Arrow == right.Arrow && left.Effect == right.Effect && left.TextBackground == right.TextBackground && left.CornerRadius == right.CornerRadius;

    private void RefreshState()
    {
        if (synchronizing) return;
        if (!canvas.Kind.HasValue) { Visibility = Visibility.Collapsed; return; }
        if (Visibility != Visibility.Visible) Visibility = Visibility.Visible;
        synchronizing = true;
        try
        {
            foreach (var pair in shapeButtons) FloatingTools.SetSelected(pair.Button, pair.Kind == canvas.Kind);
            if (fill != null) fill.IsChecked = canvas.Appearance.Fill;
            if (textBackground != null) textBackground.IsChecked = canvas.Appearance.TextBackground;
            if (bold != null) bold.IsChecked = canvas.Bold;
            if (italic != null) italic.IsChecked = canvas.Italic;
            if (dashButton != null) FloatingTools.SetMenuIcon(dashButton, canvas.Appearance.Dash == AnnotationDash.Dash ? ToolGlyph.LineDash : canvas.Appearance.Dash == AnnotationDash.Dot ? ToolGlyph.LineDot : ToolGlyph.LineSolid);
            foreach (var pair in swatches) pair.Ring.BorderBrush = pair.Argb == canvas.Argb ? Ui.Accent : Brushes.Transparent;
            foreach (var pair in dashItems) pair.Item.IsChecked = canvas.Appearance.Dash == pair.Value;
            foreach (var pair in widthItems) pair.Item.IsChecked = Math.Abs(canvas.Thickness - pair.Value) < .01;
            foreach (var pair in arrowItems) pair.Item.IsChecked = canvas.Appearance.Arrow == pair.Value;
            foreach (var pair in effectItems) pair.Item.IsChecked = Math.Abs(canvas.Appearance.Effect - pair.Value) < .001;
            foreach (var pair in opacityItems) pair.Item.IsChecked = Math.Abs(canvas.Appearance.Opacity - pair.Value) < .001;
            if (fonts != null && !fonts.IsKeyboardFocusWithin) fonts.Text = canvas.FontFamily;
            if (sizes != null && !sizes.IsKeyboardFocusWithin) sizes.Text = canvas.FontSize.ToString(CultureInfo.InvariantCulture);
            if (radius != null && !radius.IsKeyboardFocusWithin) radius.Text = Math.Round(canvas.Appearance.CornerRadius).ToString(CultureInfo.InvariantCulture);
        }
        finally { synchronizing = false; }
    }

    private void FocusCanvas()
    {
        canvas.Dispatcher.BeginInvoke(new Action(() => { Window.GetWindow(canvas)?.Activate(); canvas.Focus(); Keyboard.Focus(canvas); }));
    }
}
