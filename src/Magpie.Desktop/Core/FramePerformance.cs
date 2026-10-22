using System.Diagnostics;
namespace Magpie.Desktop.Core;

internal readonly record struct PerformanceSnapshot(double Seconds,long Received,long Scaled,long Skipped,long Forced,long Compared,long Bypassed,long PredictionMisses,long PredictionSamples,double CompareMilliseconds,double ScaleMilliseconds,double UiSubmitMilliseconds,long UiSubmitted,double GpuMilliseconds,long GpuSamples,long GpuUnavailable)
{
    public double InputFps=>Received/Seconds;
    public double ScaledFps=>Scaled/Seconds;
}
// CPU fields measure call durations including transfer/fence/UI queue time. GPU fields use valid native timestamps only.
internal sealed class FramePerformance
{
    private long _start;
    private long _received,_scaled,_skipped,_forced,_compared,_bypassed,_predictionMisses,_predictionSamples,_uiSubmitted;
    private double _compareMs,_scaleMs,_uiMs,_gpuMs;
    private long _gpuSamples,_gpuUnavailable;
    public long TotalReceived { get; private set; }
    public long TotalScaled { get; private set; }
    public long TotalSkipped { get; private set; }
    public long TotalForced { get; private set; }
    public static string Describe(PerformanceSnapshot counters,DuplicateFrameMode mode,bool dynamicStatistics,bool gpuEnabled=false)
    {
        string prediction=mode==DuplicateFrameMode.Dynamic && dynamicStatistics ? $"Prediction misses: {counters.PredictionMisses}/{counters.PredictionSamples}" : "Dynamic statistics off";
        string gpu=gpuEnabled ? counters.GpuSamples>0 ? $"GPU interval: {counters.GpuMilliseconds:F3} ms ({counters.GpuSamples} samples)" : "GPU interval: no samples" : "GPU profiler off";
        return $"Input: {counters.InputFps:F1} FPS · Scaled: {counters.ScaledFps:F1} FPS · Duplicates skipped: {counters.Skipped} · Minimum-rate refreshes: {counters.Forced}\nDetection: {counters.CompareMilliseconds:F2} ms/input · Vulkan call: {counters.ScaleMilliseconds:F2} ms/scaled · UI submit: {counters.UiSubmitMilliseconds:F2} ms · Checks bypassed: {counters.Bypassed} · {prediction} · {gpu}";
    }
    public FramePerformance(long start)=>_start=start;
    public void Receive() { ++_received;++TotalReceived; }
    public void Detect(FrameDetection result,double ms) { if(result.Compared)++_compared;if(result.Bypassed)++_bypassed;if(result.PredictionMiss)++_predictionMisses;if(result.Bypassed && result.Compared)++_predictionSamples;_compareMs+=ms; }
    public void Skip() { ++_skipped;++TotalSkipped; }
    public void Scale(bool forced,double ms) { ++_scaled;++TotalScaled;if(forced) { ++_forced;++TotalForced; }_scaleMs+=ms; }
    public void RecordGpu(GpuProfileSnapshot sample) { if(sample.Valid) { ++_gpuSamples;_gpuMs+=sample.TotalMilliseconds; }else ++_gpuUnavailable; }
    public void Submit(double ms) { ++_uiSubmitted;_uiMs+=ms; }
    public bool TrySnapshot(long now,out PerformanceSnapshot snapshot)
    {
        if(now-_start<Stopwatch.Frequency) { snapshot=default;return false; }
        double seconds=(now-_start)/(double)Stopwatch.Frequency;
        snapshot=new(seconds,_received,_scaled,_skipped,_forced,_compared,_bypassed,_predictionMisses,_predictionSamples,_received==0?0:_compareMs/_received,_scaled==0?0:_scaleMs/_scaled,_uiSubmitted==0?0:_uiMs/_uiSubmitted,_uiSubmitted,_gpuSamples==0?0:_gpuMs/_gpuSamples,_gpuSamples,_gpuUnavailable);
        _start=now;_received=_scaled=_skipped=_forced=_compared=_bypassed=_predictionMisses=_predictionSamples=_uiSubmitted=0;_compareMs=_scaleMs=_uiMs=_gpuMs=0;_gpuSamples=_gpuUnavailable=0;return true;
    }
}
