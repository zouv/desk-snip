using System;
using System.Globalization;

namespace DeskSnip.Core;

public static class InterfaceLanguageRules
{
    public static void Validate(string value)
    {
        if (value != "System" && value != "zh-CN" && value != "en-US")
            throw new ArgumentException(TextCatalog.Get("界面语言无效，请选择简体中文、English 或跟随系统。"));
    }

    public static string Resolve(string preference, CultureInfo systemCulture)
    {
        Validate(preference);
        if (systemCulture == null) throw new ArgumentNullException(nameof(systemCulture));
        return preference == "System" ? systemCulture.TwoLetterISOLanguageName == "zh" ? "zh-CN" : "en-US" : preference;
    }
}
