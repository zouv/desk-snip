using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;

namespace DeskSnip.App;

/// <summary>剪贴板可能被其它程序短暂占用；统一使用带重试的 WPF 写入，避免复制偶发失败。</summary>
internal static class ClipboardRetry
{
    internal static void SetImage(BitmapSource image) { var data = new DataObject(); data.SetImage(image); SetDataObject(data); }
    internal static void SetFileDropList(StringCollection files) { var data = new DataObject(); data.SetFileDropList(files); SetDataObject(data); }
    internal static void SetDataObject(DataObject data)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { Clipboard.SetDataObject(data, true); return; }
            catch (ExternalException) when (attempt < 9) { Thread.Sleep(50); }
        }
    }
}
