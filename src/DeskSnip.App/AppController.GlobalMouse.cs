using System;
using System.Linq;
using System.Threading.Tasks;
using DeskSnip.App.Windows;
using DeskSnip.Core;

namespace DeskSnip.App;

internal sealed partial class AppController
{
    private GlobalMouse? globalMouse;
    private GlobalMouseOverlay? gestureOverlay;
    private bool gestureBusy;
    private bool gestureMainVisible;
    private EditorWindow[] gestureEditors = Array.Empty<EditorWindow>();
    private void ApplyGlobalMouse()
    {
        globalMouse?.Dispose(); globalMouse = null; CancelGlobalGesture();
        if (Store.Settings.GlobalMouseBindings == null || !Store.Settings.GlobalMouseBindings.Any(b => b.Gesture != "None")) return;
        try { globalMouse = new GlobalMouse(Store.Settings, Window.Dispatcher, StartGlobalGesture, region => gestureOverlay?.SetRegion(region), FinishGlobalGesture, CancelGlobalGesture, () => !Exiting && !selecting && !gestureBusy && !startingRecording && session == null); }
        catch (Exception error) { Window.ShowNotice(error.Message); }
    }
    private void StartGlobalGesture()
    {
        if (Exiting || selecting || gestureBusy || startingRecording) return;
        gestureBusy = true; gestureMainVisible = Window.IsVisible; gestureEditors = editors.Where(e => e.IsVisible).ToArray(); Window.Hide(); foreach (var editor in gestureEditors) editor.Hide();
        if (Store.Settings.GlobalMouseShowMask) { gestureOverlay = new GlobalMouseOverlay(Store.Settings); gestureOverlay.Show(); }
    }
    private void CancelGlobalGesture()
    {
        gestureOverlay?.Close(); gestureOverlay = null;
        if (gestureBusy && !Exiting) { if (gestureMainVisible) Window.Show(); foreach (var editor in gestureEditors) if (editors.Contains(editor)) editor.Show(); }
        gestureEditors = Array.Empty<EditorWindow>(); gestureBusy = false;
    }
    private async void FinishGlobalGesture(string action, PixelRect region)
    {
        if (!gestureBusy) return; gestureOverlay?.Close(); gestureOverlay = null;
        try
        {
            await Task.Delay(120); if (Exiting) return; region = region.Intersect(ScreenCapture.DesktopBounds); if (region.IsEmpty) return;
            Profiles.Remember(region, Store.Settings.ScreenshotAreaHistoryLimit);
            if (action == "LongScreenshot") { CancelGlobalGesture(); LongScreenshot(region); return; }
            if (action == "Gif") { CancelGlobalGesture(); RecordRegion(region, "GIF"); return; }
            var image = ScreenCapture.Capture(region, Store.Settings.ScreenshotCursorVisible);
            if (action == "Translate" || action == "Formula") { var window = new RecognitionServiceWindow(image, Store.Settings, Store.Root, archiveSource: SaveAutomatically); window.SelectOperation(action == "Translate" ? "translation" : "formula"); window.Show(); }
            else if (action == "Ocr") { SaveAutomatically(image, "Capture"); RecognizeWithDialog(image, !Store.Settings.OcrDirectCopy && Store.Settings.OcrDialogOnCopy); }
            else CompleteCapture(image, (CaptureAction)Enum.Parse(typeof(CaptureAction), action), region);
        }
        catch (Exception error) { if (!Exiting) Ui.Error(Window, error, L.Get("全局鼠标截图")); }
        finally { CancelGlobalGesture(); }
    }
}
