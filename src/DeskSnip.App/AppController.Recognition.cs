using System;
using System.Windows.Media.Imaging;
using DeskSnip.Core;

namespace DeskSnip.App;

internal sealed partial class AppController
{
    private void Recognize(BitmapSource image)
        => RecognizeWithDialog(image, !Store.Settings.OcrDirectCopy && Store.Settings.OcrDialogOnScreenshot);
    private async void RecognizeWithDialog(BitmapSource image, bool showDialog)
    {
        var settings = Store.Settings.Clone(); SaveAutomatically(image, "Ocr");
        if (showDialog) { new OcrWindow(image, settings.LocalOcrLanguage, settings.OcrCloseAfterCopyAll, settings).Show(); return; }
        try
        {
            var lines = await Recognition.LocalOcr.RecognizeAsync(image, settings.LocalOcrLanguage, System.Threading.CancellationToken.None); var text = RecognitionText.Format(lines, settings.OcrTextLayout, settings.OcrRemoveSpaces, settings.OcrPunctuation);
            if (Exiting) return;
            if (string.IsNullOrEmpty(text)) { new OcrWindow(image, settings.LocalOcrLanguage, settings.OcrCloseAfterCopyAll, settings).Show(); return; }
            System.Windows.Clipboard.SetText(text); Window.ShowNotice(L.Get("识别文字已复制。"));
        }
        catch (Exception) { if (!Exiting) new OcrWindow(image, settings.LocalOcrLanguage, settings.OcrCloseAfterCopyAll, settings).Show(); }
    }
    private void LongScreenshot(PixelRect region)
    {
        var visible = Window.IsVisible; var openEditors = editors.FindAll(e => e.IsVisible);
        Window.Hide(); foreach (var editor in openEditors) editor.Hide();
        var bar = new LongScreenshotBar(region, Store.Settings.Clone(), CompleteLongScreenshot, () => { });
        bar.Closed += (_, _) => { if (Exiting) return; if (visible) Window.Show(); foreach (var editor in openEditors) if (editors.Contains(editor)) editor.Show(); };
        bar.Show();
    }
    private void CompleteLongScreenshot(System.Windows.Media.Imaging.BitmapSource image, CaptureAction action)
    {
        SaveAutomatically(image, "LongScreenshot");
        try
        {
            switch (action)
            {
                case CaptureAction.Copy: OnImageOutput(image, "Copy"); break;
                case CaptureAction.Save: SaveImageManual(image, Window); break;
                case CaptureAction.Pin: Pin(image); break;
                case CaptureAction.Edit: Edit(image); break;
                case CaptureAction.Ocr: Recognize(image); break;
                case CaptureAction.ServiceRecognition: RecognizeService(image); break;
            }
        }
        catch (Exception error) { Ui.Error(Window, error, L.Get("长截图输出")); }
    }
    internal async void StartLongScreenshot()
    {
        if (selecting || Exiting) return;
        try { var selected = await Select(true); if (selected.Image != null) LongScreenshot(selected.Region); }
        catch (Exception error) { Ui.Error(Window, error, L.Get("开始长截图")); }
    }
    internal void RecognizeHistory(HistoryEntry? entry)
    {
        if (entry?.Kind != "Image") { Window.ShowNotice(L.Get("请选择一条图片记录进行文字识别。")); return; }
        try { Recognize(Imaging.ImageFiles.Load(Store.HistoryPath(entry))); }
        catch (Exception error) { Ui.Error(Window, error, L.Get("本地文字识别")); }
    }
    private void RecognizeService(BitmapSource image)
        => RecognizeService(image, null);
    private void RecognizeService(BitmapSource image, string? operation)
    {
        if (Exiting) return;
        try { var window = new RecognitionServiceWindow(image, Store.Settings, Store.Root, archiveSource: SaveAutomatically); if (operation != null) window.SelectOperation(operation); window.Show(); }
        catch (Exception error) { Ui.Error(Window, error, L.Get("打开联网识别")); }
    }
    internal void RecognizeServiceHistory(HistoryEntry? entry)
    {
        if (entry?.Kind != "Image") { Window.ShowNotice(L.Get("请选择图片记录，或从“联网识别 / 翻译”输入文字。")); return; }
        try { RecognizeService(Imaging.ImageFiles.Load(Store.HistoryPath(entry))); }
        catch (Exception error) { Ui.Error(Window, error, L.Get("打开联网识别")); }
    }
    internal void OpenRecognitionService(bool restore = false)
    {
        if (Exiting) return;
        try
        {
            var image = restore ? null : System.Windows.Clipboard.GetImage();
            var text = !restore && image == null && System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : "";
            new RecognitionServiceWindow(image, Store.Settings, Store.Root, restore, text, SaveAutomatically).Show();
        }
        catch (Exception error) { Ui.Error(Window, error, L.Get("打开联网识别")); }
    }
}
