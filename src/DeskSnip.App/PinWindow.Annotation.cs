using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DeskSnip.App.Imaging;
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
        grid.Children.Insert(1, inlineCanvas);
        image.Visibility = Visibility.Hidden;
        inlineCanvas.SetTool(kind);
        if (current.MarkAutomaticWatermark) inlineCanvas.AddAutomaticWatermark();
        inlineEditing = true; UpdateInlineLayout();
        inlineBar = new InlineAnnotationBar(inlineCanvas, current, withFinish: true, showShortcuts: current.PinShowMenuShortcuts);
        inlineBar.Finished += CommitInlineEdit; inlineBar.Cancelled += CancelInlineEdit;
        if (IsLoaded)
        {
            inlinePopup = new Popup { PlacementTarget = this, Placement = PlacementMode.Custom, StaysOpen = true, AllowsTransparency = true, PopupAnimation = PopupAnimation.None, Child = inlineBar };
            inlinePopup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
            {
                var x = (targetSize.Width - popupSize.Width) / 2;
                return new[] { new CustomPopupPlacement(new Point(x, -popupSize.Height - 6), PopupPrimaryAxis.Horizontal), new CustomPopupPlacement(new Point(x, targetSize.Height + 6), PopupPrimaryAxis.Horizontal) };
            };
            inlinePopup.IsOpen = true;
        }
        Activate(); inlineCanvas.Focus(); Keyboard.Focus(inlineCanvas);
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
        inlineBar = null; inlineEditing = false;
        if (inlineCanvas != null) { grid.Children.Remove(inlineCanvas); inlineCanvas = null; }
        image.Visibility = Visibility.Visible;
        if (focus && IsVisible) Focus();
    }
    private void UpdateInlineLayout()
    {
        if (inlineCanvas == null || Bitmap.PixelWidth == 0 || Bitmap.PixelHeight == 0 || image.ActualWidth <= 0 || image.ActualHeight <= 0) return;
        inlineCanvas.LayoutTransform = new ScaleTransform(image.ActualWidth / Bitmap.PixelWidth, image.ActualHeight / Bitmap.PixelHeight);
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
