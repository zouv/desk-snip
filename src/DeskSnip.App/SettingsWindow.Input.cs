using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DeskSnip.App.Windows;
using DeskSnip.Core;
using MouseBinding = DeskSnip.Core.MouseBinding;

namespace DeskSnip.App;

internal sealed partial class SettingsWindow
{
    private readonly StackPanel actionRows = new StackPanel { Name = "SettingsActionBindings" };
    private readonly Dictionary<string, TextBox> builtinEditors = new Dictionary<string, TextBox>();
    private readonly Dictionary<string, (ComboBox Modifiers, ComboBox Gesture, TextBlock Status)> pinMouseEditors = new Dictionary<string, (ComboBox, ComboBox, TextBlock)>();
    private readonly Dictionary<string, (ComboBox Modifiers, ComboBox Gesture, TextBlock Status)> globalMouseEditors = new Dictionary<string, (ComboBox, ComboBox, TextBlock)>();
    private readonly Dictionary<string, Button> processListButtons = new Dictionary<string, Button>();
    private List<string> hiddenActions = new List<string>();
    private readonly HashSet<string> defaultActionNames = new HashSet<string>(DesktopActions.DefaultNames);
    private bool mouseDraftChanged;
    private bool globalMouseDraftChanged;
    private bool builtinDraftChanged;
    private ComboBox? clipboardFormatChoice;
    private string clipboardFormatDraft = "Auto";

