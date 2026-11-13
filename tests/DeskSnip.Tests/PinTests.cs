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
        Assert.Equal(4, pin.Scale); Assert.Equal(.15, pin.Opacity);
        for (var i = 0; i < 100; i++) { pin.Zoom(-1); pin.Fade(1); }
        Assert.Equal(.1, pin.Scale); Assert.Equal(1, pin.Opacity);
    }
    [Fact] public void RecoveryAlwaysRestoresInteraction()
    {
        var pin = new PinPresentation { Locked = true, ClickThrough = true }; pin.Fade(-1); pin.RestoreInteraction();
        Assert.False(pin.Locked); Assert.False(pin.ClickThrough); Assert.Equal(1, pin.Opacity);
    }
}
