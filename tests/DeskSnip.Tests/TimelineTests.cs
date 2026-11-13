using System;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public class TimelineTests
{
    [Fact] public void MultiplePausesAreExcluded()
    {
        var line = new RecordingTimeline(); line.Start(TimeSpan.Zero);
        line.Pause(TimeSpan.FromSeconds(2)); Assert.Equal(2, line.Elapsed(TimeSpan.FromSeconds(7)).TotalSeconds);
        line.Resume(TimeSpan.FromSeconds(8)); line.Pause(TimeSpan.FromSeconds(11));
        line.Resume(TimeSpan.FromSeconds(20)); line.Stop(TimeSpan.FromSeconds(24));
        Assert.Equal(9, line.Elapsed(TimeSpan.FromSeconds(30)).TotalSeconds);
    }
    [Fact] public void StopWhilePausedKeepsElapsed()
    {
        var line = new RecordingTimeline(); line.Start(TimeSpan.Zero); line.Pause(TimeSpan.FromSeconds(3)); line.Stop(TimeSpan.FromSeconds(20));
        Assert.Equal(3, line.Elapsed(TimeSpan.FromSeconds(20)).TotalSeconds);
    }
    [Fact] public void BackwardsClockIsRejected()
    {
        var line = new RecordingTimeline(); line.Start(TimeSpan.FromSeconds(10));
        Assert.Throws<ArgumentOutOfRangeException>(() => line.Elapsed(TimeSpan.FromSeconds(9)));
    }
    [Fact] public void IllegalTransitionsAreRejected()
    {
        var line = new RecordingTimeline(); Assert.Throws<InvalidOperationException>(() => line.Resume(TimeSpan.Zero));
        line.Start(TimeSpan.Zero); Assert.Throws<InvalidOperationException>(() => line.Start(TimeSpan.Zero));
        line.Stop(TimeSpan.Zero); Assert.Throws<InvalidOperationException>(() => line.Pause(TimeSpan.Zero));
    }
}
