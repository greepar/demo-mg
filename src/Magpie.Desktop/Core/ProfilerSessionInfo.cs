using System.Runtime.InteropServices;

namespace Magpie.Desktop.Core;

internal sealed record ProfilerSessionInfo(string Device,CaptureBackend? Backend)
{
    internal static ProfilerSessionInfo Read(EngineHandle engine,CaptureHandle? capture)
        =>new(Marshal.PtrToStringUTF8(NativeCore.DeviceName(engine))??"Vulkan",capture is { IsClosed:false,IsInvalid:false }?CaptureBackendPolicy.Available:null);

    internal string Describe(PerformanceSnapshot? metrics,DuplicateFrameMode detection,bool dynamicStatistics)
    {
        bool chinese=Localization.CurrentLanguage.StartsWith("zh",StringComparison.Ordinal);
        string backend=Backend is {} active?CaptureBackendPolicy.Name(active):chinese?"尚无捕获会话":"No capture session";
        string fps=metrics is {} measured?$"{measured.ScaledFps:F0} FPS":chinese?"等待采样":"Waiting for sample";
        string text=$"GPU: {Device}\n{Localization.Get("Overlay_Profiler_CaptureMethod")}: {backend}\n{Localization.Get("Overlay_Profiler_FrameRate")}: {fps}";
        if(detection==DuplicateFrameMode.Dynamic&&dynamicStatistics&&metrics is {} counters)
        {
            double percent=counters.PredictionSamples>0?100d*counters.PredictionMisses/counters.PredictionSamples:0;
            text+=$"\n{Localization.Get("Overlay_Profiler_DynamicDetection")}: {counters.PredictionMisses}/{counters.PredictionSamples} ({percent:F1}%)";
        }
        return text;
    }
}
