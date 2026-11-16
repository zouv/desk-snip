using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeskSnip.Core;

namespace DeskSnip.App;

internal static class Ui
{
    internal static Brush Ink => Resource("InkBrush", Color.FromRgb(28, 43, 56));
    internal static Brush Accent => Resource("AccentBrush", Color.FromRgb(52, 137, 245));
    internal static Brush Subtle => Resource("SubtleBrush", Color.FromRgb(103, 121, 132));
    private static Brush Resource(string name, Color fallback) => Application.Current?.TryFindResource(name) as Brush ?? FrozenBrush(fallback);
    internal static void ApplyTheme(AppSettings settings)
    {
        if (Application.Current == null) return;
        ApplyThemeResources(Application.Current.Resources, settings);
    }
    internal static void ApplyThemeResources(ResourceDictionary resources, AppSettings settings)
    {
        var dark = settings.Theme == "Dark";
        if (settings.Theme == "System")
        {
            using (var registry = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) dark = registry?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        void Set(string name, string hex) => resources[name] = FrozenBrush((Color)ColorConverter.ConvertFromString(hex));
        Set("CanvasBrush", dark ? "#171E23" : "#F4F7F8"); Set("PanelBrush", dark ? "#242D34" : "#FFFFFF"); Set("InkBrush", dark ? "#E7EDF1" : "#1C2B38");
        Set("SubtleBrush", dark ? "#ABBDC8" : "#677984"); Set("BorderBrush", dark ? "#46545F" : "#D3DEE3"); Set("PinPanelBrush", dark ? "#21334B" : "#EAF2FF");
        Set("AccentBrush", settings.AccentColor); Set("NoticeBrush", dark ? "#403723" : "#FFF4D9"); Set("NoticeInkBrush", dark ? "#EED18D" : "#80621D"); resources["InterfaceFont"] = new FontFamily(settings.InterfaceFont);
    }
    private static Brush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color); brush.Freeze(); return brush;
    }
    internal static Button Button(string label, Action click, bool primary = false)
    {
        var button = new Button { Content = label, MinHeight = 34, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(3), Cursor = System.Windows.Input.Cursors.Hand };
        if (primary) { button.Background = Accent; button.Foreground = Brushes.White; button.BorderBrush = Accent; if (Application.Current != null) { button.SetResourceReference(Control.BackgroundProperty, "AccentBrush"); button.SetResourceReference(Control.BorderBrushProperty, "AccentBrush"); } }
        button.Click += (_, _) => click(); return button;
    }
    internal static TextBlock Text(string value, double size = 13, Brush? color = null)
    {
        var text = new TextBlock { Text = value, FontSize = size, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap };
        if (Application.Current != null) { if (color == null) text.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush"); else if (ReferenceEquals(color, Subtle)) text.SetResourceReference(TextBlock.ForegroundProperty, "SubtleBrush"); else if (ReferenceEquals(color, Accent)) text.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); }
        return text;
    }
    internal static MenuItem Menu(string label, Action click, bool check = false)
    {
        var item = new MenuItem { Header = label, IsChecked = check };
        item.Click += (_, _) => click(); return item;
    }
    internal static void Error(Window? owner, Exception exception, string action)
    {
        // 显示错误类别和可操作说明，不把本机文件路径或内容写入日志。
        var reason = exception is System.IO.IOException ? L.Get("文件无法写入或读取，请检查位置、磁盘空间和文件占用。") :
            exception is System.Runtime.InteropServices.ExternalException ? L.Get("Windows 接口暂时不可用，请重试。") :
            exception is ArgumentException ? exception.Message : L.Get("操作未完成，请重试。");
        if (owner?.IsVisible == true) MessageBox.Show(owner, reason, action, MessageBoxButton.OK, MessageBoxImage.Warning);
        else MessageBox.Show(reason, action, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
