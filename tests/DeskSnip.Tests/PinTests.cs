using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public class PinTests
{
    [Fact] public void LockedPinCannotBeResized()
    {
        var pin = new PinPresentation(); pin.Zoom(1); var scale = pin.Scale;
        pin.Locked = true; pin.Zoom(-1); pin.ResetScale(); Assert.Equal(scale, pin.Scale);
    }
    [Fact] public void ScaleAndOpacityAreBounded()
    {
        var pin = new PinPresentation(); for (var i = 0; i < 100; i++) { pin.Zoom(1); pin.Fade(-1); }
        Assert.Equal(8, pin.Scale); Assert.Equal(.1, pin.Opacity);
        for (var i = 0; i < 100; i++) { pin.Zoom(-1); pin.Fade(1); }
        Assert.Equal(.01, pin.Scale); Assert.Equal(1, pin.Opacity);
    }
    [Fact] public void RecoveryAlwaysRestoresInteraction()
    {
        var pin = new PinPresentation { Locked = true, ClickThrough = true }; pin.Fade(-1); pin.RestoreInteraction();
        Assert.False(pin.Locked); Assert.False(pin.ClickThrough); Assert.Equal(1, pin.Opacity);
    }
    [Fact] public void MiddleClickRestoresOriginalThenPreviousView()
    {
        var pin = new PinPresentation(); pin.SetScale(.5); pin.SetOpacity(.35); pin.ToggleOriginal();
        Assert.Equal(1, pin.Scale); Assert.Equal(1, pin.Opacity); pin.ToggleOriginal();
        Assert.Equal(.5, pin.Scale); Assert.Equal(.35, pin.Opacity);
        pin.Locked = true; pin.ToggleOriginal(); Assert.Equal(.5, pin.Scale);
    }
    [Fact] public void FineZoomAndFadeUseIndependentSteps()
    {
        var pin = new PinPresentation(); pin.Zoom(1, true); Assert.Equal(1.01, pin.Scale, 5);
        pin.Fade(-1); Assert.Equal(.95, pin.Opacity);
    }
}
