using System;
using System.Globalization;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using DeskSnip.App.Windows;
using DeskSnip.Core;
using Microsoft.Win32;

namespace DeskSnip.App;

internal sealed partial class SettingsWindow : ProductWindow
{
    private readonly Action<AppSettings> save;
    private AppSettings original;
    private readonly AppSettings defaults = new AppSettings();
    private readonly Dictionary<string, TextBox> textPreferences = new Dictionary<string, TextBox>();
    private readonly Dictionary<string, CheckBox> boolPreferences = new Dictionary<string, CheckBox>();
    private readonly Dictionary<string, ComboBox> choicePreferences = new Dictionary<string, ComboBox>();
    private readonly Dictionary<string, TextBlock> preferenceLabels = new Dictionary<string, TextBlock>();
    private readonly Dictionary<string, TextBox> shortcutLanguageInputs = new Dictionary<string, TextBox>();
    private readonly List<SettingsPage> pages = new List<SettingsPage>();
    private readonly ListBox navigation = new ListBox { Name = "SettingsNavigation", BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly ContentControl pageHost = new ContentControl { Name = "SettingsPageHost", ClipToBounds = true };
    private readonly TextBlock pageTitle = SettingsLayout.Text("", 22);
    private readonly TextBlock footerHint = SettingsLayout.Text(L.Get("修改后点击保存生效"), 11, "SubtleBrush");
    private readonly Button restoreDefaults;
    private SettingsPage buildingPage = null!;
    private SettingsPage? selectedPage;
    private bool loading;
    private readonly string settingsDataRoot;
    internal event Action<bool>? HotkeyCaptureChanged;
    private ObservableCollection<ActionHotkey> actionBindings = new ObservableCollection<ActionHotkey>();
    internal SettingsWindow(AppSettings settings, Action<AppSettings> save, string? dataRoot = null)
    {
        this.save = save; original = settings.Clone(); settingsDataRoot = dataRoot ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskSnip");
        if (dataRoot != null) serviceCredentials = new ServiceCredentialStore(dataRoot);
        Title = L.Get("DeskSnip · 设置"); Width = 1020; Height = 780; MinWidth = 760; MinHeight = 540; WindowStartupLocation = WindowStartupLocation.CenterOwner; Topmost = false; ShowInTaskbar = true;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/DeskSnip;component/ControlStyles.xaml", UriKind.Relative) });
        Ui.ApplyThemeResources(Resources, settings); SetResourceReference(BackgroundProperty, "CanvasBrush"); SetResourceReference(ForegroundProperty, "InkBrush"); SetResourceReference(FontFamilyProperty, "InterfaceFont"); FontSize = 13;
        var shell = new Grid { Name = "SettingsShell" }; Content = shell; shell.SetResourceReference(Panel.BackgroundProperty, "CanvasBrush"); shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(196) }); shell.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = new Border { BorderThickness = new Thickness(0, 0, 1, 0) }; sidebar.SetResourceReference(Border.BackgroundProperty, "PanelBrush"); sidebar.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); shell.Children.Add(sidebar);
        var sideContent = new DockPanel(); sidebar.Child = sideContent; navigation.Margin = new Thickness(0, 14, 0, 0);
        var version = SettingsLayout.Text("v" + AppInfo.Version, 11, "SubtleBrush"); version.Margin = new Thickness(21, 14, 10, 18); DockPanel.SetDock(version, Dock.Bottom); sideContent.Children.Add(version); sideContent.Children.Add(navigation);
        navigation.SetResourceReference(Control.BackgroundProperty, "PanelBrush"); navigation.ItemContainerStyle = SettingsLayout.NavigationItemStyle(); ScrollViewer.SetHorizontalScrollBarVisibility(navigation, ScrollBarVisibility.Disabled); ScrollViewer.SetVerticalScrollBarVisibility(navigation, ScrollBarVisibility.Auto);
        System.Windows.Automation.AutomationProperties.SetName(navigation, L.Get("设置分类")); navigation.SelectionChanged += (_, _) => ShowSelectedPage();
        var body = new DockPanel(); Grid.SetColumn(body, 1); shell.Children.Add(body);
        var footer = new Border { Padding = new Thickness(22, 12, 22, 12), BorderThickness = new Thickness(0, 1, 0, 0) }; footer.SetResourceReference(Border.BackgroundProperty, "PanelBrush"); footer.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
        var footerContent = new Grid(); footerContent.ColumnDefinitions.Add(new ColumnDefinition()); footerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); footer.Child = footerContent;
        footerHint.VerticalAlignment = VerticalAlignment.Center; footerHint.Margin = new Thickness(0, 0, 12, 0); footerContent.Children.Add(footerHint); var buttons = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(buttons, 1); footerContent.Children.Add(buttons);
        restoreDefaults = SettingsLayout.Button(L.Get("恢复本页默认"), RestoreCurrentPage); buttons.Children.Add(restoreDefaults); var cancel = SettingsLayout.Button(L.Get("取消"), () => DialogResult = false); cancel.IsCancel = true; cancel.Margin = new Thickness(10, 0, 0, 0); buttons.Children.Add(cancel); var confirm = SettingsLayout.Button(L.Get("保存设置"), Save, true); confirm.IsDefault = true; confirm.Margin = new Thickness(10, 0, 0, 0); buttons.Children.Add(confirm);
        var heading = new StackPanel { Margin = new Thickness(22, 20, 22, 0) }; heading.Children.Add(pageTitle); var caption = SettingsLayout.Text(L.Get("加粗项表示与默认值不同，修改保留在当前设置窗口中。"), 11, "SubtleBrush"); caption.Margin = new Thickness(0, 6, 0, 0); heading.Children.Add(caption); DockPanel.SetDock(heading, Dock.Top); body.Children.Add(heading); body.Children.Add(pageHost);
        BuildPages(); AddRecognitionServicePage(); BuildAboutPage();
        boolPreferences[nameof(AppSettings.CopyFileWithImage)].Checked += (_, _) => { if (!loading && clipboardFormatChoice?.SelectedValue as string == "Disabled") clipboardFormatChoice.SelectedValue = clipboardFormatDraft; };
        boolPreferences[nameof(AppSettings.CopyFileWithImage)].Unchecked += (_, _) => { if (!loading && clipboardFormatChoice != null) clipboardFormatChoice.SelectedValue = "Disabled"; };
        LoadValues(settings); SelectPage("system");
    }
    partial void AddRecognitionServicePage();
    partial void ReadServicePreferences(AppSettings value);
    partial void LoadServicePreferences(AppSettings value);
    private SettingsPage Page(string id, string title, ToolGlyph glyph)
    {
        var page = new SettingsPage(id, title); pages.Add(page); buildingPage = page;
        var item = new ListBoxItem { Tag = page, Cursor = System.Windows.Input.Cursors.Hand }; System.Windows.Automation.AutomationProperties.SetName(item, title); System.Windows.Automation.AutomationProperties.SetAutomationId(item, "SettingsNav_" + id);
        var content = new Grid(); content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) }); content.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = new ToolIcon(glyph) { Width = 17, Height = 17, VerticalAlignment = VerticalAlignment.Center }; icon.SetBinding(ToolIcon.ColorProperty, new System.Windows.Data.Binding("Foreground") { Source = item }); content.Children.Add(icon);
        var label = new TextBlock { Text = title, FontSize = 13, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) }; label.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = item }); Grid.SetColumn(label, 1); content.Children.Add(label); item.Content = content; navigation.Items.Add(item); return page;
    }
    internal void SelectPage(string id) { navigation.SelectedItem = navigation.Items.OfType<ListBoxItem>().Single(item => ((SettingsPage)item.Tag).Id == id); }
    private void ShowSelectedPage()
    {
        if (!(navigation.SelectedItem is ListBoxItem item)) return; selectedPage = (SettingsPage)item.Tag; pageTitle.Text = selectedPage.Title; pageHost.Content = selectedPage.View; restoreDefaults.IsEnabled = selectedPage.Preferences.Count > 0 || selectedPage.Id == "shortcuts" || selectedPage.Id == "mouse" || selectedPage.Id == "builtin";
        L.Refresh(selectedPage.View);
    }
    protected override void OnInterfaceLanguageChanged()
    {
        var wasLoading = loading; loading = true;
        try
        {
            base.OnInterfaceLanguageChanged();
            foreach (var page in pages) L.Refresh(page.View, InterfaceLanguageState.Current.Culture);
            foreach (var input in shortcutLanguageInputs.Values) SetShortcutText(input, ShortcutKeys.Display(ShortcutKeys.FromDisplay(input.Text)));
            if (selectedPage != null) pageTitle.Text = selectedPage.Title;
        }
        finally { loading = wasLoading; }
    }
    private void Save()
    {
        try
        {
            var value = ReadValues();
            SaveConfigured(value); DialogResult = true;
        }
        catch (Exception e) { Ui.Error(this, e, L.Get("保存设置")); }
    }
    private FrameworkElement Bind(Panel root, string name, string label, string description = "")
    {
        var property = Property(name);
        buildingPage.Preferences.Add(name);
        if (property.PropertyType == typeof(bool))
        {
            var checkbox = new CheckBox { Content = label, VerticalContentAlignment = VerticalAlignment.Center }; checkbox.SetResourceReference(Control.ForegroundProperty, "InkBrush"); boolPreferences.Add(name, checkbox);
            System.Windows.Automation.AutomationProperties.SetName(checkbox, label); System.Windows.Automation.AutomationProperties.SetAutomationId(checkbox, name); SettingsLayout.FullRow(root, checkbox); checkbox.Checked += (_, _) => MarkChanged(name); checkbox.Unchecked += (_, _) => MarkChanged(name); return checkbox;
        }
        var input = new TextBox(); var numeric = property.PropertyType == typeof(int) || property.PropertyType == typeof(double); SettingsLayout.Input(input, name, label, numeric ? 112 : 320); textPreferences.Add(name, input);
        var bounds = NumberRange(name); FrameworkElement editor = numeric ? SettingsLayout.NumberInput(input, label, bounds.Minimum, bounds.Maximum) : input; preferenceLabels.Add(name, SettingsLayout.Row(root, label, editor, description)); input.TextChanged += (_, _) => MarkChanged(name); return editor;
    }
    private static (double Minimum, double Maximum) NumberRange(string name) => name switch
    {
        nameof(AppSettings.FramesPerSecond) => (1, 60), nameof(AppSettings.VideoQuality) => (1, 100), nameof(AppSettings.RecordingDelay) => (0, 30), nameof(AppSettings.JpegQuality) => (0, 100),
        nameof(AppSettings.AutomaticSaveDays) => (1, 36500), nameof(AppSettings.AutomaticSaveMegabytes) => (1, 1000000), nameof(AppSettings.NetworkProxyPort) => (1, 65535), nameof(AppSettings.RecognitionTimeoutSeconds) => (5, 300),
        nameof(AppSettings.SelectionBorderWidth) => (0, 20), nameof(AppSettings.ScreenshotDetectionPadding) => (-50, 999), nameof(AppSettings.MagnifierZoom) => (1, 100), nameof(AppSettings.PinHistoryLimit) => (0, 1000),
        nameof(AppSettings.PinDefaultOpacity) => (10, 100), nameof(AppSettings.PinClickThroughOpacity) => (10, 100), nameof(AppSettings.PinZoomStep) => (1, 100), nameof(AppSettings.PinFineZoomStep) => (1, 100), nameof(AppSettings.PinOpacityStep) => (1, 100),
        nameof(AppSettings.MarkPencilOutlineMinWidth) => (0, 400), nameof(AppSettings.MarkQuickSettingsPadding) => (0, 100), _ => (0, 99999)
    };
    private ComboBox Choice(Panel root, string name, string label, string[] values, string[]? labels = null, string description = "")
    {
        var choice = new ComboBox { ItemsSource = values.Select((value, index) => new ChoiceItem(value, labels?[index] ?? value)).ToArray(), DisplayMemberPath = "Label", SelectedValuePath = "Value" }; SettingsLayout.Input(choice, name, label, 250); choicePreferences.Add(name, choice); buildingPage.Preferences.Add(name); preferenceLabels.Add(name, SettingsLayout.Row(root, label, choice, description)); choice.SelectionChanged += (_, _) => MarkChanged(name); return choice;
    }
    private sealed class ChoiceItem : System.ComponentModel.INotifyPropertyChanged
    {
        private readonly string label;
        public ChoiceItem(string value, string label)
        {
            Value = value; this.label = label;
            System.ComponentModel.PropertyChangedEventManager.AddHandler(InterfaceLanguageState.Current, CultureChanged, nameof(InterfaceLanguageState.Culture));
        }
        public string Value { get; }
        public string Label => TextCatalog.Refresh(label, InterfaceLanguageState.Current.Culture);
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void CultureChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Label)));
    }
    private static PropertyInfo Property(string name) => typeof(AppSettings).GetProperty(name)!;
    private void LoadValues(AppSettings value)
    {
        loading = true;
        try
        {
            original = value.Clone();
            foreach (var pair in textPreferences) pair.Value.Text = Convert.ToString(Property(pair.Key).GetValue(value), CultureInfo.InvariantCulture) ?? "";
            foreach (var pair in boolPreferences) pair.Value.IsChecked = (bool)Property(pair.Key).GetValue(value)!;
            foreach (var pair in choicePreferences) { if (pair.Value.IsEditable) pair.Value.Text = Convert.ToString(Property(pair.Key).GetValue(value), CultureInfo.InvariantCulture) ?? ""; else pair.Value.SelectedValue = Convert.ToString(Property(pair.Key).GetValue(value), CultureInfo.InvariantCulture); }
            actionBindings = new ObservableCollection<ActionHotkey>(value.ActionBindings.Select(a => a.Clone())); LoadInputValues(value); LoadServicePreferences(value);
        }
        finally { loading = false; }
        foreach (var name in textPreferences.Keys.Concat(boolPreferences.Keys).Concat(choicePreferences.Keys)) MarkChanged(name); UpdateNamePreview(); UpdateAccentPreview();
    }
    private AppSettings ReadValues()
    {
        var value = original.Clone();
        foreach (var pair in textPreferences)
        {
            var property = Property(pair.Key);
            try { property.SetValue(value, Convert.ChangeType(pair.Value.Text.Trim(), property.PropertyType, CultureInfo.InvariantCulture)); }
            catch (Exception error) when (error is FormatException || error is OverflowException) { throw new ArgumentException(L.Join(System.Windows.Automation.AutomationProperties.GetName(pair.Value),L.Get("需要有效的数值。"))); }
        }
        foreach (var pair in boolPreferences) Property(pair.Key).SetValue(value, pair.Value.IsChecked == true);
        foreach (var pair in choicePreferences) { var property = Property(pair.Key); var selected = pair.Value.IsEditable ? pair.Value.Text.Trim() : (string?)pair.Value.SelectedValue ?? throw new ArgumentException(L.Get("请选择有效选项。")); property.SetValue(value, property.PropertyType.IsEnum ? Enum.Parse(property.PropertyType, selected) : Convert.ChangeType(selected, property.PropertyType, CultureInfo.InvariantCulture)); }
        value.ActionBindings = actionBindings.Select(a => a.Clone()).ToList(); ReadInputValues(value); ReadServicePreferences(value); value.Validate();
        var keys = new[] { value.CaptureHotkey, value.PinHotkey, value.RecordHotkey }.Concat(value.ActionBindings.Select(a => a.Hotkey)).Where(k => !string.IsNullOrWhiteSpace(k)).Select(Hotkeys.Parse).ToArray(); if (keys.Distinct().Count() != keys.Length) throw new ArgumentException(L.Get("全局快捷键不能相同。"));
        return value;
    }
    private void MarkChanged(string name)
    {
        if (loading) return; var expected = Convert.ToString(Property(name).GetValue(defaults), CultureInfo.InvariantCulture) ?? "";
        var actual = textPreferences.TryGetValue(name, out var text) ? text.Text : boolPreferences.TryGetValue(name, out var check) ? Convert.ToString(check.IsChecked == true, CultureInfo.InvariantCulture) : choicePreferences.TryGetValue(name, out var choice) ? choice.IsEditable ? choice.Text : Convert.ToString(choice.SelectedValue, CultureInfo.InvariantCulture) ?? "" : "";
        var weight = string.Equals(expected, actual, StringComparison.Ordinal) ? FontWeights.Normal : FontWeights.Bold;
        if (preferenceLabels.TryGetValue(name, out var label)) label.FontWeight = weight;
        if (textPreferences.TryGetValue(name, out text)) text.FontWeight = weight; else if (boolPreferences.TryGetValue(name, out check)) check.FontWeight = weight; else if (choicePreferences.TryGetValue(name, out choice)) choice.FontWeight = weight;
    }
    private void RestoreCurrentPage()
    {
        if (selectedPage == null) return;
        foreach (var name in selectedPage.Preferences)
        {
            var value = Property(name).GetValue(defaults); if (textPreferences.TryGetValue(name, out var text)) text.Text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""; else if (boolPreferences.TryGetValue(name, out var check)) check.IsChecked = (bool)value!; else if (choicePreferences.TryGetValue(name, out var choice)) { if (choice.IsEditable) choice.Text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""; else choice.SelectedValue = Convert.ToString(value, CultureInfo.InvariantCulture); }
        }
        RestoreInputPage(selectedPage.Id);
        if (selectedPage.Id == "service") { serviceKey.Clear(); clearServiceKey.IsChecked = false; }
        UpdateNamePreview(); UpdateAccentPreview(); footerHint.Text = L.Get("已恢复本页默认值，保存后生效");
    }
    private void Export() { var dialog = new SaveFileDialog { Filter = L.Get("偏好配置|*.json"), FileName = "DeskSnip-preferences.json", AddExtension = true }; if (dialog.ShowDialog(this) != true) return; try { PreferenceTransfer.Write(dialog.FileName, ReadValues()); } catch (Exception error) { Ui.Error(this, error, L.Get("导出偏好")); } }
    private void Import() { var dialog = new OpenFileDialog { Filter = L.Get("偏好配置|*.json") }; if (dialog.ShowDialog(this) != true) return; try { LoadValues(PreferenceTransfer.Import(ReadValues(), PreferenceTransfer.Read(dialog.FileName))); } catch (Exception error) { Ui.Error(this, error, L.Get("导入偏好")); } }
}
