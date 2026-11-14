using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DeskSnip.Core;

public sealed class ManagedDirectory
{
    public ManagedDirectory(string root)
    {
        var full = Path.GetFullPath(root);
        if (string.Equals(full, Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(TextCatalog.Get("请选择专用的数据目录。"), nameof(root));
        Root = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        RejectReparsePoints(Root);
        Directory.CreateDirectory(Root);
    }
    public string Root { get; }
    public string Resolve(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new ArgumentException(TextCatalog.Get("需要相对路径。"), nameof(relative));
        var full = Path.GetFullPath(Path.Combine(Root, relative));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(TextCatalog.Get("路径超出数据目录。"), nameof(relative));
        RejectReparsePoints(full);
        return full;
    }
    public void Delete(string relative)
    {
        var full = Resolve(relative);
        if (File.Exists(full)) File.Delete(full);
    }
    internal static void RejectReparsePoints(string full)
    {
        for (var path = full; !string.IsNullOrEmpty(path); path = Path.GetDirectoryName(path))
        {
            if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(TextCatalog.Get("数据路径不能包含符号链接或目录联接。"));
        }
    }
}

public static class AtomicFile
{
    public static string ValidateTarget(string path)
    {
        var full = Path.GetFullPath(path);
        ManagedDirectory.RejectReparsePoints(full);
        if (string.IsNullOrEmpty(Path.GetFileName(full))) throw new ArgumentException(TextCatalog.Get("需要文件路径。"), nameof(path));
        return full;
    }
    public static void Write(string path, Action<Stream> write)
    {
        var full = ValidateTarget(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { write(stream); stream.Flush(true); }
            if (File.Exists(full)) File.Replace(temp, full, null);
            else File.Move(temp, full);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static void WriteJson<T>(string path, T value) => Write(path, s => new DataContractJsonSerializer(typeof(T)).WriteObject(s, value));
    public static T ReadJson<T>(string path)
    {
        using (var stream = File.OpenRead(path))
            return (T)(new DataContractJsonSerializer(typeof(T)).ReadObject(stream) ?? throw new SerializationException(TextCatalog.Get("文档为空。")));
    }
}

[DataContract]
public sealed partial class AppSettings
{
    [DataMember] public string CaptureHotkey { get; set; } = "Ctrl+Alt+A";
    [DataMember] public string PinHotkey { get; set; } = "Ctrl+Alt+P";
    [DataMember] public string RecordHotkey { get; set; } = "Ctrl+Alt+R";
    [DataMember] public int FramesPerSecond { get; set; } = 15;
    [DataMember] public int HistoryLimit { get; set; } = 100;
    [DataMember] public bool CaptureCursor { get; set; } = true;
    [DataMember] public bool MinimizeToTray { get; set; } = true;
    [DataMember] public string RecordingFormat { get; set; } = "MP4";
    [DataMember] public string OutputDirectory { get; set; } = "";

    public void Validate()
    {
        if (FramesPerSecond < 1 || FramesPerSecond > 60) throw new ArgumentOutOfRangeException(nameof(FramesPerSecond));
        if (HistoryLimit < 0 || HistoryLimit > 99999) throw new ArgumentOutOfRangeException(nameof(HistoryLimit));
        if (RecordingFormat != "MP4" && RecordingFormat != "GIF" && RecordingFormat != "WebP") throw new ArgumentException(TextCatalog.Get("不支持此录制格式。"));
        if (new[] { CaptureHotkey, PinHotkey, RecordHotkey }.Any(k => k == null || k.Length > 80)) throw new ArgumentException(TextCatalog.Get("快捷键配置无效。"));
        if (!string.IsNullOrEmpty(OutputDirectory) && !Path.IsPathRooted(OutputDirectory)) throw new ArgumentException(TextCatalog.Get("输出目录需要完整路径。"));
        ValidatePreferences();
    }
}

[DataContract]
public sealed class HistoryEntry
{
    [DataMember] public string Id { get; set; } = "";
    [DataMember] public string Kind { get; set; } = "Image";
    [DataMember] public string FileName { get; set; } = "";
    [DataMember] public string CreatedUtc { get; set; } = "";
    [DataMember] public int Width { get; set; }
    [DataMember] public int Height { get; set; }
    [DataMember] public string RecordingId { get; set; } = "";
    public override string ToString() => TextCatalog.Format("{0}  {1} × {2}  ·  {3}",(Kind == "Image" ? TextCatalog.Get("截图") : Kind),Width,Height,DateTime.TryParse(CreatedUtc, out var time) switch { true => time.ToLocalTime().ToString("MM-dd HH:mm:ss"), _ => "" });
}

public sealed class LocalStore
{
    private readonly ManagedDirectory directory;
    private List<HistoryEntry> history;
    public LocalStore(string root)
    {
        directory = new ManagedDirectory(root);
        LoadIssue = "";
        Settings = Load("settings.json", () => new AppSettings());
        try { Settings.Validate(); }
        catch (ArgumentException) { Settings = new AppSettings(); LoadIssue = TextCatalog.Get("配置无效，已使用默认值；原文件保留。"); }
        history = Load("history.json", () => new List<HistoryEntry>())
            .Where(e => e != null && (e.Kind == "Image" || e.Kind == "MP4" || e.Kind == "GIF" || e.Kind == "WebP") && Guid.TryParseExact(e.Id, "N", out _) && e.FileName == e.Id + Extension(e.Kind))
            .Where(e => File.Exists(directory.Resolve("history/" + e.FileName))).Take(Settings.HistoryLimit).ToList();
    }
    public AppSettings Settings { get; private set; }
    public string Root => directory.Root;
    public string LoadIssue { get; private set; }
    public IReadOnlyList<HistoryEntry> History => history.AsReadOnly();
    public void SaveSettings(AppSettings settings)
    {
        settings.Validate();
        AtomicFile.WriteJson(directory.Resolve("settings.json"), settings);
        Settings = settings;
    }
    public string HistoryPath(HistoryEntry entry) => directory.Resolve("history/" + entry.FileName);
    public HistoryEntry AddImage(int width, int height, Action<Stream> write)
    {
        var entry = Create("Image", width, height);
        if (Settings.HistoryLimit > 0) AtomicFile.Write(HistoryPath(entry), write);
        AddEntry(entry);
        return entry;
    }
    public HistoryEntry ImportRecording(string path, string kind, int width, int height, string recordingId = "")
    {
        var entry = Create(kind, width, height);
        if (!string.IsNullOrEmpty(recordingId) && !Guid.TryParseExact(recordingId, "N", out _)) throw new ArgumentException(TextCatalog.Get("录制标识无效。"));
        entry.RecordingId = recordingId;
        if (Settings.HistoryLimit > 0) AtomicFile.Write(HistoryPath(entry), s => { using (var input = File.OpenRead(path)) input.CopyTo(s); });
        AddEntry(entry);
        return entry;
    }
    public string NewRecordingPath(string format)
    {
        var ext = Extension(format);
        if (ext == ".png") throw new ArgumentException(TextCatalog.Get("录制格式无效。"));
        var relative = "recordings/" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ext;
        var path = directory.Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }
    private void AddEntry(HistoryEntry entry)
    {
        var next = new[] { entry }.Concat(history).Take(Settings.HistoryLimit).ToList();
        AtomicFile.WriteJson(directory.Resolve("history.json"), next);
        var retired = history.Where(e => next.All(n => n.Id != e.Id)).ToArray();
        history = next;
        foreach (var old in retired)
        {
            try { directory.Delete("history/" + old.FileName); }
            catch (IOException) { LoadIssue = TextCatalog.Get("部分过期历史正在使用，本次保留这些文件。"); }
            catch (UnauthorizedAccessException) { LoadIssue = TextCatalog.Get("部分过期历史无法移除，本次保留这些文件。"); }
        }
    }
    private T Load<T>(string relative, Func<T> fallback)
    {
        var path = directory.Resolve(relative);
        if (!File.Exists(path)) return fallback();
        try { return AtomicFile.ReadJson<T>(path); }
        catch (Exception e) when (e is IOException || e is SerializationException || e is ArgumentException)
        { LoadIssue = TextCatalog.Get("本地数据读取失败，已使用默认值；原文件保留。"); return fallback(); }
    }
    private static HistoryEntry Create(string kind, int width, int height)
    {
        var id = Guid.NewGuid().ToString("N");
        return new HistoryEntry { Id = id, Kind = kind, FileName = id + Extension(kind), Width = width, Height = height, CreatedUtc = DateTime.UtcNow.ToString("o") };
    }
    private static string Extension(string kind) => kind switch { "Image" => ".png", "MP4" => ".mp4", "GIF" => ".gif", "WebP" => ".webp", _ => throw new ArgumentException(TextCatalog.Get("不支持此历史记录类型。")) };
}
