using System;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace DeskSnip.Core;

public sealed partial class AppSettings
{
    [DataMember] public string RecognitionServiceEndpoint { get; set; } = "";
    [DataMember] public string RecognitionKeyEnvironment { get; set; } = "";
    [DataMember] public int RecognitionTimeoutSeconds { get; set; } = 60;
    [DataMember] public string RecognitionSourceLanguage { get; set; } = "auto";
    [DataMember] public string RecognitionTargetLanguage { get; set; } = "zh";
    private void InitializeServicePreferences()
    {
        RecognitionServiceEndpoint = RecognitionKeyEnvironment = "";
        RecognitionTimeoutSeconds = 60; RecognitionSourceLanguage = "auto"; RecognitionTargetLanguage = "zh";
    }
    private void ValidateServicePreferences()
    {
        if (RecognitionServiceEndpoint == null || RecognitionKeyEnvironment == null) throw new ArgumentException(TextCatalog.Get("服务配置无效。"));
        if (RecognitionServiceEndpoint.Length > 0) RecognitionEndpoint.Validate(RecognitionServiceEndpoint);
        if (RecognitionKeyEnvironment.Length > 0 && !Regex.IsMatch(RecognitionKeyEnvironment, "^[A-Za-z_][A-Za-z0-9_]{0,127}$")) throw new ArgumentException(TextCatalog.Get("密钥环境变量名无效。"));
        if (RecognitionTimeoutSeconds < 5 || RecognitionTimeoutSeconds > 300) throw new ArgumentException(TextCatalog.Get("服务超时需要为 5–300 秒。"));
        foreach (var language in new[] { RecognitionSourceLanguage, RecognitionTargetLanguage })
            if (string.IsNullOrWhiteSpace(language) || language.Length > 80 || language.IndexOfAny(new[] { '\r', '\n' }) >= 0) throw new ArgumentException(TextCatalog.Get("服务语言设置无效。"));
    }
}
