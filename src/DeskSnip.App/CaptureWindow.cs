using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.App.Windows;
using DeskSnip.Core;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace DeskSnip.App;

internal sealed class CaptureWindow : Window
{
    private readonly PixelRect bounds;
    private readonly BitmapSource desktop;
    private readonly Canvas surface = new Canvas();
    private readonly System.Windows.Shapes.Path mask = new System.Windows.Shapes.Path { Fill = new SolidColorBrush(Color.FromArgb(125, 0, 0, 0)), IsHitTestVisible = false };
    private readonly Rectangle outline = new Rectangle { Stroke = Ui.Accent, StrokeThickness = 2, IsHitTestVisible = false };
    private readonly Border hud;
    private readonly TextBlock hint;
    private readonly StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal };
    private Native.Point dragStart;
    private PixelRect previous;
    private bool dragging;
    private bool moving;
    private string color = "";
    internal PixelRect Selection { get; private set; }

    internal CaptureWindow(BitmapSource desktop, PixelRect bounds, bool recording)
    {
        this.desktop = desktop; this.bounds = bounds;
        Title = recording ? "DeskSnip · 选择录制区域" : "DeskSnip · 截图选区";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
        Left = bounds.X; Top = bounds.Y; Width = bounds.Width; Height = bounds.Height;
        Background = Brushes.Black; Cursor = Cursors.Cross;
        var image = new Image { Source = desktop, Stretch = Stretch.Fill, IsHitTestVisible = false };
        var grid = new Grid(); grid.Children.Add(image); grid.Children.Add(surface); Content = grid;
        surface.Background = Brushes.Transparent; surface.Children.Add(mask); surface.Children.Add(outline);
        hint = Ui.Text("拖动选择区域 · Esc 取消", 13, Brushes.White);
        hud = new Border { Background = new SolidColorBrush(Color.FromArgb(240, 23, 38, 50)), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8), Child = hint, IsHitTestVisible = false };
        surface.Children.Add(hud);
        actions.Children.Add(Ui.Button(recording ? "使用此区域 ↵" : "编辑截图 ↵", Accept, true));
        actions.Children.Add(Ui.Button("重新选择", () => { Selection = default; Redraw(); }));
        actions.Children.Add(Ui.Button("取消 Esc", () => DialogResult = false)); surface.Children.Add(actions);
        SourceInitialized += (_, _) => Position();
        Loaded += (_, _) => { Position(); Focus(); Redraw(); };
        SizeChanged += (_, _) => Redraw();
        surface.MouseLeftButtonDown += Down;
        surface.MouseMove += Move;
        surface.MouseLeftButtonUp += Up;
        surface.MouseRightButtonDown += (_, _) => DialogResult = false;
        PreviewKeyDown += KeyDownHandler;
    }
    private void Position() => Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.Topmost, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0040);
    private Native.Point CursorPosition()
    {
        Native.GetCursorPos(out var point);
        point.X = Math.Max(bounds.X, Math.Min(bounds.Right, point.X)); point.Y = Math.Max(bounds.Y, Math.Min(bounds.Bottom, point.Y)); return point;
    }
    private void Down(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement element && (element is Button || FindButton(element))) return;
        if (e.ClickCount == 2 && !Selection.IsEmpty) { Accept(); return; }
        dragStart = CursorPosition(); previous = Selection;
        moving = !Selection.IsEmpty && dragStart.X >= Selection.X && dragStart.X < Selection.Right && dragStart.Y >= Selection.Y && dragStart.Y < Selection.Bottom;
        dragging = true; surface.CaptureMouse();
        if (!moving) Selection = default; Redraw();
    }
    private static bool FindButton(DependencyObject child)
    {
        for (var parent = VisualTreeHelper.GetParent(child); parent != null; parent = VisualTreeHelper.GetParent(parent)) if (parent is Button) return true;
        return false;
    }
    private void Move(object sender, MouseEventArgs e)
    {
        var point = CursorPosition();
        if (dragging)
        {
            Selection = moving ? previous.Nudge(point.X - dragStart.X, point.Y - dragStart.Y, bounds, false) : PixelRect.FromPoints(dragStart.X, dragStart.Y, point.X, point.Y).Intersect(bounds);
        }
        var x = Math.Max(0, Math.Min(desktop.PixelWidth - 1, point.X - bounds.X));
        var y = Math.Max(0, Math.Min(desktop.PixelHeight - 1, point.Y - bounds.Y));
        var pixel = new byte[4]; desktop.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        color = $"#{pixel[2]:X2}{pixel[1]:X2}{pixel[0]:X2}";
        Redraw();
    }
    private void Up(object sender, MouseButtonEventArgs e) { dragging = false; surface.ReleaseMouseCapture(); Redraw(); }
    private void Accept() { if (!Selection.IsEmpty) DialogResult = true; }
    private void KeyDownHandler(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; return; }
        if (e.Key == Key.Enter) { Accept(); e.Handled = true; return; }
        if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { Selection = bounds; Redraw(); e.Handled = true; return; }
        if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && color.Length > 0)
        { try { Clipboard.SetText(color); hint.Text = "已复制 " + color; } catch (System.Runtime.InteropServices.ExternalException) { hint.Text = "剪贴板忙，请重试"; } e.Handled = true; return; }
        var dx = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0;
        var dy = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
        if ((dx != 0 || dy != 0) && !Selection.IsEmpty)
        {
            var step = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? 10 : 1;
            Selection = Selection.Nudge(dx * step, dy * step, bounds, (Keyboard.Modifiers & ModifierKeys.Shift) != 0); Redraw(); e.Handled = true;
        }
    }
    private void Redraw()
    {
        if (surface.ActualWidth < 1 || surface.ActualHeight < 1) return;
        var sx = surface.ActualWidth / bounds.Width; var sy = surface.ActualHeight / bounds.Height;
        var rect = new Rect((Selection.X - bounds.X) * sx, (Selection.Y - bounds.Y) * sy, Selection.Width * sx, Selection.Height * sy);
        mask.Data = Selection.IsEmpty ? new RectangleGeometry(new Rect(0, 0, surface.ActualWidth, surface.ActualHeight)) :
            new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, surface.ActualWidth, surface.ActualHeight)), new RectangleGeometry(rect));
        outline.Visibility = Selection.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        outline.Width = rect.Width; outline.Height = rect.Height; Canvas.SetLeft(outline, rect.X); Canvas.SetTop(outline, rect.Y);
        hint.Text = Selection.IsEmpty ? $"拖动选择区域 · Ctrl+A 全屏 · {color} · Ctrl+C 取色 · Esc 取消" :
            $"{Selection.Width} × {Selection.Height} px · {color}\n拖动内部移动 · 外部重选 · 方向键微调 · Shift+方向键改尺寸 · Ctrl+C 取色";
        Canvas.SetLeft(hud, Math.Max(12, Math.Min(Selection.IsEmpty ? 30 : rect.X, surface.ActualWidth - 560)));
        Canvas.SetTop(hud, Selection.IsEmpty ? 30 : Math.Max(12, rect.Y - 70));
        actions.Visibility = Selection.IsEmpty || dragging ? Visibility.Collapsed : Visibility.Visible;
        Canvas.SetLeft(actions, Math.Max(12, Math.Min(rect.Right - 390, surface.ActualWidth - 420)));
        Canvas.SetTop(actions, Math.Max(12, Math.Min(rect.Bottom + 10, surface.ActualHeight - 52)));
    }
}
