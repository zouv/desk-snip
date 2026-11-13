using System;
using System.IO;
using System.Linq;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public sealed class LocalDataTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "DeskSnip-tests-" + Guid.NewGuid().ToString("N"));
    [Theory]
    [InlineData("../outside.json")]
    [InlineData("../../outside.json")]
    [InlineData("C:/outside.json")]
    [InlineData("settings.json:stream")]
    public void DataPathCannotEscape(string relative) => Assert.Throws<ArgumentException>(() => new ManagedDirectory(root).Resolve(relative));
    [Fact] public void AtomicWriteFailurePreservesExistingFile()
    {
        Directory.CreateDirectory(root); var path = Path.Combine(root, "value.txt"); File.WriteAllText(path, "previous");
        Assert.Throws<InvalidOperationException>(() => AtomicFile.Write(path, s => { s.WriteByte(1); throw new InvalidOperationException(); }));
        Assert.Equal("previous", File.ReadAllText(path)); Assert.Single(Directory.GetFiles(root));
    }
    [Fact] public void SettingsRoundtripAndRejectInvalidValues()
    {
        var store = new LocalStore(root); store.SaveSettings(new AppSettings { FramesPerSecond = 24, CaptureCursor = false });
        var reopened = new LocalStore(root); Assert.Equal(24, reopened.Settings.FramesPerSecond); Assert.False(reopened.Settings.CaptureCursor);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.SaveSettings(new AppSettings { FramesPerSecond = 0 }));
        Assert.Equal(24, new LocalStore(root).Settings.FramesPerSecond);
    }
    [Fact] public void BrokenConfigurationIsPreserved()
    {
        Directory.CreateDirectory(root); var path = Path.Combine(root, "settings.json"); File.WriteAllText(path, "broken");
        var store = new LocalStore(root); Assert.NotEmpty(store.LoadIssue); Assert.Equal("broken", File.ReadAllText(path));
    }
    [Fact] public void RetentionPersistsNewIndexBeforePruning()
    {
        var store = new LocalStore(root); store.SaveSettings(new AppSettings { HistoryLimit = 2 });
        var first = store.AddImage(2, 2, s => s.WriteByte(1)); store.AddImage(2, 2, s => s.WriteByte(2)); store.AddImage(2, 2, s => s.WriteByte(3));
        Assert.False(File.Exists(store.HistoryPath(first))); Assert.Equal(2, new LocalStore(root).History.Count);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(root, "history")).Length);
    }
    [Fact] public void MissingHistoryFilesAreSkipped()
    {
        var store = new LocalStore(root); var item = store.AddImage(2, 2, s => s.WriteByte(1)); File.Delete(store.HistoryPath(item));
        Assert.Empty(new LocalStore(root).History);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
