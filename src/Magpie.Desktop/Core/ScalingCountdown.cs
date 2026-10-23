using System.Diagnostics;
namespace Magpie.Desktop.Core;

internal sealed class ScalingCountdown(Func<long>? clock=null)
{
    private readonly Func<long> _clock=clock??Stopwatch.GetTimestamp;
    private long _started;
    private int _seconds;
    internal bool Active=>_seconds>0;
    internal bool Fullscreen { get; private set; }
    internal double SecondsLeft=>Active?Math.Max(0,_seconds-Stopwatch.GetElapsedTime(_started,_clock()).TotalSeconds):0;
    internal double Progress=>Active?Math.Clamp(SecondsLeft/_seconds,0,1):1;
    internal int LabelSeconds=>(int)Math.Ceiling(SecondsLeft);
    internal void Toggle(bool fullscreen,int seconds)
    {
        if(seconds is <1 or >5)throw new InvalidDataException("Countdown must be between one and five seconds.");
        if(Active&&Fullscreen==fullscreen){Cancel();return;}
        Fullscreen=fullscreen;_seconds=seconds;_started=_clock();
    }
    internal void Cancel()=>_seconds=0;
    internal bool Complete(out bool fullscreen)
    {
        fullscreen=Fullscreen;
        if(!Active||SecondsLeft>=.01)return false;
        Cancel();return true;
    }
}
