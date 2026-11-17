using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DeskSnip.App.Windows;
using DeskSnip.Core;

namespace DeskSnip.App;

internal sealed partial class CaptureWindow
{
    private bool configuredGuides;
    private void HandleConfiguredKeys(KeyEventArgs e)
    {
        if (inlineEditing && inlineCanvas != null && inlineCanvas.HandleEditKey(e)) { e.Handled = true; return; }
        var key = e.Key == Key.System ? e.SystemKey : e.Key; var modifiers = Keyboard.Modifiers;
        var action = ShortcutKeys.Match(settings, "Screenshot", e) ?? ShortcutKeys.Match(settings, "Common", e);
        if (action == "Cancel") { if (info.IsExpanded) { info.SetExpanded(false); Focus(); } else CancelFromKey(); e.Handled = true; return; }
        if (Keyboard.FocusedElement is TextBox) return;
        if (key == Key.Escape && !inlineEditing) { if (info.IsExpanded) { info.SetExpanded(false); Focus(); } else CancelFromKey(); e.Handled = true; return; }
        if ((key == Key.Enter || key == Key.Space) && Keyboard.FocusedElement is ButtonBase) return;
        if (action == "Record") { ToggleRecording(); e.Handled = true; return; }
        var output = action switch
        {
            "Copy" => CaptureAction.Copy, "Save" => CaptureAction.Save, "QuickSave" => CaptureAction.QuickSave, "Pin" => CaptureAction.Pin, "Ocr" => CaptureAction.Ocr,
            "Service" => CaptureAction.TranslationRecognition, "Document" => CaptureAction.DocumentRecognition, "Table" => CaptureAction.TableRecognition, "Formula" => CaptureAction.FormulaRecognition, "LongScreenshot" => CaptureAction.LongScreenshot,
            "ToolRectangle" => CaptureAction.EditRectangle, "ToolPolyline" => CaptureAction.EditPolyline, "ToolArrow" => CaptureAction.EditArrow, "ToolNumber" => CaptureAction.EditNumber, "ToolPen" => CaptureAction.EditBrush,
            "ToolCover" => CaptureAction.EditCover, "ToolMosaic" => CaptureAction.EditMosaic, "ToolText" => CaptureAction.EditText, "ToolEraser" => CaptureAction.EditEraser, "ToolHighlight" => CaptureAction.EditHighlight,
            "ToolWatermark" => CaptureAction.EditWatermark, "ToolSpotlight" => CaptureAction.EditSpotlight, "ToolMagnifier" => CaptureAction.EditMagnifier, "AutoMosaic" => CaptureAction.EditAutoMosaic,
            _ => (CaptureAction?)null
        };
        if (output.HasValue)
        {
            if (!recordingMode)
            {
                if (AnnotationTool(output.Value) is AnnotationKind kind)
                {
                    if (inlineEditing) { inlineCanvas?.SetTool(kind); inlineCanvas?.Focus(); }
                    else BeginInlineEdit(kind);
                    if (output.Value == CaptureAction.EditAutoMosaic) DetectInlinePrivacy();
                }
                else Complete(output.Value);
            }
            e.Handled = true; return;
        }
        switch (action)
        {
            case "Confirm": Complete(recordingMode ? CaptureAction.Record : CaptureAction.Copy); break;
            case "Close": CancelFromKey(); break;
            case "Detection": if (settings.ScreenshotUiDetection != "None") { elements = !elements; targetIndex = 0; UpdateHover(); Redraw(); } break;
            case "Cursor": cursorVisible = !cursorVisible; desktopImage.Source = SelectedDesktop; break;
            case "Shortcuts": showShortcuts = !showShortcuts; FloatingTools.Configure(actions.Items, settings.ToolbarIconSize, showShortcuts); FloatingTools.Configure(recordActions.Items, settings.ToolbarIconSize, showShortcuts); FloatingTools.Configure(modes.Items, settings.ToolbarIconSize, showShortcuts); Redraw(); break;
            case "PreviousArea": case "NextArea": if (recent.Count > 0) { var index = Math.Max(0, Array.FindIndex(recent.ToArray(), r => r.Equals(Selection))); index = (index + (action == "PreviousArea" ? 1 : recent.Count - 1)) % recent.Count; SetSelection(recent[index]); } break;
            case "SelectAll": var monitor = ScreenCapture.MonitorBounds(cursor).Intersect(bounds); SetSelection(Selection.Equals(monitor) ? bounds : monitor); break;
            case "CopyColor": CopySample(false); break; case "CopyCoordinates": CopySample(true); break;
            case "Coordinates": relativeCoordinates = !relativeCoordinates; Redraw(); break;
            case "MagnifierLock": magnifierPinned = !magnifierPinned; Redraw(); break;
            case "Toolbar": if (dragging) UpdateDrag(CursorPosition()); else if (!e.IsRepeat) toolsHidden = !toolsHidden; Redraw(); break;
            case "NextColor": if (!dragging && !e.IsRepeat && (Selection.IsEmpty || magnifierPinned || (modifiers & ModifierKeys.Alt) != 0)) colorFormat = colorFormats[(Array.IndexOf(colorFormats, colorFormat) + 1) % colorFormats.Length]; if (dragging) UpdateDrag(CursorPosition()); Redraw(); break;
            case "Guides": configuredGuides = true; if (dragging) UpdateDrag(CursorPosition()); Redraw(); break;
            case "CursorUp": case "CursorDown": case "CursorLeft": case "CursorRight": var next = inspector.Clamp(new PixelPoint(cursor.X + (action == "CursorLeft" ? -1 : action == "CursorRight" ? 1 : 0), cursor.Y + (action == "CursorUp" ? -1 : action == "CursorDown" ? 1 : 0))); if (Native.SetCursorPos((int)next.X, (int)next.Y)) { cursor = next; if (dragging) UpdateDrag(next); Redraw(); } break;
            case "WheelUp": case "WheelDown": if (!Selection.IsEmpty && settings.ScreenshotWheelResize) SetSelection(SelectionGeometry.Scale(Selection, action == "WheelUp" ? 2 : -2, bounds, lockedRatio)); else if (settings.ScreenshotWheelTargets && settings.ScreenshotUiDetection != "None") { targetIndex = Math.Max(0, targetIndex + (action == "WheelUp" ? -1 : 1)); UpdateHover(); Redraw(); } break;
            default:
                var dx = key == Key.Left ? -1 : key == Key.Right ? 1 : 0; var dy = key == Key.Up ? -1 : key == Key.Down ? 1 : 0;
                if ((dx != 0 || dy != 0) && !Selection.IsEmpty && !dragging) { var control = (modifiers & ModifierKeys.Control) != 0; var shift = (modifiers & ModifierKeys.Shift) != 0; SetSelection(shift || control ? SelectionGeometry.AdjustEdge(Selection, dx, dy, bounds, control && !shift) : Selection.Nudge(dx, dy, bounds, false)); e.Handled = true; }
                return;
        }
        e.Handled = true;
    }
}
