using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DeskSnip.Core;

public static class RecognitionOperations
{
    public static readonly string[] All = { "text", "translation", "table", "formula", "document" };
}

[DataContract]
public sealed class RecognitionRequest
{
    [DataMember(Name = "schema")] public int Schema { get; set; } = 1;
    [DataMember(Name = "operation")] public string Operation { get; set; } = "text";
    [DataMember(Name = "imageBase64", EmitDefaultValue = false)] public string? ImageBase64 { get; set; }
    [DataMember(Name = "text", EmitDefaultValue = false)] public string? Text { get; set; }
    [DataMember(Name = "sourceLanguage")] public string SourceLanguage { get; set; } = "auto";
    [DataMember(Name = "targetLanguage")] public string TargetLanguage { get; set; } = "zh";
    public void Validate()
    {
        if (Schema != 1 || !RecognitionOperations.All.Contains(Operation) || (string.IsNullOrEmpty(ImageBase64) && string.IsNullOrWhiteSpace(Text)) || (ImageBase64?.Length ?? 0) > 28_000_000 || (Text?.Length ?? 0) > 500_000 ||
            SourceLanguage == null || SourceLanguage.Length > 80 || TargetLanguage == null || TargetLanguage.Length > 80) throw new ArgumentException(TextCatalog.Get("识别请求类型、内容或语言无效。"));
        if (!string.IsNullOrEmpty(ImageBase64)) RecognitionImages.DecodePng(ImageBase64!);
    }
}

[DataContract]
public sealed class RecognitionAsset
{
    [DataMember(Name = "name")] public string Name { get; set; } = "";
    [DataMember(Name = "mime")] public string Mime { get; set; } = "image/png";
    [DataMember(Name = "base64")] public string Base64 { get; set; } = "";
    public RecognitionAsset Clone() => (RecognitionAsset)MemberwiseClone();
}

[DataContract]
public sealed class RecognitionResponse
{
    [DataMember(Name = "schema")] public int Schema { get; set; } = 1;
    [DataMember(Name = "operation")] public string Operation { get; set; } = "text";
    [DataMember(Name = "text")] public string Text { get; set; } = "";
    [DataMember(Name = "translatedText")] public string TranslatedText { get; set; } = "";
    [DataMember(Name = "markdown")] public string Markdown { get; set; } = "";
    [DataMember(Name = "formulas")] public List<string> Formulas { get; set; } = new List<string>();
    [DataMember(Name = "table", EmitDefaultValue = false)] public StructuredTable? Table { get; set; }
    [DataMember(Name = "assets")] public List<RecognitionAsset> Assets { get; set; } = new List<RecognitionAsset>();
    [OnDeserializing] private void Loading(StreamingContext context) { Text = TranslatedText = Markdown = ""; Formulas = new List<string>(); Assets = new List<RecognitionAsset>(); }
    public void Validate(string? expectedOperation = null)
    {
        if (Schema != 1 || !RecognitionOperations.All.Contains(Operation) || expectedOperation != null && Operation != expectedOperation || Text == null || TranslatedText == null || Markdown == null ||
            Text.Length + TranslatedText.Length + Markdown.Length > 2_000_000 || Formulas == null || Formulas.Count > 100 || Formulas.Any(f => f == null || f.Length > 100000) || Assets == null || Assets.Count > 30)
            throw new ArgumentException(TextCatalog.Get("服务返回格式或内容大小无效，原结果保留。"));
        if (Operation == "table" && Table == null) throw new ArgumentException(TextCatalog.Get("服务没有返回表格单元格。"));
        if (Operation == "formula" && Formulas.Count == 0) throw new ArgumentException(TextCatalog.Get("服务没有返回公式源码。"));
        if (Operation == "text" && string.IsNullOrWhiteSpace(Text) || Operation == "translation" && string.IsNullOrWhiteSpace(TranslatedText) || Operation == "document" && string.IsNullOrWhiteSpace(Markdown))
            throw new ArgumentException(TextCatalog.Get("服务没有返回有效内容，原结果保留。"));
        Table?.Validate();
        if (Assets.Any(a => a == null || a.Name == null || !Regex.IsMatch(a.Name, @"^[A-Za-z0-9_-]{1,80}\.(png|jpg|jpeg)$", RegexOptions.CultureInvariant) ||
            (a.Mime != "image/png" && a.Mime != "image/jpeg") || a.Base64 == null || a.Base64.Length > 8_000_000) || Assets.Sum(a => (long)a.Base64.Length) > 28_000_000 || Assets.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Assets.Count)
            throw new ArgumentException(TextCatalog.Get("服务配图无效或过大，原结果保留。"));
        long totalAssetPixels = 0;
        foreach (var asset in Assets)
        {
            var bytes = RecognitionImages.Decode(asset.Base64);
            if (asset.Mime == "image/png" ? !RecognitionImages.IsPng(bytes) : !RecognitionImages.IsJpeg(bytes)) throw new ArgumentException(TextCatalog.Get("服务配图类型与内容不一致。"));
            if (asset.Mime == "image/png" ? !asset.Name.EndsWith(".png", StringComparison.Ordinal) : asset.Name.EndsWith(".png", StringComparison.Ordinal)) throw new ArgumentException(TextCatalog.Get("服务配图扩展名与类型不一致。"));
            var dimensions = RecognitionImages.Dimensions(bytes); totalAssetPixels += (long)dimensions.Width * dimensions.Height;
            if (totalAssetPixels > 48_000_000) throw new ArgumentException(TextCatalog.Get("服务配图合计超过 4800 万像素。"));
        }
    }
    public RecognitionResponse Clone() => new RecognitionResponse { Schema = Schema, Operation = Operation, Text = Text, TranslatedText = TranslatedText, Markdown = Markdown,
        Formulas = Formulas.ToList(), Table = Table?.Clone(), Assets = Assets.Select(a => a.Clone()).ToList() };
}

