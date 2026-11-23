using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using DeskSnip.App.Windows;
using DeskSnip.Core;

namespace DeskSnip.App;

internal sealed partial class EditorWindow
{
    internal AnnotationKind LastTool => canvas.LastTool;    internal Action<System.Windows.Media.Imaging.BitmapSource, string>? ServiceOperationRequested { get; set; }
    private void ConfigureQuickSettings(FloatingToolbar toolbar)
    {
        if (!settings.MarkQuickSettings) return;
        var panel = new StackPanel { Orientation = Orientation.Horizontal }; var quickColors = new ComboBox { Width = 112, ItemsSource = colorNames, SelectedIndex = 0, Margin = new Thickness(4) }; var quickWidth = new ComboBox { Width = 62, ItemsSource = new[] { 2d, 3d, 5d, 8d, 12d, 20d }, SelectedItem = 3d, Margin = new Thickness(4) }; panel.Children.Add(quickColors); panel.Children.Add(quickWidth);
        var popup = new Popup { PlacementTarget = toolbar, Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true, VerticalOffset = -settings.MarkQuickSettingsPadding, Child = new Border { Background = Brushes.White, BorderBrush = FloatingTools.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(4), Child = panel } };
        quickColors.SelectionChanged += (_, _) => { if (quickColors.SelectedIndex >= 0) canvas.ChangeStyle(argb: colorValues[quickColors.SelectedIndex]); };
        quickWidth.SelectionChanged += (_, _) => { if (quickWidth.SelectedItem is double value) canvas.ChangeStyle(thickness: value); };
        canvas.ToolChanged += () => { if (IsVisible && canvas.Kind.HasValue) popup.IsOpen = true; }; Closed += (_, _) => popup.IsOpen = false;
    }
    private void AddConfiguredToolGroup(FloatingToolbar toolbar, string group)
    {
        switch (group)
        {
            case "Rectangle": AddToolGroup(toolbar, "Rectangle", ToolGlyph.Rectangle, L.Get("形状工具")); break;
            case "Arrow": AddToolGroup(toolbar, "Arrow", ToolGlyph.Arrow, L.Get("箭头与线条")); break;
            case "Pen": AddToolGroup(toolbar, "Pen", ToolGlyph.Brush, L.Get("画笔与高亮")); break;
            case "Text": AddToolGroup(toolbar, "Text", ToolGlyph.Text, L.Get("文字与水印")); break;
            case "Number": var number = FloatingTools.Button(ToolGlyph.Number, L.Get("序号"), () => canvas.SetTool(AnnotationKind.Number)); number.Tag = AnnotationKind.Number; toolbar.Items.Children.Add(number); toolButtons.Add((number, new AnnotationKind?[] { AnnotationKind.Number })); break;
            case "Mosaic": AddToolGroup(toolbar, "Mosaic", ToolGlyph.Mosaic, L.Get("马赛克与模糊")); break;
            case "Cover": AddToolGroup(toolbar, "Cover", ToolGlyph.Cover, L.Get("遮挡工具")); break;
            case "Eraser": AddToolGroup(toolbar, "Eraser", ToolGlyph.Eraser, L.Get("橡皮擦")); break;
            case "Spotlight": AddToolGroup(toolbar, "Spotlight", ToolGlyph.Spotlight, L.Get("聚光与放大")); break;
        }
    }
    private bool HandleConfiguredShortcut(KeyEventArgs e)
    {
        var action = ShortcutKeys.Match(settings, "Common", e); if (action == null) return false;
        if (action.StartsWith("Tool", StringComparison.Ordinal) && Enum.TryParse(action.Substring(4), out AnnotationKind tool)) { canvas.SetTool(tool); e.Handled = true; return true; }
        switch (action)
        {
            case "Copy": Copy(); break; case "Save": Save(); break; case "QuickSave": quickSave?.Invoke(Output()); break; case "Pin": Pin(); break;
            case "Undo": canvas.Document.Undo(); canvas.Refresh(); break; case "Redo": canvas.Document.Redo(); canvas.Refresh(); break; case "Clear": canvas.Document.Clear(); canvas.Refresh(); break;
            case "SecondaryEditing": canvas.ToggleSecondaryEditing(); break; case "AutoMosaic": _ = DetectPrivacy(); break;
            case "Ocr": ocrRequested?.Invoke(Output()); break;
            case "Service": case "Document": case "Table": case "Formula": var image = Output(); if (ServiceOperationRequested != null) ServiceOperationRequested(image, action == "Service" ? "translation" : action.ToLowerInvariant()); else serviceRequested?.Invoke(image); break;
            case "Cancel": canvas.ClearSelection(); break; case "Close": Close(); break;
            default: return false;
        }
        e.Handled = true; return true;
    }
}

