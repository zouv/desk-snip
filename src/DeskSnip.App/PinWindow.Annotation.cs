using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.App.Imaging;
using DeskSnip.App.Windows;
using DeskSnip.Core;

namespace DeskSnip.App;

internal sealed partial class PinWindow
{
    internal bool InlineEditing => inlineEditing;
    internal AnnotationCanvas? InlineCanvas => inlineCanvas;
    internal void BeginInlineEdit(AnnotationKind? tool = null)
    {
        if (inlineEditing || closed || Presentation.ClickThrough) return;
        var current = Settings;
        var kind = tool ?? (current.MarkRememberPinTool && Enum.TryParse(current.MarkLastPinTool, out AnnotationKind remembered) ? remembered : AnnotationKind.Arrow);
        var inherited = editableDocument;
        inlineCanvas = new AnnotationCanvas(inherited == null ? Bitmap : editableBase, current, inherited?.Clone()) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        inlineCanvas.TextEditorPointerDown += InlineTextPointerDown;
        grid.Children.Insert(1, inlineCanvas);
        image.Visibility = Visibility.Hidden;
        inlineCanvas.SetTool(kind);
        if (current.MarkAutomaticWatermark && inherited == null) inlineCanvas.AddAutomaticWatermark();
        inlineEditing = true; UpdateInlineLayout();
        inlineBar = new InlineAnnotationBar(inlineCanvas, current, withFinish: true, showShortcuts: current.PinShowMenuShortcuts);
        inlineBar.Finished += CommitInlineEdit; inlineBar.Cancelled += CancelInlineEdit;
        inlineActionBar = BuildInlineActionBar();
        inlineList = new AnnotationListBar(inlineCanvas);
        try
        {
            var origin = PointToScreen(new Point(0, 0));
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)origin.X, (int)origin.Y));
            var dpi = VisualTreeHelper.GetDpi(this);
            inlineList.MaxHeight = Math.Max(120, (screen.WorkingArea.Bottom - origin.Y) / dpi.DpiScaleY - 8);
        }
        catch (InvalidOperationException) { inlineList.MaxHeight = Math.Max(120, SystemParameters.WorkArea.Height - 16); }
        if (IsLoaded)
        {
            var inlineLayout = new StackPanel { Orientation = Orientation.Horizontal };
            inlineLayout.Children.Add(inlineBar); inlineLayout.Children.Add(inlineActionBar);
            inlinePopup = new Popup { PlacementTarget = this, Placement = PlacementMode.Custom, StaysOpen = true, AllowsTransparency = true, PopupAnimation = PopupAnimation.None, Child = inlineLayout };
            inlinePopup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
                new[] { new CustomPopupPlacement(new Point(0, targetSize.Height + 6), PopupPrimaryAxis.Horizontal), new CustomPopupPlacement(new Point(0, -popupSize.Height - 6), PopupPrimaryAxis.Horizontal) };
            inlinePopup.IsOpen = true;
            inlineListPopup = new Popup { PlacementTarget = this, Placement = PlacementMode.Custom, StaysOpen = true, AllowsTransparency = true, PopupAnimation = PopupAnimation.None, Child = inlineList };
            inlineListPopup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
                new[] { new CustomPopupPlacement(new Point(-popupSize.Width - 6, 0), PopupPrimaryAxis.Vertical), new CustomPopupPlacement(new Point(targetSize.Width + 6, 0), PopupPrimaryAxis.Vertical) };
            inlineListPopup.IsOpen = true;
        }
        Activate(); inlineCanvas.Focus(); Keyboard.Focus(inlineCanvas);
    }
    private void ShowInlinePopups()
    {
        if (!inlineEditing || !IsLoaded || !IsVisible) return;
        if (inlinePopup != null) inlinePopup.IsOpen = true;
        if (inlineListPopup != null) inlineListPopup.IsOpen = true;
    }
    private void RepositionInlinePopups()
    {
        // .NET Framework 不公开 Popup.Reposition；改变偏移会触发布局且不会释放按钮的鼠标捕获。
        if (inlinePopup != null && inlinePopup.IsOpen) { inlinePopup.HorizontalOffset = .01; inlinePopup.HorizontalOffset = 0; }
        if (inlineListPopup != null && inlineListPopup.IsOpen) { inlineListPopup.HorizontalOffset = .01; inlineListPopup.HorizontalOffset = 0; }
    }
    private void HideInlinePopups()
    {
        if (inlinePopup != null) inlinePopup.IsOpen = false;
        if (inlineListPopup != null) inlineListPopup.IsOpen = false;
    }
    private void CheckInlineFocus()
    {
        if (!inlineEditing) return;
        var foreground = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(foreground, out var process);
        if (process != PinProcess) HideInlinePopups();
    }
    internal void CommitInlineEdit()
    {
        if (!inlineEditing || inlineCanvas == null) return;
        var output = AnnotationRenderer.Flatten(inlineCanvas.Image, inlineCanvas.Document.Items);
        var baseImage = inlineCanvas.Image; var document = inlineCanvas.Document.Clone();
        var kind = inlineCanvas.LastTool;
        EndInlineEdit();
        Replace(output); editableBase = baseImage; editableDocument = document;
        RememberToolRequested?.Invoke(kind);
    }
    internal void CancelInlineEdit()
    {
        if (!inlineEditing || inlineCanvas == null) return;
        var kind = inlineCanvas.LastTool;
        EndInlineEdit();
        RememberToolRequested?.Invoke(kind);
    }
    private void EndInlineEdit(bool focus = true)
    {
        if (inlinePopup != null) { inlinePopup.IsOpen = false; inlinePopup = null; }
        if (inlineListPopup != null) { inlineListPopup.IsOpen = false; inlineListPopup = null; }
        inlineList = null;
        inlineBar = null; inlineActionBar = null; inlineEditing = false;
        if (inlineCanvas != null) { inlineCanvas.TextEditorPointerDown -= InlineTextPointerDown; grid.Children.Remove(inlineCanvas); inlineCanvas = null; }
        image.Visibility = Visibility.Visible;
        if (focus && IsVisible) Focus();
    }
    private FloatingToolbar BuildInlineActionBar()
    {
        var bar = new FloatingToolbar { Name = "PinInlineActions", VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, 0, 0, 0) };
        bar.Items.Children.Add(FloatingTools.Button(ToolGlyph.Ocr, L.Get("文字识别"), () => RecognizeRequested?.Invoke(CurrentInlineOutput()), "Shift+C"));
        bar.Items.Children.Add(FloatingTools.Button(ToolGlyph.Cloud, L.Get("联网识别"), () => ServiceRecognitionRequested?.Invoke(CurrentInlineOutput())));
        var save = FloatingTools.Menu();
        save.Items.Add(FloatingTools.Item(L.Get("保存图片"), SaveInline, "Ctrl+S"));
        save.Items.Add(FloatingTools.Item(L.Get("快速保存"), () => QuickSaveRequested?.Invoke(CurrentInlineOutput()), "Ctrl+Shift+S"));
        bar.Items.Children.Add(FloatingTools.MenuButton(ToolGlyph.Save, L.Get("保存"), save));
        var more = FloatingTools.Menu();
        more.Items.Add(FloatingTools.Item(L.Get("复制原始图片"), () => Copy(original)));
        more.Items.Add(FloatingTools.Item(L.Get("窗口置顶"), () => { Presentation.Topmost = !Presentation.Topmost; SetInteraction(); }, "T"));
        bar.Items.Children.Add(FloatingTools.MenuButton(ToolGlyph.More, L.Get("更多钉图操作"), more));
        bar.Items.Children.Add(FloatingTools.Divider());
        var drag = FloatingTools.Button(ToolGlyph.Grip, L.Get("拖动钉图面板"), () => { }); AttachInlinePanelDrag(drag); bar.Items.Children.Add(drag);
        bar.Items.Children.Add(FloatingTools.Button(ToolGlyph.Copy, L.Get("复制当前标注图"), () => Copy(CurrentInlineOutput()), "Ctrl+C", true));
        FloatingTools.Configure(bar.Items, Settings.ToolbarIconSize, Settings.PinShowMenuShortcuts); return bar;
    }
    private BitmapSource CurrentInlineOutput()
    {
        if (inlineCanvas == null) return OutputBitmap;
        inlineCanvas.CommitPendingText(); return AnnotationRenderer.Flatten(inlineCanvas.Image, inlineCanvas.Document.Items);
    }
    private void SaveInline()
    {
        var output = CurrentInlineOutput(); if (SaveRequested != null) SaveRequested(output, this); else SaveWithDialog(output);
    }
    private void AttachInlinePanelDrag(Button button)
    {
        var dragging = false; Native.Point start = default;
        button.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!CanMove) return;
            dragging = true; Native.GetCursorPos(out start); button.CaptureMouse(); e.Handled = true;
        };
        button.PreviewMouseMove += (_, e) =>
        {
            if (!dragging) return;
            Native.GetCursorPos(out var point); var dx = point.X - start.X; var dy = point.Y - start.Y; start = point;
            if (dx != 0 || dy != 0) { if (MoveRequested != null) MoveRequested(this, dx, dy); else MoveByPhysical(dx, dy); }
            e.Handled = true;
        };
        button.PreviewMouseLeftButtonUp += (_, e) => { if (!dragging) return; dragging = false; button.ReleaseMouseCapture(); PresentationChanged?.Invoke(); e.Handled = true; };
        button.LostMouseCapture += (_, _) => dragging = false;
    }
    private void UpdateInlineLayout()
    {
        if (inlineCanvas == null || Bitmap.PixelWidth == 0 || Bitmap.PixelHeight == 0 || image.ActualWidth <= 0 || image.ActualHeight <= 0) return;
        inlineCanvas.LayoutTransform = new ScaleTransform(image.ActualWidth / Bitmap.PixelWidth, image.ActualHeight / Bitmap.PixelHeight);
    }
    /// <summary>文字输入 Popup 是独立窗口；双击首下打开后，第二下落在输入框上时同样按双击复制并关闭处理。</summary>
    private void InlineTextPointerDown()
    {
        if (!inlineEditing || closed) return;
        Native.GetCursorPos(out var point);
        var threshold = System.Windows.Forms.SystemInformation.DoubleClickSize;
        if ((DateTime.UtcNow - inlineClickUtc).TotalMilliseconds > System.Windows.Forms.SystemInformation.DoubleClickTime) return;
        if (Math.Abs(point.X - inlineClickScreen.X) > threshold.Width / 2 || Math.Abs(point.Y - inlineClickScreen.Y) > threshold.Height / 2) return;
        CopyAndClose();
    }
    private bool HandleInlineEditKey(KeyEventArgs e)
    {
        if (inlineCanvas == null) return false;
        if (inlineCanvas.HandleEditKey(e)) return true;
        if (e.Key == Key.Space || e.Key == Key.Enter) { CommitInlineEdit(); return true; }
        if (e.Key == Key.Escape) { CancelInlineEdit(); return true; }
        return false;
    }
}
