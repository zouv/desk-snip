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
        if (string.Equals(full, Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Use a dedicated data directory.", nameof(root));
        Root = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        RejectReparsePoints(Root);
        Directory.CreateDirectory(Root);
    }
    public string Root { get; }
    public string Resolve(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new ArgumentException("A relative path is required.", nameof(relative));
        var full = Path.GetFullPath(Path.Combine(Root, relative));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Path escapes data directory.", nameof(relative));
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
                throw new IOException("Data paths cannot contain links or junctions.");
        }
    }
}

public static class AtomicFile
{
    public static string ValidateTarget(string path)
    {
        var full = Path.GetFullPath(path);
        ManagedDirectory.RejectReparsePoints(full);
        if (string.IsNullOrEmpty(Path.GetFileName(full))) throw new ArgumentException("A file path is required.", nameof(path));
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
            return (T)(new DataContractJsonSerializer(typeof(T)).ReadObject(stream) ?? throw new SerializationException("Empty document."));
    }
}

[DataContract]
public sealed class AppSettings
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
        if (FramesPerSecond < 1 || FramesPerSecond > 30) throw new ArgumentOutOfRangeException(nameof(FramesPerSecond));
        if (HistoryLimit < 1 || HistoryLimit > 1000) throw new ArgumentOutOfRangeException(nameof(HistoryLimit));
        if (RecordingFormat != "MP4" && RecordingFormat != "GIF") throw new ArgumentException("Unsupported recording format.");
        if (string.IsNullOrWhiteSpace(CaptureHotkey) || string.IsNullOrWhiteSpace(PinHotkey) || string.IsNullOrWhiteSpace(RecordHotkey))
            throw new ArgumentException("Hotkeys are required.");
        if (!string.IsNullOrEmpty(OutputDirectory) && !Path.IsPathRooted(OutputDirectory)) throw new ArgumentException("Output directory must be absolute.");
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
    public override string ToString() => $"{(Kind == "Image" ? "截图" : Kind)}  {Width} × {Height}  ·  {DateTime.TryParse(CreatedUtc, out var time) switch { true => time.ToLocalTime().ToString("MM-dd HH:mm:ss"), _ => "" }}";
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
        catch (ArgumentException) { Settings = new AppSettings(); LoadIssue = "配置无效，已使用默认值；原文件保留。"; }
        history = Load("history.json", () => new List<HistoryEntry>())
            .Where(e => e != null && (e.Kind == "Image" || e.Kind == "MP4" || e.Kind == "GIF") && Guid.TryParseExact(e.Id, "N", out _) && e.FileName == e.Id + Extension(e.Kind))
            .Where(e => File.Exists(directory.Resolve("history/" + e.FileName))).Take(1000).ToList();
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
        AtomicFile.Write(HistoryPath(entry), write);
        AddEntry(entry);
        return entry;
    }
    public HistoryEntry ImportRecording(string path, string kind, int width, int height)
    {
        var entry = Create(kind, width, height);
        AtomicFile.Write(HistoryPath(entry), s => { using (var input = File.OpenRead(path)) input.CopyTo(s); });
        AddEntry(entry);
        return entry;
    }
    public string NewRecordingPath(string format)
    {
        var ext = Extension(format);
        if (ext == ".png") throw new ArgumentException("Invalid recording format.");
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
            catch (IOException) { LoadIssue = "部分过期历史正在使用，本次保留这些文件。"; }
            catch (UnauthorizedAccessException) { LoadIssue = "部分过期历史无法移除，本次保留这些文件。"; }
        }
    }
    private T Load<T>(string relative, Func<T> fallback)
    {
        var path = directory.Resolve(relative);
        if (!File.Exists(path)) return fallback();
        try { return AtomicFile.ReadJson<T>(path); }
        catch (Exception e) when (e is IOException || e is SerializationException || e is ArgumentException)
        { LoadIssue = "本地数据读取失败，已使用默认值；原文件保留。"; return fallback(); }
    }
    private static HistoryEntry Create(string kind, int width, int height)
    {
        var id = Guid.NewGuid().ToString("N");
        return new HistoryEntry { Id = id, Kind = kind, FileName = id + Extension(kind), Width = width, Height = height, CreatedUtc = DateTime.UtcNow.ToString("o") };
    }
    private static string Extension(string kind) => kind switch { "Image" => ".png", "MP4" => ".mp4", "GIF" => ".gif", _ => throw new ArgumentException("Unsupported history kind.") };
}
