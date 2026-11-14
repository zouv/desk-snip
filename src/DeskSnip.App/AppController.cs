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
    private DateTime lastTrayDoubleClick;
    private readonly System.Windows.Threading.DispatcherTimer trayClickTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime) };
    private bool selecting;
    private bool startingRecording;
    private CaptureWindow? selectionWindow;
    private RecordingSession? session;
    private RecordingBar? recordingBar;
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
        L.Apply(Store.Settings.InterfaceLanguage);
        Ui.ApplyTheme(Store.Settings);
        Workspace = new PinWorkspace(dataRoot);
        var preferences = Store.Settings.Clone();
        if (preferences.AdoptLegacyPinPreferences(Workspace.RestoreOnStart, Workspace.Snapshot.ClosedLimit)) Store.SaveSettings(preferences);
        Workspace.Configure(Store.Settings.PinRestoreOnStart, Store.Settings.PinHistoryLimit);
        Profiles = new CaptureProfiles(dataRoot);
        Window = new MainWindow(this); System.Windows.Application.Current.MainWindow = Window; Window.Show();
        hotkeys = new Hotkeys(new WindowInteropHelper(Window).Handle);
        icon = TrayIcon(); tray = new Forms.NotifyIcon { Icon = icon, Text = L.Get("DeskSnip · 截图 / 钉图 / 录屏"), Visible = true };
        var menu = new Forms.ContextMenuStrip();
        Add(menu, L.Get("打开 DeskSnip"), Show); Add(menu, L.Get("截取屏幕"), Capture); Add(menu, L.Get("钉住剪贴板"), PinClipboard); Add(menu, L.Get("录制 / 暂停"), Record);
        Add(menu, L.Get("显示录制工具栏"), () => recordingBar?.Show()); Add(menu, L.Get("停止并保存录制"), () => { _ = FinishRecording(); });
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
                else CompleteCapture(selected.Image, selected.Action, selected.Region);
            }
        }
        catch (Exception e) { Ui.Error(Window, e, L.Get("截图")); Show(); }
    }
    private void CompleteCapture(BitmapSource image, CaptureAction action, PixelRect region = default)
    {
        SaveAutomatically(image, "Capture");
        if (CaptureWindow.AnnotationTool(action) is AnnotationKind tool) { var editor = EditWithTool(image, tool); if (action == CaptureAction.EditAutoMosaic) editor.StartPrivacyDetection(); return; }
        if (action != CaptureAction.Edit && Store.Settings.MarkAutomaticWatermark) image = AnnotationRenderer.Flatten(image, new[] { new Annotation(AnnotationKind.Watermark, new[] { new PixelPoint(8, 8) }, 0x55909090, 3, Store.Settings.MarkWatermarkText.Replace("{time}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")), appearance: new AnnotationAppearance(opacity: .6)) });
        switch (action)
        {
            case CaptureAction.Edit: Edit(image); break;
            case CaptureAction.Copy: CopyImage(image, region.IsEmpty ? 1 : ScreenCapture.MonitorScale(new PixelPoint(region.X + region.Width / 2, region.Y + region.Height / 2))); SaveAutomatically(image, "Copy"); Remember(image); PlaySound(Store.Settings.SoundCaptureCopy); Window.ShowNotice(L.Get("截图已复制；历史已保存。")); break;
            case CaptureAction.Pin: Remember(image); Pin(image, region); PlaySound(Store.Settings.SoundCapturePin); break;
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
    private async Task<(BitmapSource? Image, PixelRect Region, CaptureAction Action)> Select(bool recording, PixelRect initial = default, bool cursorIncluded = false, CaptureShape initialShape = CaptureShape.Rectangle)
    {
        selecting = true;
        var wasVisible = Window.IsVisible; var visibleEditors = editors.Where(e => e.IsVisible).ToArray();
        Window.Hide(); foreach (var editor in visibleEditors) editor.Hide();
        try
        {
            await Task.Delay(180); if (Exiting) return (null, default, CaptureAction.Edit);
            var bounds = ScreenCapture.DesktopBounds; var frames = ScreenCapture.CapturePair(bounds);
            var targets = Store.Settings.ScreenshotUiDetection == "None" ? Array.Empty<CaptureTarget>() : CaptureTargetReader.Snapshot(bounds);
            var preferences = Store.Settings.Clone(); if (cursorIncluded) preferences.ScreenshotCursorVisible = true;
            var recent = preferences.ScreenshotAreaHistoryLimit == 0 ? Array.Empty<PixelRect>() : Profiles.Snapshot.Recent.Take(preferences.ScreenshotAreaHistoryLimit).Select(r => r.Bounds).ToArray();
            selectionWindow = new CaptureWindow(frames.Image, bounds, recording, targets, recent, initial, initialShape, preferences, frames.WithCursor);
            if (!recording) PlaySound(preferences.SoundCaptureStart);
            if (selectionWindow.ShowDialog() != true) return (null, default, CaptureAction.Edit);
            var region = selectionWindow.Selection;
            Profiles.Remember(region, preferences.ScreenshotAreaHistoryLimit);
            var image = ScreenCapture.Crop(selectionWindow.SelectedDesktop, bounds, region);
            if (!recording) image = CaptureMasks.Apply(image, selectionWindow.ShapeResult, selectionWindow.ShapePoints);
            return (image, region, selectionWindow.ActionResult);
        }
        finally
        {
            selectionWindow = null; selecting = false;
            if (!Exiting) { if (wasVisible) Window.Show(); foreach (var editor in visibleEditors) if (editors.Contains(editor)) editor.Show(); }
        }
    }
    private HistoryEntry Remember(BitmapSource image)
    {
        var entry = Store.AddImage(image.PixelWidth, image.PixelHeight, s => ImageFiles.WritePng(image, s)); Refresh(); return entry;
    }
    private void Edit(BitmapSource image)
        => EditWithTool(image, Store.Settings.MarkRememberScreenshotTool ? (AnnotationKind)Enum.Parse(typeof(AnnotationKind), Store.Settings.MarkLastScreenshotTool) : AnnotationKind.Arrow);
    private void EditPin(BitmapSource image)
        => EditWithTool(image, Store.Settings.MarkRememberPinTool ? (AnnotationKind)Enum.Parse(typeof(AnnotationKind), Store.Settings.MarkLastPinTool) : AnnotationKind.Arrow, true);
    private EditorWindow EditWithTool(BitmapSource image, AnnotationKind tool, bool fromPin = false)
    {
        var editor = new EditorWindow(image, Pin, Remember, Recognize, QuickSaveImage, OnImageOutput, Store.Settings, RecognizeService, tool, SaveImageManual) { ServiceOperationRequested = RecognizeService }; editors.Add(editor);
        editor.Closed += (_, _) =>
        {
            editors.Remove(editor); if (Exiting || !(fromPin ? Store.Settings.MarkRememberPinTool : Store.Settings.MarkRememberScreenshotTool)) return;
            try { var next = Store.Settings.Clone(); if (fromPin) next.MarkLastPinTool = editor.LastTool.ToString(); else next.MarkLastScreenshotTool = editor.LastTool.ToString(); Store.SaveSettings(next); }
            catch (Exception) { Window.ShowNotice(L.Get("标注工具记忆未保存，已编辑图片仍保留。")); }
        }; editor.Show(); return editor;
    }
    private void Pin(BitmapSource image) => Pin(image, default);
    private void Pin(BitmapSource image, PixelRect region)
    {
        if (!Store.Settings.PinImageEnabled) { Window.ShowNotice(L.Get("图像钉图已禁用，请在钉图设置中启用。")); return; }
        if (Store.Settings.PinAlsoCopy) CopyImage(image); SaveAutomatically(image, "Pin");
        if (Workspace.Hidden)
        {
            if (Store.Settings.PinKeepOthersHidden) HidePinRecords(Workspace.Records.Where(p => p.Active).Select(p => p.Id), true);
            Workspace.SetHidden(false);
        }
        var record = Workspace.Add(image.PixelWidth, image.PixelHeight, output => ImageFiles.WritePng(image, output));
        OpenPin(image, record, false, region);
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
    internal void OpenHistory(HistoryEntry? entry)
    {
        if (entry == null) return;
        try
        {
            if (entry.Kind == "Image") Edit(ImageFiles.Load(Store.HistoryPath(entry)));
            else new PlaybackWindow(Store.HistoryPath(entry), entry.Kind, Store.Settings.FfmpegPath, FindRecordingJournal(entry), Store.Settings.RecordingCloseAfterExport, AfterRecordingSaved) { Owner = Window.IsVisible ? Window : null }.Show();
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
        Show();
        new SettingsWindow(Store.Settings, value =>
        {
            var parsed = new[] { value.CaptureHotkey, value.PinHotkey, value.RecordHotkey }.Concat(value.ActionBindings.Select(b => b.Hotkey)).Where(k => !string.IsNullOrWhiteSpace(k)).Select(Hotkeys.Parse).ToArray();
            if (parsed.Distinct().Count() != parsed.Length) throw new ArgumentException(L.Get("全局快捷键不能相同。"));
            using (var integration = new SystemIntegrationChange(Store.Settings, value)) { Store.SaveSettings(value); integration.Commit(); }
            Workspace.Configure(value.PinRestoreOnStart, value.PinHistoryLimit);
            L.Apply(value.InterfaceLanguage); Ui.ApplyTheme(value); ApplyTrayAppearance(); ApplyTrayActions(); foreach (var pin in pins.ToArray()) pin.RefreshAppearance(); ApplyHotkeys(); ApplyGlobalMouse(); Refresh();
        }, Store.Root) { Owner = Window }.ShowDialog();
    }
    private void ApplyHotkeys()
    {
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
        try
        {
            var region = initialRegion;
            if (region.IsEmpty) { var selected = await Select(true); if (selected.Image == null || Exiting) return; region = selected.Region; }
            var initialPreferences = Store.Settings.Clone(); if (initialFormat != null) initialPreferences.RecordingFormat = initialFormat;
            var setup = new RecordingSetup(region, initialPreferences, format => DefaultRecordingPath(format, region), Store.Root);
            if (setup.ShowDialog() != true) return;
            var preferences = Store.Settings.Clone(); setup.ApplyPreferences(preferences); Store.SaveSettings(preferences);
            Window.Hide();
            if (setup.Delay > 0 && new CountdownWindow(setup.Delay).ShowDialog() != true) { Show(); return; }
            if (Exiting) return;
            var recording = new RecordingSession(setup.OutputPath, region, setup.Format, setup.Fps, setup.CursorIncluded, options: setup.Options, quality: setup.Quality);
            session = recording; Window.Hide();
            await recording.StartAsync();
            if (Exiting) { await recording.StopAsync(); return; }
            recordingBar = new RecordingBar(recording, () => { _ = FinishRecording(); }, () => DiscardRecording(false), () => DiscardRecording(true), Store.Settings); recordingBar.Show(); if (!setup.ToolbarVisible) recordingBar.Hide(); _ = ObserveRecording(recording);
        }
        catch (Exception e) { session = null; if (!Exiting) { Ui.Error(Window, e, L.Get("开始录制")); Show(); } }
        finally { startingRecording = false; }
    }
    private string DefaultRecordingPath(string format, PixelRect region)
    {
        var settings = Store.Settings;
        if (settings.QuickRecordingEnabled && format == "MP4")
        {
            var directory = string.IsNullOrEmpty(settings.QuickRecordingDirectory) ? new ManagedDirectory(Store.Root).Resolve("recordings") : settings.QuickRecordingDirectory;
            return SaveNaming.ChoosePath(directory, SaveNaming.Expand(settings.QuickRecordingNameTemplate, DateTime.Now, region.Width, region.Height, "Recording"), ".mp4", settings.QuickRecordingOverwrite);
        }
        if (string.IsNullOrEmpty(Store.Settings.OutputDirectory)) return Store.NewRecordingPath(format);
        return System.IO.Path.Combine(Store.Settings.OutputDirectory, "DeskSnip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 4) + "." + format.ToLowerInvariant());
    }
    private async Task ObserveRecording(RecordingSession recording)
    {
        try { await recording.Completion; }
        catch (Exception e)
        {
            if (finishing != null) return;
            session = null; if (recordingBar != null) { recordingBar.Finishing = true; recordingBar.Close(); recordingBar = null; }
            if (Exiting) exitRecordingError = e;
            else { Ui.Error(Window, e, L.Get("录制中断")); Show(); }
        }
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
            var entry = Store.ImportRecording(recording.Path, recording.Format, recording.Region.Width, recording.Region.Height, recording.Journal?.Snapshot.Id ?? "");
            AfterRecordingSaved(recording.Path);
            if (!Exiting) { Show(); Window.ShowNotice(L.Get("录制已保存，历史中可播放和另存为。")); if (!Store.Settings.RecordingCloseAfterExport) OpenHistory(entry); }
        }
        catch (Exception e)
        {
            if (Exiting) exitRecordingError = e;
            else { Show(); Ui.Error(Window, e, L.Get("保存录制")); }
        }
        finally
        {
            if (recordingBar != null) { recordingBar.Finishing = true; recordingBar.Close(); recordingBar = null; }
            session = null; finishing = null; Refresh();
        }
    }
    private async void DiscardRecording(bool restart)
    {
        if (session == null || finishing != null) return;
        var recording = session; recordingBar?.ShowFinishing(); finishing = recording.DiscardAsync();
        try { await finishing; }
        catch (Exception error) { if (!Exiting) Ui.Error(Window, error, L.Get("丢弃录制")); }
        finally
        {
            if (recordingBar != null) { recordingBar.Finishing = true; recordingBar.Close(); recordingBar = null; }
            session = null; finishing = null;
        }
        if (!Exiting) { Show(); if (restart) Record(); }
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
    public void Dispose() { globalMouse?.Dispose(); gestureOverlay?.Close(); trayClickTimer.Stop(); hotkeys.Dispose(); tray.Dispose(); icon.Dispose(); }
}
