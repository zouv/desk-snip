using System;
using System.Windows;
using System.Windows.Controls;
using DeskSnip.Core;

namespace DeskSnip.App;

internal sealed partial class SettingsWindow
{
    private ServiceCredentialStore? serviceCredentials;
    private readonly PasswordBox serviceKey = new PasswordBox();
    private readonly CheckBox clearServiceKey = new CheckBox { Content = L.Get("清除本机已保存密钥") };
    partial void AddRecognitionServicePage()
    {
        var page = Page("service", L.Get("联网服务"), ToolGlyph.Cloud);
        var connection = SettingsLayout.Section(page.Panel, L.Get("识别与翻译服务"), L.Get("使用支持 DeskSnip 服务协议的地址；仅在点击“请求服务”后发送图片或文字。"));
        Bind(connection, nameof(AppSettings.RecognitionServiceEndpoint), L.Get("服务地址"), L.Get("完整 HTTPS 请求地址；本地模拟服务可用 HTTP")); Bind(connection, nameof(AppSettings.RecognitionTimeoutSeconds), L.Get("请求超时（秒）"), L.Get("5–300 秒"));
        var credentials = SettingsLayout.Section(page.Panel, L.Get("访问密钥")); Bind(credentials, nameof(AppSettings.RecognitionKeyEnvironment), L.Get("密钥环境变量名"), L.Get("留空使用本机已保存密钥；环境变量优先"));
        SettingsLayout.Input(serviceKey, "RecognitionServiceKey", L.Get("访问密钥"), 320); SettingsLayout.Row(credentials, L.Get("本机访问密钥"), serviceKey, L.Get("留空保留；保存设置时更新")); clearServiceKey.SetResourceReference(Control.ForegroundProperty, "InkBrush"); System.Windows.Automation.AutomationProperties.SetName(clearServiceKey, L.Get("清除本机已保存密钥")); SettingsLayout.FullRow(credentials, clearServiceKey);
        var languages = SettingsLayout.Section(page.Panel, L.Get("语言")); SettingsLayout.FullRow(languages, SettingsLayout.Button(L.Get("前往系统页设置源语言与目标语言"), () => SelectPage("system")));
        SettingsLayout.FullRow(languages, SettingsLayout.Text(L.Get("密钥由 Windows 为当前账号加密保存。偏好导出不包含密钥、服务地址和环境变量名。"), 12, "SubtleBrush"));
        clearServiceKey.Checked += (_, _) => { serviceKey.Password = ""; serviceKey.IsEnabled = false; };
        clearServiceKey.Unchecked += (_, _) => serviceKey.IsEnabled = true;
    }
    partial void ReadServicePreferences(AppSettings value) { }
    partial void LoadServicePreferences(AppSettings value) { }
    private void SaveConfigured(AppSettings value)
    {
        var change = clearServiceKey.IsChecked == true || serviceKey.Password.Length > 0;
        if (!change) { save(value); return; }
        if (serviceCredentials == null) throw new InvalidOperationException(L.Get("未指定本机数据目录，无法保存密钥。"));
        serviceCredentials.Change(clearServiceKey.IsChecked == true ? "" : serviceKey.Password, () => save(value));
        serviceKey.Clear();
    }
}
