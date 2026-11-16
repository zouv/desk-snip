using DeskSnip.Core;

namespace DeskSnip.App;

/// <summary>选区界面的录制选择；只在本次录制生效，音频/帧率偏好由控制器保存。</summary>
internal sealed class RecordingPlan
{
    internal bool Quick { get; set; }
    internal bool SystemAudio { get; set; }
    internal bool Microphone { get; set; }
    internal int Fps { get; set; } = 16;
    internal string Format { get; set; } = "MP4";
    internal bool ToolbarVisible { get; set; } = true;
    internal static RecordingPlan FromSettings(AppSettings settings)
        => new RecordingPlan
        {
            Quick = settings.QuickRecordingEnabled, SystemAudio = settings.RecordSystemAudio, Microphone = settings.RecordMicrophone,
            Fps = settings.FramesPerSecond, Format = settings.QuickRecordingEnabled ? "MP4" : settings.RecordingFormat,
            ToolbarVisible = settings.RecordingToolbarDisplayMode != "Hidden" || settings.TrayIconMode == "Hidden"
        };
}
