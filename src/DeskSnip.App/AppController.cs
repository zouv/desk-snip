using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DeskSnip.App.Imaging;
using DeskSnip.App.Recording;
using DeskSnip.App.Windows;
using DeskSnip.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace DeskSnip.App;

internal sealed partial class AppController : IDisposable
{
    private readonly List<PinWindow> pins = new List<PinWindow>();
    private readonly List<EditorWindow> editors = new List<EditorWindow>();
    private readonly Forms.NotifyIcon tray;
    private Icon icon;
    private readonly Hotkeys hotkeys;
    private bool hotkeysDisabled;
    private bool hotkeysSuspended;
    private DateTime lastTrayDoubleClick;
    private readonly System.Windows.Threading.DispatcherTimer trayClickTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime) };
    private bool selecting;
    private bool startingRecording;
    private CaptureWindow? selectionWindow;
    private RecordingSession? session;
    private RecordingBar? recordingBar;
    private RecordingPlan? pendingRecordingPlan;
    private IReadOnlyList<Annotation>? pendingRecordingAnnotations;
    private RecordingAnnotationOverlay? recordingOverlay;
    private bool recordingQuick;
    private bool recordingStartedFromSelection;
    private bool longScreenshotStartedFromSelection;
    private Task? finishing;
    private Task? exporting;
    private CancellationTokenSource? exportCancellation;
    private Exception? exitRecordingError;
    internal LocalStore Store { get; }
    internal MainWindow Window { get; }
    internal bool Exiting { get; private set; }
    internal PinWorkspace Workspace { get; }
    internal CaptureProfiles Profiles { get; }
    internal bool CanRestorePin => Workspace.Records.Any(p => !p.Active);
    internal string[] PinDescriptions => pins.Select((p, i) => L.Format("{0}. {1}", i + 1, p.Description)).ToArray();
    internal AppController(string dataRoot)
    {
        Store = new LocalStore(dataRoot);
        AnnotationToolDefaults.Load(Store.Settings.MarkToolDefaults); AnnotationToolDefaults.Changed += SaveToolDefaults;
        AnnotationStylePersistence.Changed += SaveToolStyles;
        L.Apply(Store.Settings.InterfaceLanguage);
        Ui.ApplyTheme(Store.Settings);
        Workspace = new PinWorkspace(dataRoot);
        var preferences = Store.Settings.Clone();
        if (preferences.AdoptLegacyPinPreferences(Workspace.RestoreOnStart, Workspace.Snapshot.ClosedLimit)) Store.SaveSettings(preferences);
        Workspace.Configure(Store.Settings.PinRestoreOnStart, Store.Settings.PinHistoryLimit);
        Profiles = new CaptureProfiles(dataRoot);
        Window = new MainWindow(this); System.Windows.Application.Current.MainWindow = Window;
        hotkeys = new Hotkeys(new WindowInteropHelper(Window).EnsureHandle());
        icon = TrayIcon(); tray = new Forms.NotifyIcon { Icon = icon, Text = L.Get("DeskSnip · 截图 / 钉图 / 录屏"), Visible = true };
        var menu = new Forms.ContextMenuStrip();
        Add(menu, L.Get("打开主界面"), Show); Add(menu, L.Get("截取屏幕"), Capture); Add(menu, L.Get("钉住剪贴板"), PinClipboard); Add(menu, L.Get("录制 / 暂停"), Record);
        Add(menu, L.Get("显示录制工具栏"), () => recordingBar?.Show()); Add(menu, L.Get("停止录制"), () => { _ = FinishRecording(); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        Add(menu, L.Get("显示 / 隐藏全部钉图"), TogglePins); Add(menu, L.Get("解除全部穿透 / 锁定"), RecoverPins); Add(menu, L.Get("恢复最近关闭的钉图"), RestorePin); Add(menu, L.Get("设置"), Settings);
        menu.Items.Add(new Forms.ToolStripSeparator()); Add(menu, L.Get("退出"), RequestExit);
        tray.ContextMenuStrip = menu;
        tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left && (DateTime.UtcNow - lastTrayDoubleClick).TotalMilliseconds > Forms.SystemInformation.DoubleClickTime) { trayClickTimer.Stop(); trayClickTimer.Start(); }
            else if (e.Button == Forms.MouseButtons.Middle) System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => RunTrayAction(Store.Settings.TrayMiddleClickAction)));
        };
        tray.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) { lastTrayDoubleClick = DateTime.UtcNow; trayClickTimer.Stop(); System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => RunTrayAction(Store.Settings.TrayDoubleClickAction))); } };
        trayClickTimer.Tick += (_, _) => { trayClickTimer.Stop(); RunTrayAction(Store.Settings.TraySingleClickAction); };
        tray.BalloonTipClicked += (_, _) => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(QuickSaveNoticeClicked));
        ApplyTrayAppearance(); ApplyTrayActions(); ApplyHotkeys(); ApplyGlobalMouse();
        if (Workspace.RestoreOnStart) RestoreActivePins(); Refresh();
        if (!string.IsNullOrEmpty(Store.LoadIssue)) Window.ShowNotice(Store.LoadIssue);
        if (!string.IsNullOrEmpty(Workspace.LoadIssue)) Window.ShowNotice(Workspace.LoadIssue);
        if (RecordingJournal.List(Store.Root).Count > 0) Window.ShowNotice(L.Get("发现可恢复的录制片段，可从“录制恢复”继续保存。"));
        if (!tray.Visible) Show();
    }
    private void Add(Forms.ContextMenuStrip menu, string label, Action action) => menu.Items.Add(label, null, (_, _) => System.Windows.Application.Current.Dispatcher.BeginInvoke(action));
    private static Icon TrayIcon(string? color = null)
    {
        return Branding.CreateTrayIcon(color);
    }
    private void ApplyTrayAppearance()
    {
        var current = Store.Settings; var next = TrayIcon(current.TrayIconMode == "Custom" ? current.TrayIconColor.Substring(current.TrayIconColor.Length - 6).Insert(0, "#") : null);
        tray.Icon = next; icon.Dispose(); icon = next; tray.Visible = current.TrayIconMode != "Hidden";
        tray.Text = L.Get("DeskSnip · 截图 / 钉图 / 录屏");
        if (!tray.Visible && session != null && finishing == null) recordingBar?.Show();
    }
    private void RunTrayAction(string action)
    {
        if (Exiting) return;
        switch (action)
        {
            case "Capture": Capture(); break; case "Pin": PinClipboard(); break; case "Settings": Settings(); break;
            case "TogglePins": TogglePins(); break; case "Show": Show(); break;
            case "ToggleHotkeys": ToggleHotkeys(); break;
        }
    }
    private static void PlaySound(bool enabled) { if (enabled) System.Media.SystemSounds.Asterisk.Play(); }
    internal void PinFile(string path)
    {
        if (Exiting) return;
        try { Pin(ImageFiles.Load(path)); }
        catch (Exception error) { Ui.Error(Window, error, L.Get("钉住图片文件")); }
    }
    internal void Show()
    {
        if (Exiting) return;
        Window.Show(); Window.WindowState = WindowState.Normal; Window.Activate(); Refresh();
    }
    internal async void Capture()
    {
        if (selecting || Exiting) return;
        try
        {
            var selected = await Select(false);
            if (selected.Image != null)
            {
                if (selected.Action == CaptureAction.LongScreenshot) LongScreenshot(selected.Region);
                else if (selected.Action == CaptureAction.Record) RecordRegion(selected.Region, null);
                else CompleteCapture(selected.Image, selected.Action, selected.Region, selected.AnnotationBase, selected.Annotations);
            }
        }
        catch (Exception e) { Ui.Error(Window, e, L.Get("截图")); Show(); }
    }
    private void CompleteCapture(BitmapSource image, CaptureAction action, PixelRect region = default, BitmapSource? annotationBase = null, AnnotationDocument? annotations = null)
    {
        SaveAutomatically(image, "Capture");
        if (action != CaptureAction.Edit && Store.Settings.MarkAutomaticWatermark)
        {
            var watermark = new Annotation(AnnotationKind.Watermark, new[] { new PixelPoint(8, 8) }, 0x55909090, 3, Store.Settings.MarkWatermarkText.Replace("{time}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")), appearance: new AnnotationAppearance(opacity: .6));
            image = AnnotationRenderer.Flatten(image, new[] { watermark });
            if (action == CaptureAction.Pin && annotations != null) { try { annotations.Add(watermark); } catch (ArgumentException) { } }
        }
        switch (action)
        {
            case CaptureAction.Copy: CopyImage(image, region.IsEmpty ? 1 : ScreenCapture.MonitorScale(new PixelPoint(region.X + region.Width / 2, region.Y + region.Height / 2))); SaveAutomatically(image, "Copy"); Remember(image); PlaySound(Store.Settings.SoundCaptureCopy); Window.ShowNotice(L.Get("截图已复制；历史已保存。")); break;
            case CaptureAction.Pin: Remember(image); Pin(image, region, annotationBase, annotations, true); PlaySound(Store.Settings.SoundCapturePin); break;
            case CaptureAction.Ocr: Recognize(image); break;
            case CaptureAction.ServiceRecognition: RecognizeService(image); break;
            case CaptureAction.TranslationRecognition: RecognizeService(image, "translation"); break;
            case CaptureAction.DocumentRecognition: RecognizeService(image, "document"); break;
            case CaptureAction.TableRecognition: RecognizeService(image, "table"); break;
            case CaptureAction.FormulaRecognition: RecognizeService(image, "formula"); break;
            case CaptureAction.QuickSave: QuickSaveImage(image); break;
            case CaptureAction.Save:
                SaveImageManual(image, Window);
                break;
        }
    }
    private async Task<(BitmapSource? Image, PixelRect Region, CaptureAction Action, BitmapSource? AnnotationBase, AnnotationDocument? Annotations)> Select(bool recording, PixelRect initial = default, bool cursorIncluded = false, CaptureShape initialShape = CaptureShape.Rectangle)
    {
        selecting = true;
        var wasVisible = Window.IsVisible; var visibleEditors = editors.Where(e => e.IsVisible).ToArray();
        Window.Hide(); foreach (var editor in visibleEditors) editor.Hide();
        try
        {
            await Task.Delay(180); if (Exiting) return (null, default, CaptureAction.Edit, null, null);
            var bounds = ScreenCapture.DesktopBounds; var frames = ScreenCapture.CapturePair(bounds);
            var targets = Store.Settings.ScreenshotUiDetection == "None" ? Array.Empty<CaptureTarget>() : CaptureTargetReader.Snapshot(bounds);
            var preferences = Store.Settings.Clone(); if (cursorIncluded) preferences.ScreenshotCursorVisible = true;
            var recent = preferences.ScreenshotAreaHistoryLimit == 0 ? Array.Empty<PixelRect>() : Profiles.Snapshot.Recent.Take(preferences.ScreenshotAreaHistoryLimit).Select(r => r.Bounds).ToArray();
            selectionWindow = new CaptureWindow(frames.Image, bounds, recording, targets, recent, initial, initialShape, preferences, frames.WithCursor);
            selectionWindow.RecordTransitionRequested = BeginRecordingFromSelection;
            selectionWindow.LongScreenshotTransitionRequested = BeginLongScreenshotFromSelection;
            if (!recording) PlaySound(preferences.SoundCaptureStart);
            var accepted = selectionWindow.ShowDialog() == true;
            if (longScreenshotStartedFromSelection)
            {
                longScreenshotStartedFromSelection = false;
                Profiles.Remember(selectionWindow.Selection, preferences.ScreenshotAreaHistoryLimit);
                return (null, default, CaptureAction.LongScreenshot, null, null);
            }
            if (recordingStartedFromSelection) { if (!recording) recordingStartedFromSelection = false; return (null, default, CaptureAction.Record, null, null); }
            if (!accepted) { pendingRecordingPlan = null; pendingRecordingAnnotations = null; return (null, default, CaptureAction.Edit, null, null); }
            pendingRecordingPlan = selectionWindow.RecordingMode ? selectionWindow.Plan : null;
            pendingRecordingAnnotations = selectionWindow.RecordingMode ? selectionWindow.RecordingAnnotations : null;
            var region = selectionWindow.Selection;
            Profiles.Remember(region, preferences.ScreenshotAreaHistoryLimit);
            var image = selectionWindow.InlineOutput ?? ScreenCapture.Crop(selectionWindow.SelectedDesktop, bounds, region);
            var annotationBase = selectionWindow.InlineBaseImage ?? image;
            var annotations = selectionWindow.InlineDocument ?? new AnnotationDocument(image.PixelWidth, image.PixelHeight, preferences.MarkLimitToSelection);
            if (!recording)
            {
                image = CaptureMasks.Apply(image, selectionWindow.ShapeResult, selectionWindow.ShapePoints);
                annotationBase = CaptureMasks.Apply(annotationBase, selectionWindow.ShapeResult, selectionWindow.ShapePoints);
            }
            if (selectionWindow.InlineOutput != null && selectionWindow.LastInlineTool is AnnotationKind editedTool && Store.Settings.MarkRememberScreenshotTool)
            {
                try { var next = Store.Settings.Clone(); next.MarkLastScreenshotTool = editedTool.ToString(); Store.SaveSettings(next); }
                catch (Exception) { Window.ShowNotice(L.Get("标注工具记忆未保存，已编辑图片仍保留。")); }
            }
            return (image, region, selectionWindow.ActionResult, annotationBase, annotations);
        }
        finally
        {
            var keepHidden = selectionWindow != null && selectionWindow.ActionResult == CaptureAction.Record;
            selectionWindow = null; selecting = false;
            if (!Exiting && !keepHidden) { if (wasVisible) Window.Show(); foreach (var editor in visibleEditors) if (editors.Contains(editor)) editor.Show(); }
        }
    }
    private HistoryEntry Remember(BitmapSource image)
    {
        var entry = Store.AddImage(image.PixelWidth, image.PixelHeight, s => ImageFiles.WritePng(image, s)); Refresh(); return entry;
    }
    private void Edit(BitmapSource image)
        => EditWithTool(image, Store.Settings.MarkRememberScreenshotTool ? (AnnotationKind)Enum.Parse(typeof(AnnotationKind), Store.Settings.MarkLastScreenshotTool) : AnnotationKind.Arrow);
    private EditorWindow EditWithTool(BitmapSource image, AnnotationKind tool)
    {
        var editor = new EditorWindow(image, Pin, Remember, Recognize, QuickSaveImage, OnImageOutput, Store.Settings, RecognizeService, tool, SaveImageManual) { ServiceOperationRequested = RecognizeService }; editors.Add(editor);
        editor.Closed += (_, _) =>
        {
            editors.Remove(editor); if (Exiting || !Store.Settings.MarkRememberScreenshotTool) return;
            try { var next = Store.Settings.Clone(); next.MarkLastScreenshotTool = editor.LastTool.ToString(); Store.SaveSettings(next); }
            catch (Exception) { Window.ShowNotice(L.Get("标注工具记忆未保存，已编辑图片仍保留。")); }
        }; editor.Show(); return editor;
    }
    private void Pin(BitmapSource image) => Pin(image, default);
    private void Pin(BitmapSource image, PixelRect region, BitmapSource? annotationBase = null, AnnotationDocument? annotations = null, bool activate = false)
    {
        if (!Store.Settings.PinImageEnabled) { Window.ShowNotice(L.Get("图像钉图已禁用，请在钉图设置中启用。")); return; }
        if (Store.Settings.PinAlsoCopy) CopyImage(image); SaveAutomatically(image, "Pin");
        if (Workspace.Hidden)
        {
            if (Store.Settings.PinKeepOthersHidden) HidePinRecords(Workspace.Records.Where(p => p.Active).Select(p => p.Id), true);
            Workspace.SetHidden(false);
        }
        var record = Workspace.Add(image.PixelWidth, image.PixelHeight, output => ImageFiles.WritePng(image, output));
        OpenPin(image, record, false, region, annotationBase, annotations, activate);
    }
    internal void PinClipboard()
    {
        if (Exiting) return;
        try
        {
            var image = Clipboard.GetImage();
            if (image == null) { Show(); Window.ShowNotice(L.Get("剪贴板里没有图片。可以先截图、复制图片，或使用“打开图片”。")); return; }
            var normalized = ImageFiles.Normalize(image); Remember(normalized); Pin(normalized);
        }
        catch (Exception e) { Ui.Error(Window, e, L.Get("钉住剪贴板")); }
    }
    internal void OpenImage()
    {
        var dialog = new OpenFileDialog { Filter = L.Get("图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*") };
        if (dialog.ShowDialog(Window) != true) return;
        try { Edit(ImageFiles.Load(dialog.FileName)); } catch (Exception e) { Ui.Error(Window, e, L.Get("打开图片")); }
    }
    internal void RecoverRecording() => new RecordingRecoveryWindow(Store.Root, Store.Settings.FfmpegPath, AfterRecordingSaved, Store.Settings.RecordingCloseAfterExport).Show();
    internal void OpenHistory(HistoryEntry? entry, PixelRect inlineRegion = default)
    {
        if (entry == null) return;
        try
        {
            if (entry.Kind == "Image") Edit(ImageFiles.Load(Store.HistoryPath(entry)));
            else
            {
                var playback = new PlaybackWindow(Store.HistoryPath(entry), entry.Kind, Store.Settings.FfmpegPath, FindRecordingJournal(entry), Store.Settings.RecordingCloseAfterExport, AfterRecordingSaved, inlineRegion, Store.Settings.OutputDirectory, settings: Store.Settings);
                if (inlineRegion.IsEmpty) playback.Owner = Window.IsVisible ? Window : null;
                playback.Show();
            }
        }
        catch (Exception e) { Ui.Error(Window, e, L.Get("打开历史")); }
    }
    internal void PinHistory(HistoryEntry? entry)
    {
        if (entry == null) return;
        if (entry.Kind != "Image") { Window.ShowNotice(L.Get("基础版钉图支持静态图片；录屏可在历史中播放或另存为。")); return; }
        try { Pin(ImageFiles.Load(Store.HistoryPath(entry))); } catch (Exception e) { Ui.Error(Window, e, L.Get("历史钉图")); }
    }
    internal void SaveHistory(HistoryEntry? entry)
    {
        if (entry == null) return;
        if (entry.Kind == "Image") { try { SaveImageManual(ImageFiles.Load(Store.HistoryPath(entry)), Window); } catch (Exception error) { Ui.Error(Window, error, L.Get("历史另存为")); } return; }
        var ext = entry.Kind == "Image" ? "png" : entry.Kind.ToLowerInvariant();
        var dialog = new SaveFileDialog { Filter = $"{entry.Kind}|*.{ext}", FileName = "DeskSnip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "." + ext, AddExtension = true };
        if (dialog.ShowDialog(Window) != true) return;
        try { AtomicFile.Write(dialog.FileName, s => { using (var source = File.OpenRead(Store.HistoryPath(entry))) source.CopyTo(s); }); AfterRecordingSaved(dialog.FileName); }
        catch (Exception e) { Ui.Error(Window, e, L.Get("历史另存为")); }
    }
    internal void RecoverPins() { foreach (var pin in pins.ToArray()) pin.RestoreInteraction(); Refresh(); }
    internal void TogglePins()
    {
        Workspace.SetHidden(!Workspace.Hidden); ApplyPinVisibility();
    }
    internal async void ExportGif(HistoryEntry? entry)
    {
        if (entry == null || Exiting) return;
        if (exporting != null) { Window.ShowNotice(L.Get("GIF 导出正在进行，请等它完成。")); return; }
        if (entry.Kind != "MP4") { Window.ShowNotice(L.Get("请先选择一条 MP4 录制，或在开始录制时直接选择 GIF 格式。")); return; }
        var dialog = new SaveFileDialog { Filter = L.Get("GIF 动图|*.gif"), FileName = "DeskSnip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".gif", AddExtension = true };
        if (dialog.ShowDialog(Window) != true) return;
        try
        {
            Window.ShowNotice(L.Get("正在导出 GIF，请稍候…"));
            exportCancellation = new CancellationTokenSource();
            var cancellation = exportCancellation.Token;
            var source = Store.HistoryPath(entry);
            exporting = Task.Run(() => GifExporter.Export(source, dialog.FileName, 10, cancellation));
            await exporting;
            AfterRecordingSaved(dialog.FileName);
            if (!Exiting) Window.ShowNotice(L.Get("GIF 已导出（10 FPS、256 色）。"));
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!Exiting) Ui.Error(Window, e, L.Get("导出 GIF")); }
        finally { exportCancellation?.Dispose(); exportCancellation = null; exporting = null; }
    }
    internal void RestorePin()
    {
        var record = Workspace.Records.Where(p => !p.Active).OrderByDescending(p => Store.Settings.PinRestoreOrder == "Created" ? p.CreatedUtc : p.ClosedUtc, StringComparer.Ordinal).FirstOrDefault();
        if (record != null) RestorePinRecord(record.Id);
    }
    internal void Settings()
    {
        if (hotkeysSuspended) return;
        var initialToolStyles = Store.Settings.MarkToolStyles;
        var dialog = new SettingsWindow(Store.Settings, value =>
        {
            var parsed = new[] { value.CaptureHotkey, value.PinHotkey, value.RecordHotkey }.Concat(value.ActionBindings.Select(b => b.Hotkey)).Where(k => !string.IsNullOrWhiteSpace(k)).Select(Hotkeys.Parse).ToArray();
            if (parsed.Distinct().Count() != parsed.Length) throw new ArgumentException(L.Get("全局快捷键不能相同。"));
            value.MarkToolDefaults = AnnotationToolDefaults.Serialize();
            if (value.MarkToolStyles == initialToolStyles) value.MarkToolStyles = Store.Settings.MarkToolStyles;
            using (var integration = new SystemIntegrationChange(Store.Settings, value)) { Store.SaveSettings(value); integration.Commit(); }
            Workspace.Configure(value.PinRestoreOnStart, value.PinHistoryLimit);
            L.Apply(value.InterfaceLanguage); Ui.ApplyTheme(value); ApplyTrayAppearance(); ApplyTrayActions(); foreach (var pin in pins.ToArray()) pin.RefreshAppearance(); ApplyHotkeys(); ApplyGlobalMouse(); Refresh();
        }, Store.Root);
        if (Window.IsVisible) dialog.Owner = Window;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.HotkeyCaptureChanged += capturing =>
        {
            if (capturing) { hotkeysSuspended = true; hotkeys.Clear(); }
            else { hotkeysSuspended = false; ApplyHotkeys(); }
        };
        try { dialog.ShowDialog(); }
        finally { hotkeysSuspended = false; ApplyHotkeys(); }
        if (!tray.Visible && !Window.IsVisible && session == null) Show();
    }
    private void SaveToolDefaults()
    {
        if (Exiting) return;
        try
        {
            var next = Store.Settings.Clone(); next.MarkToolDefaults = AnnotationToolDefaults.Serialize();
            if (next.MarkToolDefaults != Store.Settings.MarkToolDefaults) Store.SaveSettings(next);
        }
        catch (Exception) { Window.ShowNotice(L.Get("工具栏默认工具未保存。")); }
    }
    private void SaveToolStyles(string value)
    {
        if (Exiting) return;
        try
        {
            var next = Store.Settings.Clone(); next.MarkToolStyles = value;
            if (next.MarkToolStyles != Store.Settings.MarkToolStyles) Store.SaveSettings(next);
        }
        catch (Exception) { Window.ShowNotice(L.Get("标注样式未保存。")); }
    }
    private void ApplyHotkeys()
    {
        if (hotkeysSuspended) return;
        var preferences = Store.Settings.Clone(); if (hotkeysDisabled) { preferences.CaptureHotkey = preferences.PinHotkey = preferences.RecordHotkey = ""; preferences.ActionBindings = preferences.ActionBindings.Where(b => b.Action == "ToggleHotkeys").ToList(); }
        try { Window.ShowNotice(hotkeys.Apply(preferences, Capture, PinClipboard, Record, ActionHandlers())); }
        catch (ArgumentException e) { Window.ShowNotice(L.Join(e.Message,L.Get(" 请在设置中修改。"))); }
    }
    internal void Record() => RecordRegion(default, null);
    private async void RecordRegion(PixelRect initialRegion, string? initialFormat)
    {
        if (Exiting || selecting || finishing != null || startingRecording) return;
        if (session != null) { recordingBar?.TogglePause(); return; }
        startingRecording = true;
        var selectionStarted = false;
        try
        {
            var plan = pendingRecordingPlan; pendingRecordingPlan = null;
            var annotations = pendingRecordingAnnotations; pendingRecordingAnnotations = null;
            var region = initialRegion;
            if (region.IsEmpty)
            {
                var selected = await Select(true);
                selectionStarted = recordingStartedFromSelection;
                recordingStartedFromSelection = false;
                if (selectionStarted) return;
                if (selected.Image == null || Exiting) return;
                region = selected.Region; plan = pendingRecordingPlan; pendingRecordingPlan = null;
                annotations = pendingRecordingAnnotations; pendingRecordingAnnotations = null;
            }
            plan ??= RecordingPlan.FromSettings(Store.Settings);
            if (initialFormat != null) plan.Format = initialFormat;
            else if (plan.Quick) plan.Format = "MP4";
            await StartRecordingCore(region, plan, annotations, null);
        }
        catch (Exception e) { if (!Exiting) Ui.Error(Window, e, L.Get("开始录制")); }
        finally { if (!selectionStarted) startingRecording = false; }
    }
    /// <summary>选区窗口仍在显示时启动录制；录屏边框/控制条/叠加层就绪后再关闭选区，避免桌面空档闪烁。</summary>
    private void BeginRecordingFromSelection(CaptureWindow window)
    {
        if (recordingStartedFromSelection || Exiting) return;
        recordingStartedFromSelection = true;
        window.HideForRecordingTransition();
        _ = StartRecordingCore(window.Selection, window.Plan, window.RecordingAnnotations, window);
    }
    private async Task StartRecordingCore(PixelRect region, RecordingPlan plan, IReadOnlyList<Annotation>? annotations, CaptureWindow? selection)
    {
        startingRecording = true;
        try
        {
            var format = plan.Format;
            var preferences = Store.Settings.Clone();
            preferences.RecordSystemAudio = plan.SystemAudio; preferences.RecordMicrophone = plan.Microphone; preferences.FramesPerSecond = plan.Fps;
            if (!plan.Quick) preferences.RecordingFormat = plan.Format;
            Store.SaveSettings(preferences);
            var options = BuildRecordingOptions(plan, format, preferences);
            Window.Hide();
            if (preferences.RecordingDelay > 0 && new CountdownWindow(preferences.RecordingDelay).ShowDialog() != true)
            { selection?.CancelRecordingTransition(); Show(); return; }
            if (Exiting) { selection?.CancelRecordingTransition(); return; }
            recordingOverlay = new RecordingAnnotationOverlay(region, annotations); recordingOverlay.Show();
            var pending = !plan.Quick;
            var outputPath = plan.Quick ? QuickRecordingPath(format, region) : Store.NewPendingRecordingPath(format);
            var recording = new RecordingSession(outputPath, region, format, plan.Fps, preferences.CaptureCursor, options: options, quality: preferences.VideoQuality) { DeferFinalExport = pending && options != null };
            session = recording; recordingQuick = plan.Quick;
            recordingBar = new RecordingBar(recording, () => { _ = FinishRecording(); }, () => DiscardRecording(false), () => DiscardRecording(true), Store.Settings, recordingOverlay); recordingBar.Show(); if (!plan.ToolbarVisible) recordingBar.Hide();
            if (selection != null)
            {
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
                await Task.Delay(30);
            }
            selection?.FinishRecordingTransition();
            var starting = recording.StartAsync();
            await starting;
            if (Exiting)
            {
                if (recordingBar != null) { recordingBar.Finishing = true; recordingBar.Close(); recordingBar = null; }
                await recording.StopAsync(); CloseRecordingOverlay(); return;
            }
            _ = ObserveRecording(recording);
        }
        catch (Exception e)
        {
            if (recordingBar != null) { recordingBar.Finishing = true; recordingBar.Close(); recordingBar = null; }
            session = null; recordingQuick = false; CloseRecordingOverlay(); selection?.CancelRecordingTransition();
            if (!Exiting) Ui.Error(Window, e, L.Get("开始录制"));
        }
        finally { startingRecording = false; }
    }
    private RecordingOptions? BuildRecordingOptions(RecordingPlan plan, string format, AppSettings preferences)
    {
        var required = plan.SystemAudio || plan.Microphone || format == "WebP" || preferences.VideoEncoder != VideoEncoder.Software;
        string? executable = null;
        try { executable = Ffmpeg.Find(preferences.FfmpegPath); }
        catch (InvalidOperationException) when (!required) { }
        if (executable == null) return null;
        string systemDevice = "", microphoneDevice = "";
        if (plan.SystemAudio)
        {
            IReadOnlyList<AudioDevice> devices;
            try { devices = AudioDevices.List(true); } catch (Exception) { throw new InvalidOperationException(L.Get("未检测到系统音频设备。")); }
            if (devices.Count == 0) throw new InvalidOperationException(L.Get("未检测到系统音频设备。"));
            systemDevice = devices[0].Id;
        }
        if (plan.Microphone)
        {
            IReadOnlyList<AudioDevice> devices;
            try { devices = AudioDevices.List(false); } catch (Exception) { throw new InvalidOperationException(L.Get("未检测到麦克风设备。")); }
            if (devices.Count == 0) throw new InvalidOperationException(L.Get("未检测到麦克风设备。"));
            microphoneDevice = devices[0].Id;
        }
        return new RecordingOptions
        {
            DataRoot = Store.Root, FfmpegPath = executable, SystemAudio = plan.SystemAudio, Microphone = plan.Microphone,
            SystemDeviceId = systemDevice, MicrophoneDeviceId = microphoneDevice, Quality = preferences.VideoQuality, Encoder = preferences.VideoEncoder
        };
    }
    private string QuickRecordingPath(string format, PixelRect region)
    {
        var settings = Store.Settings;
        var directory = string.IsNullOrEmpty(settings.QuickRecordingDirectory) ? new ManagedDirectory(Store.Root).Resolve("recordings") : settings.QuickRecordingDirectory;
        return SaveNaming.ChoosePath(directory, SaveNaming.Expand(settings.QuickRecordingNameTemplate, DateTime.Now, region.Width, region.Height, "Recording"), "." + format.ToLowerInvariant(), settings.QuickRecordingOverwrite);
    }
    private async Task ObserveRecording(RecordingSession recording)
    {
        try { await recording.Completion; }
        catch (Exception e)
        {
            if (finishing != null) return;
            session = null; if (recordingBar != null) { recordingBar.Finishing = true; recordingBar.Close(); recordingBar = null; }
            CloseRecordingOverlay();
            if (Exiting) exitRecordingError = e;
            else Ui.Error(Window, e, L.Get("录制中断"));
        }
    }
    private void CloseRecordingOverlay()
    {
        var overlay = recordingOverlay; recordingOverlay = null;
        if (overlay == null) return;
        try { overlay.CloseForExit(); } catch (Exception) { }
    }
    private Task FinishRecording()
    {
        if (finishing != null) return finishing;
        if (session == null) return Task.CompletedTask;
        finishing = FinishRecordingCore(session); return finishing;
    }
    private async Task FinishRecordingCore(RecordingSession recording)
    {
        await Task.Yield();
        try
        {
            recordingBar?.ShowFinishing(); await recording.StopAsync();
            if (recordingQuick)
            {
                Store.ImportRecording(recording.Path, recording.Format, recording.Region.Width, recording.Region.Height, recording.Journal?.Snapshot.Id ?? "");
                AfterRecordingSaved(recording.Path);
            }
            else if (!Exiting) OpenPendingRecording(recording);
            else CleanupPendingRecording(recording, false);
        }
        catch (Exception e)
        {
            if (Exiting) exitRecordingError = e;
            else Ui.Error(Window, e, L.Get("完成录制"));
        }
        finally
        {
            if (recordingBar != null) { recordingBar.Finishing = true; recordingBar.Close(); recordingBar = null; }
            CloseRecordingOverlay();
            session = null; finishing = null; recordingQuick = false; Refresh();
        }
    }
    private void OpenPendingRecording(RecordingSession recording)
    {
        var committed = false;
        var sourceRequiresExport = recording.DeferFinalExport && recording.Journal != null;
        void Commit(string path)
        {
            if (committed) return;
            try
            {
                var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
                var kind = extension == ".mp4" ? "MP4" : extension == ".gif" ? "GIF" : extension == ".webp" ? "WebP" : recording.Format;
                Store.ImportRecording(path, kind, recording.Region.Width, recording.Region.Height, recording.Journal?.Snapshot.Id ?? "");
                recording.Journal?.MarkPublished();
                committed = true;
                AfterRecordingSaved(path);
            }
            catch (Exception error) { Ui.Error(Window, error, L.Get("保存录制")); }
        }
        void Discard()
        {
            try { if (System.IO.File.Exists(recording.Path)) System.IO.File.Delete(recording.Path); } catch (Exception) { }
            if (committed) return;
            try { recording.Journal?.Discard(); } catch (Exception) { }
        }
        recordingBar?.Hide();
        CloseRecordingOverlay();
        var playback = new PlaybackWindow(recording.PlaybackPath, sourceRequiresExport ? "MP4" : recording.Format, Store.Settings.FfmpegPath, recording.Journal,
            Store.Settings.RecordingCloseAfterExport, Commit, recording.Region, Store.Settings.OutputDirectory,
            pending: true, defaultSaveFormat: recording.Format, discardPending: Discard, sourceRequiresExport: sourceRequiresExport, settings: Store.Settings);
        playback.Show();
    }
    private static void CleanupPendingRecording(RecordingSession recording, bool committed)
    {
        try { if (System.IO.File.Exists(recording.Path)) System.IO.File.Delete(recording.Path); } catch (Exception) { }
        if (!committed) { try { recording.Journal?.Discard(); } catch (Exception) { } }
    }
    private async void DiscardRecording(bool restart)
    {
        if (session == null || finishing != null) return;
        var recording = session; var restartRegion = restart ? recording.Region : default;
        recordingBar?.ShowFinishing(); finishing = recording.DiscardAsync();
        try { await finishing; }
        catch (Exception error) { if (!Exiting) Ui.Error(Window, error, L.Get("丢弃录制")); }
        finally
        {
            if (recordingBar != null) { recordingBar.Finishing = true; recordingBar.Close(); recordingBar = null; }
            CloseRecordingOverlay();
            session = null; finishing = null;
        }
        if (!Exiting && restart) { if (restartRegion.IsEmpty) Record(); else RecordRegion(restartRegion, null); }
    }
    private RecordingJournal? FindRecordingJournal(HistoryEntry entry)
    {
        if (string.IsNullOrEmpty(entry.RecordingId)) return null;
        try { return RecordingJournal.Open(Store.Root, entry.RecordingId); }
        catch (Exception error) when (error is IOException || error is ArgumentException || error is System.Runtime.Serialization.SerializationException) { return null; }
    }
    internal async void RequestExit()
    {
        if (Exiting) return; Exiting = true;
        if (selectionWindow != null) selectionWindow.Close();
        exportCancellation?.Cancel();
        if (exporting != null) { try { await exporting; } catch (Exception) { } }
        await FinishRecording();
        var backgroundWindows = System.Windows.Application.Current.Windows.Cast<Window>().OfType<IBackgroundWindow>().ToArray();
        await Task.WhenAll(backgroundWindows.Select(window => window.CloseSafelyAsync()));
        if (exitRecordingError != null) Ui.Error(Window, exitRecordingError, L.Get("录制未能完成保存"));
        PersistPins();
        foreach (var editor in editors.ToArray()) editor.Close(); foreach (var pin in pins.ToArray()) pin.CloseForExit();
        tray.Visible = false; Window.Close(); System.Windows.Application.Current.Shutdown();
    }
    private void Refresh() { if (!Exiting) Window.Refresh(); }
    public void Dispose() { AnnotationToolDefaults.Changed -= SaveToolDefaults; AnnotationStylePersistence.Changed -= SaveToolStyles; StopPinSelectionWatch(); CloseRecordingOverlay(); globalMouse?.Dispose(); gestureOverlay?.Close(); trayClickTimer.Stop(); hotkeys.Dispose(); tray.Dispose(); icon.Dispose(); }
}
