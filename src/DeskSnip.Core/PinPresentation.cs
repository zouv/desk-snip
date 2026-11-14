using System;

namespace DeskSnip.Core;

public sealed class PinPresentation
{
    private double? previousScale;
    private double previousOpacity;
    public double Scale { get; private set; } = 1;
    public double Opacity { get; private set; } = 1;
    public bool Locked { get; set; }
    public bool ClickThrough { get; set; }
    public bool Topmost { get; set; } = true;
    public void Zoom(int direction, bool fine = false, int step = 10, int fineStep = 1)
    {
        if (step < 1 || step > 100 || fineStep < 1 || fineStep > 100) throw new ArgumentOutOfRangeException(nameof(step));
        if (!Locked) SetScale(Math.Max(.01, Scale + (fine ? fineStep : step) / 100d * Math.Sign(direction)));
    }
    public void SetScale(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        if (!Locked) Scale = Math.Max(.01, Math.Min(8, value));
    }
    public void SetOpacity(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
        Opacity = Math.Max(.1, Math.Min(1, value));
    }
    public void ToggleOriginal()
    {
        if (Locked) return;
        if (previousScale.HasValue && Scale == 1 && Opacity == 1)
        { Scale = previousScale.Value; Opacity = previousOpacity; previousScale = null; }
        else { previousScale = Scale; previousOpacity = Opacity; Scale = Opacity = 1; }
    }
    public void ResetScale() { if (!Locked) Scale = 1; }
    public void Fade(int direction, int step = 5)
    {
        if (step < 1 || step > 100) throw new ArgumentOutOfRangeException(nameof(step));
        SetOpacity(Math.Round(Opacity + Math.Sign(direction) * step / 100d, 2));
    }
    public void RestoreInteraction() { ClickThrough = false; Locked = false; Opacity = 1; }
}
