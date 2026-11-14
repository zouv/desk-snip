using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskSnip.App.Imaging;
using DeskSnip.App.Windows;
using DeskSnip.Core;
using Microsoft.Win32;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace DeskSnip.App;

internal sealed class LongScreenshotWindow : ProductWindow
{
    private readonly PixelRect region;
    private readonly Action<BitmapSource> edit;
    private readonly Action<BitmapSource> pin;
    private readonly Action<BitmapSource> ocr;
    private readonly ComboBox axis = new ComboBox { ItemsSource = new[] { L.Get("纵向"), L.Get("横向") }, SelectedIndex = 0, Width = 85, Margin = new Thickness(3) };
    private readonly ComboBox direction = new ComboBox { ItemsSource = new[] { L.Get("向下 / 向右"), L.Get("向上 / 向左") }, SelectedIndex = 0, Width = 135, Margin = new Thickness(3) };
    private readonly TextBox overlap = new TextBox { Width = 100, ToolTip = L.Get("匹配失败后可填写重叠像素；留空为自动匹配。"), Margin = new Thickness(3), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly CheckBox automatic = new CheckBox { Content = L.Get("滚动后持续采集"), Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock status = Ui.Text(L.Get("只框选会滚动的内容，避开固定标题和滚动条。"), 12, Ui.Subtle);
    private readonly Image preview = new Image { Stretch = Stretch.Uniform };
    private readonly Canvas picture = new Canvas();
    private readonly Rectangle current = new Rectangle { Stroke = Ui.Accent, StrokeThickness = 3, IsHitTestVisible = false };
    private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
    private LongScreenshot? capture;
    private bool busy;
    private bool closed;
    private readonly Action<BitmapSource>? beforeOutput;
    private readonly Func<BitmapSource, Window, string?>? saveImage;
    internal LongScreenshotWindow(PixelRect region, Action<BitmapSource> edit, Action<BitmapSource> pin, Action<BitmapSource> ocr, Action<BitmapSource>? service = null, AppSettings? settings = null, Action<BitmapSource, string>? outputPerformed = null, Func<BitmapSource, Window, string?>? saveImage = null, Action<BitmapSource>? beforeOutput = null)
    {
        this.region = region; this.edit = edit; this.pin = pin; this.ocr = ocr;
        this.beforeOutput = beforeOutput; this.saveImage = saveImage;
        axis.SelectedIndex = settings?.LongScreenshotStart == "Horizontal" ? 1 : 0;
        Title = L.Get("DeskSnip · 长截图"); Width = 780; Height = 640; MinWidth = 620; MinHeight = 430; WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true; ShowActivated = false;
        var root = new DockPanel { Margin = new Thickness(16) }; Content = root;
        var bar = new WrapPanel(); DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar); bar.Children.Add(axis); bar.Children.Add(direction);
        bar.Children.Add(Ui.Button(L.Get("采集当前画面"), () => { _ = Collect(); }, true)); bar.Children.Add(automatic); bar.Children.Add(Ui.Text(L.Get("重叠像素"), 12)); bar.Children.Add(overlap);
        bar.Children.Add(Ui.Button(L.Get("撤回最后一步"), () => { capture?.Undo(); Render(); }));
        bar.Children.Add(Ui.Button(L.Get("裁掉当前画面之前"), () => { capture?.TrimBeforeCurrent(); Render(); })); bar.Children.Add(Ui.Button(L.Get("裁掉当前画面之后"), () => { capture?.TrimAfterCurrent(); Render(); }));
        bar.Children.Add(Ui.Button(L.Get("重新开始"), () => { if (capture == null || MessageBox.Show(this, L.Get("清空当前拼接结果并重新开始？"), L.Get("重新开始"), MessageBoxButton.YesNo) == MessageBoxResult.Yes) { capture = null; axis.IsEnabled = true; Render(); } }));
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom); bottom.Children.Add(status);
        var outputs = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) }; bottom.Children.Add(outputs);
        outputs.Children.Add(Ui.Button(L.Get("编辑"), () => Output(edit))); outputs.Children.Add(Ui.Button(L.Get("文字识别"), () => Output(ocr))); outputs.Children.Add(Ui.Button(L.Get("钉图"), () => Output(pin)));
        if (service != null) outputs.Children.Add(Ui.Button(L.Get("联网识别"), () => Output(service)));
        outputs.Children.Add(Ui.Button(L.Get("复制"), () => Output(image => { if (outputPerformed != null) outputPerformed(image, "Copy"); else Clipboard.SetImage(image); }))); outputs.Children.Add(Ui.Button(L.Get("保存…"), Save));
        picture.Children.Add(preview); picture.Children.Add(current);
        root.Children.Add(new ScrollViewer { Content = new Viewbox { Child = picture, StretchDirection = StretchDirection.DownOnly }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromRgb(228, 235, 237)), Margin = new Thickness(0, 12, 0, 12) });
        timer.Tick += async (_, _) => { if (!busy && automatic.IsChecked == true) await Collect(); };
        automatic.Click += (_, _) => { if (automatic.IsChecked == true) timer.Start(); else timer.Stop(); };
        Closed += (_, _) => { closed = true; timer.Stop(); };
        SourceInitialized += (_, _) => Native.SetWindowDisplayAffinity(new System.Windows.Interop.WindowInteropHelper(this).Handle, 0x11);
        Loaded += async (_, _) => { if (settings?.LongScreenshotStart == "Vertical" || settings?.LongScreenshotStart == "Horizontal") { await Collect(); if (!closed && capture != null) { automatic.IsChecked = true; timer.Start(); } } };
        PreviewKeyDown += (_, e) =>
        {
            if (settings?.LongScreenshotShortcuts == false || Keyboard.FocusedElement is TextBox) return;
            var action = ShortcutKeys.Match(settings ?? new AppSettings(), "LongScreenshot", e);
            if (action == "Capture") { _ = Collect(); e.Handled = true; }
            else if (action == "Finish") { Output(edit); e.Handled = true; }
            else if (action == "Undo") { capture?.Undo(); Render(); e.Handled = true; }
            else if (action == "Cancel") { Close(); e.Handled = true; }
        };
    }
    private async Task Collect()
    {
        if (busy) return; busy = true;
        try
        {
            int? manual = null;
            if (!string.IsNullOrWhiteSpace(overlap.Text)) { if (!int.TryParse(overlap.Text, out var parsed)) throw new ArgumentException(L.Get("重叠像素请输入整数。")); manual = parsed; }
            var visible = IsVisible; Hide();
            BitmapSource bitmap;
            try { await Task.Delay(120); if (closed) return; bitmap = ScreenCapture.Capture(region, false); }
            finally { if (visible && !closed && !Dispatcher.HasShutdownStarted) Show(); }
            var pixels = PixelBuffers.FromBitmap(bitmap);
            if (capture == null) { capture = new LongScreenshot(pixels, (StitchAxis)axis.SelectedIndex); axis.IsEnabled = false; status.Text = L.Get("首帧已保留，请回到原窗口缓慢滚动，再采集。"); }
            else
            {
                var result = capture.Append(pixels, direction.SelectedIndex == 0 ? 1 : -1, manual); status.Text = result.Message;
                if (!result.Success && result.Error > 0) { automatic.IsChecked = false; timer.Stop(); }
            }
            Render();
        }
        catch (Exception error) { automatic.IsChecked = false; timer.Stop(); Ui.Error(this, error, L.Get("长截图采集")); }
        finally { busy = false; }
    }
    private void Render()
    {
        if (capture == null) { preview.Source = null; current.Visibility = Visibility.Collapsed; return; }
        var image = PixelBuffers.ToBitmap(capture.Image); preview.Source = image; preview.Width = picture.Width = image.PixelWidth; preview.Height = picture.Height = image.PixelHeight;
        var area = capture.CurrentArea; Canvas.SetLeft(current, area.X); Canvas.SetTop(current, area.Y); current.Width = area.Width; current.Height = area.Height; current.Visibility = Visibility.Visible;
    }
    private void Output(Action<BitmapSource> action)
    {
        if (capture == null) { status.Text = L.Get("请先采集一帧。"); return; }
        automatic.IsChecked = false; timer.Stop();
        try { var image = PixelBuffers.ToBitmap(capture.Image); beforeOutput?.Invoke(image); action(image); } catch (Exception error) { Ui.Error(this, error, L.Get("长图输出")); }
    }
    private void Save()
    {
        if (saveImage != null) { Output(image => saveImage(image, this)); return; }
        var dialog = new SaveFileDialog { Filter = L.Get("PNG 图片|*.png"), FileName = "DeskSnip-long.png", AddExtension = true };
        if (capture != null && dialog.ShowDialog(this) == true) Output(image => ImageFiles.Save(image, dialog.FileName));
    }
}
