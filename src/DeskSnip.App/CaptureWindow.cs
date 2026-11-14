using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.App.Imaging;
using DeskSnip.App.Windows;
using DeskSnip.Core;
using Rectangle = System.Windows.Shapes.Rectangle;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace DeskSnip.App;

internal enum CaptureAction
{
    Edit, Copy, Pin, Save, Ocr, LongScreenshot, QuickSave, ServiceRecognition,
    EditRectangle, EditEllipse, EditLine, EditArrow, EditBrush, EditHighlight, EditText, EditNumber, EditMosaic, EditCover, EditEraser, EditPolyline, EditWatermark, EditSpotlight, EditMagnifier, EditAutoMosaic,
    TranslationRecognition, DocumentRecognition, TableRecognition, FormulaRecognition
}

internal sealed partial class CaptureWindow : ProductWindow
{
    private readonly PixelRect bounds;
    private readonly bool recording;
    private readonly PixelInspector inspector;
    private readonly AppSettings settings;
    private readonly Image desktopImage;
    private readonly BitmapSource desktop;
    private readonly BitmapSource? desktopWithCursor;
    private readonly PixelColorFormat[] colorFormats;
    private bool cursorVisible;
    private bool showShortcuts;
    private readonly TextBlock shortcutHint = Ui.Text(L.Get("拖动框选 · Enter 复制 · Ctrl+T 钉图 · Ctrl+S 保存 · Space 工具栏 · Esc 取消"), 11, Brushes.White);
    private readonly Canvas surface = new Canvas();
    private readonly System.Windows.Shapes.Path mask = new System.Windows.Shapes.Path { Fill = new SolidColorBrush(Color.FromArgb(125, 0, 0, 0)), IsHitTestVisible = false };
    private readonly System.Windows.Shapes.Path guides = new System.Windows.Shapes.Path { Stroke = new SolidColorBrush(Color.FromArgb(175, 255, 255, 255)), StrokeThickness = 1, IsHitTestVisible = false };
    private readonly Rectangle outline = new Rectangle { Stroke = FloatingTools.Blue, StrokeThickness = 1.5, IsHitTestVisible = false };
    private readonly Ellipse[] handles = new Ellipse[8];
    private readonly SelectionInfoBar info;
    private readonly PixelMagnifier magnifier;
    private readonly FloatingToolbar actions = new FloatingToolbar { Name = "CaptureActions" };
    private readonly FloatingToolbar modes = new FloatingToolbar(true) { Name = "CaptureModes" };
    private readonly Button? shapeButton;
    private readonly Button targetButton;
    private CaptureShape shape;
    private readonly System.Windows.Shapes.Path contourOutline = new System.Windows.Shapes.Path { Stroke = FloatingTools.Blue, StrokeThickness = 1.5, IsHitTestVisible = false };
    private readonly IReadOnlyList<CaptureTarget> targets;
    private readonly IReadOnlyList<PixelRect> recent;
    private readonly List<PixelPoint> contour = new List<PixelPoint>();
    private PixelRect hover;
    private PixelRect pendingTarget;
    private int targetIndex;
    private bool elements;
    private bool polygonBuilding;
    private PixelPoint dragStart;
    private PixelPoint cursor;
    private PixelRect previous;
    private SelectionHandle dragHandle;
    private bool dragging;
    private bool temporaryMoving;
    private bool ratioLocked;
    private double? lockedRatio;
    private bool gridVisible;
    private bool magnifierPinned;
    private bool toolsHidden;
    private bool relativeCoordinates;
    private PixelColorFormat colorFormat;
    internal PixelRect Selection { get; private set; }
    internal CaptureAction ActionResult { get; private set; } = CaptureAction.Edit;
    internal CaptureShape ShapeResult => recording ? CaptureShape.Rectangle : shape;
    internal BitmapSource SelectedDesktop => cursorVisible && desktopWithCursor != null ? desktopWithCursor : desktop;
    internal IReadOnlyList<PixelPoint> ShapePoints => contour.Select(p => new PixelPoint(p.X - Selection.X, p.Y - Selection.Y)).ToArray();

