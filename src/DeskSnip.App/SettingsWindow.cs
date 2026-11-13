using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DeskSnip.App.Windows;
using DeskSnip.Core;

namespace DeskSnip.App;

internal sealed class SettingsWindow : Window
{
    private readonly TextBox capture;
    private readonly TextBox pin;
    private readonly TextBox record;
    private readonly TextBox fps;
    private readonly TextBox limit;
    private readonly TextBox output;
    private readonly ComboBox format;
    private readonly CheckBox cursor;
    private readonly CheckBox tray;
    private readonly Action<AppSettings> save;
    internal SettingsWindow(AppSettings settings, Action<AppSettings> save)
    {
        this.save = save;
        Title = "DeskSnip · 设置"; Width = 570; Height = 655; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Thickness(25) }; Content = root;
        root.Children.Add(Ui.Text("设置", 24)); root.Children.Add(Ui.Text("修改后立即生效，配置保存在本机。", 12, Ui.Subtle));
        capture = Field(root, "截图快捷键", settings.CaptureHotkey);
        pin = Field(root, "剪贴板钉图快捷键", settings.PinHotkey);
        record = Field(root, "录屏 / 暂停快捷键", settings.RecordHotkey);
        root.Children.Add(Ui.Text("格式：Ctrl+Alt+A，也支持 Shift、F1…F12。冲突会显示在主窗口。", 12, Ui.Subtle));
        fps = Field(root, "录制帧率（1–30；GIF 最大 15）", settings.FramesPerSecond.ToString(CultureInfo.InvariantCulture));
        limit = Field(root, "历史保留条数（1–1000）", settings.HistoryLimit.ToString(CultureInfo.InvariantCulture));
        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) }; row.Children.Add(Ui.Text("默认录制格式", 13));
        format = new ComboBox { Width = 130, ItemsSource = new[] { "MP4", "GIF" }, SelectedItem = settings.RecordingFormat, Margin = new Thickness(20, 0, 0, 0) }; row.Children.Add(format); root.Children.Add(row);
        output = Field(root, "录屏默认目录（留空使用本机数据目录）", settings.OutputDirectory);
        cursor = new CheckBox { Content = "录屏包含鼠标指针", IsChecked = settings.CaptureCursor, Margin = new Thickness(0, 15, 0, 0) };
        tray = new CheckBox { Content = "关闭主窗口时收起到托盘", IsChecked = settings.MinimizeToTray, Margin = new Thickness(0, 12, 0, 0) };
        root.Children.Add(cursor); root.Children.Add(tray);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(Ui.Button("取消", () => DialogResult = false)); buttons.Children.Add(Ui.Button("保存设置", Save, true)); root.Children.Add(buttons);
    }
    private static TextBox Field(Panel root, string label, string value)
    {
        var row = new Grid { Margin = new Thickness(0, 12, 0, 0) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(275) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        var text = Ui.Text(label); text.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(text);
        var input = new TextBox { Text = value, MinHeight = 30, Padding = new Thickness(7, 4, 7, 4), VerticalContentAlignment = VerticalAlignment.Center }; Grid.SetColumn(input, 1); row.Children.Add(input); root.Children.Add(row); return input;
    }
    private void Save()
    {
        try
        {
            if (!int.TryParse(fps.Text, out var frames) || !int.TryParse(limit.Text, out var count)) throw new ArgumentException("帧率与历史条数请输入整数。");
            var value = new AppSettings { CaptureHotkey = capture.Text.Trim(), PinHotkey = pin.Text.Trim(), RecordHotkey = record.Text.Trim(), FramesPerSecond = frames, HistoryLimit = count,
                RecordingFormat = (string)format.SelectedItem, CaptureCursor = cursor.IsChecked == true, MinimizeToTray = tray.IsChecked == true, OutputDirectory = output.Text.Trim() };
            value.Validate(); Hotkeys.Parse(value.CaptureHotkey); Hotkeys.Parse(value.PinHotkey); Hotkeys.Parse(value.RecordHotkey);
            save(value); DialogResult = true;
        }
        catch (Exception e) { Ui.Error(this, e, "保存设置"); }
    }
}