    private void BuildAnnotationPage()
    {
        var page = Page("annotation", L.Get("标注"), ToolGlyph.Brush); var general = SettingsLayout.Section(page.Panel, L.Get("通用"));
        var colorRow = new DockPanel(); var palette = SettingsLayout.Button(L.Get("编辑调色板…"), () => EditPalette()); DockPanel.SetDock(palette, Dock.Right); colorRow.Children.Add(palette);
        var shared = new CheckBox { Content = L.Get("所有工具使用相同颜色"), VerticalContentAlignment = VerticalAlignment.Center }; RegisterBoolean(shared, nameof(AppSettings.MarkSharedColor)); colorRow.Children.Add(shared); SettingsLayout.FullRow(general, colorRow);
        Choice(general, nameof(AppSettings.MarkSecondaryEditing), L.Get("标注二次编辑"), new[] { "True", "False" }, new[] { L.Get("启用"), L.Get("禁用") });
        Choice(general, nameof(AppSettings.MarkQuickSettings), L.Get("快捷标注设置"), new[] { "True", "False" }, new[] { L.Get("启用"), L.Get("禁用") });
        Choice(general, nameof(AppSettings.MarkPencilCursor), L.Get("画笔光标类型"), new[] { "CrossAndOutline", "Cross", "Outline", "Arrow" }, new[] { L.Get("十字和轮廓"), L.Get("十字"), L.Get("轮廓"), L.Get("箭头") });
        Bind(general, nameof(AppSettings.MarkPencilOutlineMinWidth), L.Get("显示画笔轮廓的最小宽度"), L.Get("0–400 像素"));
        Bind(general, nameof(AppSettings.MarkRememberPinTool), L.Get("记住钉图上最后使用的标注工具")); Bind(general, nameof(AppSettings.MarkRememberScreenshotTool), L.Get("记住截图中最后使用的标注工具"));
        Bind(general, nameof(AppSettings.MarkLimitToSelection), L.Get("将标注内容限制在选区内"));
        SettingsLayout.FullRow(general, SettingsLayout.Text(L.Get("点击工具栏分组图标直接使用当前默认工具；右侧箭头或右键切换，选择会记住。"), 11, "SubtleBrush"));
        SettingsLayout.Row(general, L.Get("调整标注按钮"), SettingsLayout.Button(L.Get("编辑按钮…"), EditMarkToolbar));
        Bind(general, nameof(AppSettings.MarkDisableWheel), L.Get("禁用鼠标滚轮调整标注工具")); Bind(general, nameof(AppSettings.MarkQuickSettingsPadding), L.Get("快捷标注面板边距"), L.Get("0–100 像素"));
        var geometry = SettingsLayout.Section(page.Panel, L.Get("几何形状")); Choice(geometry, nameof(AppSettings.MarkRoundedControls), L.Get("圆角控制点显示"), new[] { "Hover", "Always", "Never" }, new[] { L.Get("鼠标悬停时显示"), L.Get("始终显示"), L.Get("不显示") });
        var serial = SettingsLayout.Section(page.Panel, L.Get("序号")); Bind(serial, nameof(AppSettings.MarkRenumberAfterDelete), L.Get("删除序号时同步调整其他序号")); Bind(serial, nameof(AppSettings.MarkSerialStartAtZero), L.Get("序号默认从 0 开始")); Bind(serial, nameof(AppSettings.MarkGlobalSerial), L.Get("全局序号连续编号"));
        OptionalColorField(serial, nameof(AppSettings.MarkSerialDefaultColor), nameof(AppSettings.MarkSerialBackground), L.Get("序号背景颜色"), L.Get("默认颜色"));
        Choice(serial, nameof(AppSettings.MarkSerialTextSize), L.Get("序号文字大小"), new[] { "Small", "Medium", "Large" }, new[] { L.Get("小"), L.Get("中"), L.Get("大") });
        UnavailableChoice(serial, L.Get("序号箭头大小"), new[] { L.Get("小"), L.Get("中"), L.Get("大") }, L.Get("中"), L.Get("序号引线尚未实现，当前使用圆形序号"));
        var pencil = SettingsLayout.Section(page.Panel, L.Get("画笔")); Choice(pencil, nameof(AppSettings.MarkPencilShiftMode), L.Get("按住 Shift 时的画笔模式"), new[] { "FreeAngle", "Snap45", "Axis" }, new[] { L.Get("任意角度直线"), L.Get("45° 角度直线"), L.Get("水平 / 垂直直线") }); Bind(pencil, nameof(AppSettings.MarkSmoothPencil), L.Get("平滑笔迹"));
        var polyline = SettingsLayout.Section(page.Panel, L.Get("折线")); Choice(polyline, nameof(AppSettings.MarkPolylineJoin), L.Get("折线连接样式"), new[] { "Round", "Miter", "Bevel" }, new[] { L.Get("圆角"), L.Get("尖角"), L.Get("斜角") }); Choice(polyline, nameof(AppSettings.MarkPolylineCap), L.Get("折线端点样式"), new[] { "Round", "Flat", "Square" }, new[] { L.Get("圆头"), L.Get("平头"), L.Get("方头") });
        var arrow = SettingsLayout.Section(page.Panel, L.Get("箭头")); Bind(arrow, nameof(AppSettings.MarkArrowDoubleClickDisable), L.Get("双击箭头退出当前标注工具")); Bind(arrow, nameof(AppSettings.MarkHideArrowControls), L.Get("隐藏箭头控制点"));
        var highlighter = SettingsLayout.Section(page.Panel, L.Get("荧光笔")); Choice(highlighter, nameof(AppSettings.MarkHighlighterMode), L.Get("默认模式"), new[] { "FreeAngle", "Horizontal", "Vertical" }, new[] { L.Get("任意角度"), L.Get("水平"), L.Get("垂直") });
        var text = SettingsLayout.Section(page.Panel, L.Get("文字")); Choice(text, nameof(AppSettings.MarkTextWrap), L.Get("自动换行模式"), new[] { "WordOrCharacter", "Character", "None" }, new[] { L.Get("单词边界或任意位置换行"), L.Get("任意位置换行"), L.Get("不自动换行") });
        var eraser = SettingsLayout.Section(page.Panel, L.Get("橡皮擦")); Bind(eraser, nameof(AppSettings.MarkEraserSecondaryEditing), L.Get("橡皮擦允许二次编辑"));
        var mosaic = SettingsLayout.Section(page.Panel, L.Get("自动马赛克")); Bind(mosaic, nameof(AppSettings.MarkAutoPrivacyProcessing), L.Get("自动处理检测到的隐私区域"));
        var watermark = SettingsLayout.Section(page.Panel, L.Get("水印")); Bind(watermark, nameof(AppSettings.MarkAutomaticWatermark), L.Get("截图时自动添加水印")); Bind(watermark, nameof(AppSettings.MarkWatermarkText), L.Get("水印文字"), L.Get("{time} 表示当前日期与时间"));
    }
    private void RegisterBoolean(CheckBox input, string name)
    {
        input.SetResourceReference(Control.ForegroundProperty, "InkBrush"); boolPreferences.Add(name, input); buildingPage.Preferences.Add(name); AutomationProperties.SetAutomationId(input, name); AutomationProperties.SetName(input, Convert.ToString(input.Content)); input.Checked += (_, _) => MarkChanged(name); input.Unchecked += (_, _) => MarkChanged(name);
    }
    private static void UnavailableChoice(Panel group, string title, string[] choices, string current, string reason)
    {
        var control = new ComboBox { ItemsSource = choices, SelectedItem = current, IsEnabled = false, ToolTip = reason }; SettingsLayout.Input(control, "Unavailable_" + title, title, 250); SettingsLayout.Row(group, title, control, reason);
    }
    private void EditPalette()
    {
        var prompt = new TextPrompt(paletteDraft) { Title = L.Get("编辑颜色调色板"), Owner = this };
        if (prompt.ShowDialog() != true) return;
        var value = prompt.Value.Replace(" ", "").Replace("\r", "").Replace("\n", ",");
        if (!AnnotationPreferenceRules.ValidPalette(value)) { footerHint.Text = L.Get("请填写 1–24 个 #RRGGBB 颜色，用逗号分隔"); return; }
        paletteDraft = value.ToUpperInvariant(); footerHint.Text = L.Get("调色板已修改，保存后生效");
    }
    private string paletteDraft = "";
    private string toolbarDraft = "";
    private void EditMarkToolbar()
    {
        var dialog = new ProductWindow { Title = L.Get("调整标注按钮"), Width = 360, Height = 440, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var panel = new DockPanel { Margin = new Thickness(16) }; dialog.Content = panel; var list = new ListBox();
        var current = toolbarDraft.Split(','); foreach (var name in current.Concat(AnnotationPreferenceRules.ToolbarGroups.Except(current))) list.Items.Add(new CheckBox { Content = MarkGroupLabel(name), Tag = name, IsChecked = current.Contains(name), Margin = new Thickness(6) });
        var buttons = new WrapPanel(); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        void Move(int offset) { var index = list.SelectedIndex; if (index < 0 || index + offset < 0 || index + offset >= list.Items.Count) return; var item = list.Items[index]; list.Items.RemoveAt(index); list.Items.Insert(index + offset, item); list.SelectedIndex = index + offset; }
        buttons.Children.Add(SettingsLayout.Button(L.Get("上移"), () => Move(-1))); buttons.Children.Add(SettingsLayout.Button(L.Get("下移"), () => Move(1)));
        buttons.Children.Add(SettingsLayout.Button(L.Get("应用"), () => { var tools = list.Items.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToArray(); if (tools.Length == 0) return; toolbarDraft = string.Join(",", tools); dialog.DialogResult = true; }, true)); panel.Children.Add(list); dialog.ShowDialog();
    }
    private static string MarkGroupLabel(string name) => name switch { "Rectangle" => L.Get("几何形状"), "Arrow" => L.Get("箭头与线条"), "Pen" => L.Get("画笔与高亮"), "Text" => L.Get("文字与水印"), "Number" => L.Get("序号"), "Mosaic" => L.Get("马赛克与模糊"), "Cover" => L.Get("遮挡"), "Eraser" => L.Get("橡皮擦"), "Spotlight" => L.Get("聚光与放大"), _ => name };

    private void BuildShortcutPage()
    {
        var page = Page("shortcuts", L.Get("快捷键 / 动作"), ToolGlyph.Keyboard); var group = SettingsLayout.Section(page.Panel, L.Get("动作")); SettingsLayout.FullRow(group, actionRows);
        foreach (var name in new[] { nameof(AppSettings.CaptureHotkey), nameof(AppSettings.PinHotkey), nameof(AppSettings.RecordHotkey) }) { var input = new TextBox(); textPreferences.Add(name, input); buildingPage.Preferences.Add(name); input.TextChanged += (_, _) => MarkChanged(name); }
        var buttons = new WrapPanel(); buttons.Children.Add(SettingsLayout.Button(L.Get("添加新动作…"), AddAction)); buttons.Children.Add(SpacedButton(L.Get("清空所有动作"), () => { actionBindings.Clear(); hiddenActions = DesktopActions.DefaultNames.ToList(); foreach (var name in new[] { nameof(AppSettings.CaptureHotkey), nameof(AppSettings.PinHotkey), nameof(AppSettings.RecordHotkey) }) textPreferences[name].Text = ""; RenderActions(); }));
        buttons.Children.Add(SpacedButton(L.Get("清空所有快捷键"), () => { foreach (var item in actionBindings) item.Hotkey = ""; foreach (var name in new[] { nameof(AppSettings.CaptureHotkey), nameof(AppSettings.PinHotkey), nameof(AppSettings.RecordHotkey) }) textPreferences[name].Text = ""; RenderActions(); }));
        var ignored = ProcessListButton(nameof(AppSettings.ShortcutIgnoredProcesses), L.Get("快捷键忽略进程")); ignored.Margin = new Thickness(8, 0, 0, 6); buttons.Children.Add(ignored); SettingsLayout.FullRow(group, buttons);
        Bind(group, nameof(AppSettings.ShortcutDisableInFullscreen), L.Get("全屏应用中禁用快捷键"));
        SettingsLayout.FullRow(group, SettingsLayout.Text(L.Get("点击输入框后按组合键完成设置；Esc / Backspace 取消，Delete 或 × 清空。绿色表示格式有效，实际系统占用在保存后提示。"), 11, "SubtleBrush"));
    }
    private static Button SpacedButton(string label, Action action) { var button = SettingsLayout.Button(label, action); button.Margin = new Thickness(8, 0, 0, 6); return button; }
    private void RenderActions()
    {
        actionRows.Children.Clear();
        foreach (var name in DesktopActions.DefaultNames.Where(name => !hiddenActions.Contains(name)).ToArray())
        {
            var existing = actionBindings.FirstOrDefault(a => a.Action == name);
            var property = name == "Capture" ? nameof(AppSettings.CaptureHotkey) : name == "Pin" ? nameof(AppSettings.PinHotkey) : name == "Record" ? nameof(AppSettings.RecordHotkey) : null;
            AddActionRow(name, existing, property);
        }
        foreach (var binding in actionBindings.Where(a => !defaultActionNames.Contains(a.Action)).ToArray()) AddActionRow(binding.Action, binding, null);
        if (actionRows.Children.Count == 0) SettingsLayout.FullRow(actionRows, SettingsLayout.Text(L.Get("暂无动作，点击“添加新动作”开始配置。"), 12, "SubtleBrush"));
    }
    private void AddActionRow(string name, ActionHotkey? binding, string? property)
    {
        var row = new Grid { MinHeight = 51 }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.30, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.70, GridUnitType.Star) });
        var label = SettingsLayout.Text(string.IsNullOrEmpty(binding?.Title) ? DesktopActions.Label(name) : binding!.Title); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(0, 5, 12, 5); row.Children.Add(label);
        var editors = new Grid(); Grid.SetColumn(editors, 1); row.Children.Add(editors); editors.ColumnDefinitions.Add(new ColumnDefinition()); editors.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); editors.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); editors.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ActionHotkey Ensure() { if (binding == null) { binding = new ActionHotkey { Action = name, Hotkey = "", TrayMenu = DefaultTray(name) }; actionBindings.Add(binding); } return binding; }
        var key = new TextBox();
        if (property == null) key.Text = binding?.Hotkey ?? "";
        else { key.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("Text") { Source = textPreferences[property], Converter = new ShortcutDisplayConverter(), Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged }); key.SetBinding(Control.FontWeightProperty, new System.Windows.Data.Binding("FontWeight") { Source = textPreferences[property] }); }
        var keyHost = ShortcutEditor(key, "Action_" + name, DesktopActions.Label(name), true, () => { if (!loading && property == null) Ensure().Hotkey = ShortcutKeys.FromDisplay(key.Text); }); editors.Children.Add(keyHost);
        void MarkAction() { var expected = property == null ? "" : Convert.ToString(Property(property).GetValue(defaults)) ?? ""; var different = ShortcutKeys.FromDisplay(key.Text) != expected || !string.IsNullOrEmpty(binding?.Title) && binding!.Title != DesktopActions.Label(name); label.FontWeight = different ? FontWeights.Bold : FontWeights.Normal; if (property == null) key.FontWeight = label.FontWeight; }
        key.TextChanged += (_, _) => MarkAction(); MarkAction();
        var tray = new CheckBox { Content = L.Get("托盘菜单"), IsChecked = binding?.TrayMenu ?? DefaultTray(name), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 4, 0) }; tray.SetResourceReference(Control.ForegroundProperty, "InkBrush"); AutomationProperties.SetAutomationId(tray, "Tray_" + name); tray.Click += (_, _) => Ensure().TrayMenu = tray.IsChecked == true; Grid.SetColumn(tray, 1); editors.Children.Add(tray);
        var edit = SettingsLayout.Button("", () => EditAction(Ensure(), property)); edit.Content = new ToolIcon(ToolGlyph.Brush) { Width = 17, Height = 17 }; edit.ToolTip = L.Get("编辑动作"); AutomationProperties.SetName(edit, L.Join(L.Get("编辑动作："),DesktopActions.Label(name))); edit.VerticalAlignment = VerticalAlignment.Center; edit.Padding = new Thickness(5); edit.Margin = new Thickness(3, 0, 0, 0); Grid.SetColumn(edit, 2); editors.Children.Add(edit);
        var remove = SettingsLayout.Button("×", () => { if (defaultActionNames.Contains(name)) hiddenActions.Add(name); if (binding != null) actionBindings.Remove(binding); if (property != null) textPreferences[property].Text = ""; RenderActions(); }); remove.Foreground = Brushes.IndianRed; remove.VerticalAlignment = VerticalAlignment.Center; remove.Padding = new Thickness(8, 4, 8, 4); remove.Margin = new Thickness(4, 0, 0, 0); AutomationProperties.SetName(remove, L.Join(L.Get("删除动作："),DesktopActions.Label(name))); Grid.SetColumn(remove, 3); editors.Children.Add(remove); SettingsLayout.FullRow(actionRows, row);
    }
    private static bool DefaultTray(string name) => name != "SwitchPinGroup" && name != "ToggleOtherPins" && name != "PinFile" && name != "TogglePenetration" && name != "ToggleHotkeys";
    private void AddAction()
    {
        var choices = DesktopActions.Names.Where(n => defaultActionNames.Contains(n) ? hiddenActions.Contains(n) : actionBindings.All(a => a.Action != n)).Select(n => new ChoiceItem(n, DesktopActions.Label(n))).ToArray();
        if (choices.Length == 0) { footerHint.Text = L.Get("所有动作均已添加"); return; }
        var dialog = new ProductWindow { Title = L.Get("添加动作"), Width = 390, Height = 190, Owner = this, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner }; var panel = new StackPanel { Margin = new Thickness(18) }; dialog.Content = panel;
        var choice = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Label", SelectedValuePath = "Value", SelectedIndex = 0, MinHeight = 32 }; panel.Children.Add(choice);
        panel.Children.Add(SettingsLayout.Button(L.Get("添加"), () => { if (!(choice.SelectedValue is string name)) return; if (defaultActionNames.Contains(name)) hiddenActions.Remove(name); else actionBindings.Add(new ActionHotkey { Action = name }); dialog.DialogResult = true; }, true)); if (dialog.ShowDialog() == true) RenderActions();
    }
    private void EditAction(ActionHotkey item, string? property)
    {
        var dialog = new ProductWindow { Title = L.Get("编辑动作"), Width = 430, Height = 255, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize }; var panel = new StackPanel { Margin = new Thickness(18) }; dialog.Content = panel;
        panel.Children.Add(SettingsLayout.Text(L.Get("显示名称"))); var title = new TextBox { Text = item.Title.Length == 0 ? DesktopActions.Label(item.Action) : item.Title, Margin = new Thickness(0, 5, 0, 12) }; panel.Children.Add(title);
        var shortcut = new TextBox { Text = property == null ? item.Hotkey : textPreferences[property].Text }; panel.Children.Add(ShortcutEditor(shortcut, "EditActionShortcut", L.Get("快捷键"), true, () => { }));
        var tray = new CheckBox { Content = L.Get("显示在托盘菜单"), IsChecked = item.TrayMenu, Margin = new Thickness(0, 12, 0, 12) }; panel.Children.Add(tray);
        panel.Children.Add(SettingsLayout.Button(L.Get("应用"), () => { if (title.Text.Trim().Length > 80) return; item.Title = title.Text.Trim(); item.TrayMenu = tray.IsChecked == true; if (property == null) item.Hotkey = ShortcutKeys.FromDisplay(shortcut.Text); else textPreferences[property].Text = ShortcutKeys.FromDisplay(shortcut.Text); dialog.DialogResult = true; }, true)); if (dialog.ShowDialog() == true) RenderActions();
    }
    private readonly Dictionary<string, string> processDrafts = new Dictionary<string, string>();
    private Button ProcessListButton(string name, string title)
    {
        var button = SettingsLayout.Button(L.Get("忽略进程名单（0）…"), () => EditProcessList(name, title)); processListButtons.Add(name, button); return button;
    }
    private void UpdateProcessListButtons()
    {
        foreach (var pair in processListButtons) { var count = processDrafts.TryGetValue(pair.Key, out var value) ? InputBindingRules.ProcessNames(value).Length : 0; pair.Value.Content = L.Format("忽略进程名单（{0}）…", count); }
    }
    private void EditProcessList(string name, string title)
    {
        var prompt = new TextPrompt(processDrafts[name]) { Title = L.Format("{0}（每行一个 exe 名称）", title), Owner = this }; if (prompt.ShowDialog() != true) return;
        try { InputBindingRules.ProcessNames(prompt.Value); processDrafts[name] = prompt.Value.Trim(); UpdateProcessListButtons(); footerHint.Text = L.Get("忽略进程已修改，保存后生效"); } catch (ArgumentException error) { footerHint.Text = error.Message; }
    }

    private void BuildBuiltinPage()
    {
        var page = Page("builtin", L.Get("内置快捷键"), ToolGlyph.Keyboard); var tabs = new TabControl { BorderThickness = new Thickness(0) }; AutomationProperties.SetAutomationId(tabs, "BuiltinContexts"); page.Panel.Children.Add(tabs);
        for (var index = 0; index < InputBindingCatalog.Contexts.Length; index++)
        {
            var context = InputBindingCatalog.Contexts[index]; var rows = new StackPanel(); var item = new TabItem { Header = InputBindingCatalog.ContextLabels[index], Content = rows }; tabs.Items.Add(item);
            foreach (var action in InputBindingCatalog.Actions(context))
            {
                var key = new TextBox { Text = action.DefaultHotkey, IsEnabled = action.Supported }; var id = context + ":" + action.Name; builtinEditors.Add(id, key);
                var title = SettingsLayout.Row(rows, action.Label, ShortcutEditor(key, "Builtin_" + context + "_" + action.Name, action.Label, false, () => { if (!loading) builtinDraftChanged = true; }), action.Supported ? "" : L.Get("此钉图类型尚未开放"));
                void UpdateStyle() { title.FontWeight = key.FontWeight = ShortcutKeys.FromDisplay(key.Text) == action.DefaultHotkey ? FontWeights.Normal : FontWeights.Bold; }
                key.TextChanged += (_, _) => UpdateStyle(); UpdateStyle();
            }
        }
        var common = SettingsLayout.Section(page.Panel, L.Get("按键匹配")); Bind(common, nameof(AppSettings.BuiltinMergeNumberKeys), L.Get("合并小键盘与主键盘数字键")); SettingsLayout.FullRow(common, SettingsLayout.Text(L.Get("同场景的重复组合会阻止保存。截图和钉图先匹配各自场景，再匹配通用键位。"), 11, "SubtleBrush"));
    }
    private FrameworkElement ShortcutEditor(TextBox input, string id, string label, bool global, Action changed)
    {
        shortcutLanguageInputs[id] = input;
        SetShortcutText(input, ShortcutKeys.Display(input.Text));
        SettingsLayout.Input(input, id, label, double.PositiveInfinity); input.IsReadOnly = true; input.FontSize = 12; input.Padding = new Thickness(3, 5, 3, 5); input.TextWrapping = TextWrapping.Wrap; input.HorizontalContentAlignment = HorizontalAlignment.Center; input.VerticalAlignment = VerticalAlignment.Center; input.Margin = new Thickness(0); input.MinHeight = 30; input.BorderThickness = new Thickness(0); input.ToolTip = L.Get("点击后按快捷键完成设置；Esc / Backspace 取消，Delete 或 × 清空");
        if (input.Parent is Panel oldParent) oldParent.Children.Remove(input);
        var shell = new Grid(); shell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); shell.ColumnDefinitions.Add(new ColumnDefinition()); shell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); Grid.SetColumn(input, 1); shell.Children.Add(input);
        var placeholder = SettingsLayout.Text(L.Get("按下快捷键"), 12, "SubtleBrush"); placeholder.IsHitTestVisible = false; placeholder.VerticalAlignment = VerticalAlignment.Center; placeholder.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(placeholder, 1); shell.Children.Add(placeholder);
        var state = SettingsLayout.Text("", 14); state.VerticalAlignment = VerticalAlignment.Center; state.Margin = new Thickness(7, 0, 3, 0); shell.Children.Add(state);
        var clear = SettingsLayout.Button("×", () => SetShortcutText(input, "")); clear.Padding = new Thickness(4, 0, 4, 0); clear.MinHeight = 22; clear.BorderThickness = new Thickness(0); clear.Margin = new Thickness(2, 0, 4, 0); clear.Foreground = Brushes.IndianRed;
        Grid.SetColumn(clear, 2); shell.Children.Add(clear); clear.IsEnabled = input.IsEnabled; AutomationProperties.SetName(clear, L.Join(L.Get("清空快捷键："),label));
        void Update() { placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; var valid = input.Text.Length > 0; try { if (valid) { var stored = ShortcutKeys.FromDisplay(input.Text); if (global) Hotkeys.Parse(stored); else ShortcutKeys.Validate(stored); } state.Text = valid ? "✓" : "−"; state.Foreground = valid ? Brushes.SeaGreen : Brushes.Gray; state.ToolTip = valid ? global ? L.Get("格式有效，保存后检查系统占用") : L.Get("格式有效") : L.Get("未绑定"); } catch (ArgumentException error) { state.Text = "!"; state.Foreground = Brushes.IndianRed; state.ToolTip = error.Message; } }
        var previous = input.Text;
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Tab && global) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            switch (ShortcutEditRules.Resolve(key, Keyboard.Modifiers, global, out var value))
            {
                case ShortcutEditAction.Cancel: SetShortcutText(input, previous); input.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); break;
                case ShortcutEditAction.Clear: SetShortcutText(input, ""); break;
                case ShortcutEditAction.Set: SetShortcutText(input, value); input.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); break;
            }
            e.Handled = true;
        };
        input.TextChanged += (_, _) => { Update(); changed(); }; Update();
        var frame = new Border { Child = shell, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), MinHeight = 32, VerticalAlignment = VerticalAlignment.Center, IsEnabled = input.IsEnabled }; frame.SetResourceReference(Border.BackgroundProperty, "PanelBrush"); frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); if (!input.IsEnabled) frame.Opacity = .45;
        shell.MouseLeftButtonDown += (_, _) => input.Focus();
        input.GotKeyboardFocus += (_, _) => { previous = input.Text; frame.BorderBrush = Ui.Accent; input.SelectAll(); };
        input.LostKeyboardFocus += (_, _) => frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return frame;
    }
    private static void SetShortcutText(TextBox input, string value) { input.SetCurrentValue(TextBox.TextProperty, value); input.GetBindingExpression(TextBox.TextProperty)?.UpdateSource(); }

    private void BuildMousePage()
    {
        var page = Page("mouse", L.Get("鼠标"), ToolGlyph.Mouse); var group = SettingsLayout.Section(page.Panel, L.Get("钉图")); BuildMouseRows(group, InputBindingCatalog.PinMouse, pinMouseEditors, false);
    }
    private void BuildGlobalMousePage()
    {
        var page = Page("globalmouse", L.Get("全局鼠标"), ToolGlyph.GlobalMouse); var group = SettingsLayout.Section(page.Panel, L.Get("全局鼠标")); BuildMouseRows(group, InputBindingCatalog.GlobalMouse, globalMouseEditors, true);
        var bottom = new DockPanel(); var clear = SettingsLayout.Button(L.Get("清空全部"), () => { foreach (var editor in globalMouseEditors.Values) editor.Gesture.SelectedValue = "None"; globalMouseDraftChanged = true; }); DockPanel.SetDock(clear, Dock.Right); bottom.Children.Add(clear);
        var mask = new CheckBox { Content = L.Get("使用全局鼠标截图时显示遮罩"), VerticalContentAlignment = VerticalAlignment.Center }; RegisterBoolean(mask, nameof(AppSettings.GlobalMouseShowMask)); bottom.Children.Add(mask); SettingsLayout.FullRow(group, bottom);
        SettingsLayout.FullRow(group, ProcessListButton(nameof(AppSettings.GlobalMouseIgnoredProcesses), L.Get("全局鼠标忽略进程")));
        SettingsLayout.FullRow(group, SettingsLayout.Text(L.Get("配置后按住修饰键拖出区域。首次默认不绑定，避免覆盖已有鼠标操作；翻译与公式先打开确认窗口。"), 11, "SubtleBrush"));
    }
    private void BuildMouseRows(Panel group, IEnumerable<InputAction> catalog, Dictionary<string, (ComboBox Modifiers, ComboBox Gesture, TextBlock Status)> editors, bool global)
    {
        foreach (var action in catalog)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.46, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.54, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var modifiers = new ComboBox { ItemsSource = InputBindingRules.Modifiers.Select(v => new ChoiceItem(v, v.Length == 0 ? L.Get("无修饰键") : v)).ToArray(), DisplayMemberPath = "Label", SelectedValuePath = "Value", IsEnabled = action.Supported }; SettingsLayout.Input(modifiers, (global ? "GlobalMouse_" : "PinMouse_") + action.Name + "Modifiers", L.Format("{0}修饰键", action.Label), double.PositiveInfinity); row.Children.Add(modifiers);
            var plus = SettingsLayout.Text("+", 12); plus.Margin = new Thickness(7, 0, 7, 0); plus.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(plus, 1); row.Children.Add(plus);
            var gestures = global || action.Name == "Move" || action.Name == "DragContent" ? new[] { "None", "LeftDrag", "MiddleDrag", "RightDrag" } : action.Name == "Scale" || action.Name == "FineScale" || action.Name == "Opacity" ? new[] { "None", "Wheel" } : InputBindingRules.Gestures;
            var gesture = new ComboBox { ItemsSource = gestures.Select(v => new ChoiceItem(v, GestureLabel(v))).ToArray(), DisplayMemberPath = "Label", SelectedValuePath = "Value", IsEnabled = action.Supported }; SettingsLayout.Input(gesture, (global ? "GlobalMouse_" : "PinMouse_") + action.Name + "Gesture", L.Format("{0}鼠标组合", action.Label), double.PositiveInfinity); Grid.SetColumn(gesture, 2); row.Children.Add(gesture);
            var status = SettingsLayout.Text("—", 12); status.VerticalAlignment = VerticalAlignment.Center; status.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(status, 3); row.Children.Add(status); editors.Add(action.Name, (modifiers, gesture, status));
            void Update() { if (!loading) { if (global) globalMouseDraftChanged = true; else mouseDraftChanged = true; } UpdateMouseStatuses(editors, global); }
            modifiers.SelectionChanged += (_, _) => Update(); gesture.SelectionChanged += (_, _) => Update(); status.Tag = SettingsLayout.Row(group, action.Label, row, action.Supported ? "" : L.Get("此功能尚未开放，设置保留在此处方便对照"), minimumEditorWidth: 320);
        }
    }
    private static string GestureLabel(string value) => value switch { "None" => L.Get("无"), "Wheel" => L.Get("滚轮"), "LeftClick" => L.Get("左键单击"), "LeftDoubleClick" => L.Get("左键双击"), "LeftDrag" => L.Get("左键拖动"), "RightClick" => L.Get("右键单击"), "RightDoubleClick" => L.Get("右键双击"), "RightDrag" => L.Get("右键拖动"), "MiddleClick" => L.Get("中键单击"), "MiddleDoubleClick" => L.Get("中键双击"), "MiddleDrag" => L.Get("中键拖动"), "X1Click" => L.Get("侧键 1"), "X2Click" => L.Get("侧键 2"), _ => value };
    private static List<MouseBinding> ReadMouse(Dictionary<string, (ComboBox Modifiers, ComboBox Gesture, TextBlock Status)> editors) => editors.Select(pair => new MouseBinding { Action = pair.Key, Modifiers = (string?)pair.Value.Modifiers.SelectedValue ?? "", Gesture = (string?)pair.Value.Gesture.SelectedValue ?? "None" }).ToList();
    private static void UpdateMouseStatuses(Dictionary<string, (ComboBox Modifiers, ComboBox Gesture, TextBlock Status)> editors, bool global)
    {
        var bindings = ReadMouse(editors); var defaults = global ? InputBindingCatalog.DefaultGlobal() : InputBindingCatalog.DefaultPin(new AppSettings()); foreach (var binding in bindings)
        {
            var status = editors[binding.Action].Status; var enabled = binding.Gesture != "None"; var conflict = enabled && bindings.Count(b => b.Gesture == binding.Gesture && InputBindingRules.NormalizeModifiers(b.Modifiers) == InputBindingRules.NormalizeModifiers(binding.Modifiers)) > 1;
            var invalid = conflict || global && enabled && binding.Modifiers.Length == 0; status.Text = invalid ? "!" : enabled ? "✓" : "−"; status.Foreground = invalid ? Brushes.IndianRed : enabled ? Brushes.SeaGreen : Brushes.Gray; status.ToolTip = conflict ? L.Get("组合重复") : invalid ? L.Get("全局拖动需要修饰键") : enabled ? L.Get("有效") : L.Get("未绑定");
            var original = defaults.Single(b => b.Action == binding.Action); if (status.Tag is TextBlock label) label.FontWeight = binding.Gesture == original.Gesture && binding.Modifiers == original.Modifiers ? FontWeights.Normal : FontWeights.Bold;
        }
    }
    private void LoadInputValues(AppSettings value)
    {
        paletteDraft = value.MarkColorPalette; toolbarDraft = value.MarkToolbarTools; hiddenActions = value.HiddenDefaultActions?.ToList() ?? new List<string>();
        clipboardFormatDraft = value.ClipboardImageFileFormat; if (clipboardFormatChoice != null) clipboardFormatChoice.SelectedValue = value.CopyFileWithImage ? value.ClipboardImageFileFormat : "Disabled";
        processDrafts[nameof(AppSettings.ShortcutIgnoredProcesses)] = value.ShortcutIgnoredProcesses; processDrafts[nameof(AppSettings.GlobalMouseIgnoredProcesses)] = value.GlobalMouseIgnoredProcesses;
        UpdateProcessListButtons();
        RenderActions(); foreach (var pair in builtinEditors) { var parts = pair.Key.Split(':'); pair.Value.Text = ShortcutKeys.Display(InputBindingRules.Shortcut(value, parts[0], parts[1])); }
        void LoadMouse(Dictionary<string, (ComboBox Modifiers, ComboBox Gesture, TextBlock Status)> editors, List<MouseBinding> bindings) { foreach (var pair in editors) { var binding = bindings.FirstOrDefault(b => b.Action == pair.Key); pair.Value.Modifiers.SelectedValue = binding?.Modifiers ?? ""; pair.Value.Gesture.SelectedValue = binding?.Gesture ?? "None"; } }
        LoadMouse(pinMouseEditors, value.PinMouseBindings ?? InputBindingCatalog.DefaultPin(value)); LoadMouse(globalMouseEditors, value.GlobalMouseBindings ?? InputBindingCatalog.DefaultGlobal()); UpdateMouseStatuses(pinMouseEditors, false); UpdateMouseStatuses(globalMouseEditors, true);
        mouseDraftChanged = globalMouseDraftChanged = builtinDraftChanged = false;
    }
    private void ReadInputValues(AppSettings value)
    {
        value.MarkColorPalette = paletteDraft; value.MarkToolbarTools = toolbarDraft;
        value.ClipboardImageFileFormat = clipboardFormatDraft;
        value.HiddenDefaultActions = hiddenActions.Count == 0 ? original.HiddenDefaultActions == null ? null : new List<string>() : hiddenActions.Distinct().ToList();
        value.ShortcutIgnoredProcesses = processDrafts[nameof(AppSettings.ShortcutIgnoredProcesses)]; value.GlobalMouseIgnoredProcesses = processDrafts[nameof(AppSettings.GlobalMouseIgnoredProcesses)];
        if (mouseDraftChanged) value.PinMouseBindings = ReadMouse(pinMouseEditors); if (globalMouseDraftChanged) value.GlobalMouseBindings = ReadMouse(globalMouseEditors);
        if (builtinDraftChanged) value.BuiltinShortcuts = builtinEditors.Where(pair => pair.Value.IsEnabled).Select(pair => new ShortcutBinding { Context = pair.Key.Split(':')[0], Action = pair.Key.Split(':')[1], Hotkey = ShortcutKeys.FromDisplay(pair.Value.Text) }).ToList();
        foreach (var binding in value.BuiltinShortcuts ?? new List<ShortcutBinding>()) if (binding.Hotkey.Length > 0) ShortcutKeys.Validate(binding.Hotkey);
    }
    private void RestoreInputPage(string id)
    {
        if (id == "system" && clipboardFormatChoice != null) { clipboardFormatDraft = defaults.ClipboardImageFileFormat; clipboardFormatChoice.SelectedValue = "Disabled"; }
        if (id == "annotation") { paletteDraft = defaults.MarkColorPalette; toolbarDraft = defaults.MarkToolbarTools; }
        if (id == "shortcuts") { actionBindings.Clear(); hiddenActions.Clear(); processDrafts[nameof(AppSettings.ShortcutIgnoredProcesses)] = ""; RenderActions(); }
        if (id == "builtin") { foreach (var pair in builtinEditors) { var parts = pair.Key.Split(':'); pair.Value.Text = ShortcutKeys.Display(InputBindingRules.Shortcut(defaults, parts[0], parts[1])); } builtinDraftChanged = true; }
        if (id == "mouse" || id == "globalmouse")
        {
            var editors = id == "mouse" ? pinMouseEditors : globalMouseEditors; var bindings = id == "mouse" ? InputBindingCatalog.DefaultPin(defaults) : InputBindingCatalog.DefaultGlobal();
            foreach (var pair in editors) { var binding = bindings.Single(b => b.Action == pair.Key); pair.Value.Modifiers.SelectedValue = binding.Modifiers; pair.Value.Gesture.SelectedValue = binding.Gesture; }
            if (id == "mouse") mouseDraftChanged = true; else { globalMouseDraftChanged = true; processDrafts[nameof(AppSettings.GlobalMouseIgnoredProcesses)] = ""; }
        }
        UpdateProcessListButtons();
    }
    private sealed class InverseBooleanConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => !(value is bool enabled && enabled);
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
    private sealed class ShortcutDisplayConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => ShortcutKeys.Display(value as string ?? "");
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => ShortcutKeys.FromDisplay(value as string ?? "");
    }
}
