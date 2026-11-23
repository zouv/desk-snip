using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DeskSnip.Core;

namespace DeskSnip.App;

/// <summary>截图选区和钉图共用的就地标注工具栏；工具以当前标注画布为作用对象。</summary>
internal sealed class InlineAnnotationBar : StackPanel
{
    private readonly AnnotationCanvas canvas;
    private readonly FloatingToolbar toolbar;
    private readonly AnnotationOptionsBar options;
    private readonly List<(Button Button, AnnotationKind?[] Kinds)> toolButtons = new List<(Button, AnnotationKind?[])>();
    private readonly List<Action> groupLabels = new List<Action>();
    private readonly Button undo;
    private readonly Button redo;
    internal event Action? Finished;
    internal event Action? Cancelled;
    internal WrapPanel Items => toolbar.Items;
    internal AnnotationOptionsBar Options => options;
    internal InlineAnnotationBar(AnnotationCanvas canvas, AppSettings settings, bool withFinish, bool showShortcuts)
    {
        this.canvas = canvas;
        Name = "InlineAnnotationTools"; Orientation = Orientation.Vertical; HorizontalAlignment = HorizontalAlignment.Left;
        toolbar = new FloatingToolbar { Name = "InlineAnnotationToolsMain", HorizontalAlignment = HorizontalAlignment.Left };
        Children.Add(toolbar);
        var select = FloatingTools.Button(ToolGlyph.Select, L.Get("选择"), () => SelectTool(null));
        Items.Children.Add(select); toolButtons.Add((select, new AnnotationKind?[] { null }));
        AddGroup(ToolGlyph.Rectangle, L.Get("形状工具"), "Rectangle");
        AddGroup(ToolGlyph.Arrow, L.Get("箭头与线条"), "Arrow");
        AddGroup(ToolGlyph.Brush, L.Get("画笔与高亮"), "Pen");
        AddGroup(ToolGlyph.Text, L.Get("文字与水印"), "Text");
        var number = FloatingTools.Button(ToolGlyph.Number, L.Get("序号"), () => SelectTool(AnnotationKind.Number));
        Items.Children.Add(number); toolButtons.Add((number, new AnnotationKind?[] { AnnotationKind.Number }));
        AddGroup(ToolGlyph.Mosaic, L.Get("马赛克与模糊"), "Mosaic");
        AddGroup(ToolGlyph.Cover, L.Get("遮挡工具"), "Cover");
        AddGroup(ToolGlyph.Eraser, L.Get("橡皮擦"), "Eraser");
        AddGroup(ToolGlyph.Spotlight, L.Get("聚光与放大"), "Spotlight");
        Items.Children.Add(FloatingTools.Divider());
        undo = FloatingTools.Button(ToolGlyph.Undo, L.Get("撤销"), () => { canvas.Document.Undo(); canvas.Refresh(); FocusCanvas(); }, "Ctrl+Z");
        redo = FloatingTools.Button(ToolGlyph.Redo, L.Get("重做"), () => { canvas.Document.Redo(); canvas.Refresh(); FocusCanvas(); }, "Ctrl+Y");
        Items.Children.Add(undo); Items.Children.Add(redo);
        Items.Children.Add(FloatingTools.MenuButton(ToolGlyph.Palette, L.Get("标注样式"), StyleMenu(settings)));
        if (withFinish)
        {
            Items.Children.Add(FloatingTools.Divider());
            Items.Children.Add(FloatingTools.Button(ToolGlyph.Check, L.Get("完成标注"), () => Finished?.Invoke(), "Space", true));
            Items.Children.Add(FloatingTools.Button(ToolGlyph.Close, L.Get("取消标注"), () => Cancelled?.Invoke(), "Esc"));
        }
        FloatingTools.Configure(Items, settings.ToolbarIconSize, showShortcuts);
        options = new AnnotationOptionsBar(canvas, settings) { Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        Children.Add(options);
        canvas.ToolChanged += UpdateSelection;
        canvas.Changed += UpdateHistory;
        UpdateSelection(); UpdateHistory();
    }
    private void SelectTool(AnnotationKind? kind)
    {
        canvas.SetTool(kind); FocusCanvas();
    }
    private void AddGroup(ToolGlyph glyph, string name, string group)
    {
        var kinds = AnnotationPreferenceRules.GroupTools(group);
        var menu = FloatingTools.Menu();
        Button button = null!;
        foreach (var kind in kinds)
        {
            var tool = kind;
            var item = FloatingTools.Item(EditorWindow.ToolNames[(int)tool], () => { AnnotationToolDefaults.Set(group, tool); SelectTool(tool); UpdateGroupLabel(); }); item.Tag = tool; menu.Items.Add(item);
        }
        menu.Opened += (_, _) => { for (var i = 0; i < kinds.Length; i++) ((MenuItem)menu.Items[i]).IsChecked = canvas.Kind == kinds[i]; };
        button = FloatingTools.SplitMenuButton(glyph, name, () => SelectTool(AnnotationToolDefaults.Get(group, kinds[0])), menu);
        Items.Children.Add(button); toolButtons.Add((button, kinds.Select(kind => (AnnotationKind?)kind).ToArray()));
        void UpdateGroupLabel()
        {
            var label = AnnotationToolDefaults.Label(name, AnnotationToolDefaults.Get(group, kinds[0]));
            AutomationProperties.SetName(button, label); button.ToolTip = label;
        }
        UpdateGroupLabel(); groupLabels.Add(UpdateGroupLabel);
    }
    private ContextMenu StyleMenu(AppSettings settings)
    {
        var menu = FloatingTools.Menu();
        void Apply(bool? fill = null, AnnotationDash? dash = null, double? opacity = null, AnnotationArrow? arrow = null, double? effect = null, bool? textBackground = null, bool? bold = null, bool? italic = null)
        {
            var current = canvas.Appearance;
            canvas.ChangeStyle(bold: bold, italic: italic, appearance: current.WithEditorStyle(fill ?? current.Fill, dash ?? current.Dash, opacity ?? current.Opacity, arrow ?? current.Arrow, effect ?? current.Effect, textBackground ?? current.TextBackground));
            FocusCanvas();
        }
        foreach (var hex in settings.MarkColorPalette.Split(','))
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var argb = 0xff000000u | (uint)color.R << 16 | (uint)color.G << 8 | color.B;
            var item = FloatingTools.Item(hex, () => { canvas.ChangeStyle(argb: argb); FocusCanvas(); });
            item.Header = new StackPanel { Orientation = Orientation.Horizontal, Children = { new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(color), BorderBrush = FloatingTools.Edge, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0) }, new TextBlock { Text = hex, Foreground = FloatingTools.Ink, VerticalAlignment = VerticalAlignment.Center } } };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        foreach (var width in new[] { 2d, 3d, 5d, 8d, 12d, 20d })
        {
            var value = width;
            menu.Items.Add(FloatingTools.Item(width.ToString(CultureInfo.InvariantCulture), () => { canvas.ChangeStyle(thickness: value); FocusCanvas(); }));
        }
        var boldItem = FloatingTools.Item(L.Get("粗体"), () => Apply(bold: !canvas.Bold)); boldItem.IsCheckable = true; menu.Items.Add(boldItem);
        var italicItem = FloatingTools.Item(L.Get("斜体"), () => Apply(italic: !canvas.Italic)); italicItem.IsCheckable = true; menu.Items.Add(italicItem);
        var fontItems = new List<(MenuItem Item, string Value)>();
        var fontMenu = new MenuItem { Header = L.Get("字体") };
        foreach (var family in new[] { "Microsoft YaHei UI", "SimSun", "Arial", "Segoe UI", "Consolas", "Times New Roman", "Calibri", "Verdana" })
        {
            var name = family; var item = FloatingTools.Item(name, () => { canvas.ChangeStyle(fontFamily: name); FocusCanvas(); }); item.IsCheckable = true; fontMenu.Items.Add(item); fontItems.Add((item, name));
        }
        menu.Items.Add(fontMenu);
        var sizeItems = new List<(MenuItem Item, double Value)>();
        var sizeMenu = new MenuItem { Header = L.Get("字号") };
        foreach (var size in new[] { 14d, 18d, 24d, 28d, 36d, 48d, 72d })
        {
            var value = size; var item = FloatingTools.Item(size.ToString(CultureInfo.InvariantCulture), () => { canvas.ChangeStyle(fontSize: value); FocusCanvas(); }); item.IsCheckable = true; sizeMenu.Items.Add(item); sizeItems.Add((item, value));
        }
        menu.Items.Add(sizeMenu);
        var squareItem = FloatingTools.Item(L.Get("方角"), () => { canvas.ChangeCorner(false); FocusCanvas(); }); squareItem.IsCheckable = true; menu.Items.Add(squareItem);
        var roundItem = FloatingTools.Item(L.Get("圆角"), () => { canvas.ChangeCorner(true); FocusCanvas(); }); roundItem.IsCheckable = true; menu.Items.Add(roundItem);
        menu.Items.Add(new Separator());
        var fillItem = FloatingTools.Item(L.Get("填充"), () => Apply(fill: !canvas.Appearance.Fill)); fillItem.IsCheckable = true; menu.Items.Add(fillItem);
        var backgroundItem = FloatingTools.Item(L.Get("文字底色"), () => Apply(textBackground: !canvas.Appearance.TextBackground)); backgroundItem.IsCheckable = true; menu.Items.Add(backgroundItem);
        var dashItems = new List<(MenuItem Item, AnnotationDash Value)>();
        var dashMenu = new MenuItem { Header = L.Get("线型") };
        foreach (var pair in new[] { (L.Get("实线"), AnnotationDash.Solid), (L.Get("虚线"), AnnotationDash.Dash), (L.Get("点线"), AnnotationDash.Dot) })
        {
            var value = pair.Item2; var item = FloatingTools.Item(pair.Item1, () => Apply(dash: value)); item.IsCheckable = true; dashMenu.Items.Add(item); dashItems.Add((item, value));
        }
        menu.Items.Add(dashMenu);
        var arrowItems = new List<(MenuItem Item, AnnotationArrow Value)>();
        var arrowMenu = new MenuItem { Header = L.Get("箭头样式") };
        foreach (var pair in new[] { (L.Get("实心箭头"), AnnotationArrow.Filled), (L.Get("轮廓箭头"), AnnotationArrow.Outline), (L.Get("双向箭头"), AnnotationArrow.Double), (L.Get("尺寸线"), AnnotationArrow.Dimension) })
        {
            var value = pair.Item2; var item = FloatingTools.Item(pair.Item1, () => Apply(arrow: value)); item.IsCheckable = true; arrowMenu.Items.Add(item); arrowItems.Add((item, value));
        }
        menu.Items.Add(arrowMenu);
        var opacityItems = new List<(MenuItem Item, double Value)>();
        var opacityMenu = new MenuItem { Header = L.Get("不透明度") };
        foreach (var pair in new[] { ("100%", 1d), ("75%", .75), ("50%", .5), ("25%", .25) })
        {
            var value = pair.Item2; var item = FloatingTools.Item(pair.Item1, () => Apply(opacity: value)); item.IsCheckable = true; opacityMenu.Items.Add(item); opacityItems.Add((item, value));
        }
        menu.Items.Add(opacityMenu);
        var effectItems = new List<(MenuItem Item, double Value)>();
        var effectMenu = new MenuItem { Header = L.Get("效果强度 / 放大倍率") };
        foreach (var value in new[] { 1d, 2d, 3d, 4d, 8d, 16d })
        {
            var effect = value; var item = FloatingTools.Item(value.ToString(CultureInfo.InvariantCulture), () => Apply(effect: effect)); item.IsCheckable = true; effectMenu.Items.Add(item); effectItems.Add((item, effect));
        }
        menu.Items.Add(effectMenu);
        menu.Opened += (_, _) =>
        {
            var current = canvas.Appearance;
            boldItem.IsChecked = canvas.Bold; italicItem.IsChecked = canvas.Italic;
            foreach (var pair in fontItems) pair.Item.IsChecked = string.Equals(pair.Value, canvas.FontFamily, StringComparison.OrdinalIgnoreCase);
            foreach (var pair in sizeItems) pair.Item.IsChecked = Math.Abs(canvas.FontSize - pair.Value) < .01;
            squareItem.IsChecked = canvas.Selected?.Kind == AnnotationKind.Rectangle; roundItem.IsChecked = canvas.Selected?.Kind == AnnotationKind.RoundedRectangle;
            fillItem.IsChecked = current.Fill; backgroundItem.IsChecked = current.TextBackground;
            foreach (var pair in dashItems) pair.Item.IsChecked = current.Dash == pair.Value;
            foreach (var pair in arrowItems) pair.Item.IsChecked = current.Arrow == pair.Value;
            foreach (var pair in opacityItems) pair.Item.IsChecked = Math.Abs(current.Opacity - pair.Value) < .001;
            foreach (var pair in effectItems) pair.Item.IsChecked = Math.Abs(current.Effect - pair.Value) < .001;
        };
        return menu;
    }
    private void UpdateSelection()
    {
        foreach (var pair in toolButtons) FloatingTools.SetSelected(pair.Button, pair.Kinds.Contains(canvas.Kind));
        foreach (var update in groupLabels) update();
    }
    private void UpdateHistory()
    {
        undo.IsEnabled = canvas.Document.CanUndo; redo.IsEnabled = canvas.Document.CanRedo;
    }
    private void FocusCanvas()
    {
        canvas.Dispatcher.BeginInvoke(new Action(() => { Window.GetWindow(canvas)?.Activate(); canvas.Focus(); Keyboard.Focus(canvas); }));
    }
}
