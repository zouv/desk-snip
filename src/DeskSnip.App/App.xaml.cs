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
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var root = Environment.GetEnvironmentVariable("DESK_SNIP_DATA_DIR");
        if (string.IsNullOrWhiteSpace(root)) root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskSnip");
        try
        {
            string identity;
            using (var hash = SHA256.Create()) identity = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(root!).ToUpperInvariant()))).Replace("-", "").Substring(0, 16);
            instance = new Mutex(true, "Local\\DeskSnip.Primary." + identity, out var created);
            if (!created)
            {
                try { using (var signal = EventWaitHandle.OpenExisting("Local\\DeskSnip.Show." + identity)) signal.Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
                instance.Dispose(); instance = null; Shutdown(); return;
            }
            controller = new AppController(root!);
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DeskSnip.Show." + identity);
            stopEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DeskSnip.Stop." + Process.GetCurrentProcess().Id);
            showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => Dispatcher.BeginInvoke(new Action(() => controller?.Show())), null, Timeout.Infinite, false);
            stopWait = ThreadPool.RegisterWaitForSingleObject(stopEvent, (_, _) => Dispatcher.BeginInvoke(new Action(() => controller?.RequestExit())), null, Timeout.Infinite, false);
        }
        catch (Exception error) { Ui.Error(null, error, "DeskSnip 启动失败"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        showWait?.Unregister(null); stopWait?.Unregister(null); showEvent?.Dispose(); stopEvent?.Dispose(); controller?.Dispose();
        if (instance != null) { instance.ReleaseMutex(); instance.Dispose(); }
        base.OnExit(e);
    }
}
