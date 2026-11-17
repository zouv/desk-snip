using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DeskSnip.App.Imaging;
using DeskSnip.App.Windows;
using DeskSnip.Core;

namespace DeskSnip.App;

internal sealed partial class AppController
{
    private bool persistingPins;
    private readonly HashSet<string> selectedPins = new HashSet<string>();
    private readonly System.Windows.Threading.DispatcherTimer pinSaveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly System.Windows.Threading.DispatcherTimer pinSelectionTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
    private static readonly uint PinProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
    private bool pinSelectionMouseDown;
    private void OpenPin(BitmapSource image, PinRecord record, bool restored, PixelRect region = default, BitmapSource? annotationBase = null, AnnotationDocument? annotations = null, bool activate = false)
    {
        if (!restored) { record.Topmost = Store.Settings.PinDefaultTopmost; record.Opacity = Store.Settings.PinDefaultOpacity / 100d; }
        var originalPath = Workspace.ImagePath(record.Id, true);
        var window = new PinWindow(image, record, changed => Workspace.SaveImage(record.Id, output => ImageFiles.WritePng(changed, output)), restored,
            File.Exists(originalPath) ? ImageFiles.Load(originalPath) : image, Store.Settings, region, annotationBase, annotations);
        window.RememberToolRequested = kind =>
        {
            if (Exiting || !Store.Settings.MarkRememberPinTool) return;
            try { var next = Store.Settings.Clone(); next.MarkLastPinTool = kind.ToString(); Store.SaveSettings(next); }
            catch (Exception) { Window.ShowNotice(L.Get("标注工具记忆未保存，已编辑图片仍保留。")); }
        };
        pins.Add(window);
        window.RecognizeRequested = imageToRead => RecognizeWithDialog(imageToRead, !Store.Settings.OcrDirectCopy && Store.Settings.OcrDialogOnPin);
        window.CopyTextRequested = imageToRead => RecognizeWithDialog(imageToRead, !Store.Settings.OcrDirectCopy && Store.Settings.OcrDialogOnCopy);
        window.DestroyRequested = () => DestroyPinRecords(new[] { window.Id });
        window.ServiceRecognitionRequested = RecognizeService;
        window.ServiceOperationRequested = RecognizeService;
        window.SettingsRequested = () => Store.Settings; window.QuickSaveRequested = QuickSaveImage;
        window.CopyRequested = imageToCopy => { CopyImage(imageToCopy); SaveAutomatically(imageToCopy, "Copy"); };
        window.SaveRequested = SaveImageManual;
        window.PresentationChanged = () =>
        {
            if (persistingPins || Exiting) return;
            pinSaveTimer.Stop(); pinSaveTimer.Start(); Refresh();
        };
        window.SelectRequested = (pin, multiple) =>
        {
            if (!multiple && !selectedPins.Contains(pin.Id)) selectedPins.Clear();
            if (multiple && selectedPins.Contains(pin.Id)) selectedPins.Remove(pin.Id); else selectedPins.Add(pin.Id);
            foreach (var active in pins) active.Selected = selectedPins.Contains(active.Id);
            RefreshPinSelectionTimer();
        };
        window.MoveRequested = (pin, dx, dy) =>
        {
            foreach (var active in pins.Where(p => p == pin || (selectedPins.Contains(pin.Id) && selectedPins.Contains(p.Id)))) active.MoveByPhysical(dx, dy);
        };
        window.Closed += (_, _) =>
        {
            pins.Remove(window); selectedPins.Remove(window.Id); RefreshPinSelectionTimer();
            if (!Exiting)
            {
                try { Workspace.Update(window.CaptureRecord(record)); Workspace.Close(record.Id); }
                catch (Exception error) { Ui.Error(Window, error, L.Get("保存钉图状态")); }
            }
            Refresh();
        };
        pinSaveTimer.Tick -= SavePinsTick; pinSaveTimer.Tick += SavePinsTick;
        window.Show(); ApplyPinVisibility(); Refresh();
        if (activate && window.IsVisible) { window.Activate(); window.Focus(); Keyboard.Focus(window); }
    }
    private void SavePinsTick(object? sender, EventArgs e) { pinSaveTimer.Stop(); PersistPins(); }
    private void RefreshPinSelectionTimer()
    {
        if (Exiting || selectedPins.Count == 0) StopPinSelectionWatch();
        else StartPinSelectionWatch();
    }
    private void StartPinSelectionWatch()
    {
        if (pinSelectionTimer.IsEnabled) return;
        pinSelectionMouseDown = (Native.GetAsyncKeyState(0x01) & 0x8000) != 0;
        pinSelectionTimer.Tick -= PinSelectionTick; pinSelectionTimer.Tick += PinSelectionTick;
        pinSelectionTimer.Start();
    }
    private void StopPinSelectionWatch()
    {
        pinSelectionTimer.Stop(); pinSelectionTimer.Tick -= PinSelectionTick; pinSelectionMouseDown = false;
    }
    private void PinSelectionTick(object? sender, EventArgs e)
    {
        var state = Native.GetAsyncKeyState(0x01);
        var down = (state & 0x8000) != 0;
        var pressed = down && !pinSelectionMouseDown || !down && (state & 0x0001) != 0;
        pinSelectionMouseDown = down;
        if (!pressed || selectedPins.Count == 0 || pins.Any(pin => pin.InlineEditing)) return;
        Native.GetCursorPos(out var point);
        Native.GetWindowThreadProcessId(Native.WindowFromPoint(point), out var processId);
        if (processId == PinProcessId) return;
        ClearPinSelection();
    }
    private void ClearPinSelection()
    {
        if (selectedPins.Count == 0) return;
        selectedPins.Clear();
        foreach (var pin in pins) pin.Selected = false;
        StopPinSelectionWatch();
        Refresh();
    }
    private void PersistPins()
    {
        pinSaveTimer.Stop(); persistingPins = true;
        try
        {
            var records = Workspace.Records.ToDictionary(p => p.Id);
            Workspace.UpdateMany(pins.Where(p => records.ContainsKey(p.Id)).Select(p => p.CaptureRecord(records[p.Id])).ToArray());
        }
        catch (Exception error) { if (!Exiting) Ui.Error(Window, error, L.Get("保存钉图布局")); }
        finally { persistingPins = false; }
    }
    private void RestoreActivePins()
    {
        foreach (var record in Workspace.Records.Where(p => p.Active))
        {
            try { OpenPin(ImageFiles.Load(Workspace.ImagePath(record.Id)), record, true); }
            catch (Exception) { Window.ShowNotice(L.Get("部分钉图无法恢复，原记录和图片保留。")); }
        }
    }
    private void ApplyPinVisibility()
    {
        foreach (var pin in pins)
        {
            var show = !Workspace.Hidden && pin.WantedVisible && pin.GroupId == Workspace.CurrentGroupId;
            if (show && !pin.IsVisible) { pin.Show(); if (Store.Settings.PinRescueOnShow) pin.Rescue(); } else if (!show) pin.Hide();
        }
    }
    internal void RestorePinRecord(string id)
    {
        try
        {
            var record = Workspace.Restore(id); var active = pins.FirstOrDefault(p => p.Id == id);
            if (active == null) OpenPin(ImageFiles.Load(Workspace.ImagePath(id)), record, true);
            else { active.WantedVisible = true; ApplyPinVisibility(); }
            Refresh();
        }
        catch (Exception error) { Ui.Error(Window, error, L.Get("恢复钉图")); }
    }
    internal void PinManager() => new PinManagerWindow(this) { Owner = Window.IsVisible ? Window : null }.Show();
    internal void ChoosePinGroup(string id) { Workspace.SelectGroup(id); ApplyPinVisibility(); Refresh(); }
    internal void MovePinGroup(IEnumerable<string> ids, string groupId)
    {
        PersistPins(); var selected = new HashSet<string>(ids); Workspace.MoveToGroup(selected, groupId);
        foreach (var pin in pins.Where(p => selected.Contains(p.Id))) pin.GroupId = groupId;
        ApplyPinVisibility(); Refresh();
    }
    internal void DeletePinGroup(string id, bool clear)
    {
        var selected = Workspace.Records.Where(p => p.GroupId == id).Select(p => p.Id).ToArray();
        if (clear) DestroyPinRecords(selected);
        Workspace.DeleteGroup(id, clear);
        foreach (var pin in pins.Where(p => p.GroupId == id)) pin.GroupId = "default";
        ApplyPinVisibility(); Refresh();
    }
    internal void ClosePinRecords(IEnumerable<string> ids)
    {
        foreach (var id in ids.ToArray())
        {
            var active = pins.FirstOrDefault(p => p.Id == id);
            if (active != null) active.RequestClose(); else if (Workspace.Records.Any(p => p.Id == id && p.Active)) Workspace.Close(id);
        }
        Refresh();
    }
    internal void HidePinRecords(IEnumerable<string> ids, bool hide)
    {
        var selected = new HashSet<string>(ids); var records = Workspace.Records.Where(p => selected.Contains(p.Id)).ToArray();
        foreach (var record in records) record.Visible = !hide;
        Workspace.UpdateMany(records);
        foreach (var pin in pins.Where(p => selected.Contains(p.Id))) pin.WantedVisible = !hide;
        ApplyPinVisibility(); Refresh();
    }
    internal void MovePinRecords(IEnumerable<string> ids, int dx, int dy)
    {
        var selected = new HashSet<string>(ids);
        foreach (var pin in pins.Where(p => selected.Contains(p.Id) && p.CanMove)) { pin.MoveByPhysical(dx, dy); pin.Rescue(); }
        PersistPins();
    }
    internal void RescuePinRecords(IEnumerable<string> ids)
    {
        var selected = new HashSet<string>(ids); foreach (var pin in pins.Where(p => selected.Contains(p.Id))) pin.Rescue(); PersistPins();
    }
    internal void DestroyPinRecords(IEnumerable<string> ids)
    {
        var selected = ids.ToArray();
        foreach (var pin in pins.Where(p => selected.Contains(p.Id)).ToArray()) pin.CloseForDestruction();
        Workspace.Destroy(selected); Refresh();
    }
    internal void SavePinRecords(IEnumerable<string> ids, string folder)
    {
        var selected = new HashSet<string>(ids); var target = new ManagedDirectory(folder);
        foreach (var record in Workspace.Records.Where(p => selected.Contains(p.Id)))
            AtomicFile.Write(target.Resolve("DeskSnip-" + record.Id + ".png"), output => { using (var input = File.OpenRead(Workspace.ImagePath(record.Id))) input.CopyTo(output); });
    }
    internal void ConfigurePins(bool restoreOnStart, int closedLimit)
    {
        var next = Store.Settings.Clone(); next.PinRestoreOnStart = restoreOnStart; next.PinHistoryLimit = closedLimit; next.Validate();
        Workspace.Configure(restoreOnStart, closedLimit); Store.SaveSettings(next);
    }
    internal void RenamePinRecord(string id, string title)
    {
        var record = Workspace.Records.Single(p => p.Id == id); record.Title = title.Trim(); Workspace.Update(record);
        pins.FirstOrDefault(p => p.Id == id)?.Rename(record.Title); Refresh();
    }
}