    internal CaptureWindow(BitmapSource desktop, PixelRect bounds, bool recording, IReadOnlyList<CaptureTarget>? targets = null, IReadOnlyList<PixelRect>? recent = null, PixelRect initial = default, CaptureShape initialShape = CaptureShape.Rectangle, AppSettings? settings = null, BitmapSource? desktopWithCursor = null)
    {
        this.bounds = bounds; this.recording = recording;
        this.settings = settings?.Clone() ?? new AppSettings(); this.desktop = desktop; this.desktopWithCursor = desktopWithCursor;
        cursorVisible = this.settings.ScreenshotCursorVisible; showShortcuts = this.settings.ScreenshotShowShortcuts;
        elements = this.settings.ScreenshotUiDetection == "Elements";
        colorFormats = CapturePreferenceRules.ColorFormats(this.settings.ColorFormatOrder); colorFormat = colorFormats[0];
        var borderColor = (Brush)new BrushConverter().ConvertFromString(this.settings.SelectionBorderFollowsTheme ? this.settings.AccentColor : this.settings.SelectionBorderColor)!;
        mask.Fill = (Brush)new BrushConverter().ConvertFromString(this.settings.CaptureMaskColor)!;
        outline.Stroke = contourOutline.Stroke = borderColor;
        this.targets = targets ?? Array.Empty<CaptureTarget>(); this.recent = recent ?? Array.Empty<PixelRect>();
        inspector = new PixelInspector(desktop, bounds); cursor = new PixelPoint(bounds.X, bounds.Y);
        Title = recording ? L.Get("DeskSnip · 选择录制区域") : L.Get("DeskSnip · 截图选区");
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
        Left = bounds.X; Top = bounds.Y; Width = bounds.Width; Height = bounds.Height;
        Background = Brushes.Black; Cursor = Cursors.Cross;
        desktopImage = new Image { Source = cursorVisible && desktopWithCursor != null ? desktopWithCursor : desktop, Stretch = Stretch.Fill, IsHitTestVisible = false };
        var grid = new Grid(); grid.Children.Add(desktopImage); grid.Children.Add(surface); Content = grid;
        surface.Background = Brushes.Transparent; surface.Children.Add(mask); surface.Children.Add(guides); surface.Children.Add(outline); surface.Children.Add(contourOutline);
        for (var i = 0; i < handles.Length; i++)
        {
            handles[i] = new Ellipse { Width = 7, Height = 7, Stroke = Brushes.White, Fill = borderColor, StrokeThickness = 1, IsHitTestVisible = false };
            surface.Children.Add(handles[i]);
        }
        info = new SelectionInfoBar(bounds, SetSelection, value =>
        {
            ratioLocked = value; lockedRatio = value && !Selection.IsEmpty ? Selection.Width / (double)Selection.Height : (double?)null;
        }, value => { gridVisible = value; Redraw(); });
        info.LayoutChanged += Redraw;
        surface.Children.Add(info);
        shape = recording ? CaptureShape.Rectangle : initialShape; Selection = initial.Intersect(bounds);
        if (!recording)
        {
            var menu = FloatingTools.Menu(); var names = new[] { L.Get("矩形选区"), L.Get("椭圆选区"), L.Get("自由形状选区"), L.Get("多边形选区") };
            for (var i = 0; i < names.Length; i++) { var kind = (CaptureShape)i; menu.Items.Add(FloatingTools.Item(names[i], () => { shape = kind; contour.Clear(); polygonBuilding = false; SetSelection(default); })); }
            menu.Opened += (_, _) => { for (var i = 0; i < menu.Items.Count; i++) ((MenuItem)menu.Items[i]).IsChecked = (int)shape == i; };
            shapeButton = FloatingTools.MenuButton(ToolGlyph.Rectangle, L.Get("选区形状"), menu); shapeButton.Width = 32; FloatingTools.SetSelected(shapeButton, true); modes.Items.Children.Add(shapeButton);
        }
        targetButton = FloatingTools.Button(ToolGlyph.Window, L.Get("切换窗口 / 控件"), () => { elements = !elements; targetIndex = 0; UpdateHover(); Redraw(); }, "Tab"); modes.Items.Children.Add(targetButton);
        targetButton.IsEnabled = this.settings.ScreenshotUiDetection != "None";
        var history = FloatingTools.Menu();
        foreach (var area in this.recent) { var region = area; history.Items.Add(FloatingTools.Item(region.ToString(), () => SetSelection(region))); }
        var historyButton = FloatingTools.MenuButton(ToolGlyph.History, L.Get("历史选区"), history); historyButton.Width = 32; historyButton.IsEnabled = this.recent.Count > 0; modes.Items.Children.Add(historyButton);
        modes.Items.Children.Add(FloatingTools.Button(ToolGlyph.Restart, L.Get("重新框选"), () => SetSelection(default))); surface.Children.Add(modes);
        if (recording)
        {
            var caption = Ui.Text(L.Get("录制区域"), 11, FloatingTools.MutedInk); caption.Margin = new Thickness(8, 0, 8, 0); caption.VerticalAlignment = VerticalAlignment.Center; actions.Items.Children.Add(caption);
            actions.Items.Children.Add(FloatingTools.Button(ToolGlyph.Styles, L.Get("精确位置与尺寸"), () => info.SetExpanded(!info.IsExpanded)));
            actions.Items.Children.Add(FloatingTools.Divider());
        }
        else
        {
            var grip = new ToolIcon(ToolGlyph.Grip) { Width = 12, Height = 20, Color = FloatingTools.MutedInk, Margin = new Thickness(2, 7, 2, 7) }; actions.Items.Children.Add(grip);
            AddEditGroup(ToolGlyph.Rectangle, L.Get("形状标注"), (L.Get("矩形"), CaptureAction.EditRectangle), (L.Get("椭圆"), CaptureAction.EditEllipse), (L.Get("直线"), CaptureAction.EditLine));
            AddEditGroup(ToolGlyph.Brush, L.Get("画笔与高亮"), (L.Get("画笔"), CaptureAction.EditBrush), (L.Get("高亮"), CaptureAction.EditHighlight));
            actions.Items.Children.Add(FloatingTools.Button(ToolGlyph.Arrow, L.Get("箭头标注"), () => Complete(CaptureAction.EditArrow)));
            AddEditGroup(ToolGlyph.Text, L.Get("文字与序号"), (L.Get("文字"), CaptureAction.EditText), (L.Get("序号"), CaptureAction.EditNumber));
            AddEditGroup(ToolGlyph.Mosaic, L.Get("马赛克与遮挡"), (L.Get("马赛克"), CaptureAction.EditMosaic), (L.Get("遮挡"), CaptureAction.EditCover), (L.Get("橡皮擦"), CaptureAction.EditEraser));
            actions.Items.Children.Add(FloatingTools.Divider());
            actions.Items.Children.Add(FloatingTools.Button(ToolGlyph.Ocr, L.Get("文字识别"), () => Complete(CaptureAction.Ocr), "Shift+C"));
            if (this.settings.ScreenshotSecondaryButtons)
            {
                actions.Items.Children.Add(FloatingTools.Button(ToolGlyph.Cloud, L.Get("联网识别"), () => Complete(CaptureAction.ServiceRecognition)));
                actions.Items.Children.Add(FloatingTools.Button(ToolGlyph.Scroll, L.Get("长截图"), () => Complete(CaptureAction.LongScreenshot), "L"));
            }
            actions.Items.Children.Add(FloatingTools.Button(ToolGlyph.Pin, L.Get("钉图"), () => Complete(CaptureAction.Pin), "Ctrl+T"));
            var save = FloatingTools.Menu(); save.Items.Add(FloatingTools.Item(L.Get("保存图片"), () => Complete(CaptureAction.Save), "Ctrl+S")); save.Items.Add(FloatingTools.Item(L.Get("快速保存"), () => Complete(CaptureAction.QuickSave), "Ctrl+Shift+S")); actions.Items.Children.Add(FloatingTools.MenuButton(ToolGlyph.Save, L.Get("保存"), save));
            var more = FloatingTools.Menu(); more.Items.Add(FloatingTools.Item(L.Get("编辑截图（全部工具）"), () => Complete(CaptureAction.Edit))); more.Items.Add(FloatingTools.Item(L.Get("精确位置与尺寸"), () => info.SetExpanded(!info.IsExpanded))); more.Items.Add(FloatingTools.Item(L.Get("重新框选"), () => SetSelection(default))); actions.Items.Children.Add(FloatingTools.MenuButton(ToolGlyph.More, L.Get("更多截图操作"), more));
            actions.Items.Children.Add(FloatingTools.Divider());
        }
        actions.Items.Children.Add(FloatingTools.Button(ToolGlyph.Close, L.Get("取消"), () => DialogResult = false, "Esc"));
        actions.Items.Children.Add(FloatingTools.Button(recording ? ToolGlyph.Check : ToolGlyph.Copy, recording ? L.Get("使用此区域") : L.Get("复制截图"), () => Complete(recording ? CaptureAction.Edit : CaptureAction.Copy), recording ? "Enter" : "Enter / Ctrl+C", true)); surface.Children.Add(actions);
        magnifier = new PixelMagnifier(inspector, this.settings); surface.Children.Add(magnifier);
        shortcutHint.Background = new SolidColorBrush(Color.FromArgb(210, 25, 30, 37)); shortcutHint.Padding = new Thickness(10, 5, 10, 5); shortcutHint.Name = "CaptureShortcutHints"; shortcutHint.IsHitTestVisible = false; surface.Children.Add(shortcutHint);
        FloatingTools.Configure(actions.Items, this.settings.ToolbarIconSize, showShortcuts); FloatingTools.Configure(modes.Items, this.settings.ToolbarIconSize, showShortcuts);
        SourceInitialized += (_, _) => Position();
        Loaded += (_, _) => { Position(); cursor = inspector.Clamp(CursorPosition()); Focus(); Redraw(); };
        SizeChanged += (_, _) => Redraw();
        surface.MouseLeftButtonDown += Down; surface.MouseMove += Move; surface.MouseLeftButtonUp += Up;
        surface.LostMouseCapture += (_, _) => { dragging = false; Redraw(); };
        surface.MouseRightButtonDown += (_, e) => { if (!IsControl(e.OriginalSource as DependencyObject)) DialogResult = false; };
        surface.MouseWheel += Wheel;
        surface.MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle && !IsControl(e.OriginalSource as DependencyObject)) { InvokeMouseAction(this.settings.ScreenshotMiddleClickAction); e.Handled = true; } };
        PreviewKeyDown += KeyDownHandler; PreviewKeyUp += KeyUpHandler;
    }

    private void AddEditGroup(ToolGlyph glyph, string name, params (string Name, CaptureAction Action)[] tools)
    {
        var menu = FloatingTools.Menu(); foreach (var tool in tools) { var action = tool.Action; menu.Items.Add(FloatingTools.Item(tool.Name, () => Complete(action))); }
        actions.Items.Children.Add(FloatingTools.MenuButton(glyph, name, menu));
    }
    internal static AnnotationKind? AnnotationTool(CaptureAction action) => action switch
    {
        CaptureAction.EditRectangle => AnnotationKind.Rectangle, CaptureAction.EditEllipse => AnnotationKind.Ellipse,
        CaptureAction.EditLine => AnnotationKind.Line, CaptureAction.EditArrow => AnnotationKind.Arrow,
        CaptureAction.EditBrush => AnnotationKind.Pen, CaptureAction.EditHighlight => AnnotationKind.Highlight,
        CaptureAction.EditText => AnnotationKind.Text, CaptureAction.EditNumber => AnnotationKind.Number,
        CaptureAction.EditMosaic => AnnotationKind.Mosaic, CaptureAction.EditCover => AnnotationKind.Cover,
        CaptureAction.EditEraser => AnnotationKind.Eraser, CaptureAction.EditPolyline => AnnotationKind.Polyline, CaptureAction.EditWatermark => AnnotationKind.Watermark,
        CaptureAction.EditSpotlight => AnnotationKind.Spotlight, CaptureAction.EditMagnifier => AnnotationKind.Magnifier, CaptureAction.EditAutoMosaic => AnnotationKind.Mosaic, _ => null
    };

    private void Position() => Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.Topmost, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0040);
    private PixelPoint CursorPosition()
    {
        Native.GetCursorPos(out var point);
        return new PixelPoint(Math.Max(bounds.X, Math.Min(bounds.Right, point.X)), Math.Max(bounds.Y, Math.Min(bounds.Bottom, point.Y)));
    }
    private bool IsControl(DependencyObject? source)
    {
        for (var node = source; node != null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (ReferenceEquals(node, info) || ReferenceEquals(node, actions) || ReferenceEquals(node, modes)) return true;
        return false;
    }
    internal void SetSelection(PixelRect selection)
    {
        var old = Selection;
        Selection = selection.IsEmpty ? default : selection.Intersect(bounds);
        if (!old.IsEmpty && !Selection.IsEmpty && !Selection.Equals(old) && !dragging && !polygonBuilding && (ShapeResult == CaptureShape.Freehand || ShapeResult == CaptureShape.Polygon))
            for (var i = 0; i < contour.Count; i++) contour[i] = new PixelPoint(Selection.X + (contour[i].X - old.X) * Selection.Width / old.Width, Selection.Y + (contour[i].Y - old.Y) * Selection.Height / old.Height);
        if (ratioLocked && !Selection.IsEmpty && !lockedRatio.HasValue) lockedRatio = Selection.Width / (double)Selection.Height;
        Redraw();
    }
    internal void BeginInteraction(PixelPoint point)
    {
        if (info.IsExpanded) info.SetExpanded(false);
        dragStart = point; previous = Selection; temporaryMoving = false;
        var sx = surface.ActualWidth > 0 ? surface.ActualWidth / bounds.Width : 1;
        var sy = surface.ActualHeight > 0 ? surface.ActualHeight / bounds.Height : 1;
        dragHandle = SelectionGeometry.HitTest(Selection, point, 6 / sx, 6 / sy);
        dragging = true;
        if (dragHandle == SelectionHandle.None) Selection = default;
    }
    internal void UpdateInteraction(PixelPoint point, bool shift, bool alt, bool space)
    {
        if (!dragging) return;
        if (dragHandle == SelectionHandle.None) Selection = SelectionGeometry.Create(dragStart, point, bounds, shift);
        else
        {
            if (dragHandle != SelectionHandle.Move && space != temporaryMoving)
            {
                previous = Selection; dragStart = point; temporaryMoving = space;
            }
            var delta = new PixelPoint(point.X - dragStart.X, point.Y - dragStart.Y);
            Selection = SelectionGeometry.Transform(previous, delta, dragHandle, bounds,
                axisLocked: shift, symmetric: alt, aspectRatio: shift && dragHandle != SelectionHandle.Move ? 1 : lockedRatio, temporaryMove: space);
        }
        cursor = inspector.Clamp(point); Redraw();
    }
    internal void EndInteraction() { dragging = false; Redraw(); }
    private void UpdateDrag(PixelPoint point)
        => UpdateInteraction(point, (Keyboard.Modifiers & ModifierKeys.Shift) != 0, (Keyboard.Modifiers & ModifierKeys.Alt) != 0, Keyboard.IsKeyDown(Key.Space));
    private void Down(object sender, MouseButtonEventArgs e)
    {
        if (IsControl(e.OriginalSource as DependencyObject)) return;
        var point = CursorPosition();
        if (ShapeResult == CaptureShape.Polygon)
        {
            if (!polygonBuilding) { contour.Clear(); SetSelection(default); polygonBuilding = true; }
            contour.Add(point); if (e.ClickCount == 2) FinishPolygon(); Redraw(); e.Handled = true; return;
        }
        if (ShapeResult == CaptureShape.Freehand)
        {
            contour.Clear(); contour.Add(point); dragging = true; SetSelection(default); surface.CaptureMouse(); e.Handled = true; return;
        }
        if (e.ClickCount == 2 && !Selection.IsEmpty && point.X >= Selection.X && point.X < Selection.Right && point.Y >= Selection.Y && point.Y < Selection.Bottom)
        { InvokeMouseAction(settings.ScreenshotDoubleClickAction); e.Handled = true; return; }
        Focus(); Keyboard.Focus(this); pendingTarget = Selection.IsEmpty && ShapeResult == CaptureShape.Rectangle ? hover : default; BeginInteraction(point); surface.CaptureMouse(); Redraw(); e.Handled = true;
    }
    private void Move(object sender, MouseEventArgs e)
    {
        var point = CursorPosition(); cursor = inspector.Clamp(point);
        if (dragging && ShapeResult == CaptureShape.Freehand) { contour.Add(point); UpdateContourBounds(); }
        else if (dragging) UpdateDrag(point);
        else if (Selection.IsEmpty) UpdateHover();
        if (!IsControl(e.OriginalSource as DependencyObject))
        {
            var sx = surface.ActualWidth / bounds.Width; var sy = surface.ActualHeight / bounds.Height;
            if (sx > 0 && sy > 0) Cursor = HandleCursor(dragging ? dragHandle : SelectionGeometry.HitTest(Selection, point, 6 / sx, 6 / sy));
        }
        else Cursor = Cursors.Arrow;
        Redraw();
    }
    private static Cursor HandleCursor(SelectionHandle handle)
    {
        switch (handle)
        {
            case SelectionHandle.Move: return Cursors.SizeAll;
            case SelectionHandle.Left: case SelectionHandle.Right: return Cursors.SizeWE;
            case SelectionHandle.Top: case SelectionHandle.Bottom: return Cursors.SizeNS;
            case SelectionHandle.Left | SelectionHandle.Top: case SelectionHandle.Right | SelectionHandle.Bottom: return Cursors.SizeNWSE;
            case SelectionHandle.Right | SelectionHandle.Top: case SelectionHandle.Left | SelectionHandle.Bottom: return Cursors.SizeNESW;
            default: return Cursors.Cross;
        }
    }
    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (!dragging) return;
        if (ShapeResult == CaptureShape.Freehand) { contour.Add(CursorPosition()); UpdateContourBounds(); EndInteraction(); surface.ReleaseMouseCapture(); e.Handled = true; return; }
        UpdateDrag(CursorPosition()); EndInteraction(); surface.ReleaseMouseCapture(); e.Handled = true;
        if (Selection.Width < 4 && Selection.Height < 4)
        {
            if (!previous.IsEmpty && dragHandle == SelectionHandle.None && settings.ScreenshotExpandOutside) SetSelection(CapturePreferenceRules.ExpandToPoint(previous, cursor, bounds));
            else if (!pendingTarget.IsEmpty) SetSelection(pendingTarget);
        }
        pendingTarget = default;
    }
    private void Wheel(object sender, MouseWheelEventArgs e)
    {
        if (dragging || IsControl(e.OriginalSource as DependencyObject)) return;
        if (Selection.IsEmpty)
        {
            if (settings.ScreenshotWheelTargets && settings.ScreenshotUiDetection != "None") { targetIndex = Math.Max(0, targetIndex + (e.Delta > 0 ? -1 : 1)); UpdateHover(); Redraw(); e.Handled = true; }
            return;
        }
        if (settings.ScreenshotWheelResize) { SetSelection(SelectionGeometry.Scale(Selection, e.Delta > 0 ? 2 : -2, bounds, lockedRatio)); e.Handled = true; }
    }
    internal static CaptureAction? MouseCaptureAction(string action) => action switch
    {
        "Copy" => CaptureAction.Copy, "Ocr" => CaptureAction.Ocr, "QuickSave" => CaptureAction.QuickSave,
        "Save" => CaptureAction.Save, "Pin" => CaptureAction.Pin, _ => null
    };
    private void InvokeMouseAction(string action)
    {
        if (recording) { if (action != "None" && action != "Close") Complete(CaptureAction.Edit); else if (action == "Close") DialogResult = false; return; }
        if (action == "Close") DialogResult = false;
        else if (MouseCaptureAction(action) is CaptureAction captureAction) Complete(captureAction);
    }
    private void CancelFromKey()
    {
        if (!Selection.IsEmpty && settings.ScreenshotCloseConfirmation && MessageBox.Show(this, L.Get("放弃当前截图？"), L.Get("取消截图"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        DialogResult = false;
    }
    private void Complete(CaptureAction action)
    {
        if (polygonBuilding) FinishPolygon();
        if (Selection.IsEmpty || dragging) return;
        if ((ShapeResult == CaptureShape.Polygon || ShapeResult == CaptureShape.Freehand) && contour.Count < 3) { info.Notice(L.Get("轮廓至少需要三个点。")); return; }
        if (action == CaptureAction.LongScreenshot && ShapeResult != CaptureShape.Rectangle) { info.Notice(L.Get("长截图需要矩形选区。")); return; }
        ActionResult = action; DialogResult = true;
    }
    private void CopySample(bool coordinates)
    {
        try
        {
            Clipboard.SetText(coordinates ? PixelColor.Coordinates(cursor, Selection, relativeCoordinates) : inspector.Sample(cursor).Format(colorFormat));
            DialogResult = false;
        }
        catch (System.Runtime.InteropServices.ExternalException) { info.Notice(L.Get("剪贴板忙，请重试。")); }
    }
    private void KeyDownHandler(object sender, KeyEventArgs e) => HandleConfiguredKeys(e);
    private void KeyUpHandler(object sender, KeyEventArgs e)
    {
        if (ShortcutKeys.Match(settings, "Common", e) == "Guides" || ShortcutKeys.IsModifier(e.Key)) { configuredGuides = false; Redraw(); }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftShift || key == Key.RightShift || key == Key.Space)
        { if (dragging) UpdateDrag(CursorPosition()); Redraw(); }
    }
    private void Redraw()
    {
        info.Update(Selection, inspector.Sample(cursor).Format(colorFormat));
        if (shapeButton?.Content is StackPanel shapeIcons && shapeIcons.Children[0] is ToolIcon icon) icon.SetGlyph(shape == CaptureShape.Ellipse ? ToolGlyph.Ellipse : shape == CaptureShape.Polygon ? ToolGlyph.Polygon : shape == CaptureShape.Freehand ? ToolGlyph.Brush : ToolGlyph.Rectangle);
        FloatingTools.SetSelected(targetButton, elements);
        if (surface.ActualWidth < 1 || surface.ActualHeight < 1) return;
        var sx = surface.ActualWidth / bounds.Width; var sy = surface.ActualHeight / bounds.Height;
        outline.StrokeThickness = contourOutline.StrokeThickness = settings.SelectionBorderWidth * Math.Min(sx, sy);
        var shown = Selection.IsEmpty && !dragging ? hover : Selection;
        var rect = new Rect((shown.X - bounds.X) * sx, (shown.Y - bounds.Y) * sy, shown.Width * sx, shown.Height * sy);
        Geometry region = ShapeResult == CaptureShape.Ellipse ? new EllipseGeometry(rect) : new RectangleGeometry(rect);
        if ((ShapeResult == CaptureShape.Freehand || ShapeResult == CaptureShape.Polygon) && contour.Count > 0)
        {
            var path = new StreamGeometry();
            using (var context = path.Open()) { context.BeginFigure(new Point((contour[0].X - bounds.X) * sx, (contour[0].Y - bounds.Y) * sy), true, true); foreach (var point in contour.Skip(1)) context.LineTo(new Point((point.X - bounds.X) * sx, (point.Y - bounds.Y) * sy), true, false); if (polygonBuilding) context.LineTo(new Point((cursor.X - bounds.X) * sx, (cursor.Y - bounds.Y) * sy), true, false); }
            region = path;
        }
        mask.Data = shown.IsEmpty && contour.Count == 0 ? new RectangleGeometry(new Rect(0, 0, surface.ActualWidth, surface.ActualHeight)) :
            new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, surface.ActualWidth, surface.ActualHeight)), region);
        var irregular = ShapeResult != CaptureShape.Rectangle;
        contourOutline.Data = region; contourOutline.Visibility = irregular && (!shown.IsEmpty || contour.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
        outline.Visibility = shown.IsEmpty || irregular ? Visibility.Collapsed : Visibility.Visible;
        outline.Width = rect.Width; outline.Height = rect.Height; Canvas.SetLeft(outline, rect.X); Canvas.SetTop(outline, rect.Y);
        var positions = new[] { new Point(rect.Left, rect.Top), new Point(rect.Left + rect.Width / 2, rect.Top), new Point(rect.Right, rect.Top),
            new Point(rect.Left, rect.Top + rect.Height / 2), new Point(rect.Right, rect.Top + rect.Height / 2),
            new Point(rect.Left, rect.Bottom), new Point(rect.Left + rect.Width / 2, rect.Bottom), new Point(rect.Right, rect.Bottom) };
        for (var i = 0; i < handles.Length; i++)
        {
            handles[i].Visibility = Selection.IsEmpty || ShapeResult == CaptureShape.Freehand || ShapeResult == CaptureShape.Polygon || settings.SelectionAnchorDisplay == "None" || settings.SelectionAnchorDisplay == "Four" && (i == 1 || i == 3 || i == 4 || i == 6) ? Visibility.Collapsed : Visibility.Visible;
            Canvas.SetLeft(handles[i], positions[i].X - handles[i].Width / 2); Canvas.SetTop(handles[i], positions[i].Y - handles[i].Height / 2);
        }
        var alt = configuredGuides;
        DrawGuides(rect, sx, sy, alt);
        var viewport = new Rect(0, 0, surface.ActualWidth, surface.ActualHeight);
        actions.Visibility = Selection.IsEmpty || dragging || toolsHidden ? Visibility.Collapsed : Visibility.Visible;
        actions.MaxWidth = Math.Max(1, Math.Min(650, surface.ActualWidth - 16)); actions.Measure(new Size(actions.MaxWidth, double.PositiveInfinity));
        var actionRect = actions.Visibility == Visibility.Visible ? FloatingLayout.Actions(viewport, rect, actions.DesiredSize) : Rect.Empty;
        if (!actionRect.IsEmpty) Place(actions, actionRect);
        info.Visibility = toolsHidden || !settings.ScreenshotShowInfoBar && !info.IsExpanded ? Visibility.Collapsed : Visibility.Visible;
        info.MaxWidth = Math.Max(1, Math.Min(340, surface.ActualWidth - 16)); info.Measure(new Size(info.MaxWidth, double.PositiveInfinity));
        var infoRect = Selection.IsEmpty ? FloatingLayout.Clamp(viewport, new Point(24, 24), info.DesiredSize) : FloatingLayout.Info(viewport, rect, info.DesiredSize, actionRect);
        Place(info, infoRect);
        modes.Visibility = dragging || toolsHidden ? Visibility.Collapsed : Visibility.Visible;
        modes.Items.MaxHeight = Math.Max(34, surface.ActualHeight - 26); modes.Measure(new Size(double.PositiveInfinity, surface.ActualHeight - 16));
        var modeRect = Selection.IsEmpty ? FloatingLayout.Clamp(viewport, new Point(viewport.Right - modes.DesiredSize.Width - 12, 24), modes.DesiredSize) : FloatingLayout.Modes(viewport, rect, modes.DesiredSize, infoRect, actionRect);
        Place(modes, modeRect);
        var guideVisible = settings.ScreenshotGuideVisibility == "Always" || settings.ScreenshotGuideVisibility == "Alt" && alt;
        magnifier.Visibility = magnifierPinned || settings.ScreenshotShowMagnifier && Selection.IsEmpty || settings.GuideMagnifier && guideVisible ? Visibility.Visible : Visibility.Collapsed;
        magnifier.Update(cursor, Selection, colorFormat, relativeCoordinates);
        var mouseX = (cursor.X - bounds.X) * sx; var mouseY = (cursor.Y - bounds.Y) * sy;
        var mx = mouseX + 25 + magnifier.Width < surface.ActualWidth ? mouseX + 25 : mouseX - magnifier.Width - 25;
        var my = mouseY + 25 + magnifier.Height < surface.ActualHeight ? mouseY + 25 : mouseY - magnifier.Height - 25;
        Place(magnifier, mx, my, new Size(magnifier.Width, magnifier.Height));
        shortcutHint.Visibility = showShortcuts && !toolsHidden && !dragging ? Visibility.Visible : Visibility.Collapsed;
        shortcutHint.MaxWidth = Math.Max(1, surface.ActualWidth - 24); shortcutHint.Measure(new Size(shortcutHint.MaxWidth, double.PositiveInfinity));
        Place(shortcutHint, (surface.ActualWidth - shortcutHint.DesiredSize.Width) / 2, surface.ActualHeight - shortcutHint.DesiredSize.Height - 10, shortcutHint.DesiredSize);
    }
    private void UpdateHover()
    {
        if (settings.ScreenshotUiDetection == "None") { hover = default; return; }
        var candidates = CaptureTargets.At(targets, cursor, elements); if (candidates.Count == 0) { hover = default; targetIndex = 0; return; }
        targetIndex = Math.Min(targetIndex, candidates.Count - 1); hover = CapturePreferenceRules.DetectionBounds(candidates[targetIndex].Bounds, settings.ScreenshotDetectionPadding, bounds);
    }
    private void UpdateContourBounds()
    {
        if (contour.Count == 0) return;
        var left = (int)Math.Floor(contour.Min(p => p.X)); var top = (int)Math.Floor(contour.Min(p => p.Y));
        Selection = new PixelRect(left, top, Math.Max(1, (int)Math.Ceiling(contour.Max(p => p.X)) - left), Math.Max(1, (int)Math.Ceiling(contour.Max(p => p.Y)) - top)).Intersect(bounds); Redraw();
    }
    private void FinishPolygon() { if (contour.Count < 3) return; polygonBuilding = false; UpdateContourBounds(); }
    private void Place(UIElement element, double x, double y, Size size)
    {
        Place(element, FloatingLayout.Clamp(new Rect(0, 0, surface.ActualWidth, surface.ActualHeight), new Point(x, y), size));
    }
    private static void Place(UIElement element, Rect rect) { Canvas.SetLeft(element, rect.X); Canvas.SetTop(element, rect.Y); }
    private void DrawGuides(Rect rect, double sx, double sy, bool alt)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            void Line(double x1, double y1, double x2, double y2) { context.BeginFigure(new Point(x1, y1), false, false); context.LineTo(new Point(x2, y2), true, false); }
            var show = settings.ScreenshotGuideVisibility == "Always" || settings.ScreenshotGuideVisibility == "Alt" && alt;
            if (show)
            {
                var x = (cursor.X - bounds.X) * sx; var y = (cursor.Y - bounds.Y) * sy;
                if (settings.GuideMouseLines) { Line(0, y, surface.ActualWidth, y); Line(x, 0, x, surface.ActualHeight); }
                if (!Selection.IsEmpty && settings.GuideSelectionCenter)
                { Line(rect.Left + rect.Width / 2, rect.Top, rect.Left + rect.Width / 2, rect.Bottom); Line(rect.Left, rect.Top + rect.Height / 2, rect.Right, rect.Top + rect.Height / 2); }
                if (!Selection.IsEmpty && settings.GuideSelectionBorder)
                { Line(0, rect.Top, surface.ActualWidth, rect.Top); Line(0, rect.Bottom, surface.ActualWidth, rect.Bottom); Line(rect.Left, 0, rect.Left, surface.ActualHeight); Line(rect.Right, 0, rect.Right, surface.ActualHeight); }
            }
            if ((gridVisible || show && settings.GuideRuleOfThirds) && !Selection.IsEmpty)
                for (var i = 1; i < 3; i++) { Line(rect.Left + rect.Width * i / 3, rect.Top, rect.Left + rect.Width * i / 3, rect.Bottom); Line(rect.Left, rect.Top + rect.Height * i / 3, rect.Right, rect.Top + rect.Height * i / 3); }
        }
        geometry.Freeze(); guides.Data = geometry;
    }
}
