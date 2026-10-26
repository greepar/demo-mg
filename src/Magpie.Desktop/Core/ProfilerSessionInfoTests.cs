namespace Magpie.Desktop.Core;

internal static class ProfilerSessionInfoTests
{
    internal static void Verify()
    {
        static void Check(bool ok,string message) { if(!ok)throw new InvalidOperationException("Profiler metadata: "+message); }
        var info=new ProfilerSessionInfo("Example GPU",null);
        var metrics=default(PerformanceSnapshot) with { Seconds=2,Scaled=120,PredictionSamples=10,PredictionMisses=2 };
        string waiting=info.Describe(null,DuplicateFrameMode.Always,false);
        Check(waiting.Contains("Example GPU")&&!waiting.Contains("60 FPS"),"Unmeasured frame rate was fabricated.");
        string ordinary=info.Describe(metrics,DuplicateFrameMode.Always,true);
        Check(ordinary.Contains("60 FPS")&&!ordinary.Contains("2/10"),"FPS/dynamic detection gating differs.");
        string dynamic=info.Describe(metrics,DuplicateFrameMode.Dynamic,true);
        Check(dynamic.Contains("2/10")&&dynamic.Contains($"{20d:F1}%"),"Dynamic prediction counters or ratio differ.");
        Check(!info.Describe(metrics,DuplicateFrameMode.Dynamic,false).Contains("2/10"),"Disabled dynamic counters remained visible.");
        foreach(var backend in new[]{CaptureBackend.Portal,CaptureBackend.ScreenCaptureKit,CaptureBackend.Gdi})
            Check((info with { Backend=backend }).Describe(metrics,DuplicateFrameMode.Always,false).Contains(CaptureBackendPolicy.Name(backend)),"Actual backend identity was substituted.");
        var sample=new GpuProfileSnapshot(true,true,1,1,3,[new(11,2,[1,1],true),new(0,1,[1])]);
        Check(!GpuTimingPresentation.Build(sample,false,false).Rows[0].Name.Contains("FP16"),"Ordinary mode exposed developer precision suffix.");
        Check(GpuTimingPresentation.Build(sample,false,true).Rows[0].Name.EndsWith(" (FP16)",StringComparison.Ordinal),"FP16 pipeline flag was lost.");
        var average=new GpuTimingAverages();average.Push(sample,0);
        average.Push(sample with { Sequence=2 },1);
        var next=average.Push(sample with { Sequence=3 },System.Diagnostics.Stopwatch.Frequency);
        Check(next.Stages[0].UsesFP16,"Averaging cleared pipeline precision metadata.");
        var changed=sample with { Sequence=4,Stages=[new(11,10,[5,5],false),new(0,1,[1])] };
        Check(!average.Push(changed,System.Diagnostics.Stopwatch.Frequency+1).Stages[0].UsesFP16&&average.Push(changed,System.Diagnostics.Stopwatch.Frequency+2).Stages[0].Milliseconds==10,"Precision change failed to reset prior averages.");
        Console.WriteLine("PROFILER SESSION MODEL PASS: actual identity fields, absent/available metrics, dynamic counter gating, three capture labels, developer-only precision and averaged/changed pipeline precision.");
    }
}
