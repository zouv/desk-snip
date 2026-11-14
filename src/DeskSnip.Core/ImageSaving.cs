using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace DeskSnip.Core;

public static class SaveNaming
{
    private static readonly Regex token = new Regex(@"\{([a-z]+)(?::([^{}]+))?\}", RegexOptions.CultureInvariant);
    public static string Expand(string template, DateTime now, int width, int height, string kind, string title = "")
    {
        if (string.IsNullOrWhiteSpace(template) || template.Length > 300) throw new ArgumentException(TextCatalog.Get("文件名模板需要 1–300 字符。"));
        var expanded = token.Replace(template, match =>
        {
            var format = match.Groups[2].Value;
            switch (match.Groups[1].Value)
            {
                case "date": return now.ToString(format == "" ? "yyyyMMdd" : format, CultureInfo.InvariantCulture);
                case "time": return now.ToString(format == "" ? "HHmmss" : format, CultureInfo.InvariantCulture);
                case "width": return width.ToString(CultureInfo.InvariantCulture);
                case "height": return height.ToString(CultureInfo.InvariantCulture);
                case "kind": return kind;
                case "title": return title;
                default: throw new ArgumentException(TextCatalog.Join(TextCatalog.Get("不支持的文件名变量："),match.Groups[1].Value));
            }
        });
        return Sanitize(expanded);
    }
    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars(); var sanitized = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');
        if (sanitized.Length > 160) sanitized = sanitized.Substring(0, 160).TrimEnd('.', ' ');
        if (sanitized.Length == 0) sanitized = "DeskSnip";
        if (Regex.IsMatch(sanitized, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) sanitized = "_" + sanitized;
        return sanitized;
    }
    public static string ChoosePath(string directory, string basename, string extension, bool overwrite = false)
    {
        if (!Regex.IsMatch(extension, @"^\.[a-zA-Z0-9]{1,8}$")) throw new ArgumentException(TextCatalog.Get("文件扩展名无效。"));
        var root = new ManagedDirectory(directory); var safe = Sanitize(basename);
        var target = root.Resolve(safe + extension); if (overwrite || !File.Exists(target)) return target;
        for (var index = 1; index <= 100000; index++) { target = root.Resolve(safe + " (" + index + ")" + extension); if (!File.Exists(target)) return target; }
        throw new IOException(TextCatalog.Get("同名文件过多，请修改命名模板。"));
    }
}

[DataContract]
public sealed class AutoArchiveEntry
{
    [DataMember] public string File { get; set; } = "";
    [DataMember] public string CreatedUtc { get; set; } = "";
    [DataMember] public long Bytes { get; set; }
}

/// <summary>清理仅操作本应用登记的文件；未登记文件及用户手工输出保留。</summary>
public sealed class AutomaticArchive
{
    private readonly ManagedDirectory directory;
    private List<AutoArchiveEntry> entries = new List<AutoArchiveEntry>();
    public AutomaticArchive(string root)
    {
        directory = new ManagedDirectory(root); var index = directory.Resolve(".desksnip-auto.json");
        if (File.Exists(index))
        {
            var loaded = AtomicFile.ReadJson<List<AutoArchiveEntry>>(index);
            if (loaded == null || loaded.Count > 100000 || loaded.Any(e => e == null || !ValidName(e.File) || !DateTime.TryParse(e.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) || e.Bytes < 0)) throw new ArgumentException(TextCatalog.Get("自动保存索引无效，原文件保留。"));
            entries = loaded;
        }
    }
    private static bool ValidName(string name) => name != null && name.Length <= 200 && name.StartsWith("DeskSnip-auto-", StringComparison.Ordinal) && name == Path.GetFileName(name) && !name.Contains(':') && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    public string Save(DateTime now, string basename, string extension, Action<string> write, int days, long maximumBytes, bool cleanByAge = true, bool overwrite = false)
    {
        if (days < 1 || days > 36500 || maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(days));
        var name = "DeskSnip-auto-" + SaveNaming.Sanitize(basename);
        var target = SaveNaming.ChoosePath(directory.Root, name, extension, overwrite); write(target);
        var next = entries.Where(entry => entry.File != Path.GetFileName(target)).Concat(new[] { new AutoArchiveEntry { File = Path.GetFileName(target), CreatedUtc = now.ToUniversalTime().ToString("o"), Bytes = new FileInfo(target).Length } }).ToList();
        var candidates = next.OrderByDescending(e => e.CreatedUtc, StringComparer.Ordinal).ToArray(); var keep = new List<AutoArchiveEntry>(); long size = 0;
        foreach (var entry in candidates)
        {
            if (!File.Exists(directory.Resolve(entry.File))) continue;
            if (entry.File == Path.GetFileName(target) || (!cleanByAge || DateTime.Parse(entry.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) >= now.ToUniversalTime().AddDays(-days)) && size + entry.Bytes <= maximumBytes)
            { keep.Add(entry); size += entry.Bytes; }
        }
        AtomicFile.WriteJson(directory.Resolve(".desksnip-auto.json"), keep);
        var retired = next.Where(e => keep.All(k => k.File != e.File)).ToArray(); entries = keep;
        foreach (var entry in retired) directory.Delete(entry.File);
        return target;
    }
}
