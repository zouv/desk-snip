using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using DeskSnip.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace DeskSnip.App;

internal sealed partial class AppController
{
    private Dictionary<string, Action> ActionHandlers() => new Dictionary<string, Action>
    {
        ["Capture"] = Capture, ["CustomCapture"] = CapturePresets, ["CopyCapture"] = () => CaptureAs(CaptureAction.Copy), ["Pin"] = PinClipboard,
        ["RestorePin"] = RestorePin, ["SwitchPinGroup"] = SwitchPinGroup, ["ClosePins"] = () => ClosePinRecords(pins.Select(p => p.Id).ToArray()),
        ["TogglePins"] = TogglePins, ["ToggleOtherPins"] = ToggleOtherPins, ["PinFile"] = ChoosePinFile, ["TogglePenetration"] = TogglePinPenetration,
        ["ToggleHotkeys"] = ToggleHotkeys, ["Record"] = Record, ["QuickCapture"] = QuickCapture, ["LongScreenshot"] = StartLongScreenshot,
        ["OcrClipboard"] = OcrClipboard, ["RecoverPins"] = RecoverPins, ["Show"] = Show
    };
    private void ApplyTrayActions()
    {
        var settings = Store.Settings; var handlers = ActionHandlers(); var menu = new Forms.ContextMenuStrip();
        foreach (var name in DesktopActions.DefaultNames.Where(n => settings.HiddenDefaultActions == null || !settings.HiddenDefaultActions.Contains(n)))
        {
            var configured = settings.ActionBindings.FirstOrDefault(b => b.Action == name);
            var visible = configured?.TrayMenu ?? name != "SwitchPinGroup" && name != "ToggleOtherPins" && name != "PinFile" && name != "TogglePenetration" && name != "ToggleHotkeys";
            if (visible) Add(menu, string.IsNullOrEmpty(configured?.Title) ? DesktopActions.Label(name) : configured!.Title, handlers[name]);
        }
        foreach (var binding in settings.ActionBindings.Where(b => b.TrayMenu && !DesktopActions.DefaultNames.Contains(b.Action))) Add(menu, binding.Title.Length == 0 ? DesktopActions.Label(binding.Action) : binding.Title, handlers[binding.Action]);
        menu.Items.Add(new Forms.ToolStripSeparator()); Add(menu, L.Get("打开主界面"), Show); Add(menu, L.Get("设置"), Settings); Add(menu, L.Get("解除全部穿透 / 锁定"), RecoverPins);
        Add(menu, L.Get("显示录制工具栏"), () => recordingBar?.Show()); Add(menu, L.Get("停止录制"), () => { _ = FinishRecording(); }); menu.Items.Add(new Forms.ToolStripSeparator()); Add(menu, L.Get("退出"), RequestExit);
        var old = tray.ContextMenuStrip; tray.ContextMenuStrip = menu; old?.Dispose();
    }
    private void SwitchPinGroup()
    {
        var groups = Workspace.Groups.ToArray(); var index = Array.FindIndex(groups, g => g.Id == Workspace.CurrentGroupId); ChoosePinGroup(groups[(index + 1) % groups.Length].Id);
    }
    private void ToggleOtherPins()
    {
        if (selectedPins.Count == 0) { Window.ShowNotice(L.Get("请先选择一张或多张钉图。")); return; }
        var others = pins.Where(p => !selectedPins.Contains(p.Id)).ToArray(); HidePinRecords(others.Select(p => p.Id).ToArray(), others.Any(p => p.WantedVisible));
    }
    private void ChoosePinFile()
    {
        var dialog = new OpenFileDialog { Filter = L.Get("图像文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff"), Title = L.Get("选择需要钉住的图像") }; if (dialog.ShowDialog(Window) == true) PinFile(dialog.FileName);
    }
    private void TogglePinPenetration()
    {
        var enabled = pins.Any(p => !p.Presentation.ClickThrough); foreach (var pin in pins.ToArray()) pin.SetClickThrough(enabled);
    }
    private void ToggleHotkeys()
    {
        hotkeysDisabled = !hotkeysDisabled; ApplyHotkeys(); Window.ShowNotice(hotkeysDisabled ? L.Get("其他全局快捷键已禁用，禁用 / 恢复快捷键仍可使用。") : L.Get("全局快捷键已恢复。"));
    }
    private async void CaptureAs(CaptureAction action)
    {
        if (selecting || Exiting) return;
        try { var selected = await Select(false); if (selected.Image != null) CompleteCapture(selected.Image, action, selected.Region, selected.AnnotationBase, selected.Annotations); }
        catch (Exception error) { Ui.Error(Window, error, L.Get("截图动作")); }
    }
}
