using System;
using System.IO;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public sealed class CaptureProfilesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "DeskSnip-presets-" + Guid.NewGuid().ToString("N"));
    [Fact] public void PresetsAndDistinctRecentAreasPersistWithValidation()
    {
        var profiles = new CaptureProfiles(root); var area = new PixelRect(-1200, 40, 300, 200); profiles.Remember(area); profiles.Remember(area); Assert.Single(profiles.Snapshot.Recent);
        profiles.SavePreset(new CapturePreset { Name = "固定", X = -1200, Y = 40, Width = 300, Height = 200, FixedPosition = true, DelaySeconds = 2, Shape = CaptureShape.Ellipse });
        var reopened = new CaptureProfiles(root); Assert.Equal(area, reopened.Snapshot.Recent[0].Bounds); Assert.Equal(4, reopened.Snapshot.Presets.Count);
        Assert.Throws<ArgumentException>(() => reopened.SavePreset(new CapturePreset { Name = "失败", Width = -1 })); Assert.Equal(4, new CaptureProfiles(root).Snapshot.Presets.Count);
        var snapshot = reopened.Snapshot; snapshot.Presets.Clear(); Assert.Equal(4, reopened.Snapshot.Presets.Count);
    }
    [Fact] public void WindowStackAndChildAreaSelectionRespectOcclusion()
    {
        var targets = new[] { new CaptureTarget("front", new PixelRect(0, 0, 400, 300), 0), new CaptureTarget("front", new PixelRect(40, 40, 80, 60), 1), new CaptureTarget("back", new PixelRect(0, 0, 600, 500), 0) };
        Assert.Equal(80, CaptureTargets.At(targets, new PixelPoint(50, 50), true)[0].Bounds.Width); Assert.Single(CaptureTargets.At(targets, new PixelPoint(50, 50), false));
        Assert.Equal("back", CaptureTargets.At(targets, new PixelPoint(500, 400), true)[0].Group); Assert.Empty(CaptureTargets.At(targets, new PixelPoint(-10, 0), true));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
