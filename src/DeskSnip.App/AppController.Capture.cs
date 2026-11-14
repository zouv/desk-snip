using System;
using System.Linq;
using System.Threading.Tasks;
using DeskSnip.App.Imaging;
using DeskSnip.App.Windows;
using DeskSnip.Core;

namespace DeskSnip.App;

internal sealed partial class AppController
{
    internal void CapturePresets() => new CapturePresetWindow(this).Show();
    internal async void UseCapturePreset(CapturePreset preset)
    {
        if (selecting || Exiting) return;
        var visible = Window.IsVisible;
        try
        {
            Window.Hide();
            if (preset.DelaySeconds > 0 && new CountdownWindow(preset.DelaySeconds).ShowDialog() != true) return;
            if (Exiting) return;
            var bounds = ScreenCapture.DesktopBounds; var initial = preset.Bounds;
            if (!preset.FixedPosition) initial = new PixelRect(bounds.X + Math.Max(0, (bounds.Width - preset.Width) / 2), bounds.Y + Math.Max(0, (bounds.Height - preset.Height) / 2), preset.Width, preset.Height);
            if (!initial.Intersect(bounds).Equals(initial)) throw new ArgumentException(L.Get("预设选区超出当前桌面，请调整位置或尺寸。"));
            if (preset.FixedPosition && (preset.Shape == CaptureShape.Rectangle || preset.Shape == CaptureShape.Ellipse))
            {
                selecting = true; var open = editors.Where(e => e.IsVisible).ToArray(); foreach (var editor in open) editor.Hide();
                try { await Task.Delay(150); if (Exiting) return; var image = CaptureMasks.Apply(ScreenCapture.Capture(initial, preset.Cursor), preset.Shape); Profiles.Remember(initial, Store.Settings.ScreenshotAreaHistoryLimit); CompleteCapture(image, CaptureAction.Edit, initial); }
                finally { selecting = false; if (!Exiting) foreach (var editor in open) if (editors.Contains(editor)) editor.Show(); }
            }
            else { var selected = await Select(false, initial, preset.Cursor, preset.Shape); if (selected.Image != null) CompleteCapture(selected.Image, selected.Action, selected.Region); }
        }
        catch (Exception error) { Ui.Error(Window, error, L.Get("预设截图")); }
        finally { if (visible && !Exiting) Window.Show(); }
    }
}
