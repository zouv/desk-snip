using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace DeskSnip.Core;

public enum CaptureShape { Rectangle, Ellipse, Freehand, Polygon }

[DataContract]
public sealed class CapturePreset
{
    [DataMember] public string Id { get; set; } = "";
    [DataMember] public string Name { get; set; } = "";
    [DataMember] public int X { get; set; }
    [DataMember] public int Y { get; set; }
    [DataMember] public int Width { get; set; } = 800;
    [DataMember] public int Height { get; set; } = 600;
    [DataMember] public bool FixedPosition { get; set; }
    [DataMember] public int DelaySeconds { get; set; }
    [DataMember] public bool Cursor { get; set; }
    [DataMember] public CaptureShape Shape { get; set; }
    public PixelRect Bounds => new PixelRect(X, Y, Width, Height);
    public CapturePreset Clone() => (CapturePreset)MemberwiseClone();
    public override string ToString() => $"{Name} · {Width} × {Height}";
}

[DataContract]
public sealed class CaptureArea
{
    [DataMember] public int X { get; set; }
    [DataMember] public int Y { get; set; }
    [DataMember] public int Width { get; set; }
    [DataMember] public int Height { get; set; }
    public PixelRect Bounds => new PixelRect(X, Y, Width, Height);
    public CaptureArea Clone() => (CaptureArea)MemberwiseClone();
}

[DataContract]
public sealed class CaptureProfileState
{
    [DataMember] public List<CapturePreset> Presets { get; set; } = new List<CapturePreset>();
    [DataMember] public List<CaptureArea> Recent { get; set; } = new List<CaptureArea>();
    public CaptureProfileState Clone() => new CaptureProfileState { Presets = Presets.Select(p => p.Clone()).ToList(), Recent = Recent.Select(a => a.Clone()).ToList() };
}

public sealed class CaptureProfiles
{
    private readonly ManagedDirectory directory;
    private CaptureProfileState state = new CaptureProfileState();
    public string LoadIssue { get; private set; } = "";
    public CaptureProfiles(string root)
    {
        directory = new ManagedDirectory(root); var path = directory.Resolve("captures.json");
        if (File.Exists(path))
        {
            try { var loaded = AtomicFile.ReadJson<CaptureProfileState>(path); Validate(loaded); state = loaded; }
            catch (Exception error) when (error is IOException || error is ArgumentException || error is SerializationException) { LoadIssue = TextCatalog.Get("截图预设读取失败，原文件保留。"); }
        }
        else ResetDefaults();
    }
    public CaptureProfileState Snapshot => state.Clone();
    public void SavePreset(CapturePreset preset)
    {
        var next = state.Clone(); var copy = preset.Clone(); if (copy.Id == "") copy.Id = Guid.NewGuid().ToString("N");
        var index = next.Presets.FindIndex(p => p.Id == copy.Id); if (index >= 0) next.Presets[index] = copy; else next.Presets.Add(copy); Commit(next);
    }
    public void RemovePreset(string id) { var next = state.Clone(); next.Presets.RemoveAll(p => p.Id == id); Commit(next); }
    public void ResetDefaults()
    {
        var next = state.Clone(); next.Presets = new List<CapturePreset> { new CapturePreset { Id = Guid.NewGuid().ToString("N"), Name = "800 × 600", Width = 800, Height = 600 },
            new CapturePreset { Id = Guid.NewGuid().ToString("N"), Name = "1920 × 1080", Width = 1920, Height = 1080 }, new CapturePreset { Id = Guid.NewGuid().ToString("N"), Name = "正方形", Width = 600, Height = 600 } }; Commit(next);
    }
    public void Remember(PixelRect region, int limit = 20)
    {
        if (limit < 0 || limit > 99999) throw new ArgumentOutOfRangeException(nameof(limit));
        if (region.IsEmpty) return;
        var next = state.Clone(); next.Recent.RemoveAll(p => p.Bounds.Equals(region)); next.Recent.Insert(0, new CaptureArea { X = region.X, Y = region.Y, Width = region.Width, Height = region.Height });
        next.Recent = next.Recent.Take(limit).ToList(); Commit(next);
    }
    private void Commit(CaptureProfileState next) { Validate(next); AtomicFile.WriteJson(directory.Resolve("captures.json"), next); state = next; }
    private static void Validate(CaptureProfileState value)
    {
        if (value.Presets == null || value.Recent == null || value.Presets.Count > 100 || value.Recent.Count > 99999) throw new ArgumentException(TextCatalog.Get("截图预设配置无效。"));
        if (value.Presets.Any(p => p == null || !Guid.TryParseExact(p.Id, "N", out _) || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 80 || !ValidArea(p.X, p.Y, p.Width, p.Height) || p.DelaySeconds < 0 || p.DelaySeconds > 30 || !Enum.IsDefined(typeof(CaptureShape), p.Shape)) ||
            value.Presets.Select(p => p.Id).Distinct().Count() != value.Presets.Count || value.Recent.Any(p => p == null || !ValidArea(p.X, p.Y, p.Width, p.Height))) throw new ArgumentException(TextCatalog.Get("截图预设无效。"));
    }
    private static bool ValidArea(int x, int y, int width, int height) => Math.Abs((long)x) <= 1000000 && Math.Abs((long)y) <= 1000000 && width > 0 && height > 0 && width <= 20000 && height <= 20000 && (long)width * height <= 100_000_000;
}

public sealed class CaptureTarget
{
    public CaptureTarget(string group, PixelRect bounds, int depth) { Group = group; Bounds = bounds; Depth = depth; }
    public string Group { get; }
    public PixelRect Bounds { get; }
    public int Depth { get; }
}

public static class CaptureTargets
{
    public static IReadOnlyList<CaptureTarget> At(IReadOnlyList<CaptureTarget> targets, PixelPoint point, bool elements)
    {
        bool Contains(CaptureTarget target) => point.X >= target.Bounds.X && point.X < target.Bounds.Right && point.Y >= target.Bounds.Y && point.Y < target.Bounds.Bottom;
        var window = targets.FirstOrDefault(t => t.Depth == 0 && Contains(t)); if (window == null) return Array.Empty<CaptureTarget>();
        return targets.Where(t => t.Group == window.Group && Contains(t) && (elements || t.Depth == 0)).OrderBy(t => (long)t.Bounds.Width * t.Bounds.Height).ThenByDescending(t => t.Depth).ToArray();
    }
}
