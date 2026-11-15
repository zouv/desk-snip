using System;
using System.Windows.Automation;
using DeskSnip.Core;

namespace DeskSnip.App.Windows;

/// <summary>从光标下的顶层窗口按 UIA 子树找到最深的可见元素，覆盖无子 HWND 的浏览器/资源管理器元素。</summary>
internal static class UiAutomationTargetReader
{
    internal static PixelRect? ElementAt(IntPtr window, PixelPoint point)
    {
        if (window == IntPtr.Zero) return null;
        try
        {
            var root = AutomationElement.FromHandle(window);
            if (root == null) return null;
            var element = Deepest(root, point, 0);
            if (element == null) return null;
            var rect = element.Current.BoundingRectangle;
            if (rect.IsEmpty || rect.Width < 2 || rect.Height < 2) return null;
            return new PixelRect((int)Math.Round(rect.Left), (int)Math.Round(rect.Top), Math.Max(1, (int)Math.Round(rect.Width)), Math.Max(1, (int)Math.Round(rect.Height)));
        }
        catch (Exception) { return null; }
    }
    private static AutomationElement? Deepest(AutomationElement element, PixelPoint point, int depth)
    {
        if (depth > 16) return null;
        AutomationElementCollection children;
        try { children = element.FindAll(TreeScope.Children, Condition.TrueCondition); }
        catch (Exception) { return null; }
        if (children.Count == 0 || children.Count > 256) return null;
        AutomationElement? best = null; var bestArea = double.MaxValue;
        foreach (AutomationElement child in children)
        {
            try
            {
                var current = child.Current;
                if (current.IsOffscreen) continue;
                var rect = current.BoundingRectangle;
                if (rect.IsEmpty || rect.Width < 2 || rect.Height < 2) continue;
                if (point.X < rect.Left || point.X >= rect.Right || point.Y < rect.Top || point.Y >= rect.Bottom) continue;
                var area = rect.Width * rect.Height;
                if (area < bestArea) { best = child; bestArea = area; }
            }
            catch (Exception) { }
        }
        return best == null ? null : Deepest(best, point, depth + 1) ?? best;
    }
}
