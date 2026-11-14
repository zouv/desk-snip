using System;
using System.IO;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public sealed class PreferenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "DeskSnip-preferences-" + Guid.NewGuid().ToString("N"));
    [Fact] public void PortableExportExcludesMachinePathsAndPartialImportKeepsCurrentValues()
    {
        var current = new AppSettings { Theme = "Dark", JpegQuality = 80, OutputDirectory = root, FfmpegPath = Path.Combine(root, "ffmpeg.exe"), AutomaticSaveDirectory = Path.Combine(root, "auto"), QuickSaveDirectory = root };
        var document = PreferenceTransfer.Export(current); Assert.Equal("Dark", document.Values["Theme"]); Assert.False(document.Values.ContainsKey("FfmpegPath")); Assert.False(document.Values.ContainsKey("OutputDirectory"));
        var target = new AppSettings { FramesPerSecond = 60, OutputDirectory = root }; var imported = PreferenceTransfer.Import(target, document); Assert.Equal(60, imported.FramesPerSecond); Assert.Equal(root, imported.OutputDirectory); Assert.Equal("Dark", imported.Theme);
        document.Values.Add("FfmpegPath", "forbidden"); Assert.Throws<ArgumentException>(() => PreferenceTransfer.Import(target, document)); Assert.Equal("Light", target.Theme);
    }
    [Fact] public void BadImportIsAtomicAndActionBindingsAreCopied()
    {
        var current = new AppSettings(); current.ActionBindings.Add(new ActionHotkey { Hotkey = "Ctrl+Alt+Q" }); var clone = current.Clone(); clone.ActionBindings[0].Hotkey = "Ctrl+Alt+X"; Assert.Equal("Ctrl+Alt+Q", current.ActionBindings[0].Hotkey);
        var document = PreferenceTransfer.Export(current); Assert.Single(document.ActionBindings!); document.Values["JpegQuality"] = "101";
        Assert.Throws<ArgumentException>(() => PreferenceTransfer.Import(current, document)); Assert.Equal(95, current.JpegQuality);
    }
    [Fact] public void OldConfigurationGetsNewPreferenceDefaults()
    {
        Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "settings.json"), "{\"CaptureHotkey\":\"Ctrl+Alt+A\",\"PinHotkey\":\"Ctrl+Alt+P\",\"RecordHotkey\":\"Ctrl+Alt+R\",\"FramesPerSecond\":15,\"HistoryLimit\":100,\"RecordingFormat\":\"MP4\"}");
        var store = new LocalStore(root); Assert.Empty(store.LoadIssue); Assert.Equal("Light", store.Settings.Theme); Assert.Equal(95, store.Settings.JpegQuality); Assert.Equal(80, store.Settings.VideoQuality); Assert.Empty(store.Settings.ActionBindings);
        Assert.Equal(30, store.Settings.AutomaticSaveDays);
        Assert.Equal(60, store.Settings.RecognitionTimeoutSeconds); Assert.Equal("auto", store.Settings.RecognitionSourceLanguage); Assert.Equal("zh", store.Settings.RecognitionTargetLanguage);
    }
    [Fact] public void NamingSanitizesTokensAndReservedNamesAndAddsSuffix()
    {
        Assert.Equal("capture-20010203-040506-10x20", SaveNaming.Expand("capture-{date}-{time}-{width}x{height}", new DateTime(2001, 2, 3, 4, 5, 6), 10, 20, "Image"));
        Assert.Equal("_CON", SaveNaming.Expand("CON", DateTime.Now, 1, 1, "Image")); Assert.Equal("a_b", SaveNaming.Expand("a:b", DateTime.Now, 1, 1, "Image"));
        Assert.Throws<ArgumentException>(() => SaveNaming.Expand("{unknown}", DateTime.Now, 1, 1, "Image"));
        var first = SaveNaming.ChoosePath(root, "{literal}", ".png"); File.WriteAllText(first, "previous"); var second = SaveNaming.ChoosePath(root, "{literal}", ".png"); Assert.EndsWith("{literal} (1).png", second); Assert.Equal("previous", File.ReadAllText(first));
    }
    [Fact] public void AutomaticRetentionOnlyRemovesOwnedFilesAndKeepsNewest()
    {
        Directory.CreateDirectory(root); var manual = Path.Combine(root, "manual.png"); File.WriteAllText(manual, "mine"); var archive = new AutomaticArchive(root); var now = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var old = archive.Save(now, "old", ".png", path => File.WriteAllBytes(path, new byte[8]), 30, 16);
        var latest = archive.Save(now.AddDays(40), "new", ".png", path => File.WriteAllBytes(path, new byte[32]), 30, 16);
        Assert.False(File.Exists(old)); Assert.True(File.Exists(latest)); Assert.Equal("mine", File.ReadAllText(manual));
        var reopened = new AutomaticArchive(root); Assert.Throws<IOException>(() => reopened.Save(now, "failed", ".png", _ => throw new IOException(), 30, 16)); Assert.True(File.Exists(latest));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
