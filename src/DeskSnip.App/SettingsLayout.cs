using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Globalization;

namespace DeskSnip.App;

/// <summary>设置页共用的卡片、对齐行和侧栏；颜色从窗口资源读取，支持独立主题预览。</summary>
internal static class SettingsLayout
{
    internal static TextBlock Text(string value, double size = 13, string brush = "InkBrush")
    {
        var text = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.ForegroundProperty, brush); return text;
    }
    internal static StackPanel Section(Panel page, string title, string description = "")
    {
        var heading = Text(title, 14); heading.Margin = new Thickness(2, 0, 2, 10); page.Children.Add(heading);
        var items = new StackPanel(); var card = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(16, 2, 16, 2), Margin = new Thickness(0, 0, 0, 23), Child = items };
        card.SetResourceReference(Border.BackgroundProperty, "PanelBrush"); page.Children.Add(card);
        if (description.Length > 0) { var note = Text(description, 11, "SubtleBrush"); note.Margin = new Thickness(0, 12, 0, 9); items.Children.Add(note); }
        return items;
    }
    internal static TextBlock Row(Panel group, string label, FrameworkElement editor, string description = "", double minimumEditorWidth = 0)
    {
        var row = new Grid { MinHeight = 54, Margin = new Thickness(0, 1, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.51, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.49, GridUnitType.Star), MinWidth = minimumEditorWidth });
        var title = new StackPanel { Margin = new Thickness(0, 10, 16, 10), VerticalAlignment = VerticalAlignment.Center }; var text = Text(label); title.Children.Add(text);
        if (description.Length > 0) { var detail = Text(description, 11, "SubtleBrush"); detail.Margin = new Thickness(0, 3, 0, 0); title.Children.Add(detail); }
        row.Children.Add(title); var host = new Grid(); Grid.SetColumn(host, 1); row.Children.Add(host); editor.VerticalAlignment = VerticalAlignment.Center; editor.Margin = new Thickness(0, 8, 0, 8);
        FitLeft(editor, host); AddRow(group, row); return text;
    }
    internal static void FitLeft(FrameworkElement editor, Grid host)
    {
        editor.HorizontalAlignment = HorizontalAlignment.Left; editor.SetBinding(FrameworkElement.WidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = host }); host.Children.Add(editor);
    }
    internal static void FullRow(Panel group, FrameworkElement content)
    {
        content.Margin = new Thickness(0, 12, 0, 12); AddRow(group, content);
    }
    private static void AddRow(Panel group, FrameworkElement content)
    {
        if (group.Children.Count > 0) { var divider = new Border { Height = 1, Opacity = .45 }; divider.SetResourceReference(Border.BackgroundProperty, "BorderBrush"); group.Children.Add(divider); }
        group.Children.Add(content);
    }
    internal static void Input(Control control, string id, string label, double maxWidth = 280)
    {
        control.MinHeight = 32; control.Padding = new Thickness(8, 5, 8, 5); control.HorizontalAlignment = HorizontalAlignment.Stretch; control.HorizontalContentAlignment = HorizontalAlignment.Left; control.MaxWidth = maxWidth;
        control.SetResourceReference(Control.BackgroundProperty, "PanelBrush"); control.SetResourceReference(Control.ForegroundProperty, "InkBrush"); control.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        AutomationProperties.SetAutomationId(control, id); AutomationProperties.SetName(control, label);
        if (control is TextBox box) { box.VerticalContentAlignment = VerticalAlignment.Center; box.SetResourceReference(TextBox.CaretBrushProperty, "InkBrush"); }
    }
    internal static FrameworkElement NumberInput(TextBox input, string label, double minimum, double maximum, double step = 1)
    {
        input.BorderThickness = new Thickness(0); input.Padding = new Thickness(8, 4, 3, 4); input.MaxWidth = double.PositiveInfinity;
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.Children.Add(input);
        var arrows = new StackPanel { Width = 22 }; Grid.SetColumn(arrows, 1); grid.Children.Add(arrows);
        void Change(int direction)
        {
            if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || double.IsNaN(value) || double.IsInfinity(value)) return;
            input.Text = Math.Max(minimum, Math.Min(maximum, value + direction * step)).ToString(CultureInfo.InvariantCulture);
        }
        foreach (var direction in new[] { 1, -1 })
        {
            var button = Button("", () => Change(direction)); button.MinHeight = 0; button.Height = 16; button.Padding = new Thickness(0); button.BorderThickness = new Thickness(0); button.Focusable = false;
            button.Content = new System.Windows.Shapes.Path { Data = Geometry.Parse(direction > 0 ? "M 0,4 L 4,0 L 8,4" : "M 0,0 L 4,4 L 8,0"), StrokeThickness = 1.2, Stroke = input.Foreground, Width = 8, Height = 4 }; AutomationProperties.SetName(button, L.Format("{0}：{1}", direction > 0 ? L.Get("增加") : L.Get("减少"), label)); arrows.Children.Add(button);
        }
        var border = new Border { Child = grid, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), MaxWidth = 112 }; border.SetResourceReference(Border.BackgroundProperty, "PanelBrush"); border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); return border;
    }
    internal static Button Button(string label, Action action, bool primary = false)
    {
        var button = Ui.Button(label, action); button.MinHeight = 32; button.Padding = new Thickness(12, 5, 12, 5); button.Margin = new Thickness(0); button.BorderThickness = new Thickness(1); button.Style = ButtonStyle();
        button.SetResourceReference(Control.BackgroundProperty, primary ? "AccentBrush" : "PanelBrush"); button.SetResourceReference(Control.BorderBrushProperty, primary ? "AccentBrush" : "BorderBrush");
        if (primary) button.Foreground = Brushes.White; else button.SetResourceReference(Control.ForegroundProperty, "InkBrush");
        AutomationProperties.SetName(button, label); return button;
    }
    private static Style ButtonStyle()
    {
        var frame = new FrameworkElementFactory(typeof(Border)); frame.Name = "Frame"; frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        frame.SetBinding(Border.BackgroundProperty, Parent("Background")); frame.SetBinding(Border.BorderBrushProperty, Parent("BorderBrush")); frame.SetBinding(Border.BorderThicknessProperty, Parent("BorderThickness")); frame.SetBinding(Border.PaddingProperty, Parent("Padding"));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); frame.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = frame }; var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(UIElement.OpacityProperty, .82, "Frame")); template.Triggers.Add(hover);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .4, "Frame")); template.Triggers.Add(disabled);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Frame")); template.Triggers.Add(focus);
        var style = new Style(typeof(Button)); style.Setters.Add(new Setter(Control.TemplateProperty, template)); return style;
    }
    private static System.Windows.Data.Binding Parent(string property)
        => new System.Windows.Data.Binding(property) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent };
    internal static Style NavigationItemStyle()
    {
        var frame = new FrameworkElementFactory(typeof(Border)); frame.Name = "Frame"; frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(6)); frame.SetValue(Border.PaddingProperty, new Thickness(11, 9, 11, 9)); frame.SetBinding(Border.BackgroundProperty, Parent("Background"));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch); frame.AppendChild(presenter);
        var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = frame };
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("AccentBrush"))); selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White)); template.Triggers.Add(selected);
        var hover = new MultiTrigger(); hover.Conditions.Add(new System.Windows.Condition(UIElement.IsMouseOverProperty, true)); hover.Conditions.Add(new System.Windows.Condition(ListBoxItem.IsSelectedProperty, false)); hover.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("CanvasBrush"))); template.Triggers.Add(hover);
        var style = new Style(typeof(ListBoxItem)); style.Setters.Add(new Setter(Control.TemplateProperty, template)); style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent)); style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("InkBrush"))); style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(10, 2, 10, 2))); style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)); style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center)); style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 38d)); return style;
    }
}

internal sealed class SettingsPage
{
    internal string Id { get; }
    private readonly string title;
    internal string Title => L.Get(title);
    internal StackPanel Panel { get; }
    internal ScrollViewer View { get; }
    internal HashSet<string> Preferences { get; } = new HashSet<string>();
    internal SettingsPage(string id, string title)
    {
        Id = id; this.title = title; Panel = new StackPanel { Margin = new Thickness(22, 20, 22, 8) };
        View = new ScrollViewer { Content = Panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.VerticalOnly };
        AutomationProperties.SetName(View, L.Format("{0}设置", title)); AutomationProperties.SetAutomationId(View, "SettingsPage_" + id);
    }
}
