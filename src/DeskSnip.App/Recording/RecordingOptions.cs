using DeskSnip.Core;

namespace DeskSnip.App.Recording;

internal sealed class RecordingOptions
{
    internal string DataRoot { get; set; } = "";
    internal string FfmpegPath { get; set; } = "";
    internal bool SystemAudio { get; set; }
    internal bool Microphone { get; set; }
    internal string SystemDeviceId { get; set; } = "";
    internal string MicrophoneDeviceId { get; set; } = "";
    internal int Quality { get; set; } = 80;
    internal VideoEncoder Encoder { get; set; } = VideoEncoder.Software;
}
