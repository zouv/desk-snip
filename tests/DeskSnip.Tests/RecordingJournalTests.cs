using System;
using System.IO;
using DeskSnip.Core;
using NAudio.Wave;
using Xunit;

namespace DeskSnip.Tests;

public sealed class RecordingJournalTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "DeskSnip-drafts-" + Guid.NewGuid().ToString("N"));
    [Fact] public void CompletedSegmentsSurviveReopenAndBadReferencesCannotCommit()
    {
        var journal = RecordingJournal.Create(root, 64, 48, 60); var segment = journal.SegmentPath(0); File.WriteAllBytes(segment, new byte[] { 1 }); journal.AddSegment(segment, TimeSpan.FromSeconds(2).Ticks);
        Assert.Single(RecordingJournal.List(root)); var reopened = RecordingJournal.Open(root, journal.Snapshot.Id); Assert.Equal(TimeSpan.FromSeconds(2).Ticks, reopened.Snapshot.DurationTicks);
        Assert.Throws<ArgumentException>(() => reopened.AddSegment(Path.Combine(root, "outside.mp4"), 1)); Assert.Single(reopened.Snapshot.Segments);
        var snapshot = reopened.Snapshot; snapshot.Segments.Clear(); Assert.Single(reopened.Snapshot.Segments);
        reopened.MarkPublished(); Assert.Empty(RecordingJournal.List(root)); Assert.Single(RecordingJournal.List(root, true)); reopened.Discard(); Assert.Empty(RecordingJournal.List(root, true));
    }
    [Fact] public void WavLengthIsRecoveredWithoutChangingPcm()
    {
        Directory.CreateDirectory(root); var path = Path.Combine(root, "track.wav");
        using (var writer = new WaveFileWriter(path, new WaveFormat(48000, 16, 1))) writer.Write(new byte[9600], 0, 9600);
        var good = File.ReadAllBytes(path); using (var file = File.Open(path, FileMode.Open, FileAccess.Write)) { file.Position = 4; file.Write(new byte[4], 0, 4); }
        Assert.True(WaveRecovery.Repair(path)); Assert.Equal(good, File.ReadAllBytes(path)); Assert.False(WaveRecovery.Repair(path));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
