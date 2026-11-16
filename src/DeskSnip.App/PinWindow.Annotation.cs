using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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
        inlineCanvas = new AnnotationCanvas(Bitmap, current) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        inlineCanvas.TextEditorPointerDown += InlineTextPointerDown;
        grid.Children.Insert(1, inlineCanvas);
        image.Visibility = Visibility.Hidden;
        inlineCanvas.SetTool(kind);
        if (current.MarkAutomaticWatermark) inlineCanvas.AddAutomaticWatermark();
        inlineEditing = true; UpdateInlineLayout();
        inlineBar = new InlineAnnotationBar(inlineCanvas, current, withFinish: true, showShortcuts: current.PinShowMenuShortcuts);
        inlineBar.Finished += CommitInlineEdit; inlineBar.Cancelled += CancelInlineEdit;
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
            inlinePopup = new Popup { PlacementTarget = this, Placement = PlacementMode.Custom, StaysOpen = true, AllowsTransparency = true, PopupAnimation = PopupAnimation.None, Child = inlineBar };
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
        if (!inlineEditing || !IsLoaded) return;
        if (inlinePopup != null) inlinePopup.IsOpen = true;
        if (inlineListPopup != null) inlineListPopup.IsOpen = true;
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
        var kind = inlineCanvas.LastTool;
        EndInlineEdit();
        Replace(output);
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
        inlineBar = null; inlineEditing = false;
        if (inlineCanvas != null) { inlineCanvas.TextEditorPointerDown -= InlineTextPointerDown; grid.Children.Remove(inlineCanvas); inlineCanvas = null; }
        image.Visibility = Visibility.Visible;
        if (focus && IsVisible) Focus();
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
