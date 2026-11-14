using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DeskSnip.App;

public class ProductWindow : Window
{
    public ProductWindow()
    {
        Icon = Branding.Image;
        PropertyChangedEventManager.AddHandler(InterfaceLanguageState.Current, LanguageChanged, nameof(InterfaceLanguageState.Culture));
        Loaded += (_, _) => OnInterfaceLanguageChanged();
        SourceInitialized += (_, _) =>
        {
            Branding.ApplyWindowIcon(this);
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
        };
        Closed += (_, _) => PropertyChangedEventManager.RemoveHandler(InterfaceLanguageState.Current, LanguageChanged, nameof(InterfaceLanguageState.Culture));
    }
    private void LanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (Dispatcher.CheckAccess()) OnInterfaceLanguageChanged();
        else Dispatcher.BeginInvoke(new Action(OnInterfaceLanguageChanged));
    }
    protected virtual void OnInterfaceLanguageChanged() => L.Refresh(this, InterfaceLanguageState.Current.Culture);
    private IntPtr WindowMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x02E0) Dispatcher.BeginInvoke(new Action(() => Branding.ApplyWindowIcon(this)), DispatcherPriority.Loaded);
        return IntPtr.Zero;
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ShowInTaskbarProperty && new WindowInteropHelper(this).Handle != IntPtr.Zero)
            Dispatcher.BeginInvoke(new Action(() => Branding.ApplyWindowIcon(this)), DispatcherPriority.Loaded);
    }
}
