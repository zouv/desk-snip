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

internal sealed class AppController : IDisposable
{
    private readonly List<PinWindow> pins = new List<PinWindow>();
    private readonly LinkedList<BitmapSource> closedPins = new LinkedList<BitmapSource>();
    private readonly List<EditorWindow> editors = new List<EditorWindow>();
    private readonly Forms.NotifyIcon tray;
    private readonly Icon icon;
    private readonly Hotkeys hotkeys;
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
    internal bool CanRestorePin => closedPins.Count > 0;
    internal string[] PinDescriptions => pins.Select((p, i) => $"{i + 1}. {p.Description}").ToArray();
    internal AppController(string dataRoot)
    {
        Store = new LocalStore(dataRoot);
        Window = new MainWindow(this); System.Windows.Application.Current.MainWindow = Window; Window.Show();
        hotkeys = new Hotkeys(new WindowInteropHelper(Window).Handle);
        icon = TrayIcon(); tray = new Forms.NotifyIcon { Icon = icon, Text = "DeskSnip · 截图 / 钉图 / 录屏", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        Add(menu, "打开 DeskSnip", Show); Add(menu, "截取屏幕", Capture); Add(menu, "钉住剪贴板", PinClipboard); Add(menu, "录制 / 暂停", Record);
        menu.Items.Add(new Forms.ToolStripSeparator());
        Add(menu, "显示 / 隐藏全部钉图", TogglePins); Add(menu, "解除全部穿透 / 锁定", RecoverPins); Add(menu, "恢复最近关闭的钉图", RestorePin); Add(menu, "设置", Settings);
        menu.Items.Add(new Forms.ToolStripSeparator()); Add(menu, "退出", RequestExit);
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Show(); ApplyHotkeys(); Refresh();
        if (!string.IsNullOrEmpty(Store.LoadIssue)) Window.ShowNotice(Store.LoadIssue);
    }
    private void Add(Forms.ContextMenuStrip menu, string label, Action action) => menu.Items.Add(label, null, (_, _) => System.Windows.Application.Current.Dispatcher.BeginInvoke(action));
    private static Icon TrayIcon()
    {
        using (var bitmap = new Bitmap(32, 32))
        {
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(System.Drawing.Color.FromArgb(0, 137, 120));
                using (var pen = new System.Drawing.Pen(System.Drawing.Color.White, 2))
                {
                    graphics.DrawRectangle(pen, 7, 8, 18, 15); graphics.DrawLine(pen, 4, 5, 11, 5); graphics.DrawLine(pen, 4, 5, 4, 12);
                    graphics.DrawLine(pen, 21, 27, 28, 27); graphics.DrawLine(pen, 28, 20, 28, 27);
                }
            }
            var handle = bitmap.GetHicon();
            try { using (var temporary = Icon.FromHandle(handle)) return (Icon)temporary.Clone(); }
            finally { Native.DestroyIcon(handle); }
        }
    }
    internal void Show()
    {
        if (Exiting) return;
        Window.Show(); Window.WindowState = WindowState.Normal; Window.Activate(); Refresh();
    }
    internal async void Capture()
    {
        if (selecting || Exiting) return;
        try { var selected = await Select(false); if (selected.Image != null) Edit(selected.Image); }
        catch (Exception e) { Ui.Error(Window, e, "截图"); Show(); }
    }
    private async Task<(BitmapSource? Image, PixelRect Region)> Select(bool recording)
    {
        selecting = true;
        var wasVisible = Window.IsVisible; var visibleEditors = editors.Where(e => e.IsVisible).ToArray();
        Window.Hide(); foreach (var editor in visibleEditors) editor.Hide();
        try
        {
            await Task.Delay(180); if (Exiting) return (null, default);
            var bounds = ScreenCapture.DesktopBounds; var frozen = ScreenCapture.Capture(bounds, false);
            selectionWindow = new CaptureWindow(frozen, bounds, recording);
            if (selectionWindow.ShowDialog() != true) return (null, default);
            var region = selectionWindow.Selection;
            return (ScreenCapture.Crop(frozen, bounds, region), region);
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
    {
        var editor = new EditorWindow(image, Pin, Remember); editors.Add(editor);
        editor.Closed += (_, _) => editors.Remove(editor); editor.Show();
    }
    private void Pin(BitmapSource image)
    {
        var window = new PinWindow(image, Edit); pins.Add(window); window.PresentationChanged = Refresh;
        window.Closed += (_, _) =>
        {
            pins.Remove(window);
            if (!Exiting) { closedPins.AddFirst(window.Bitmap); while (closedPins.Count > 10) closedPins.RemoveLast(); }
            Refresh();
        };
        window.Show(); Refresh();
    }
    internal void PinClipboard()
    {
        if (Exiting) return;
        try
        {
            var image = Clipboard.GetImage();
            if (image == null) { Show(); Window.ShowNotice("剪贴板里没有图片。可以先截图、复制图片，或使用“打开图片”。"); return; }
            var normalized = ImageFiles.Normalize(image); Remember(normalized); Pin(normalized);
        }
        catch (Exception e) { Ui.Error(Window, e, "钉住剪贴板"); }
    }
    internal void OpenImage()
    {
        var dialog = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*" };
        if (dialog.ShowDialog(Window) != true) return;
        try { Edit(ImageFiles.Load(dialog.FileName)); } catch (Exception e) { Ui.Error(Window, e, "打开图片"); }
    }
    internal void OpenHistory(HistoryEntry? entry)
    {
        if (entry == null) return;
        try
        {
            if (entry.Kind == "Image") Edit(ImageFiles.Load(Store.HistoryPath(entry)));
            else new PlaybackWindow(Store.HistoryPath(entry), entry.Kind) { Owner = Window.IsVisible ? Window : null }.Show();
        }
        catch (Exception e) { Ui.Error(Window, e, "打开历史"); }
    }
    internal void PinHistory(HistoryEntry? entry)
    {
        if (entry == null) return;
        if (entry.Kind != "Image") { Window.ShowNotice("基础版钉图支持静态图片；录屏可在历史中播放或另存为。"); return; }
        try { Pin(ImageFiles.Load(Store.HistoryPath(entry))); } catch (Exception e) { Ui.Error(Window, e, "历史钉图"); }
    }
    internal void SaveHistory(HistoryEntry? entry)
    {
        if (entry == null) return;
        var ext = entry.Kind == "Image" ? "png" : entry.Kind.ToLowerInvariant();
        var dialog = new SaveFileDialog { Filter = $"{entry.Kind}|*.{ext}", FileName = "DeskSnip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "." + ext, AddExtension = true };
        if (dialog.ShowDialog(Window) != true) return;
        try { AtomicFile.Write(dialog.FileName, s => { using (var source = File.OpenRead(Store.HistoryPath(entry))) source.CopyTo(s); }); }
        catch (Exception e) { Ui.Error(Window, e, "历史另存为"); }
    }
    internal void RecoverPins() { foreach (var pin in pins.ToArray()) pin.RestoreInteraction(); Refresh(); }
    internal void TogglePins()
    {
        var hide = pins.Any(p => p.IsVisible);
        foreach (var pin in pins) { if (hide) pin.Hide(); else pin.Show(); }
    }
    internal async void ExportGif(HistoryEntry? entry)
    {
        if (entry == null || Exiting) return;
        if (exporting != null) { Window.ShowNotice("GIF 导出正在进行，请等它完成。"); return; }
        if (entry.Kind != "MP4") { Window.ShowNotice("请先选择一条 MP4 录制，或在开始录制时直接选择 GIF 格式。"); return; }
        var dialog = new SaveFileDialog { Filter = "GIF 动图|*.gif", FileName = "DeskSnip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".gif", AddExtension = true };
        if (dialog.ShowDialog(Window) != true) return;
        try
        {
            Window.ShowNotice("正在导出 GIF，请稍候…");
            exportCancellation = new CancellationTokenSource();
            var cancellation = exportCancellation.Token;
            var source = Store.HistoryPath(entry);
            exporting = Task.Run(() => GifExporter.Export(source, dialog.FileName, 10, cancellation));
            await exporting;
            if (!Exiting) Window.ShowNotice("GIF 已导出（10 FPS、256 色）。");
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!Exiting) Ui.Error(Window, e, "导出 GIF"); }
        finally { exportCancellation?.Dispose(); exportCancellation = null; exporting = null; }
    }
    internal void RestorePin()
    {
        if (closedPins.First == null) return;
        var image = closedPins.First.Value; closedPins.RemoveFirst(); Pin(image);
    }
    internal void Settings()
    {
        Show();
        new SettingsWindow(Store.Settings, value =>
        {
            var parsed = new[] { value.CaptureHotkey, value.PinHotkey, value.RecordHotkey }.Select(Hotkeys.Parse).ToArray();
            if (parsed.Distinct().Count() != 3) throw new ArgumentException("三个快捷键不能相同。");
            Store.SaveSettings(value); ApplyHotkeys(); Refresh();
        }) { Owner = Window }.ShowDialog();
    }
    private void ApplyHotkeys()
    {
        try { Window.ShowNotice(hotkeys.Apply(Store.Settings, Capture, PinClipboard, Record)); }
        catch (ArgumentException e) { Window.ShowNotice(e.Message + " 请在设置中修改。"); }
    }
    internal async void Record()
    {
        if (Exiting || selecting || finishing != null || startingRecording) return;
        if (session != null) { recordingBar?.TogglePause(); return; }
        startingRecording = true;
        try
        {
            var selected = await Select(true); if (selected.Image == null || Exiting) return;
            var setup = new RecordingSetup(selected.Region, Store.Settings, DefaultRecordingPath);
            if (setup.ShowDialog() != true) return;
            var recording = new RecordingSession(setup.OutputPath, selected.Region, setup.Format, setup.Fps, setup.CursorIncluded);
            session = recording; Window.Hide();
            await recording.StartAsync();
            if (Exiting) { await recording.StopAsync(); return; }
            recordingBar = new RecordingBar(recording, () => { _ = FinishRecording(); }); recordingBar.Show(); _ = ObserveRecording(recording);
        }
        catch (Exception e) { session = null; if (!Exiting) { Ui.Error(Window, e, "开始录制"); Show(); } }
        finally { startingRecording = false; }
    }
    private string DefaultRecordingPath(string format)
    {
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
            Ui.Error(Window, e, "录制中断"); Show();
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
            Store.ImportRecording(recording.Path, recording.Format, recording.Region.Width, recording.Region.Height);
            if (!Exiting) { Show(); Window.ShowNotice("录制已保存，历史中可播放和另存为。"); }
        }
        catch (Exception e)
        {
            if (Exiting) exitRecordingError = e;
            else { Show(); Ui.Error(Window, e, "保存录制"); }
        }
        finally
        {
            if (recordingBar != null) { recordingBar.Finishing = true; recordingBar.Close(); recordingBar = null; }
            session = null; finishing = null; Refresh();
        }
    }
    internal async void RequestExit()
    {
        if (Exiting) return; Exiting = true;
        if (selectionWindow != null) selectionWindow.Close();
        exportCancellation?.Cancel();
        if (exporting != null) { try { await exporting; } catch (Exception) { } }
        await FinishRecording();
        if (exitRecordingError != null) Ui.Error(Window, exitRecordingError, "录制未能完成保存");
        foreach (var editor in editors.ToArray()) editor.Close(); foreach (var pin in pins.ToArray()) pin.Close();
        tray.Visible = false; Window.Close(); System.Windows.Application.Current.Shutdown();
    }
    private void Refresh() { if (!Exiting) Window.Refresh(); }
    public void Dispose() { hotkeys.Dispose(); tray.Dispose(); icon.Dispose(); }
}
