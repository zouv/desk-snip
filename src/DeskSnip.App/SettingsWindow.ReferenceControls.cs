using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeskSnip.Core;

namespace DeskSnip.App;

internal sealed partial class SettingsWindow
{
    private static void Unavailable(Panel group, string title, string reason, string value)
    {
        var input = new TextBox { IsEnabled = false, ToolTip = reason }; L.BindReadOnlyLabel(input, value); SettingsLayout.Input(input, "", title, 250); SettingsLayout.Row(group, title, input, reason);
    }
    private static void UnavailableButton(Panel group, string title, string reason)
    {
        var button = SettingsLayout.Button(title, () => { }); button.IsEnabled = false; button.ToolTip = reason; SettingsLayout.FullRow(group, button);
    }
    private void MirrorBoolean(Panel group, string name, string title)
    {
        var source = boolPreferences[name]; var input = new CheckBox { Content = title, VerticalContentAlignment = VerticalAlignment.Center };
        input.SetResourceReference(Control.ForegroundProperty, "InkBrush"); input.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding("IsChecked") { Source = source, Mode = System.Windows.Data.BindingMode.TwoWay });
        input.SetBinding(Control.FontWeightProperty, new System.Windows.Data.Binding("FontWeight") { Source = source });
        System.Windows.Automation.AutomationProperties.SetName(input, title); System.Windows.Automation.AutomationProperties.SetAutomationId(input, name); buildingPage.Preferences.Add(name); SettingsLayout.FullRow(group, input);
    }
    private void EnableWhen(FrameworkElement dependent, params string[] controllingPreferences)
    {
        var binding = new System.Windows.Data.MultiBinding { Converter = new AllEnabledConverter() };
        foreach (var name in controllingPreferences) binding.Bindings.Add(new System.Windows.Data.Binding("IsChecked") { Source = boolPreferences[name] });
        dependent.SetBinding(IsEnabledProperty, binding);
    }
    private sealed class AllEnabledConverter : System.Windows.Data.IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture) => values.Length > 0 && Array.TrueForAll(values, value => value is bool enabled && enabled);
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
    private void ColorField(Panel group, string name, string label, string description = "")
    {
        var input = new TextBox(); SettingsLayout.Input(input, name, label, 130); textPreferences.Add(name, input); buildingPage.Preferences.Add(name);
        var swatch = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 7, 0) };
        void Update() { try { swatch.Background = (Brush)new BrushConverter().ConvertFromString(input.Text)!; } catch (Exception) { swatch.Background = Brushes.Transparent; } }
        var row = new DockPanel(); var picker = SettingsLayout.Button(L.Get("选择颜色"), () =>
        {
            using (var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true })
            {
                try { var color = (Color)ColorConverter.ConvertFromString(input.Text); dialog.Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B); } catch (Exception) { }
                if (dialog.ShowDialog(new NativeWindowOwner(new System.Windows.Interop.WindowInteropHelper(this).Handle)) == System.Windows.Forms.DialogResult.OK)
                {
                    var alpha = input.Text.Length == 9 ? input.Text.Substring(1, 2) : ""; input.Text = $"#{alpha}{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                }
            }
        });
        var content = new StackPanel { Orientation = Orientation.Horizontal }; content.Children.Add(swatch); content.Children.Add(SettingsLayout.Text(L.Get("选择颜色"), 12)); picker.Content = content;
        picker.Margin = new Thickness(8, 0, 0, 0); DockPanel.SetDock(picker, Dock.Right); row.Children.Add(picker); var host = new Grid(); SettingsLayout.FitLeft(input, host); row.Children.Add(host); preferenceLabels.Add(name, SettingsLayout.Row(group, label, row, description)); input.TextChanged += (_, _) => { MarkChanged(name); Update(); };
    }
    private void OptionalColorField(Panel group, string mode, string color, string label, string defaultLabel)
    {
        var source = new CheckBox { Content = defaultLabel, Visibility = Visibility.Collapsed }; RegisterBoolean(source, mode);
        ColorField(group, color, label); var row = (Grid)group.Children[group.Children.Count - 1]; var host = (Grid)row.Children[1]; var colorEditor = (DockPanel)host.Children[0]; host.Children.Clear();
        var inputHost = (Grid)textPreferences[color].Parent; inputHost.Visibility = Visibility.Collapsed; colorEditor.Width = 112; colorEditor.Margin = new Thickness(4, 4, 0, 4);
        colorEditor.SetBinding(IsEnabledProperty, new System.Windows.Data.Binding("IsChecked") { Source = source, Converter = new InverseBooleanConverter() });
        var options = new WrapPanel { VerticalAlignment = VerticalAlignment.Center }; options.Children.Add(source);
        var standard = new RadioButton { Content = defaultLabel, GroupName = mode, Margin = new Thickness(0, 4, 9, 4), VerticalContentAlignment = VerticalAlignment.Center };
        standard.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding("IsChecked") { Source = source, Mode = System.Windows.Data.BindingMode.TwoWay });
        var custom = new RadioButton { Content = L.Get("自定义"), GroupName = mode, Margin = new Thickness(0, 4, 4, 4), VerticalContentAlignment = VerticalAlignment.Center };
        custom.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding("IsChecked") { Source = source, Mode = System.Windows.Data.BindingMode.OneWay, Converter = new InverseBooleanConverter() }); custom.Checked += (_, _) => source.IsChecked = false;
        options.Children.Add(standard); options.Children.Add(custom); options.Children.Add(colorEditor); SettingsLayout.FitLeft(options, host);
    }
    private string PreviewPath(string directory, string fallback, string filename)
    {
        var folder = textPreferences[directory].Text.Trim(); if (folder.Length == 0) folder = Path.Combine(settingsDataRoot, fallback); return Path.Combine(folder, filename);
    }
    private void AddSavePreview(Panel group, string template, string? format, string extension = ".png", string kind = "Image", string prefix = "", string? directory = null, string fallback = "")
    {
        var preview = SettingsLayout.Text("", 12, "SubtleBrush"); System.Windows.Automation.AutomationProperties.SetName(preview, L.Get("文件名预览"));
        System.Windows.Automation.AutomationProperties.SetAutomationId(preview, template + "Preview");
        void Update()
        {
            try
            {
                var value = format == null ? "" : (string?)choicePreferences[format].SelectedValue ?? "PNG";
                var ext = format == null ? extension : value == "JPEG" ? ".jpg" : value == "TIFF" ? ".tiff" : "." + value.ToLowerInvariant();
                var filename = SaveNaming.Sanitize(prefix + SaveNaming.Expand(textPreferences[template].Text, DateTime.Now, 1920, 1080, kind, L.Get("示例"))) + ext;
                preview.Text = L.Join(L.Get("预览："),(directory == null ? filename : PreviewPath(directory, fallback, filename)));
            }
            catch (Exception error) when (error is ArgumentException || error is FormatException) { preview.Text = error.Message; }
        }
        textPreferences[template].TextChanged += (_, _) => Update(); if (format != null) choicePreferences[format].SelectionChanged += (_, _) => Update(); if (directory != null) textPreferences[directory].TextChanged += (_, _) => Update(); SettingsLayout.FullRow(group, preview);
    }
    private void OpenSaveFolder(string name, string fallback)
    {
        try { var directory = textPreferences[name].Text.Trim(); if (directory.Length == 0) directory = Path.Combine(settingsDataRoot, fallback); if (!Directory.Exists(directory)) throw new ArgumentException(L.Get("目录尚不存在，请先保存文件或选择已有目录。")); Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true }); }
        catch (Exception error) { Ui.Error(this, error, L.Get("打开文件夹")); }
    }
    private async void VerifyProxy()
    {
        try
        {
            var mode = (string?)choicePreferences[nameof(AppSettings.NetworkProxyMode)].SelectedValue;
            if (mode != "Http") { footerHint.Text = L.Get("系统代理 / 无代理无需手动验证"); return; }
            var draft = ReadValues(); footerHint.Text = L.Get("正在验证代理连接…");
            using (var client = new TcpClient()) { var task = client.ConnectAsync(draft.NetworkProxyHost, draft.NetworkProxyPort); if (await Task.WhenAny(task, Task.Delay(5000)) != task) throw new ArgumentException(L.Get("代理连接超时，请检查主机与端口。")); await task; }
            footerHint.Text = L.Get("代理端口可连接，保存后应用到联网服务");
        }
        catch (Exception error) { footerHint.Text = L.Get("代理连接未通过"); Ui.Error(this, error, L.Get("验证代理")); }
    }
    private void BuildAccountPage()
    {
        var page = Page("account", L.Get("账号 / 会员"), ToolGlyph.Info); var group = SettingsLayout.Section(page.Panel, L.Get("本地应用")); SettingsLayout.FullRow(group, SettingsLayout.Text(L.Get("DeskSnip 当前提供本地功能，不提供账号登录、会员购买和配置云同步。"), 13, "SubtleBrush"));
    }

}
