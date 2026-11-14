using System;
using System.IO;
using System.Linq;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public sealed class PinWorkspaceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "DeskSnip-pins-" + Guid.NewGuid().ToString("N"));
    [Fact] public void ActiveLayoutAndClosedPinsSurviveReopening()
    {
        var workspace = new PinWorkspace(root); var record = workspace.Add(20, 10, s => s.WriteByte(1));
        record.X = -1200; record.Y = 80; record.Scale = .75; record.Locked = true; workspace.Update(record);
        var reopened = new PinWorkspace(root); Assert.Equal(-1200, reopened.Records[0].X); Assert.True(reopened.Records[0].Locked);
        reopened.Close(record.Id); reopened = new PinWorkspace(root); Assert.False(reopened.Records[0].Active);
        reopened.Restore(record.Id); Assert.True(new PinWorkspace(root).Records[0].Active);
    }
    [Fact] public void ClosedLimitNeverEvictsActiveImages()
    {
        var workspace = new PinWorkspace(root); workspace.Configure(true, 1);
        var active = workspace.Add(2, 2, s => s.WriteByte(1)); var old = workspace.Add(2, 2, s => s.WriteByte(2)); workspace.Close(old.Id);
        var recent = workspace.Add(2, 2, s => s.WriteByte(3)); workspace.Close(recent.Id);
        Assert.True(File.Exists(workspace.ImagePath(active.Id))); Assert.True(File.Exists(workspace.ImagePath(recent.Id))); Assert.False(File.Exists(workspace.ImagePath(old.Id)));
        Assert.Equal(2, new PinWorkspace(root).Records.Count);
    }
    [Fact] public void ReplacingImageRetainsOriginalAndFailureRetainsCurrent()
    {
        var workspace = new PinWorkspace(root); var record = workspace.Add(1, 1, s => s.WriteByte(1));
        workspace.SaveImage(record.Id, s => s.WriteByte(2));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(workspace.ImagePath(record.Id, true)));
        Assert.Throws<IOException>(() => workspace.SaveImage(record.Id, _ => throw new IOException()));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(workspace.ImagePath(record.Id)));
    }
    [Fact] public void DeletingGroupCanPreserveImagesAndSnapshotCannotMutateStore()
    {
        var workspace = new PinWorkspace(root); var group = workspace.AddGroup("参考图"); var record = workspace.Add(2, 2, s => s.WriteByte(1));
        var snapshot = workspace.Snapshot; snapshot.Pins.Clear(); Assert.Single(workspace.Records);
        workspace.DeleteGroup(group, false); Assert.Equal("default", workspace.Records[0].GroupId); Assert.True(File.Exists(workspace.ImagePath(record.Id)));
        workspace.Destroy(new[] { record.Id }); Assert.Empty(workspace.Records); Assert.False(File.Exists(workspace.ImagePath(record.Id)));
    }
    [Fact] public void InvalidLayoutIsRejectedWithoutChangingFile()
    {
        var workspace = new PinWorkspace(root); var record = workspace.Add(2, 2, s => s.WriteByte(1)); var json = File.ReadAllText(Path.Combine(root, "pins.json"));
        record.Scale = double.NaN; Assert.Throws<ArgumentException>(() => workspace.Update(record));
        Assert.Equal(json, File.ReadAllText(Path.Combine(root, "pins.json")));
        Assert.Throws<ArgumentException>(() => workspace.ImagePath("../outside"));
    }
    [Fact] public void ScreenRescueFitsNegativeCoordinatesAndKeepsAspectRatio()
    {
        var screens = new[] { new PixelRect(-1920, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1080) };
        Assert.Equal(new PixelRect(-1920, 780, 400, 300), ScreenPlacement.Rescue(new PixelRect(-2200, 1000, 400, 300), screens));
        var rescued = ScreenPlacement.Rescue(new PixelRect(5000, 0, 4000, 2000), screens);
        Assert.Equal(new PixelRect(0, 0, 1920, 960), rescued);
    }
    [Fact] public void StartupOptionAndGroupVisibilityPersist()
    {
        var workspace = new PinWorkspace(root); workspace.Configure(false, 10); var id = workspace.AddGroup("设计", "#336699"); workspace.SetHidden(true);
        var reopened = new PinWorkspace(root); Assert.False(reopened.RestoreOnStart); Assert.True(reopened.Hidden); Assert.Equal(id, reopened.CurrentGroupId);
    }
    [Fact] public void MissingImageKeepsItsRecordForRecovery()
    {
        var workspace = new PinWorkspace(root); var record = workspace.Add(2, 2, s => s.WriteByte(1));
        File.Delete(workspace.ImagePath(record.Id)); var reopened = new PinWorkspace(root);
        Assert.Single(reopened.Records); Assert.NotEmpty(reopened.LoadIssue);
        reopened.SetHidden(true); Assert.Single(new PinWorkspace(root).Records);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
