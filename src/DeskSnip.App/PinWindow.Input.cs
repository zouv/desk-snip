using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeskSnip.App.Imaging;
using DeskSnip.App.Windows;
using DeskSnip.Core;

namespace DeskSnip.App;

internal sealed partial class PinWindow
{
    internal Action? DestroyRequested { get; set; }
    internal Action<System.Windows.Media.Imaging.BitmapSource>? CopyTextRequested { get; set; }
    internal Action<System.Windows.Media.Imaging.BitmapSource, string>? ServiceOperationRequested { get; set; }
    private MouseButton? draggingButton;
    private readonly PinPointerGesture pointerGesture = new PinPointerGesture();
    internal void SetClickThrough(bool enabled) { Presentation.ClickThrough = enabled; SetInteraction(); }
    private string? BoundMouse(string gesture)
    {
        var preferences = Settings;
        if (preferences.PinMouseBindings != null) return InputBindingRules.MouseAction(preferences.PinMouseBindings, ShortcutKeys.Modifiers(Keyboard.Modifiers), gesture);
        var modifiers = Keyboard.Modifiers;
        if (gesture == "Wheel") return (modifiers & ModifierKeys.Control) != 0 ? preferences.PinControlWheelAction : (modifiers & ModifierKeys.Alt) != 0 && preferences.PinWheelAction == "Zoom" ? "FineScale" : preferences.PinWheelAction;
        if (gesture == "MiddleClick") return preferences.PinMiddleAction;
        if (gesture == "LeftDoubleClick") return preferences.PinDoubleClickAction;
        return InputBindingRules.MouseAction(InputBindingCatalog.DefaultPin(preferences), ShortcutKeys.Modifiers(modifiers), gesture);
    }
    private static string ButtonName(MouseButton button) => button == MouseButton.Left ? "Left" : button == MouseButton.Right ? "Right" : button == MouseButton.Middle ? "Middle" : button == MouseButton.XButton1 ? "X1" : "X2";
    private void InitializeConfiguredMouse()
    {
        MouseDown += (_, e) =>
        {
            if (e.OriginalSource is Button || OverControls(e.GetPosition(controls))) return;
            var button = ButtonName(e.ChangedButton); var action = BoundMouse(button + (e.ClickCount >= 2 ? "DoubleClick" : "Click"));
            if (e.ClickCount >= 2) { pointerGesture.Cancel(); if (action != null && action != "None") { ExecuteMouse(action); e.Handled = true; } return; }
            if (e.ChangedButton == MouseButton.Left && action != "MultiSelect") SelectRequested?.Invoke(this, false);
            var dragAction = BoundMouse(button + "Drag");
            if (dragAction == null && Settings.PinMouseBindings == null && e.ChangedButton == MouseButton.Left && Keyboard.Modifiers == ModifierKeys.None) dragAction = "Move";
            if (dragAction == "Move" && !CanMove) { dragAction = null; UpdateFrame(); }
            Native.GetCursorPos(out dragStart); pointerGesture.Begin(button, new PixelPoint(dragStart.X, dragStart.Y), action == "None" ? null : action, dragAction == "None" ? null : dragAction);
            if (!pointerGesture.Active) return;
            var point = e.GetPosition(this); resizeHandle = SelectionGeometry.HitTest(new PixelRect(0, 0, (int)ActualWidth, (int)ActualHeight), new PixelPoint(point.X, point.Y), 6, 6);
            draggingButton = e.ChangedButton; initialScale = Presentation.Scale; initialLeft = Left; initialTop = Top;
            CaptureMouse(); e.Handled = true;
        };
        MouseMove += (_, e) =>
        {
            if (!pointerGesture.Active) return; Native.GetCursorPos(out var point);
            var began = pointerGesture.Move(new PixelPoint(point.X, point.Y));
            if (began == "DragContent") { pointerGesture.Cancel(); ReleaseMouseCapture(); var data = new DataObject(); data.SetData(DataFormats.Bitmap, OutputBitmap); DragDrop.DoDragDrop(this, data, DragDropEffects.Copy); e.Handled = true; return; }
            if (began == "Move") { if (Settings.PinResizeFromBorder && resizeHandle != SelectionHandle.None && resizeHandle != SelectionHandle.Move) resizing = true; else moving = true; }
            ResizeDrag(); if (!moving || !CanMove) return; var dx = point.X - dragStart.X; var dy = point.Y - dragStart.Y; dragStart = point; if (dx == 0 && dy == 0) return;
            if (MoveRequested != null) MoveRequested(this, dx, dy); else MoveByPhysical(dx, dy);
        };
        MouseUp += (_, e) => { if (draggingButton == e.ChangedButton) { Native.GetCursorPos(out var point); var action = pointerGesture.Finish(ButtonName(e.ChangedButton), new PixelPoint(point.X, point.Y)); resizing = moving = false; draggingButton = null; ReleaseMouseCapture(); if (action != null && action != "Move" && action != "DragContent") ExecuteMouse(action); PresentationChanged?.Invoke(); e.Handled = true; } };
        LostMouseCapture += (_, _) => { pointerGesture.Cancel(); resizing = moving = false; draggingButton = null; };
        MouseWheel += (_, e) => { var action = BoundMouse("Wheel"); if (action == null || action == "None") return; ExecuteMouse(action, e.Delta); e.Handled = true; };
    }
    internal void ExecuteMouse(string action, int direction = 1)
    {
        switch (action)
        {
            case "Scale": MouseAction("Zoom", direction, Settings.PinWheelAnchor); break;
            case "FineScale": ResizeAnchored(() => Presentation.Zoom(direction, true, Settings.PinZoomStep, Settings.PinFineZoomStep), Settings.PinWheelAnchor); break;
            case "Reset": MouseAction("Original", direction, Settings.PinMiddleAnchor); break;
            case "CopyClose": if (!PinPreferenceRules.CloseAllowed(Settings, Presentation.Locked, false)) return; Copy(); RequestCloseFromInput(false); break;
            case "Destroy": if (!PinPreferenceRules.CloseAllowed(Settings, Presentation.Locked, false)) return; if (MessageBox.Show(this, L.Get("永久销毁当前钉图？此操作不能从关闭历史恢复。"), L.Get("销毁钉图"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) DestroyRequested?.Invoke(); break;
            case "CopyText": (CopyTextRequested ?? RecognizeRequested)?.Invoke(OutputBitmap); break;
            case "MultiSelect": SelectRequested?.Invoke(this, true); break;
            case "Menu": Menu(); break; case "Markup": edit(Bitmap); break;
            default: MouseAction(action, direction, action == "Original" ? Settings.PinMiddleAnchor : Settings.PinWheelAnchor); break;
        }
    }
    private bool HandleConfiguredKey(KeyEventArgs e)
    {
        var action = ShortcutKeys.Match(Settings, "ImagePin", e) ?? ShortcutKeys.Match(Settings, "Pin", e) ?? ShortcutKeys.Match(Settings, "Common", e);
        if (action == null) return false;
        switch (action)
        {
            case "Cancel": case "Close": RequestCloseFromInput(true); break; case "Lock": Presentation.Locked = !Presentation.Locked; SetInteraction(); break;
            case "Topmost": Presentation.Topmost = !Presentation.Topmost; SetInteraction(); break; case "Hide": WantedVisible = false; Hide(); PresentationChanged?.Invoke(); break;
            case "Copy": Copy(); break; case "Save": Save(); break; case "QuickSave": QuickSaveRequested?.Invoke(OutputBitmap); break;
            case "Ocr": RecognizeRequested?.Invoke(Bitmap); break;
            case "Service": case "Document": case "Table": case "Formula": if (ServiceOperationRequested != null) ServiceOperationRequested(Bitmap, action == "Service" ? "translation" : action.ToLowerInvariant()); else ServiceRecognitionRequested?.Invoke(Bitmap); break;
            case "Paste": if (!Presentation.Locked || !Settings.PinLockDrop) { var bitmap = Clipboard.GetImage(); if (bitmap != null) Replace(bitmap); } break;
            case "Original": MouseAction("Original", 1, Settings.PinMiddleAnchor); break;
            case "RotateLeft": Replace(ImageTransforms.Apply(ImageTransforms.Apply(ImageTransforms.Apply(Bitmap, ImageTransform.RotateClockwise), ImageTransform.RotateClockwise), ImageTransform.RotateClockwise)); break;
            case "RotateRight": Transform(ImageTransform.RotateClockwise); break; case "Mirror": Transform(ImageTransform.FlipHorizontal); break; case "Flip": Transform(ImageTransform.FlipVertical); break;
            case "Edit": edit(Bitmap); break;
            case "MoveLeft": case "MoveRight": case "MoveUp": case "MoveDown": var dx = action == "MoveLeft" ? -1 : action == "MoveRight" ? 1 : 0; var dy = action == "MoveUp" ? -1 : action == "MoveDown" ? 1 : 0; if (CanMove) { if (MoveRequested != null) MoveRequested(this, dx, dy); else MoveByPhysical(dx, dy); } break;
            default: return false;
        }
        e.Handled = true; return true;
    }
}
