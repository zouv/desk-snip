using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace DeskSnip.Core;

[DataContract]
public sealed class PinGroup
{
    [DataMember] public string Id { get; set; } = "default";
    [DataMember] public string Name { get; set; } = "默认组";
    [DataMember] public string Color { get; set; } = "#3489F5";
    public PinGroup Clone() => new PinGroup { Id = Id, Name = Name, Color = Color };
    public override string ToString() => Name;
}

[DataContract]
public sealed class PinRecord
{
    [DataMember] public string Id { get; set; } = "";
    [DataMember] public string GroupId { get; set; } = "default";
    [DataMember] public string Title { get; set; } = "钉图";
    [DataMember] public bool Active { get; set; } = true;
    [DataMember] public bool Visible { get; set; } = true;
    [DataMember] public bool Locked { get; set; }
    [DataMember] public bool ClickThrough { get; set; }
    [DataMember] public bool Topmost { get; set; } = true;
    [DataMember] public double X { get; set; }
    [DataMember] public double Y { get; set; }
    [DataMember] public double Scale { get; set; } = 1;
    [DataMember] public double Opacity { get; set; } = 1;
    [DataMember] public int PixelWidth { get; set; }
    [DataMember] public int PixelHeight { get; set; }
    [DataMember] public string CreatedUtc { get; set; } = "";
    [DataMember] public string ClosedUtc { get; set; } = "";
    public PinRecord Clone() => (PinRecord)MemberwiseClone();
    public override string ToString() => TextCatalog.Format("{0} · {1} × {2}{3}{4}{5}",Title,PixelWidth,PixelHeight,(Active ? "" : TextCatalog.Get(" · 已关闭")),(Locked ? TextCatalog.Get(" · 锁定") : ""),(ClickThrough ? TextCatalog.Get(" · 穿透") : ""));
}

[DataContract]
public sealed class PinWorkspaceState
{
    [DataMember] public List<PinGroup> Groups { get; set; } = new List<PinGroup> { new PinGroup() };
    [DataMember] public List<PinRecord> Pins { get; set; } = new List<PinRecord>();
    [DataMember] public string CurrentGroupId { get; set; } = "default";
    [DataMember] public bool Hidden { get; set; }
    [DataMember] public bool RestoreOnStart { get; set; } = true;
    [DataMember] public int ClosedLimit { get; set; } = 100;
    public PinWorkspaceState Clone() => new PinWorkspaceState
    {
        Groups = Groups.Select(g => g.Clone()).ToList(), Pins = Pins.Select(p => p.Clone()).ToList(), CurrentGroupId = CurrentGroupId,
        Hidden = Hidden, RestoreOnStart = RestoreOnStart, ClosedLimit = ClosedLimit
    };
}

