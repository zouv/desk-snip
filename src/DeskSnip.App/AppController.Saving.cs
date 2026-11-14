using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using DeskSnip.App.Imaging;
using DeskSnip.Core;

namespace DeskSnip.App;

internal sealed partial class AppController
{
    private string? quickSaveNoticePath;
    private static string Extension(string format) => format == "JPEG" ? ".jpg" : format == "TIFF" ? ".tiff" : "." + format.ToLowerInvariant();
    private string SaveDirectory => string.IsNullOrEmpty(Store.Settings.QuickSaveDirectory) ? new ManagedDirectory(Store.Root).Resolve("quick-saves") : Store.Settings.QuickSaveDirectory;
    private string Basename(BitmapSource image, string kind) => SaveNaming.Expand(Store.Settings.SaveNameTemplate, DateTime.Now, image.PixelWidth, image.PixelHeight, kind);
    private string SaveImageQuick(BitmapSource image)
    {
        var settings = Store.Settings;
        var path = SaveNaming.ChoosePath(SaveDirectory, Basename(image, "Image"), Extension(settings.QuickSaveFormat), settings.QuickSaveOverwrite); ImageFiles.Save(image, path, settings.JpegQuality, settings.SaveExif); Remember(image); AfterManualOrQuickSave(path); SaveAutomatically(image, "Save"); PlaySound(settings.SoundQuickSave);
        Window.ShowNotice(L.Get("图片已快速保存。"));
        if (settings.QuickSaveNotification) { quickSaveNoticePath = path; tray.ShowBalloonTip(2500, "DeskSnip", L.Join(L.Get("截图已保存，点击通知"),(settings.QuickSaveNotificationAction == "Locate" ? L.Get("定位文件") : L.Get("执行保存后的动作"))), System.Windows.Forms.ToolTipIcon.Info); }
        return path;
    }
    private string? SaveImageManual(BitmapSource image, Window owner)
    {
        var settings = Store.Settings;
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = L.Get("PNG 图片|*.png|JPEG 图片|*.jpg|BMP 图片|*.bmp|TIFF 图片|*.tiff"), FilterIndex = settings.ManualSaveFormat == "JPEG" ? 2 : settings.ManualSaveFormat == "BMP" ? 3 : settings.ManualSaveFormat == "TIFF" ? 4 : 1,
            FileName = SaveNaming.Expand(settings.ManualSaveNameTemplate, DateTime.Now, image.PixelWidth, image.PixelHeight, "Image"), DefaultExt = Extension(settings.ManualSaveFormat), AddExtension = true };
        if (dialog.ShowDialog(owner) != true) return null;
        SaveImageToPath(image, dialog.FileName); Remember(image); Window.ShowNotice(L.Get("图片已保存；历史已更新。")); return dialog.FileName;
    }
    internal void SaveImageToPath(BitmapSource image, string path)
    {
        var settings = Store.Settings; ImageFiles.Save(image, path, settings.JpegQuality, settings.SaveExif);
        if (settings.RememberSaveExtension)
        {
            var next = settings.Clone(); var extension = Path.GetExtension(path).ToLowerInvariant(); next.ManualSaveFormat = extension == ".jpg" || extension == ".jpeg" ? "JPEG" : extension == ".bmp" ? "BMP" : extension == ".tiff" || extension == ".tif" ? "TIFF" : "PNG";
            if (next.ManualSaveFormat != settings.ManualSaveFormat) Store.SaveSettings(next);
        }
        AfterManualOrQuickSave(path); SaveAutomatically(image, "Save"); PlaySound(settings.SoundSaveSuccess);
    }
    private void AfterManualOrQuickSave(string path)
    {
        var current = Store.Settings; if (!current.SaveCopyFile && !current.SaveCopyPath) return;
        try { CopySavedFile(path, current.SaveCopyFile, current.SaveCopyPath); }
        catch (Exception) { Window.ShowNotice(L.Get("文件已保存，剪贴板当前忙，未完成保存后的复制。")); }
    }
    private void AfterRecordingSaved(string path) { AfterManualOrQuickSave(path); PlaySound(Store.Settings.SoundSaveSuccess); }
    private static void CopySavedFile(string path, bool file, bool text)
    {
        var data = new DataObject(); if (file) data.SetFileDropList(new StringCollection { path }); if (text) data.SetText(path); Clipboard.SetDataObject(data, true);
    }
    private void QuickSaveNoticeClicked()
    {
        if (quickSaveNoticePath == null || !File.Exists(quickSaveNoticePath)) return;
        try
        {
            var action = Store.Settings.QuickSaveNotificationAction;
            if (action == "Locate") Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + quickSaveNoticePath + "\"") { UseShellExecute = true });
            else if (action == "CopyFile" || action == "CopyPath") CopySavedFile(quickSaveNoticePath, action == "CopyFile", action == "CopyPath");
        }
        catch (Exception error) { Ui.Error(Window, error, L.Get("保存通知")); }
    }
    private void QuickSaveImage(BitmapSource image) { try { SaveImageQuick(image); } catch (Exception error) { Ui.Error(Window, error, L.Get("快速保存")); } }
    private void OnImageOutput(BitmapSource image, string kind)
    {
        SaveAutomatically(image, kind);
        if (kind == "Copy") CopyImage(image);
    }
    private void SaveAutomatically(BitmapSource image, string kind)
    {
        var settings = Store.Settings;
        if (!settings.AutomaticSaveEnabled || !(kind == "Capture" && settings.AutoSaveCapture || kind == "Copy" && settings.AutoSaveCopy || kind == "Pin" && settings.AutoSavePin || kind == "Save" && settings.AutoSaveManual || kind == "Ocr" && settings.AutoSaveOcr || kind == "Translation" && settings.AutoSaveTranslation || kind == "LongScreenshot" && settings.AutoSaveLongScreenshot)) return;
        try
        {
            var directory = string.IsNullOrEmpty(settings.AutomaticSaveDirectory) ? new ManagedDirectory(Store.Root).Resolve("auto-saves") : settings.AutomaticSaveDirectory;
            var basename = SaveNaming.Expand(settings.AutomaticSaveNameTemplate, DateTime.Now, image.PixelWidth, image.PixelHeight, kind);
            new AutomaticArchive(directory).Save(DateTime.Now, basename, Extension(settings.AutomaticSaveFormat), path => ImageFiles.Save(image, path, settings.JpegQuality, settings.SaveExif), settings.AutomaticSaveDays, settings.AutomaticSaveMegabytes * 1024L * 1024L, settings.AutomaticSaveCleanup, settings.AutomaticSaveOverwrite);
        }
        catch (Exception) { Window.ShowNotice(L.Get("自动留档未完成，当前图片仍可复制、保存或钉图。请检查自动保存目录和容量。")); }
    }
    private void CopyImage(BitmapSource image) => CopyImage(image, 1);
    private void CopyImage(BitmapSource image, double screenScale)
    {
        var factor = CapturePreferenceRules.CopyScale(Store.Settings.ScreenshotCopyDpiLimit, screenScale);
        if (factor < 1) { image = new TransformedBitmap(image, new ScaleTransform(factor, factor)); image.Freeze(); }
        if (!Store.Settings.CopyFileWithImage) { Clipboard.SetImage(image); return; }
        var directory = new ManagedDirectory(new ManagedDirectory(Store.Root).Resolve("clipboard-files")); var format = Store.Settings.ClipboardImageFileFormat == "Auto" ? "PNG" : Store.Settings.ClipboardImageFileFormat; var path = directory.Resolve("DeskSnip-clipboard" + Extension(format)); ImageFiles.Save(image, path, Store.Settings.JpegQuality, Store.Settings.SaveExif);
        var data = new DataObject(); data.SetImage(image); data.SetFileDropList(new StringCollection { path }); Clipboard.SetDataObject(data, true);
    }
    internal async void QuickCapture()
    {
        if (selecting || Exiting) return;
        try { var result = await Select(false); if (result.Image != null) QuickSaveImage(result.Image); }
        catch (Exception error) { Ui.Error(Window, error, L.Get("快速截图")); }
    }
    private void OcrClipboard()
    {
        try { var image = Clipboard.GetImage(); if (image == null) Window.ShowNotice(L.Get("剪贴板没有图片。")); else Recognize(ImageFiles.Normalize(image)); }
        catch (Exception error) { Ui.Error(Window, error, L.Get("识别剪贴板")); }
    }
}
