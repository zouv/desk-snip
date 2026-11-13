using System;

namespace DeskSnip.Core;

public sealed class PinPresentation
{
    public double Scale { get; private set; } = 1;
    public double Opacity { get; private set; } = 1;
    public bool Locked { get; set; }
    public bool ClickThrough { get; set; }
    public void Zoom(int direction)
    {
        if (!Locked) Scale = Math.Max(.1, Math.Min(4, Scale * Math.Pow(1.1, Math.Sign(direction))));
    }
    public void ResetScale() { if (!Locked) Scale = 1; }
    public void Fade(int direction) => Opacity = Math.Max(.15, Math.Min(1, Math.Round(Opacity + Math.Sign(direction) * .1, 2)));
    public void RestoreInteraction() { ClickThrough = false; Locked = false; Opacity = 1; }
}
