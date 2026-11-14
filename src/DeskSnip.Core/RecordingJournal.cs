using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace DeskSnip.Core;

[DataContract]
public sealed class RecordingSegment
{
    [DataMember] public string File { get; set; } = "";
    [DataMember] public long DurationTicks { get; set; }
    public RecordingSegment Clone() => (RecordingSegment)MemberwiseClone();
}

[DataContract]
public sealed class RecordingDraft
{
    [DataMember] public string Id { get; set; } = "";
    [DataMember] public int Width { get; set; }
    [DataMember] public int Height { get; set; }
    [DataMember] public int Fps { get; set; }
    [DataMember] public string CreatedUtc { get; set; } = "";
    [DataMember] public bool Published { get; set; }
    [DataMember] public string SystemAudio { get; set; } = "";
    [DataMember] public string MicrophoneAudio { get; set; } = "";
    [DataMember] public List<RecordingSegment> Segments { get; set; } = new List<RecordingSegment>();
    public long DurationTicks => Segments.Sum(s => s.DurationTicks);
    public RecordingDraft Clone() => new RecordingDraft { Id = Id, Width = Width, Height = Height, Fps = Fps, CreatedUtc = CreatedUtc, Published = Published,
        SystemAudio = SystemAudio, MicrophoneAudio = MicrophoneAudio, Segments = Segments.Select(s => s.Clone()).ToList() };
    public override string ToString() => TextCatalog.Format("{0} × {1} · {2:0.0} 秒 · {3}",Width,Height,TimeSpan.FromTicks(DurationTicks).TotalSeconds,(Published ? TextCatalog.Get("已保存素材") : TextCatalog.Get("未完成录制")));
}

/// <summary>每段编码完成后原子登记。崩溃最多丢失当前尚未收尾的一段，已登记片段可重导。</summary>
public sealed class RecordingJournal
{
    private readonly ManagedDirectory root;
    private RecordingDraft draft;
    private RecordingJournal(string folder, RecordingDraft draft) { root = new ManagedDirectory(folder); Validate(draft); this.draft = draft.Clone(); }
    public string Root => root.Root;
    public RecordingDraft Snapshot => draft.Clone();
    public static RecordingJournal Create(string dataRoot, int width, int height, int fps)
    {
        var directory = new ManagedDirectory(dataRoot); var id = Guid.NewGuid().ToString("N");
        var draft = new RecordingDraft { Id = id, Width = width, Height = height, Fps = fps, CreatedUtc = DateTime.UtcNow.ToString("o") };
        var journal = new RecordingJournal(directory.Resolve("recording-drafts/" + id), draft); journal.Commit(draft); return journal;
    }
    public static RecordingJournal Open(string dataRoot, string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException(TextCatalog.Get("录制标识无效。"));
        var directory = new ManagedDirectory(dataRoot); var folder = directory.Resolve("recording-drafts/" + id);
        return new RecordingJournal(folder, AtomicFile.ReadJson<RecordingDraft>(new ManagedDirectory(folder).Resolve("draft.json")));
    }
    public static IReadOnlyList<RecordingDraft> List(string dataRoot, bool includePublished = false)
    {
        var directory = new ManagedDirectory(dataRoot); var folder = directory.Resolve("recording-drafts");
        if (!Directory.Exists(folder)) return Array.Empty<RecordingDraft>();
        var result = new List<RecordingDraft>();
        foreach (var child in Directory.EnumerateDirectories(folder))
        {
            var id = Path.GetFileName(child); if (!Guid.TryParseExact(id, "N", out _)) continue;
            try { var snapshot = Open(dataRoot, id).Snapshot; if ((includePublished || !snapshot.Published) && snapshot.Segments.Count > 0) result.Add(snapshot); }
            catch (Exception error) when (error is IOException || error is ArgumentException || error is SerializationException || error is UnauthorizedAccessException) { }
        }
        return result.OrderByDescending(d => d.CreatedUtc, StringComparer.Ordinal).ToArray();
    }
    public string SegmentPath(int number)
    {
        if (number < 0 || number > 100000) throw new ArgumentOutOfRangeException(nameof(number)); return root.Resolve("segment-" + number.ToString("D6") + ".mp4");
    }
    public string FilePath(string relative) => root.Resolve(relative);
    public void AddSegment(string path, long durationTicks)
    {
        var full = AtomicFile.ValidateTarget(path); var name = Path.GetFileName(full);
        if (!string.Equals(full, root.Resolve(name), StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) throw new ArgumentException(TextCatalog.Get("录制片段必须位于当前素材目录且已保存。"));
        var next = draft.Clone(); next.Segments.Add(new RecordingSegment { File = name, DurationTicks = durationTicks }); Commit(next);
    }
    public void SetAudio(bool system, bool microphone)
    {
        var next = draft.Clone(); next.SystemAudio = system ? "system.wav" : ""; next.MicrophoneAudio = microphone ? "microphone.wav" : ""; Commit(next);
    }
    public void MarkPublished() { var next = draft.Clone(); next.Published = true; Commit(next); }
    public void Discard()
    {
        foreach (var file in Directory.EnumerateFiles(root.Root))
        {
            var name = Path.GetFileName(file);
            if (name == "draft.json" || name == "segments.txt" || name == "source.mp4" || name == "system.wav" || name == "microphone.wav" || Regex.IsMatch(name, @"^segment-[0-9]{6}\.mp4$")) root.Delete(name);
        }
        if (!Directory.EnumerateFileSystemEntries(root.Root).Any()) Directory.Delete(root.Root);
    }
    private void Commit(RecordingDraft next) { Validate(next); AtomicFile.WriteJson(root.Resolve("draft.json"), next); draft = next; }
    private static void Validate(RecordingDraft value)
    {
        if (!Guid.TryParseExact(value.Id, "N", out _) || value.Width < 2 || value.Height < 2 || value.Width > 4096 || value.Height > 4096 || (long)value.Width * value.Height > 8_294_400 ||
            value.Fps < 1 || value.Fps > 60 || value.Segments == null || value.Segments.Count > 100000 || (value.SystemAudio != "" && value.SystemAudio != "system.wav") ||
            (value.MicrophoneAudio != "" && value.MicrophoneAudio != "microphone.wav") || value.Segments.Any(s => s == null || s.DurationTicks <= 0 || s.DurationTicks > TimeSpan.FromMinutes(10).Ticks ||
                s.File == null || !Regex.IsMatch(s.File, @"^segment-[0-9]{6}\.mp4$")) || value.Segments.Select(s => s.File).Distinct().Count() != value.Segments.Count)
            throw new ArgumentException(TextCatalog.Get("录制素材记录无效。"));
    }
}