/// <summary>活动钉图独立存图；所有状态先落盘，再改变内存或淘汰已关闭记录。</summary>
public sealed class PinWorkspace
{
    private readonly ManagedDirectory directory;
    private PinWorkspaceState state;
    public PinWorkspace(string root)
    {
        directory = new ManagedDirectory(root); state = new PinWorkspaceState();
        var path = directory.Resolve("pins.json");
        if (!File.Exists(path)) return;
        try
        {
            var loaded = AtomicFile.ReadJson<PinWorkspaceState>(path); Validate(loaded);
            if (loaded.Pins.Any(p => !File.Exists(ImagePath(p.Id)))) LoadIssue = TextCatalog.Get("部分钉图文件缺失，原布局记录保留。");
            state = loaded;
        }
        catch (Exception e) when (e is IOException || e is ArgumentException || e is SerializationException)
        { LoadIssue = TextCatalog.Get("钉图布局无法读取，原文件保留。"); }
    }
    public string LoadIssue { get; private set; } = "";
    public PinWorkspaceState Snapshot => state.Clone();
    public IReadOnlyList<PinRecord> Records => state.Pins.Select(p => p.Clone()).ToArray();
    public IReadOnlyList<PinGroup> Groups => state.Groups.Select(g => g.Clone()).ToArray();
    public string CurrentGroupId => state.CurrentGroupId;
    public bool Hidden => state.Hidden;
    public bool RestoreOnStart => state.RestoreOnStart;
    public PinRecord Add(int width, int height, Action<Stream> write)
    {
        if (width < 1 || height < 1 || (long)width * height > 100_000_000) throw new ArgumentOutOfRangeException(nameof(width));
        var record = new PinRecord { Id = Guid.NewGuid().ToString("N"), PixelWidth = width, PixelHeight = height, GroupId = state.CurrentGroupId,
            Title = $"图片 {width} × {height}", CreatedUtc = DateTime.UtcNow.ToString("o") };
        AtomicFile.Write(ImagePath(record.Id), write);
        var next = state.Clone(); next.Pins.Add(record); Commit(next); return record.Clone();
    }
    public string ImagePath(string id, bool original = false)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException(TextCatalog.Get("钉图标识无效。"), nameof(id));
        return directory.Resolve("pins/" + id + (original ? "-original" : "") + ".png");
    }
    public void SaveImage(string id, Action<Stream> write)
    {
        Find(state, id); var original = ImagePath(id, true);
        if (!File.Exists(original)) AtomicFile.Write(original, output => { using (var input = File.OpenRead(ImagePath(id))) input.CopyTo(output); });
        AtomicFile.Write(ImagePath(id), write);
    }
    public void Update(PinRecord record)
    {
        var next = state.Clone(); var index = next.Pins.FindIndex(p => p.Id == record.Id);
        if (index < 0) throw new ArgumentException(TextCatalog.Get("未找到此钉图。"), nameof(record));
        next.Pins[index] = record.Clone(); Commit(next);
    }
    public void UpdateMany(IEnumerable<PinRecord> records)
    {
        var next = state.Clone();
        foreach (var record in records)
        {
            var index = next.Pins.FindIndex(p => p.Id == record.Id);
            if (index >= 0) next.Pins[index] = record.Clone();
        }
        Commit(next);
    }
    public void Close(string id)
    {
        var next = state.Clone(); var record = Find(next, id); record.Active = false; record.ClosedUtc = DateTime.UtcNow.ToString("o"); Commit(next);
    }
    public PinRecord Restore(string id)
    {
        var next = state.Clone(); var record = Find(next, id); record.Active = record.Visible = true; next.CurrentGroupId = record.GroupId;
        Commit(next); return record.Clone();
    }
    public string AddGroup(string name, string color = "#3489F5")
    {
        var next = state.Clone(); var group = new PinGroup { Id = Guid.NewGuid().ToString("N"), Name = name.Trim(), Color = color };
        next.Groups.Add(group); next.CurrentGroupId = group.Id; Commit(next); return group.Id;
    }
    public void EditGroup(string id, string name, string color)
    {
        var next = state.Clone(); var group = next.Groups.Single(g => g.Id == id); group.Name = name.Trim(); group.Color = color; Commit(next);
    }
    public void DeleteGroup(string id, bool clear)
    {
        if (id == "default") throw new ArgumentException(TextCatalog.Get("默认分组不能删除。"));
        var next = state.Clone();
        if (clear) next.Pins.RemoveAll(p => p.GroupId == id); else foreach (var pin in next.Pins.Where(p => p.GroupId == id)) pin.GroupId = "default";
        next.Groups.RemoveAll(g => g.Id == id); if (next.CurrentGroupId == id) next.CurrentGroupId = "default"; Commit(next);
    }
    public void MoveToGroup(IEnumerable<string> ids, string groupId)
    {
        var next = state.Clone(); var selected = new HashSet<string>(ids);
        foreach (var pin in next.Pins.Where(p => selected.Contains(p.Id))) pin.GroupId = groupId;
        Commit(next);
    }
    public void SelectGroup(string id) { var next = state.Clone(); next.CurrentGroupId = id; Commit(next); }
    public void SetHidden(bool hidden) { var next = state.Clone(); next.Hidden = hidden; Commit(next); }
    public void Configure(bool restoreOnStart, int closedLimit) { var next = state.Clone(); next.RestoreOnStart = restoreOnStart; next.ClosedLimit = closedLimit; Commit(next); }
    public void Destroy(IEnumerable<string> ids)
    {
        var selected = new HashSet<string>(ids); var next = state.Clone(); next.Pins.RemoveAll(p => selected.Contains(p.Id)); Commit(next);
    }
    private void Commit(PinWorkspaceState next)
    {
        Validate(next);
        var retained = new HashSet<string>(next.Pins.Where(p => !p.Active).OrderByDescending(p => p.ClosedUtc, StringComparer.Ordinal).Take(next.ClosedLimit).Select(p => p.Id));
        next.Pins.RemoveAll(p => !p.Active && !retained.Contains(p.Id));
        AtomicFile.WriteJson(directory.Resolve("pins.json"), next);
        var retired = state.Pins.Where(p => next.Pins.All(n => n.Id != p.Id)).ToArray(); state = next;
        foreach (var pin in retired)
        {
            try { directory.Delete("pins/" + pin.Id + ".png"); directory.Delete("pins/" + pin.Id + "-original.png"); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { LoadIssue = TextCatalog.Get("部分已淘汰钉图文件正被占用，本次保留。"); }
        }
    }
    private static PinRecord Find(PinWorkspaceState value, string id) => value.Pins.Single(p => p.Id == id);
    private static void Validate(PinWorkspaceState value)
    {
        if (value.Groups == null || value.Pins == null || value.Groups.Count < 1 || value.Groups.Count > 100 || value.Pins.Count > 5000 || value.ClosedLimit < 0 || value.ClosedLimit > 1000)
            throw new ArgumentException(TextCatalog.Get("钉图工作区无效。"));
        if (!value.Groups.Any(g => g != null && g.Id == "default") || value.Groups.Any(g => g == null ||
            (g.Id != "default" && !Guid.TryParseExact(g.Id, "N", out _)) || string.IsNullOrWhiteSpace(g.Name) || g.Name.Length > 80 || g.Color == null ||
            !System.Text.RegularExpressions.Regex.IsMatch(g.Color, "^#[0-9A-Fa-f]{6}$")) || value.Groups.Select(g => g.Id).Distinct().Count() != value.Groups.Count)
            throw new ArgumentException(TextCatalog.Get("钉图分组无效。"));
        if (!value.Groups.Any(g => g.Id == value.CurrentGroupId) || value.Pins.Select(p => p?.Id).Distinct().Count() != value.Pins.Count) throw new ArgumentException(TextCatalog.Get("钉图分组引用无效。"));
        foreach (var p in value.Pins)
            if (p == null || !Guid.TryParseExact(p.Id, "N", out _) || !value.Groups.Any(g => g.Id == p.GroupId) || string.IsNullOrWhiteSpace(p.Title) || p.Title.Length > 128 ||
                p.PixelWidth < 1 || p.PixelHeight < 1 || (long)p.PixelWidth * p.PixelHeight > 100_000_000 ||
                double.IsNaN(p.X) || double.IsNaN(p.Y) || Math.Abs(p.X) > 1000000 || Math.Abs(p.Y) > 1000000 ||
                double.IsNaN(p.Scale) || p.Scale < .01 || p.Scale > 8 || double.IsNaN(p.Opacity) || p.Opacity < .1 || p.Opacity > 1)
                throw new ArgumentException(TextCatalog.Get("钉图记录无效。"));
    }
}

public static class ScreenPlacement
{
    public static PixelRect Rescue(PixelRect rectangle, IReadOnlyList<PixelRect> workAreas)
    {
        if (rectangle.IsEmpty || workAreas.Count == 0) throw new ArgumentException(TextCatalog.Get("需要有效的屏幕范围。"));
        var screen = workAreas.Where(s => !s.IsEmpty).OrderByDescending(s => (long)rectangle.Intersect(s).Width * rectangle.Intersect(s).Height)
            .ThenBy(s => Math.Pow(rectangle.X + rectangle.Width / 2d - (s.X + s.Width / 2d), 2) + Math.Pow(rectangle.Y + rectangle.Height / 2d - (s.Y + s.Height / 2d), 2)).First();
        var scale = Math.Min(1, Math.Min(screen.Width / (double)rectangle.Width, screen.Height / (double)rectangle.Height));
        var width = Math.Max(1, (int)Math.Floor(rectangle.Width * scale)); var height = Math.Max(1, (int)Math.Floor(rectangle.Height * scale));
        return new PixelRect(Math.Max(screen.X, Math.Min(screen.Right - width, rectangle.X)), Math.Max(screen.Y, Math.Min(screen.Bottom - height, rectangle.Y)), width, height);
    }
}
