using System;
using System.Collections.Generic;
using System.Linq;

namespace DeskSnip.Core;

/// <summary>只处理已绑定的拖动，点击和取消不产生截图；不依赖桌面接口。</summary>
public sealed class MouseGestureSession
{
    private readonly List<MouseBinding> bindings;
    private PixelPoint start;
    private string? button;
    public MouseGestureSession(IEnumerable<MouseBinding> bindings) { this.bindings = bindings.Select(b => b.Clone()).ToList(); InputBindingRules.ValidateMouse(this.bindings, true); }
    public bool Active => button != null;
    public string? Action { get; private set; }
    public PixelRect Region { get; private set; }
    public bool Begin(string mouseButton, string modifiers, PixelPoint point, bool allowed)
    {
        if (Active || !allowed) return false;
        var action = InputBindingRules.MouseAction(bindings, modifiers, mouseButton + "Drag"); if (action == null) return false;
        button = mouseButton; Action = action; start = point; Region = default; return true;
    }
    public void Move(PixelPoint point)
    {
        if (!Active) return; Region = PixelRect.FromPoints((int)start.X, (int)start.Y, (int)point.X, (int)point.Y);
    }
    public (string Action, PixelRect Region)? Finish(string mouseButton, PixelPoint point)
    {
        if (button != mouseButton) return null; Move(point); var action = Action!; var region = Region; Cancel(); return region.Width >= 3 && region.Height >= 3 ? (action, region) : ((string, PixelRect)?)null;
    }
    public void Cancel() { button = Action = null; Region = default; }
}

/// <summary>同一组合可以同时绑定单击与拖动；越过移动阈值后只执行拖动。</summary>
public sealed class PinPointerGesture
{
    private string? button;
    private PixelPoint start;
    private string? clickAction;
    private string? dragAction;
    public bool Active => button != null;
    public bool Dragging { get; private set; }
    public void Begin(string mouseButton, PixelPoint point, string? click, string? drag)
    {
        Cancel(); if (string.IsNullOrEmpty(click) && string.IsNullOrEmpty(drag)) return;
        button = mouseButton; start = point; clickAction = click; dragAction = drag;
    }
    public string? Move(PixelPoint point)
    {
        if (!Active || Dragging || string.IsNullOrEmpty(dragAction) || Math.Max(Math.Abs(point.X - start.X), Math.Abs(point.Y - start.Y)) < 4) return null;
        Dragging = true; return dragAction;
    }
    public string? Finish(string mouseButton, PixelPoint point)
    {
        if (button != mouseButton) return null;
        Move(point); var result = Dragging ? dragAction : clickAction; Cancel(); return result;
    }
    public void Cancel() { button = clickAction = dragAction = null; Dragging = false; }
}
