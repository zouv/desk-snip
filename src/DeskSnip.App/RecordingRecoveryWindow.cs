using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DeskSnip.App.Recording;
using DeskSnip.Core;
using Microsoft.Win32;

namespace DeskSnip.App;

internal sealed class RecordingRecoveryWindow : ProductWindow, IBackgroundWindow
{
    private readonly string dataRoot;
    private readonly string configured;
    private readonly ListBox list = new ListBox { Margin = new Thickness(0, 12, 0, 12) };
    private readonly TextBlock status = Ui.Text(L.Get("已完成的片段可恢复；崩溃前最后未收尾的片段可能丢失。"), 12, Ui.Subtle);
    private bool busy;
    private bool closeRequested;
    private bool closed;
    private Task? recovering;
    private Task? closeTask;
    private readonly Action<string>? outputSaved;
    private readonly bool closeAfterExport;
    private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
    internal RecordingRecoveryWindow(string dataRoot, string configured, Action<string>? outputSaved = null, bool closeAfterExport = false)
    {
        this.outputSaved = outputSaved; this.closeAfterExport = closeAfterExport;
        this.dataRoot = dataRoot; this.configured = configured; Title = L.Get("DeskSnip · 录制恢复与素材"); Width = 740; Height = 480; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root; var bar = new WrapPanel(); DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        bar.Children.Add(Ui.Button(L.Get("刷新"), Refresh)); bar.Children.Add(Ui.Button(L.Get("恢复为 MP4…"), () => { _ = Recover(); }, true)); bar.Children.Add(Ui.Button(L.Get("删除所选素材"), Discard));
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status); root.Children.Add(list); Refresh();
        Closing += (_, e) => { if (closed) return; e.Cancel = true; _ = CloseSafelyAsync(); };
        Closed += (_, _) => cancellation.Dispose();
    }
    private void Refresh() => list.ItemsSource = RecordingJournal.List(dataRoot, true);
    private async Task Recover()
    {
        if (busy || closeRequested || !(list.SelectedItem is RecordingDraft draft)) return;
        var dialog = new SaveFileDialog { Filter = L.Get("MP4 视频|*.mp4"), FileName = "DeskSnip-recovered.mp4", AddExtension = true }; if (dialog.ShowDialog(this) != true) return;
        busy = true; status.Text = L.Get("正在恢复…");
        try
        {
            var journal = RecordingJournal.Open(dataRoot, draft.Id);
            recovering = Task.Run(() =>
            {
                foreach (var audio in new[] { draft.SystemAudio, draft.MicrophoneAudio }) if (!string.IsNullOrEmpty(audio) && System.IO.File.Exists(journal.FilePath(audio))) WaveRecovery.Repair(journal.FilePath(audio));
                var source = journal.FilePath("source.mp4"); Ffmpeg.Join(journal, source, configured, cancellation.Token); var info = Ffmpeg.Probe(source, configured, cancellation.Token);
                Ffmpeg.Export(source, dialog.FileName, new VideoEditOptions { EndSeconds = info.Duration, Crop = new PixelRect(0, 0, info.Width, info.Height), FramesPerSecond = draft.Fps }, configured,
                    string.IsNullOrEmpty(draft.SystemAudio) ? null : journal.FilePath(draft.SystemAudio), string.IsNullOrEmpty(draft.MicrophoneAudio) ? null : journal.FilePath(draft.MicrophoneAudio), cancellation.Token);
                journal.MarkPublished();
            }); await recovering; outputSaved?.Invoke(dialog.FileName); if (!closeRequested) { status.Text = L.Get("恢复完成，素材保留，可继续回放和剪辑。"); Refresh(); new PlaybackWindow(dialog.FileName, "MP4", configured, journal, closeAfterExport, outputSaved).Show(); }
        }
        catch (OperationCanceledException) { status.Text = L.Get("恢复已取消，完成片段和原输出保留。"); }
        catch (InvalidOperationException error) { status.Text = error.Message; }
        catch (Exception error) { if (!closeRequested) Ui.Error(this, error, L.Get("恢复录制")); }
        finally { busy = false; recovering = null; }
    }
    private void Discard()
    {
        if (busy || !(list.SelectedItem is RecordingDraft draft)) return;
        if (MessageBox.Show(this, L.Get("删除这些可恢复片段和独立音轨？已导出的视频保留。"), L.Get("删除素材"), MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try { RecordingJournal.Open(dataRoot, draft.Id).Discard(); Refresh(); } catch (Exception error) { Ui.Error(this, error, L.Get("删除素材")); }
    }
    public Task CloseSafelyAsync()
    {
        if (closeTask != null) return closeTask; closeRequested = true; cancellation.Cancel(); closeTask = CloseCore(); return closeTask;
    }
    private async Task CloseCore()
    {
        await Task.Yield(); if (recovering != null) { try { await recovering; } catch (Exception) { } }
        closed = true; Close();
    }
}
