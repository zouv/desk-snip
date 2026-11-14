using System;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public sealed class VideoEditingTests
{
    [Fact] public void EditRangeSpeedCropAndVolumeAreValidated()
    {
        var options = new VideoEditOptions { StartSeconds = 2, EndSeconds = 6, Speed = .5, Crop = new PixelRect(4, 2, 100, 80) }; options.Validate(10, 200, 100); Assert.Equal(8, options.OutputSeconds);
        options.EndSeconds = 20; Assert.Throws<ArgumentException>(() => options.Validate(10, 200, 100)); options.EndSeconds = 6;
        options.Speed = double.NaN; Assert.Throws<ArgumentException>(() => options.Validate(10, 200, 100)); options.Speed = 1;
        options.MicrophoneVolume = -1; Assert.Throws<ArgumentException>(() => options.Validate(10, 200, 100));
    }
    [Fact] public void AudioPlacementPadsSilenceAndDropsOverlappingSamples()
    {
        var first = AudioTimeline.Place(TimeSpan.FromSeconds(1).Ticks, 4800, 48000, 0); Assert.Equal(43200, first.SilenceFrames); Assert.Equal(4800, first.Frames);
        var overlap = AudioTimeline.Place(TimeSpan.FromSeconds(1.05).Ticks, 4800, 48000, 48000); Assert.Equal(2400, overlap.SkipFrames); Assert.Equal(2400, overlap.Frames); Assert.Equal(0, overlap.SilenceFrames);
        var delayed = AudioTimeline.Place(TimeSpan.FromSeconds(.5).Ticks, 100, 48000, 48000); Assert.Equal(0, delayed.Frames);
    }
    [Theory] [InlineData("simple", "\"simple\"")] [InlineData("a b", "\"a b\"")] [InlineData("x\"y", "\"x\\\"y\"")] [InlineData("a\\", "\"a\\\\\"")]
    public void ProcessArgumentsQuoteWithoutShellInterpretation(string input, string expected) => Assert.Equal(expected, ProcessArguments.Quote(input));
    [Fact] public void PacketBeforeRecordingOriginDropsOnlyNegativeTimePrefix()
    {
        var packet = AudioTimeline.Place(TimeSpan.FromMilliseconds(50).Ticks, 4800, 48000, 0);
        Assert.Equal(2400, packet.SkipFrames); Assert.Equal(2400, packet.Frames); Assert.Equal(0, packet.SilenceFrames);
        var origin = AudioTimeline.Place(0, 4800, 48000, 0); Assert.Equal(4800, origin.SkipFrames); Assert.Equal(0, origin.Frames);
    }
}
