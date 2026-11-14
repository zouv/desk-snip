using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DeskSnip.Core;

namespace DeskSnip.App;

internal sealed class SelectionInfoBar : Border
{
    private readonly PixelRect bounds;
    private readonly Action<PixelRect> apply;
    private readonly TextBlock hint = Ui.Text("", 11, Brushes.White);
    private readonly StackPanel inputs = new StackPanel { Margin = new Thickness(5, 8, 5, 4), Visibility = Visibility.Collapsed };
    private readonly Button summary;
    private readonly TextBox x;
    private readonly TextBox y;
    private readonly TextBox width;
    private readonly TextBox height;
    private PixelRect shown;
    private string notice = "";
    internal event Action? LayoutChanged;
    internal bool IsExpanded => inputs.Visibility == Visibility.Visible;
    internal SelectionInfoBar(PixelRect bounds, Action<PixelRect> apply, Action<bool> lockRatio, Action<bool> showGrid)
    {
        this.bounds = bounds; this.apply = apply;
        Background = new SolidColorBrush(Color.FromArgb(240, 25, 30, 37)); CornerRadius = new CornerRadius(5); Padding = new Thickness(4, 2, 4, 2); Cursor = Cursors.Arrow;
        var content = new StackPanel(); Child = content;
        summary = new Button { Background = Brushes.Transparent, BorderThickness = new Thickness(0), MinHeight = 22, Padding = new Thickness(5, 2, 5, 2), Cursor = Cursors.Hand, Foreground = Brushes.White };
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(5, 2, 5, 2)); summary.Template = new ControlTemplate(typeof(Button)) { VisualTree = presenter };
        var heading = new StackPanel { Orientation = Orientation.Horizontal }; hint.TextWrapping = TextWrapping.NoWrap; hint.TextTrimming = TextTrimming.CharacterEllipsis; hint.MaxWidth = 300; hint.VerticalAlignment = VerticalAlignment.Center; heading.Children.Add(hint);
        heading.Children.Add(new ToolIcon(ToolGlyph.Chevron) { Width = 10, Height = 12, Color = Brushes.White, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }); summary.Content = heading;
        System.Windows.Automation.AutomationProperties.SetName(summary, L.Get("选区位置与尺寸")); summary.ToolTip = L.Get("点击调整 X / Y / 宽 / 高、锁定比例和九宫格");
        summary.Click += (_, _) => SetExpanded(!IsExpanded); content.Children.Add(summary); content.Children.Add(inputs);
        var first = new WrapPanel(); var second = new WrapPanel(); inputs.Children.Add(first); inputs.Children.Add(second);
        x = Input(first, "X", L.Get("选区 X 坐标")); y = Input(first, "Y", L.Get("选区 Y 坐标"));
        width = Input(second, L.Get("宽"), L.Get("选区宽度")); height = Input(second, L.Get("高"), L.Get("选区高度"));
        var options = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) }; inputs.Children.Add(options);
        var ratio = new CheckBox { Content = L.Get("锁定比例"), Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        ratio.Checked += (_, _) => lockRatio(true); ratio.Unchecked += (_, _) => lockRatio(false); options.Children.Add(ratio);
        var grid = new CheckBox { Content = L.Get("九宫格"), Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        grid.Checked += (_, _) => showGrid(true); grid.Unchecked += (_, _) => showGrid(false); options.Children.Add(grid);
        var confirm = Ui.Button(L.Get("应用"), Apply); confirm.MinHeight = 28; confirm.Padding = new Thickness(9, 3, 9, 3); confirm.Background = FloatingTools.Blue; confirm.Foreground = Brushes.White; confirm.BorderThickness = new Thickness(0); options.Children.Add(confirm);
    }
    private TextBox Input(Panel row, string label, string name)
    {
        var field = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 5, 3) };
        var text = Ui.Text(label, 11, Brushes.White); text.Width = 19; text.VerticalAlignment = VerticalAlignment.Center; field.Children.Add(text);
        var box = new TextBox { Width = 84, MinHeight = 25, Padding = new Thickness(5, 3, 5, 3), VerticalContentAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(box, name);
        box.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { Apply(); e.Handled = true; } };
        field.Children.Add(box); row.Children.Add(field); return box;
    }
    internal void SetExpanded(bool expanded)
    {
        inputs.Visibility = expanded && !shown.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        LayoutChanged?.Invoke();
    }
    internal void Update(PixelRect selection, string color)
    {
        if (!selection.Equals(shown)) notice = "";
        hint.Text = notice.Length > 0 ? notice : selection.IsEmpty ? L.Get("拖动框选区域 · Esc 取消") : $"{selection.X},{selection.Y}  {selection.Width} × {selection.Height} px";
        hint.ToolTip = notice.Length > 0 ? notice : L.Format("当前颜色 {0} · Shift 正方形 · Alt 对称调整",color);
        if (selection.Equals(shown)) return;
        shown = selection;
        if (selection.IsEmpty) inputs.Visibility = Visibility.Collapsed;
        x.Text = selection.X.ToString(CultureInfo.InvariantCulture); y.Text = selection.Y.ToString(CultureInfo.InvariantCulture);
        width.Text = selection.Width.ToString(CultureInfo.InvariantCulture); height.Text = selection.Height.ToString(CultureInfo.InvariantCulture);
    }
    internal void Notice(string text) { notice = text; hint.Text = text; hint.ToolTip = text; LayoutChanged?.Invoke(); }
    private void Apply()
    {
        if (!SelectionGeometry.TryParse(x.Text, y.Text, width.Text, height.Text, bounds, out var selection))
        { Notice(L.Get("坐标或尺寸无效，请输入桌面范围内的整数。")); return; }
        notice = ""; apply(selection); SetExpanded(false);
    }
}
