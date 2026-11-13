using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using DeskSnip.App.Recording;
using DeskSnip.App.Windows;
using DeskSnip.Core;
using Microsoft.Win32;

namespace DeskSnip.App;

internal sealed class RecordingSetup : Window
{
    private readonly ComboBox format;
    private readonly ComboBox fps;
    private readonly CheckBox cursor;
    private readonly TextBox output;
    internal string Format => (string)format.SelectedItem;
    internal int Fps => (int)fps.SelectedItem;
    internal bool CursorIncluded => cursor.IsChecked == true;
    internal string OutputPath => output.Text;
    internal RecordingSetup(PixelRect region, AppSettings settings, Func<string, string> defaultPath)
    {
        Title = "DeskSnip · 开始录制"; Width = 550; Height = 400; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new StackPanel { Margin = new Thickness(25) }; Content = root;
        root.Children.Add(Ui.Text("录制区域", 24)); root.Children.Add(Ui.Text($"{region.Width} × {region.Height} px · MP4 右 / 下边缘最多缩减 1 px", 12, Ui.Subtle));
        var options = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 14) };
        options.Children.Add(Ui.Text("格式", 13)); format = new ComboBox { Width = 100, ItemsSource = new[] { "MP4", "GIF" }, SelectedItem = settings.RecordingFormat, Margin = new Thickness(12, 0, 25, 0) }; options.Children.Add(format);
        options.Children.Add(Ui.Text("帧率", 13)); fps = new ComboBox { Width = 85, ItemsSource = new[] { 5, 10, 15, 20, 24, 30 }, SelectedItem = settings.FramesPerSecond, Margin = new Thickness(12, 0, 0, 0) };
        if (fps.SelectedIndex < 0) { fps.ItemsSource = new[] { settings.FramesPerSecond, 5, 10, 15, 20, 24, 30 }; fps.SelectedIndex = 0; } options.Children.Add(fps); root.Children.Add(options);
        cursor = new CheckBox { Content = "包含鼠标指针", IsChecked = settings.CaptureCursor, Margin = new Thickness(0, 0, 0, 15) }; root.Children.Add(cursor);
        root.Children.Add(Ui.Text("保存位置", 13));
        var pathRow = new DockPanel { Margin = new Thickness(0, 7, 0, 0) };
        var browse = Ui.Button("选择…", Browse); DockPanel.SetDock(browse, Dock.Right); pathRow.Children.Add(browse);
        output = new TextBox { Text = defaultPath(Format), IsReadOnly = true, Padding = new Thickness(7), VerticalContentAlignment = VerticalAlignment.Center }; pathRow.Children.Add(output); root.Children.Add(pathRow);
        format.SelectionChanged += (_, _) => output.Text = defaultPath(Format);
        root.Children.Add(Ui.Text("基础版录制画面；GIF 最多 15 FPS、207 万像素。", 12, Ui.Subtle));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(Ui.Button("取消", () => DialogResult = false)); buttons.Children.Add(Ui.Button("开始录制", () => DialogResult = true, true)); root.Children.Add(buttons);
    }
    private void Browse()
    {
        var dialog = new SaveFileDialog { Filter = Format == "MP4" ? "MP4 视频|*.mp4" : "GIF 动图|*.gif", FileName = System.IO.Path.GetFileName(output.Text), AddExtension = true, DefaultExt = Format.ToLowerInvariant() };
        if (dialog.ShowDialog(this) == true) output.Text = dialog.FileName;
    }
}

internal sealed class RecordingBar : Window
{
    private readonly RecordingSession session;
    private readonly Button pause;
    private readonly TextBlock label;
    private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
    internal bool Finishing { get; set; }
    internal RecordingBar(RecordingSession session, Action stop)
    {
        this.session = session;
        Title = "DeskSnip · 录制中"; Width = 420; Height = 105; WindowStyle = WindowStyle.ToolWindow; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12) }; Content = root;
        label = Ui.Text("● 录制 00:00", 16, Ui.Accent); label.Width = 165; label.VerticalAlignment = VerticalAlignment.Center; root.Children.Add(label);
        pause = Ui.Button("暂停", TogglePause); root.Children.Add(pause); root.Children.Add(Ui.Button("停止保存", stop, true));
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowDisplayAffinity(handle, 0x11);
            var desktop = ScreenCapture.DesktopBounds; var region = session.Region;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            var width = (int)Math.Ceiling(Width * dpi.DpiScaleX); var height = (int)Math.Ceiling(Height * dpi.DpiScaleY);
            var top = region.Y >= desktop.Y + height + 10 ? region.Y - height - 5 : region.Bottom + height + 10 <= desktop.Bottom ? region.Bottom + 5 : desktop.Y + 10;
            Native.SetWindowPos(handle, Native.Topmost, Math.Max(desktop.X, Math.Min(region.X, desktop.Right - width)), top, width, height, 0x0040);
        };
        timer.Tick += (_, _) => Update(); timer.Start();
        Closing += (_, e) => { if (!Finishing) { e.Cancel = true; stop(); } };
        Closed += (_, _) => timer.Stop();
    }
    internal void TogglePause() { session.TogglePause(); Update(); }
    internal void Update()
    {
        var elapsed = session.Elapsed;
        var paused = session.State == RecordingState.Paused;
        label.Text = $"{(paused ? "Ⅱ 暂停" : "● 录制")} {((int)elapsed.TotalMinutes):00}:{elapsed.Seconds:00}";
        pause.Content = paused ? "继续" : "暂停";
    }
    internal void ShowFinishing()
    {
        Finishing = true; label.Text = "正在保存…"; pause.IsEnabled = false; timer.Stop();
    }
}
