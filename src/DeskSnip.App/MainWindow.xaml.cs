using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using DeskSnip.Core;

namespace DeskSnip.App;

public partial class MainWindow : ProductWindow
{
    private readonly AppController controller;
    internal MainWindow(AppController controller)
    {
        this.controller = controller; InitializeComponent();
        Footer.Text = $"DeskSnip {AppInfo.Version} · Windows OCR · MP4 / GIF / WebP";
        Closing += OnClosing;
    }
    internal void Refresh()
    {
        var selected = HistoryList.SelectedIndex;
        HistoryList.ItemsSource = null; HistoryList.ItemsSource = controller.Store.History;
        if (controller.Store.History.Count > 0) HistoryList.SelectedIndex = Math.Max(0, Math.Min(selected, controller.Store.History.Count - 1));
        EmptyHistory.Visibility = controller.Store.History.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PinList.ItemsSource = controller.PinDescriptions; RestoreButton.IsEnabled = controller.CanRestorePin;
        CaptureKey.Text = L.Join(controller.Store.Settings.CaptureHotkey,L.Get(" · 框选、复制与编辑"));
        PinKey.Text = L.Join(controller.Store.Settings.PinHotkey,L.Get(" · 图片贴到桌面"));
        RecordKey.Text = controller.Store.Settings.RecordHotkey + " · MP4 / GIF / WebP";
    }
    internal void ShowNotice(string value)
    {
        Notice.Text = TextCatalog.Refresh(value, InterfaceLanguageState.Current.Culture); NoticeBox.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (controller.Exiting) return;
        e.Cancel = true;
        if (controller.Store.Settings.MinimizeToTray) Hide(); else controller.RequestExit();
    }
    private void CaptureClick(object sender, RoutedEventArgs e) => controller.Capture();
    private void ClipboardClick(object sender, RoutedEventArgs e) => controller.PinClipboard();
    private void RecordClick(object sender, RoutedEventArgs e) => controller.Record();
    private void SettingsClick(object sender, RoutedEventArgs e) => controller.Settings();
    private void TrayClick(object sender, RoutedEventArgs e) => Hide();
    private void OpenClick(object sender, RoutedEventArgs e) => controller.OpenImage();
    private void LongClick(object sender, RoutedEventArgs e) => controller.StartLongScreenshot();
    private void RecordingRecoveryClick(object sender, RoutedEventArgs e) => controller.RecoverRecording();
    private void PresetClick(object sender, RoutedEventArgs e) => controller.CapturePresets();
    private void HistoryOcrClick(object sender, RoutedEventArgs e) => controller.RecognizeHistory(HistoryList.SelectedItem as HistoryEntry);
    private void HistoryServiceClick(object sender, RoutedEventArgs e) => controller.RecognizeServiceHistory(HistoryList.SelectedItem as HistoryEntry);
    private void RecognitionServiceClick(object sender, RoutedEventArgs e) => controller.OpenRecognitionService();
    private void RecognitionRestoreClick(object sender, RoutedEventArgs e) => controller.OpenRecognitionService(true);
    private void RecoverClick(object sender, RoutedEventArgs e) => controller.RecoverPins();
    private void TogglePinsClick(object sender, RoutedEventArgs e) => controller.TogglePins();
    private void RestoreClick(object sender, RoutedEventArgs e) => controller.RestorePin();
    private void PinManagerClick(object sender, RoutedEventArgs e) => controller.PinManager();
    private void HistoryOpenClick(object sender, RoutedEventArgs e) => controller.OpenHistory(HistoryList.SelectedItem as HistoryEntry);
    private void HistoryDoubleClick(object sender, MouseButtonEventArgs e) => controller.OpenHistory(HistoryList.SelectedItem as HistoryEntry);
    private void HistoryPinClick(object sender, RoutedEventArgs e) => controller.PinHistory(HistoryList.SelectedItem as HistoryEntry);
    private void HistorySaveClick(object sender, RoutedEventArgs e) => controller.SaveHistory(HistoryList.SelectedItem as HistoryEntry);
    private void HistoryGifClick(object sender, RoutedEventArgs e) => controller.ExportGif(HistoryList.SelectedItem as HistoryEntry);
    private void ExitClick(object sender, RoutedEventArgs e) => controller.RequestExit();
}
