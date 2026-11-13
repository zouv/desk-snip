using System;
using System.Reflection;

namespace DeskSnip.Core;

/// <summary>应用身份：名称与版本的唯一出处（版本来自 Directory.Build.props）。</summary>
public static class AppInfo
{
    public const string Name = "DeskSnip";

    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var info = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = info ?? typeof(AppInfo).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        var plus = version.IndexOf('+');
        return plus < 0 ? version : version.Substring(0, plus);
    }
}
