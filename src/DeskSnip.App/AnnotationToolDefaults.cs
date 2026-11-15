using System;
using System.Collections.Generic;
using System.Linq;
using DeskSnip.Core;

namespace DeskSnip.App;

/// <summary>按工具栏分组记忆默认标注工具；MarkToolDefaults 负责跨启动持久化。</summary>
internal static class AnnotationToolDefaults
{
    private static readonly Dictionary<string, AnnotationKind> values = new Dictionary<string, AnnotationKind>(StringComparer.Ordinal);
    internal static event Action? Changed;
    internal static void Load(string? value)
    {
        values.Clear();
        foreach (var pair in AnnotationPreferenceRules.ParseToolDefaults(value ?? "")) values[pair.Key] = pair.Value;
    }
    internal static AnnotationKind Get(string group, AnnotationKind fallback)
    {
        if (values.TryGetValue(group, out var kind) && AnnotationPreferenceRules.GroupTools(group).Contains(kind)) return kind;
        return fallback;
    }
    internal static void Set(string group, AnnotationKind kind)
    {
        if (!AnnotationPreferenceRules.GroupTools(group).Contains(kind)) return;
        if (values.TryGetValue(group, out var current) && current == kind) return;
        values[group] = kind; Changed?.Invoke();
    }
    internal static string Serialize()
        => string.Join(",", values.OrderBy(pair => Array.IndexOf(AnnotationPreferenceRules.ToolbarGroups, pair.Key)).Select(pair => pair.Key + "=" + pair.Value));
    internal static string Label(string name, AnnotationKind kind) => name + " · " + EditorWindow.ToolNames[(int)kind];
}