public static class RecognitionEndpoint
{
    public static Uri Validate(string address)
    {
        if (address == null || address.Length > 2000 || !Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0 ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))) throw new ArgumentException(TextCatalog.Get("服务地址需要 HTTPS；本地模拟服务可使用 http://127.0.0.1。地址不能包含账号密码或片段。"));
        return uri;
    }
}

public sealed class ServiceCredentialStore
{
    private readonly ManagedDirectory directory;
    private static readonly byte[] entropy = Encoding.UTF8.GetBytes("DeskSnip recognition service v1");
    public ServiceCredentialStore(string root) { directory = new ManagedDirectory(root); }
    public string Read()
    {
        var file = directory.Resolve("credentials/recognition.dat"); if (!File.Exists(file)) return "";
        var bytes = File.ReadAllBytes(file); if (bytes.Length > 65536) throw new ArgumentException(TextCatalog.Get("本机密钥文件无效。"));
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, entropy, DataProtectionScope.CurrentUser));
    }
    public void Write(string value)
    {
        if (value == null || value.Length > 8192 || value.Contains('\r') || value.Contains('\n')) throw new ArgumentException(TextCatalog.Get("访问密钥无效。"));
        if (value.Length == 0) { directory.Delete("credentials/recognition.dat"); return; }
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), entropy, DataProtectionScope.CurrentUser);
        AtomicFile.Write(directory.Resolve("credentials/recognition.dat"), stream => stream.Write(protectedBytes, 0, protectedBytes.Length));
    }
    public void Change(string value, Action commit)
    {
        var path = directory.Resolve("credentials/recognition.dat"); var previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
        if (previous != null && previous.Length > 65536) throw new ArgumentException(TextCatalog.Get("本机密钥文件无效。"));
        Write(value);
        try { commit(); }
        catch
        {
            if (previous == null) directory.Delete("credentials/recognition.dat");
            else AtomicFile.Write(path, stream => stream.Write(previous, 0, previous.Length));
            throw;
        }
    }
    public string Resolve(string environmentName)
    {
        if (string.IsNullOrEmpty(environmentName)) return Read();
        if (!Regex.IsMatch(environmentName, "^[A-Za-z_][A-Za-z0-9_]{0,127}$")) throw new ArgumentException(TextCatalog.Get("密钥环境变量名无效。"));
        return Environment.GetEnvironmentVariable(environmentName) ?? Read();
    }
}

