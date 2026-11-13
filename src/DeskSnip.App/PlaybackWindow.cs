using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DeskSnip.App;

internal sealed class PlaybackWindow : Window
{
    internal PlaybackWindow(string path, string kind)
    {
        Title = "DeskSnip · 录制预览"; Width = 900; Height = 620; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Margin = new Thickness(15) }; Content = root;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        if (kind == "GIF")
        {
            GifBitmapDecoder decoder;
            using (var stream = System.IO.File.OpenRead(path)) decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var image = new Image { Source = decoder.Frames[0] }; root.Children.Add(image);
            var index = 0; var timer = new DispatcherTimer();
            TimeSpan Delay(int i)
            {
                var metadata = decoder.Frames[i].Metadata as BitmapMetadata; var value = metadata?.GetQuery("/grctlext/Delay");
                return TimeSpan.FromMilliseconds(Math.Max(10, value is ushort delay ? delay * 10 : 100));
            }
            timer.Interval = Delay(0);
            timer.Tick += (_, _) => { index = (index + 1) % decoder.Frames.Count; image.Source = decoder.Frames[index]; timer.Interval = Delay(index); };
            buttons.Children.Add(Ui.Button("播放", timer.Start)); buttons.Children.Add(Ui.Button("暂停", timer.Stop)); timer.Start(); Closed += (_, _) => timer.Stop();
        }
        else
        {
            var player = new MediaElement { Source = new Uri(path), LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close }; root.Children.Add(player);
            var status = Ui.Text("", 12, Ui.Subtle); player.MediaFailed += (_, _) => status.Text = "系统播放器无法解码，可将录制另存后使用本机播放器。";
            buttons.Children.Add(Ui.Button("播放", player.Play)); buttons.Children.Add(Ui.Button("暂停", player.Pause)); buttons.Children.Add(Ui.Button("从头播放", () => { player.Position = TimeSpan.Zero; player.Play(); })); buttons.Children.Add(status);
            Loaded += (_, _) => player.Play(); Closed += (_, _) => player.Close();
        }
    }
}
