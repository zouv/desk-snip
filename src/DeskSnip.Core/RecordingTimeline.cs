using System;

namespace DeskSnip.Core;

public enum RecordingState { Ready, Recording, Paused, Stopped }

/// <summary>调用方传入单调时间；暂停不进入输出时间线。</summary>
public sealed class RecordingTimeline
{
    private TimeSpan accumulated;
    private TimeSpan segmentStart;
    private TimeSpan lastClock;
    public RecordingState State { get; private set; }

    public void Start(TimeSpan now)
    {
        if (State != RecordingState.Ready) throw new InvalidOperationException("Already started.");
        CheckClock(now); segmentStart = now; State = RecordingState.Recording;
    }
    public void Pause(TimeSpan now)
    {
        if (State != RecordingState.Recording) throw new InvalidOperationException("Not recording.");
        CheckClock(now); accumulated += now - segmentStart; State = RecordingState.Paused;
    }
    public void Resume(TimeSpan now)
    {
        if (State != RecordingState.Paused) throw new InvalidOperationException("Not paused.");
        CheckClock(now); segmentStart = now; State = RecordingState.Recording;
    }
    public void Stop(TimeSpan now)
    {
        if (State != RecordingState.Recording && State != RecordingState.Paused) throw new InvalidOperationException("Not started.");
        CheckClock(now);
        if (State == RecordingState.Recording) accumulated += now - segmentStart;
        State = RecordingState.Stopped;
    }
    public TimeSpan Elapsed(TimeSpan now)
    {
        CheckClock(now);
        return State == RecordingState.Recording ? accumulated + now - segmentStart : accumulated;
    }
    private void CheckClock(TimeSpan now)
    {
        if (now < lastClock) throw new ArgumentOutOfRangeException(nameof(now), "Clock must be monotonic.");
        lastClock = now;
    }
}
