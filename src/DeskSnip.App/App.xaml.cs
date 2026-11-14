using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;

namespace DeskSnip.App;

public partial class App : Application
{
    private Mutex? instance;
    private EventWaitHandle? showEvent;
    private EventWaitHandle? stopEvent;
    private RegisteredWaitHandle? showWait;
    private RegisteredWaitHandle? stopWait;
    private AppController? controller;
    private Windows.PinFileChannel? pinFileChannel;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var root = Environment.GetEnvironmentVariable("DESK_SNIP_DATA_DIR");
        if (string.IsNullOrWhiteSpace(root)) root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskSnip");
        try
        {
            Branding.Initialize();
            string identity;
            using (var hash = SHA256.Create()) identity = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(root!).ToUpperInvariant()))).Replace("-", "").Substring(0, 16);
            instance = new Mutex(true, "Local\\DeskSnip.Primary." + identity, out var created);
            var pinFile = e.Args.Length == 2 && e.Args[0] == "--pin-file" ? System.IO.Path.GetFullPath(e.Args[1]) : null;
            if (!created)
            {
                try { if (pinFile != null) DeskSnip.App.Windows.PinFileChannel.Send(identity, pinFile); else using (var signal = EventWaitHandle.OpenExisting("Local\\DeskSnip.Show." + identity)) signal.Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
                instance.Dispose(); instance = null; Shutdown(); return;
            }
            controller = new AppController(root!);
            pinFileChannel = new Windows.PinFileChannel(identity, file => Dispatcher.BeginInvoke(new Action(() => controller?.PinFile(file))));
            if (pinFile != null) controller.PinFile(pinFile);
            else if (e.Args.Length == 1 && e.Args[0] == "--background" && controller.Store.Settings.TrayIconMode != "Hidden") controller.Window.Hide();
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DeskSnip.Show." + identity);
            stopEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DeskSnip.Stop." + Process.GetCurrentProcess().Id);
            showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => Dispatcher.BeginInvoke(new Action(() => controller?.Show())), null, Timeout.Infinite, false);
            stopWait = ThreadPool.RegisterWaitForSingleObject(stopEvent, (_, _) => Dispatcher.BeginInvoke(new Action(() => controller?.RequestExit())), null, Timeout.Infinite, false);
        }
        catch (Exception error) { Ui.Error(null, error, L.Get("DeskSnip 启动失败")); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        showWait?.Unregister(null); stopWait?.Unregister(null); showEvent?.Dispose(); stopEvent?.Dispose(); controller?.Dispose();
        pinFileChannel?.Dispose();
        if (instance != null) { instance.ReleaseMutex(); instance.Dispose(); }
        base.OnExit(e);
        Branding.DisposeNativeIcons();
    }
}
