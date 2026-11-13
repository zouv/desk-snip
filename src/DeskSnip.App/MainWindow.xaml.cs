using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using DeskSnip.Core;

namespace DeskSnip.App;

public partial class MainWindow : Window
{
    private readonly AppController controller;
    internal MainWindow(AppController controller)
    {
        this.controller = controller; InitializeComponent();
        Footer.Text = $"DeskSnip {AppInfo.Version} · 本地处理 · MP4 / GIF 基础录制";
        Closing += OnClosing;
    }
    internal void Refresh()
    {
        var selected = HistoryList.SelectedIndex;
        HistoryList.ItemsSource = null; HistoryList.ItemsSource = controller.Store.History;
        if (controller.Store.History.Count > 0) HistoryList.SelectedIndex = Math.Max(0, Math.Min(selected, controller.Store.History.Count - 1));
        EmptyHistory.Visibility = controller.Store.History.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PinList.ItemsSource = controller.PinDescriptions; RestoreButton.IsEnabled = controller.CanRestorePin;
        CaptureKey.Text = controller.Store.Settings.CaptureHotkey + " · 框选后编辑";
        PinKey.Text = controller.Store.Settings.PinHotkey + " · 图片贴到桌面";
        RecordKey.Text = controller.Store.Settings.RecordHotkey + " · MP4 / GIF";
    }
    internal void ShowNotice(string value)
    {
        Notice.Text = value; NoticeBox.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
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
    private void RecoverClick(object sender, RoutedEventArgs e) => controller.RecoverPins();
    private void TogglePinsClick(object sender, RoutedEventArgs e) => controller.TogglePins();
    private void RestoreClick(object sender, RoutedEventArgs e) => controller.RestorePin();
    private void HistoryOpenClick(object sender, RoutedEventArgs e) => controller.OpenHistory(HistoryList.SelectedItem as HistoryEntry);
    private void HistoryDoubleClick(object sender, MouseButtonEventArgs e) => controller.OpenHistory(HistoryList.SelectedItem as HistoryEntry);
    private void HistoryPinClick(object sender, RoutedEventArgs e) => controller.PinHistory(HistoryList.SelectedItem as HistoryEntry);
    private void HistorySaveClick(object sender, RoutedEventArgs e) => controller.SaveHistory(HistoryList.SelectedItem as HistoryEntry);
    private void HistoryGifClick(object sender, RoutedEventArgs e) => controller.ExportGif(HistoryList.SelectedItem as HistoryEntry);
    private void ExitClick(object sender, RoutedEventArgs e) => controller.RequestExit();
}
