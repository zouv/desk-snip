using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

[CollectionDefinition("Interface language", DisableParallelization = true)]
public sealed class InterfaceLanguageCollection { }

public sealed class LocalizationTests
{
    private static readonly CultureInfo Chinese = CultureInfo.GetCultureInfo("zh-CN");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    [Theory]
    [InlineData("System", "zh-TW", "zh-CN")]
    [InlineData("System", "en-GB", "en-US")]
    [InlineData("System", "ja-JP", "en-US")]
    [InlineData("zh-CN", "en-US", "zh-CN")]
    [InlineData("en-US", "zh-CN", "en-US")]
    public void OnlySupportedInterfaceLanguagesResolve(string preference, string system, string expected)
    {
        Assert.Equal(expected, InterfaceLanguageRules.Resolve(preference, CultureInfo.GetCultureInfo(system)));
        Assert.Throws<ArgumentException>(() => InterfaceLanguageRules.Validate("fr-FR"));
    }

    [Fact] public void TranslationsKeepFormatArgumentsAndTechnicalTokens()
    {
        Assert.NotEmpty(TextCatalog.English);
        foreach (var pair in TextCatalog.English)
        {
            Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key);
            var placeholders = new Regex(@"(?<!\{)\{\d+(?:[^{}]*)\}");
            Assert.Equal(placeholders.Matches(pair.Key).Cast<Match>().Select(match => match.Value).OrderBy(value => value), placeholders.Matches(pair.Value).Cast<Match>().Select(match => match.Value).OrderBy(value => value));
            if (pair.Key != "简体中文") Assert.DoesNotMatch(@"[\p{IsCJKUnifiedIdeographs}]", pair.Value);
            Assert.Equal(pair.Key, TextCatalog.Get(pair.Key, Chinese));
        }
    }

    [Fact] public void RefreshTranslatesOnlyProductTextAndPreservesUserArguments()
    {
        var user = new string("保存设置".ToCharArray());
        var label = TextCatalog.Format("{0}：{1}", Chinese, TextCatalog.Get("编辑动作", Chinese), user);
        Assert.Equal("Edit action: 保存设置", TextCatalog.Refresh(label, English));
        Assert.Equal(user, TextCatalog.Refresh(user, English));
        Assert.False(TextCatalog.IsProductText(user));
        Assert.Equal("", TextCatalog.Join("", "")); Assert.Equal("", TextCatalog.Join("", ""));
        Assert.Equal(label, TextCatalog.Refresh(TextCatalog.Refresh(label, English), Chinese));
        var filename = @"C:\example\保存设置-{date}.png";
        var preview = TextCatalog.Format("{0}{1}", English, TextCatalog.Get("预览：", English), filename);
        Assert.EndsWith(filename, TextCatalog.Refresh(preview, Chinese));
    }

    [Fact] public void LanguagePreferenceRoundTripsWithoutChangingRecognitionOrUserData()
    {
        var root = Path.Combine(Path.GetTempPath(), "DeskSnip-language-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new AppSettings { InterfaceLanguage = "en-US", LocalOcrLanguage = "zh-Hans-CN", RecognitionSourceLanguage = "ja", RecognitionTargetLanguage = "zh", MarkWatermarkText = "保存设置", SaveNameTemplate = "保存设置-{date}-{width}", CaptureHotkey = "Ctrl+Alt+F9" };
            new LocalStore(root).SaveSettings(settings);
            var restored = new LocalStore(root).Settings;
            var imported = PreferenceTransfer.Import(new AppSettings(), PreferenceTransfer.Export(restored));
            Assert.Equal("en-US", imported.InterfaceLanguage);
            Assert.Equal(settings.LocalOcrLanguage, imported.LocalOcrLanguage);
            Assert.Equal(settings.RecognitionSourceLanguage, imported.RecognitionSourceLanguage);
            Assert.Equal(settings.RecognitionTargetLanguage, imported.RecognitionTargetLanguage);
            Assert.Equal(settings.MarkWatermarkText, imported.MarkWatermarkText);
            Assert.Equal(settings.SaveNameTemplate, imported.SaveNameTemplate);
            Assert.Equal(settings.CaptureHotkey, imported.CaptureHotkey);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    [InlineData("System")]
    public void SavedLanguageKeepsAnExplicitLegacyColorOnEveryReload(string language)
    {
        var root = Path.Combine(Path.GetTempPath(), "DeskSnip-language-accent-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalStore(root);
            store.SaveSettings(new AppSettings { InterfaceLanguage = language, AccentColor = "#008978" });
            var restored = new LocalStore(root);
            Assert.Equal("", restored.LoadIssue);
            Assert.Equal(language, restored.Settings.InterfaceLanguage);
            Assert.Equal("#008978", restored.Settings.AccentColor);
            restored.SaveSettings(restored.Settings);
            Assert.Equal("#008978", new LocalStore(root).Settings.AccentColor);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("#008978", "#3489F5")]
    [InlineData("#456ABC", "#456ABC")]
    public void LegacySettingsDefaultToChineseAndMigrateOnlyTheOldAccent(string oldAccent, string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "DeskSnip-language-legacy-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalStore(root);
            store.SaveSettings(new AppSettings { AccentColor = oldAccent });
            var path = Path.Combine(root, "settings.json"); var json = File.ReadAllText(path);
            json = Regex.Replace(json, @",?""InterfaceLanguage"":""zh-CN"",?", match => match.Value.StartsWith(",", StringComparison.Ordinal) && match.Value.EndsWith(",", StringComparison.Ordinal) ? "," : "");
            File.WriteAllText(path, json);
            var restored = new LocalStore(root);
            Assert.Equal("", restored.LoadIssue);
            Assert.Equal("zh-CN", restored.Settings.InterfaceLanguage);
            Assert.Equal(expected, restored.Settings.AccentColor);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