internal sealed partial class AnnotationCanvas
{
    private readonly AppSettings preferences;
    private bool secondaryEditing;
    private PixelPoint pointer;
    private static int globalSerial;
    internal AnnotationKind LastTool { get; private set; } = AnnotationKind.Arrow;
    internal event Action? ToolChanged;
    internal event Action? TextEditorPointerDown;
    internal void ToggleSecondaryEditing() { secondaryEditing = !secondaryEditing; ClearSelection(); }
    internal bool HandleEditKey(KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox) return false;
        if ((pathBuilding || stroke.Count > 0 || draggingObject) && e.Key == Key.Escape) { CancelInteraction(); return true; }
        if (e.Key == Key.Delete) { DeleteSelected(); return true; }
        if (e.Key == Key.Escape) { if (Selected != null) { ClearSelection(); return true; } return false; }
        if (e.Key == Key.Enter && (Selected?.Kind == AnnotationKind.Text || Selected?.Kind == AnnotationKind.Watermark)) { EditSelectedText(); return true; }
        var control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (control && e.Key == Key.Z) { if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) Document.Clear(); else Document.Undo(); Refresh(); return true; }
        if (control && e.Key == Key.Y) { Document.Redo(); Refresh(); return true; }
        if (control && e.Key == Key.D) { DuplicateSelected(); return true; }
        var dx = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0;
        var dy = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
        if ((dx != 0 || dy != 0) && Selected != null) { NudgeSelected(dx, dy); return true; }
        return false;
    }
    internal void AddAutomaticWatermark()
    {
        Document.Add(new Annotation(AnnotationKind.Watermark, new[] { new PixelPoint(8, 8) }, 0x55909090, 3, preferences.MarkWatermarkText.Replace("{time}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")), appearance: new AnnotationAppearance(opacity: .6))); Refresh();
    }
    private void DrawPencilOutline(DrawingContext dc)
    {
        if (!IsMouseOver || !Kind.HasValue || preferences.MarkPencilCursor != "CrossAndOutline" && preferences.MarkPencilCursor != "Outline") return;
        var width = Kind == AnnotationKind.Pen ? Thickness : Kind == AnnotationKind.Highlight || Kind == AnnotationKind.EraserPen || Kind == AnnotationKind.MosaicPen || Kind == AnnotationKind.CoverPen ? Math.Max(8, Thickness * 5) : 0;
        if (width < preferences.MarkPencilOutlineMinWidth || width == 0) return;
        dc.DrawEllipse(null, new Pen(Brushes.White, 2), new Point(pointer.X, pointer.Y), width / 2, width / 2); dc.DrawEllipse(null, new Pen(Brushes.Black, 1), new Point(pointer.X, pointer.Y), width / 2, width / 2);
    }
    private void DrawControlPoints(DrawingContext dc, Annotation selected, Rect bounds)
    {
        if (selected.Kind == AnnotationKind.Arrow)
        {
            if (preferences.MarkHideArrowControls) return;
            foreach (var point in selected.Points) dc.DrawEllipse(Brushes.White, new Pen(Ui.Accent, 1), new Point(point.X, point.Y), 3, 3);
            return;
        }
        if (!IsResizable(selected.Kind)) return;
        if (selected.Kind == AnnotationKind.RoundedRectangle && (preferences.MarkRoundedControls == "Never" || preferences.MarkRoundedControls == "Hover" && !IsMouseOver)) return;
        var controls = new[]
        {
            bounds.TopLeft, bounds.TopRight, bounds.BottomLeft, bounds.BottomRight,
            new Point(bounds.Left, bounds.Top + bounds.Height / 2), new Point(bounds.Left + bounds.Width / 2, bounds.Top),
            new Point(bounds.Right, bounds.Top + bounds.Height / 2), new Point(bounds.Left + bounds.Width / 2, bounds.Bottom)
        };
        foreach (var point in controls) dc.DrawEllipse(Brushes.White, new Pen(Ui.Accent, 1), point, 3, 3);
    }
}