public sealed class RecognitionResultStore
{
    private readonly ManagedDirectory directory;
    public RecognitionResultStore(string root) { directory = new ManagedDirectory(root); }
    public void Save(RecognitionResponse result) => SaveSession(new StoredRecognitionSession { Result = result });
    public void SaveSession(StoredRecognitionSession session) { session.Validate(); AtomicFile.WriteJson(directory.Resolve("recognition/last-session.json"), session); }
    public RecognitionResponse? Read()
        => ReadSession()?.Result;
    public StoredRecognitionSession? ReadSession()
    {
        var path = directory.Resolve("recognition/last-session.json");
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new ArgumentException(TextCatalog.Get("本机识别结果过大。"));
        var session = AtomicFile.ReadJson<StoredRecognitionSession>(path); session.Validate(); return session;
    }
}

[DataContract]
public sealed class StoredRecognitionSession
{
    [DataMember(Name = "schema")] public int Schema { get; set; } = 1;
    [DataMember(Name = "result")] public RecognitionResponse Result { get; set; } = new RecognitionResponse();
    [DataMember(Name = "imageBase64", EmitDefaultValue = false)] public string? ImageBase64 { get; set; }
    [DataMember(Name = "requestText", EmitDefaultValue = false)] public string? RequestText { get; set; }
    public void Validate()
    {
        if (Schema != 1 || Result == null || (ImageBase64?.Length ?? 0) > 28_000_000 || (RequestText?.Length ?? 0) > 500_000) throw new ArgumentException(TextCatalog.Get("本机识别结果无效。"));
        Result.Validate(); if (!string.IsNullOrEmpty(ImageBase64)) RecognitionImages.DecodePng(ImageBase64!);
    }
}

public static class RecognitionImages
{
    public static byte[] Decode(string value)
    {
        try { return Convert.FromBase64String(value); }
        catch (FormatException) { throw new ArgumentException(TextCatalog.Get("图片编码无效。")); }
    }
    public static bool IsPng(byte[] value) => value.Length >= 33 && value.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && Encoding.ASCII.GetString(value, 12, 4) == "IHDR";
    public static bool IsJpeg(byte[] value) => value.Length >= 4 && value[0] == 255 && value[1] == 216 && value[2] == 255;
    public static byte[] DecodePng(string value)
    {
        var bytes = Decode(value); if (!IsPng(bytes)) throw new ArgumentException(TextCatalog.Get("请求原图需要 PNG 格式。")); Dimensions(bytes); return bytes;
    }
    public static (int Width, int Height) Dimensions(byte[] bytes)
    {
        if (IsPng(bytes))
        {
            long Int32(int offset) => ((long)bytes[offset] << 24) | ((long)bytes[offset + 1] << 16) | ((long)bytes[offset + 2] << 8) | bytes[offset + 3];
            return CheckDimensions(Int32(16), Int32(20));
        }
        if (IsJpeg(bytes))
        {
            for (var offset = 2; offset + 3 < bytes.Length;)
            {
                if (bytes[offset++] != 255) break;
                while (offset < bytes.Length && bytes[offset] == 255) offset++;
                if (offset >= bytes.Length) break; var marker = bytes[offset++];
                if (marker == 0xd8 || marker == 0x01 || marker >= 0xd0 && marker <= 0xd7) continue;
                if (marker == 0xd9 || marker == 0xda || offset + 1 >= bytes.Length) break;
                var length = bytes[offset] * 256 + bytes[offset + 1]; if (length < 2 || offset + length > bytes.Length) break;
                if (marker >= 0xc0 && marker <= 0xcf && marker != 0xc4 && marker != 0xc8 && marker != 0xcc && length >= 8)
                    return CheckDimensions(bytes[offset + 5] * 256 + bytes[offset + 6], bytes[offset + 3] * 256 + bytes[offset + 4]);
                offset += length;
            }
        }
        throw new ArgumentException(TextCatalog.Get("图片尺寸或内容无效。"));
    }
    private static (int Width, int Height) CheckDimensions(long width, long height)
    {
        if (width < 1 || height < 1 || width > 20000 || height > 20000 || width * height > 32_000_000) throw new ArgumentException(TextCatalog.Get("识别图片最多 3200 万像素，单边不超过 20000 像素。"));
        return ((int)width, (int)height);
    }
}
