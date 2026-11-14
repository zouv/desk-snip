using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DeskSnip.Core;

namespace DeskSnip.App.Recording;

internal sealed class MediaInfo
{
    internal int Width { get; set; }
    internal int Height { get; set; }
    internal double Duration { get; set; }
    internal bool Audio { get; set; }
}

internal static class Ffmpeg
{
    internal static string Find(string configured = "", string name = "ffmpeg.exe")
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Path.IsPathRooted(configured) || !File.Exists(configured) || !string.Equals(Path.GetExtension(configured), ".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(L.Get("请选择可用的 FFmpeg 可执行文件。"));
            return Path.GetFullPath(configured);
        }
        var bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", name); if (File.Exists(bundled)) return bundled;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            try { var candidate = Path.Combine(directory.Trim('"'), name); if (File.Exists(candidate)) return Path.GetFullPath(candidate); }
            catch (ArgumentException) { }
        }
        throw new InvalidOperationException(L.Get("此功能需要 FFmpeg。请在设置中选择本机 ffmpeg.exe，或将它加入 PATH；基础 MP4/GIF 录制仍可使用。"));
    }
    internal static MediaInfo Probe(string path, string configured = "", CancellationToken cancellation = default)
    {
        var executable = Find(configured); var probe = Path.Combine(Path.GetDirectoryName(executable)!, "ffprobe.exe");
        if (!File.Exists(probe)) probe = Find(name: "ffprobe.exe");
        var json = Run(probe, new[] { "-v", "error", "-show_entries", "format=duration:stream=codec_type,width,height", "-of", "json", path }, cancellation);
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
        {
            var result = (ProbeResult)new DataContractJsonSerializer(typeof(ProbeResult)).ReadObject(stream)!;
            var video = result.Streams?.FirstOrDefault(s => s.Kind == "video") ?? throw new InvalidOperationException(L.Get("文件没有可读取的视频流。"));
            if (result.Format == null || !double.TryParse(result.Format.Duration, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) || duration <= 0) throw new InvalidOperationException(L.Get("视频时长无法读取。"));
            return new MediaInfo { Width = video.Width, Height = video.Height, Duration = duration, Audio = result.Streams!.Any(s => s.Kind == "audio") };
        }
    }
    [DataContract] private sealed class ProbeResult { [DataMember(Name = "streams")] public List<ProbeStream>? Streams { get; set; } [DataMember(Name = "format")] public ProbeFormat? Format { get; set; } }
    [DataContract] private sealed class ProbeStream { [DataMember(Name = "codec_type")] public string Kind { get; set; } = ""; [DataMember(Name = "width")] public int Width { get; set; } [DataMember(Name = "height")] public int Height { get; set; } }
    [DataContract] private sealed class ProbeFormat { [DataMember(Name = "duration")] public string Duration { get; set; } = ""; }
    private static string Number(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    internal static string Run(string executable, IEnumerable<string> arguments, CancellationToken cancellation, double seconds = 0, Action<double>? progress = null)
    {
        cancellation.ThrowIfCancellationRequested();
        using (var process = new Process { StartInfo = new ProcessStartInfo(executable, string.Join(" ", arguments.Select(ProcessArguments.Quote)))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true } })
        {
            if (!process.Start()) throw new InvalidOperationException(L.Get("媒体处理程序无法启动。"));
            var output = new StringBuilder();
            var readOutput = Task.Run(async () =>
            {
                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync()) != null)
                {
                    if (output.Length < 2_000_000) output.AppendLine(line);
                    if (seconds > 0 && line.StartsWith("out_time_us=", StringComparison.Ordinal) && long.TryParse(line.Substring(12), out var microseconds)) progress?.Invoke(Math.Max(0, Math.Min(99, microseconds / (seconds * 10000))));
                }
            });
            var readError = process.StandardError.ReadToEndAsync();
            using (cancellation.Register(() => { try { if (!process.HasExited) process.StandardInput.WriteLine("q"); } catch (Exception error) when (error is IOException || error is InvalidOperationException) { } }))
            { process.WaitForExit(); }
            readOutput.GetAwaiter().GetResult(); readError.GetAwaiter().GetResult(); cancellation.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new InvalidOperationException(L.Get("媒体处理未完成；原文件和录制素材保留，可检查编码器或重新导出。"));
            progress?.Invoke(100); return output.ToString();
        }
    }
    internal static string TestEncoder(string executable, VideoEncoder preference, CancellationToken cancellation = default)
    {
        var candidates = preference == VideoEncoder.Software ? Array.Empty<string>() : preference == VideoEncoder.Auto ? new[] { "h264_nvenc", "h264_qsv", "h264_amf" } :
            new[] { preference == VideoEncoder.Nvidia ? "h264_nvenc" : preference == VideoEncoder.Intel ? "h264_qsv" : "h264_amf" };
        foreach (var encoder in candidates)
        {
            try { Run(executable, new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=size=64x64:rate=10", "-frames:v", "1", "-c:v", encoder, "-f", "null", "-" }, cancellation); return encoder; }
            catch (InvalidOperationException) { }
        }
        return "libx264";
    }
    internal static void Join(RecordingJournal journal, string output, string configured, CancellationToken cancellation)
    {
        var segments = journal.Snapshot.Segments; if (segments.Count == 0) throw new ArgumentException(L.Get("没有可恢复的已完成片段。"));
        var list = journal.FilePath("segments.txt");
        AtomicFile.Write(list, stream => { using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true)) foreach (var segment in segments) writer.WriteLine("file '" + segment.File + "'"); });
        WriteOutput(output, temporary => Run(Find(configured), new[] { "-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "1", "-i", list, "-c", "copy", "-movflags", "+faststart", temporary }, cancellation));
    }
    internal static string Export(string input, string output, VideoEditOptions options, string configured, string? systemAudio, string? microphoneAudio, CancellationToken cancellation, Action<double>? progress = null)
    {
        cancellation.ThrowIfCancellationRequested(); var executable = Find(configured); var info = Probe(input, configured, cancellation); options.Validate(info.Duration, info.Width, info.Height);
        var extension = Path.GetExtension(output).ToLowerInvariant(); if (extension != ".mp4" && extension != ".gif" && extension != ".webp") throw new ArgumentException(L.Get("请选择 MP4、GIF 或 WebP 文件。"));
        var encoder = extension == ".mp4" ? TestEncoder(executable, options.Encoder, cancellation) : extension == ".webp" ? "libwebp_anim" : "gif";
        void Encode(string temporary, string selectedEncoder)
        {
            var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-progress", "pipe:1", "-i", input };
            var tracks = new List<(int Input, double Volume)>(); var inputIndex = 1;
            foreach (var track in new[] { (Path: systemAudio, Volume: options.SystemVolume), (Path: microphoneAudio, Volume: options.MicrophoneVolume) })
                if (!string.IsNullOrEmpty(track.Path) && File.Exists(track.Path)) { args.AddRange(new[] { "-i", track.Path! }); tracks.Add((inputIndex++, track.Volume)); }
            var range = "start=" + Number(options.StartSeconds) + ":end=" + Number(options.EndSeconds);
            var crop = options.Crop; if (extension == ".mp4") crop = crop.EvenSize();
            var filters = new List<string> { "[0:v]trim=" + range + ",setpts=(PTS-STARTPTS)/" + Number(options.Speed) + ",crop=" + crop.Width + ":" + crop.Height + ":" + crop.X + ":" + crop.Y + ",fps=" + options.FramesPerSecond + "[v]" };
            if (extension == ".gif") filters[0] = filters[0].Replace("[v]", ",split[v0][v1];[v0]palettegen=stats_mode=diff[p];[v1][p]paletteuse=dither=bayer[v]");
            var audioLabels = new List<string>();
            if (extension == ".mp4")
            {
                if (tracks.Count == 0 && info.Audio) tracks.Add((0, options.SystemVolume));
                for (var index = 0; index < tracks.Count; index++)
                {
                    var track = tracks[index]; var label = "a" + index; var tempo = options.Speed < .5 ? "atempo=0.5,atempo=" + Number(options.Speed / .5) : "atempo=" + Number(options.Speed);
                    filters.Add("[" + track.Input + ":a]atrim=" + range + ",asetpts=PTS-STARTPTS,aresample=48000," + tempo + ",volume=" + Number(track.Volume) + ",apad[" + label + "]"); audioLabels.Add("[" + label + "]");
                }
                if (audioLabels.Count > 0) filters.Add(string.Concat(audioLabels) + "amix=inputs=" + audioLabels.Count + ":duration=longest:normalize=0,alimiter=limit=0.95[a]");
            }
            args.AddRange(new[] { "-filter_complex", string.Join(";", filters), "-map", "[v]" });
            if (extension == ".mp4" && audioLabels.Count > 0) args.AddRange(new[] { "-map", "[a]", "-c:a", "aac", "-b:a", "192k", "-t", Number(options.OutputSeconds) }); else args.Add("-an");
            args.AddRange(new[] { "-c:v", selectedEncoder });
            if (extension == ".mp4")
            {
                args.AddRange(new[] { "-pix_fmt", "yuv420p", "-movflags", "+faststart" });
                if (selectedEncoder == "libx264") args.AddRange(new[] { "-crf", Math.Max(14, 40 - options.Quality * 26 / 100).ToString(CultureInfo.InvariantCulture), "-preset", "veryfast" });
                else args.AddRange(new[] { "-b:v", Math.Max(1_000_000, crop.Width * (double)crop.Height * options.FramesPerSecond * options.Quality / 400).ToString("0", CultureInfo.InvariantCulture) });
            }
            else if (extension == ".webp") args.AddRange(new[] { "-quality", options.Quality.ToString(CultureInfo.InvariantCulture), "-loop", "0" });
            else args.AddRange(new[] { "-loop", "0" });
            args.Add(temporary); Run(executable, args, cancellation, options.OutputSeconds, progress);
        }
        WriteOutput(output, temporary =>
        {
            try { Encode(temporary, encoder); }
            catch (InvalidOperationException) when (extension == ".mp4" && encoder != "libx264") { encoder = "libx264"; Encode(temporary, encoder); }
        });
        return encoder;
    }
    internal static void WriteOutput(string output, Action<string> write)
    {
        var target = AtomicFile.ValidateTarget(output); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, ".desksnip-export-" + Guid.NewGuid().ToString("N") + Path.GetExtension(target));
        try { write(temporary); if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0) throw new IOException(L.Get("媒体输出为空。")); if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
