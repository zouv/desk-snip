using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DeskSnip.App;

internal static class Ui
{
    internal static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(28, 43, 56));
    internal static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0, 137, 120));
    internal static readonly Brush Subtle = new SolidColorBrush(Color.FromRgb(103, 121, 132));
    internal static Button Button(string label, Action click, bool primary = false)
    {
        var button = new Button { Content = label, MinHeight = 34, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(3), Cursor = System.Windows.Input.Cursors.Hand };
        if (primary) { button.Background = Accent; button.Foreground = Brushes.White; button.BorderBrush = Accent; }
        button.Click += (_, _) => click(); return button;
    }
    internal static TextBlock Text(string value, double size = 13, Brush? color = null)
        => new TextBlock { Text = value, FontSize = size, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap };
    internal static MenuItem Menu(string label, Action click, bool check = false)
    {
        var item = new MenuItem { Header = label, IsChecked = check };
        item.Click += (_, _) => click(); return item;
    }
    internal static void Error(Window? owner, Exception exception, string action)
    {
        // 显示错误类别和可操作说明，不把本机文件路径或内容写入日志。
        var reason = exception is System.IO.IOException ? "文件无法写入或读取，请检查位置、磁盘空间和文件占用。" :
            exception is System.Runtime.InteropServices.ExternalException ? "Windows 接口暂时不可用，请重试；MP4 编码需要系统媒体组件。" :
            exception is ArgumentException ? exception.Message : "操作未完成，请重试。";
        if (owner?.IsVisible == true) MessageBox.Show(owner, reason, action, MessageBoxButton.OK, MessageBoxImage.Warning);
        else MessageBox.Show(reason, action, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
