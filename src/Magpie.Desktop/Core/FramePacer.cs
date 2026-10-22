using System.Diagnostics;

namespace Magpie.Desktop.Core;

// Wait before acquiring the next frame so capped rendering uses the latest capture.
// Processing time counts toward the interval; slow frames never cause catch-up bursts.
internal sealed class FramePacer
{
    private long? _lastFrame;
    public void MarkFrame(long? timestamp=null) => _lastFrame=timestamp ?? Stopwatch.GetTimestamp();
    public async ValueTask WaitAsync(double maximumFrameRate,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if(maximumFrameRate==0) { _lastFrame=null;return; }
        if(!double.IsFinite(maximumFrameRate) || maximumFrameRate is <10 or >1000)
            throw new ArgumentOutOfRangeException(nameof(maximumFrameRate));
        if(_lastFrame is not { } previous)return;
        long deadline=checked(previous+(long)Math.Ceiling(Stopwatch.Frequency/maximumFrameRate));
        while(true)
        {
            long remaining=deadline-Stopwatch.GetTimestamp();
            if(remaining<=0)return;
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1,Math.Ceiling(remaining*1000.0/Stopwatch.Frequency))),cancellation).ConfigureAwait(false);
        }
    }
}
