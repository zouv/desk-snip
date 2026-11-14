using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DeskSnip.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DeskSnip.App.Recording;

internal sealed class AudioDevice
{
    internal AudioDevice(string id, string name) { Id = id; Name = name; }
    internal string Id { get; }
    internal string Name { get; }
    public override string ToString() => Name;
}

internal static class AudioDevices
{
    internal static IReadOnlyList<AudioDevice> List(bool loopback)
    {
        using (var devices = new MMDeviceEnumerator())
        {
            var result = new List<AudioDevice>();
            foreach (var device in devices.EnumerateAudioEndPoints(loopback ? DataFlow.Render : DataFlow.Capture, DeviceState.Active))
            { using (device) result.Add(new AudioDevice(device.ID, device.FriendlyName)); }
            return result;
        }
    }
}

internal sealed class AudioCapture : IDisposable
{
    private readonly object gate = new object();
    private readonly MMDevice device;
    private readonly WasapiCapture capture;
    private readonly WaveFileWriter writer;
    private readonly Func<(bool Recording, long Ticks)> position;
    private readonly ManualResetEventSlim stopped = new ManualResetEventSlim();
    private bool finishing;
    private bool started;
    private Exception? failure;
    internal Exception? Failure { get { lock (gate) return failure; } }
    internal AudioCapture(string path, bool loopback, string deviceId, Func<(bool Recording, long Ticks)> position)
    {
        this.position = position;
        using (var enumerator = new MMDeviceEnumerator()) device = string.IsNullOrEmpty(deviceId) ? enumerator.GetDefaultAudioEndpoint(loopback ? DataFlow.Render : DataFlow.Capture, Role.Multimedia) : enumerator.GetDevice(deviceId);
        capture = loopback ? new WasapiLoopbackCapture(device) : new WasapiCapture(device);
        writer = new WaveFileWriter(path, capture.WaveFormat);
        capture.DataAvailable += Data;
        capture.RecordingStopped += (_, e) => { lock (gate) { if (e.Exception != null) failure = e.Exception; } stopped.Set(); };
    }
    internal void Start() { capture.StartRecording(); started = true; }
    private void Data(object? sender, WaveInEventArgs e)
    {
        var stamp = position();
        lock (gate)
        {
            if (finishing || !stamp.Recording || failure != null) return;
            try
            {
                var format = capture.WaveFormat; var placement = AudioTimeline.Place(stamp.Ticks, e.BytesRecorded / format.BlockAlign, format.SampleRate, writer.Length / format.BlockAlign);
                Silence(placement.SilenceFrames);
                if (placement.Frames > 0) writer.Write(e.Buffer, placement.SkipFrames * format.BlockAlign, placement.Frames * format.BlockAlign);
            }
            catch (Exception error) when (error is IOException || error is ArgumentException) { failure = error; }
        }
    }
    private void Silence(long frames)
    {
        var bytes = frames * capture.WaveFormat.BlockAlign; var buffer = new byte[65536];
        if (capture.WaveFormat.BitsPerSample == 8 && capture.WaveFormat.Encoding == WaveFormatEncoding.Pcm) for (var i = 0; i < buffer.Length; i++) buffer[i] = 128;
        while (bytes > 0) { var count = (int)Math.Min(bytes, buffer.Length); writer.Write(buffer, 0, count); bytes -= count; }
    }
    internal void Stop(long ticks)
    {
        lock (gate) finishing = true;
        if (started) { capture.StopRecording(); if (!stopped.Wait(TimeSpan.FromSeconds(5))) throw new InvalidOperationException(L.Get("音频设备未响应停止请求；素材保留，请重试。")); started = false; }
        lock (gate)
        {
            var frames = (long)(ticks / (double)TimeSpan.TicksPerSecond * capture.WaveFormat.SampleRate); Silence(Math.Max(0, frames - writer.Length / capture.WaveFormat.BlockAlign)); writer.Flush();
        }
    }
    public void Dispose()
    {
        try { if (started) Stop(position().Ticks); }
        finally { capture.Dispose(); writer.Dispose(); device.Dispose(); stopped.Dispose(); }
    }
}
