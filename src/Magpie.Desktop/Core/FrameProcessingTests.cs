using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
namespace Magpie.Desktop.Core;

internal static class FrameProcessingTests
{
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
    public static unsafe void Verify(EngineHandle engine)
    {
        var detector=new DuplicateFrameDetector();byte[] a=[1,2,3,255],b=[2,2,3,255];
        Require(!detector.Observe(a,1,1,DuplicateFrameMode.Always,false).Duplicate,"First frame was skipped.");
        Require(detector.Observe((byte[])a.Clone(),1,1,DuplicateFrameMode.Always,false).Duplicate,"Identical frame not detected.");
        a[0]=3;Require(!detector.Observe(a,1,1,DuplicateFrameMode.Always,false).Duplicate,"Detector aliases producer memory.");
        Require(!detector.Observe(a,1,1,DuplicateFrameMode.Never,false).Duplicate,"Never mode skipped a frame.");
        detector=new();detector.Observe(b,1,1,DuplicateFrameMode.Dynamic,true);
        for(int i=1;i<=16;i++) { b[0]=(byte)(i+2);var detection=detector.Observe(b,1,1,DuplicateFrameMode.Dynamic,true);Require(detection.Compared && !detection.Bypassed && !detection.Duplicate,"Original initial 16-check schedule differs."); }
        var prediction=detector.Observe(b,1,1,DuplicateFrameMode.Dynamic,true);
        Require(prediction.Bypassed && prediction.Compared && prediction.PredictionMiss && !prediction.Duplicate,"Dynamic statistics did not measure a bypassed duplicate.");
        Require(detector.Observe(b,1,1,DuplicateFrameMode.Dynamic,true).Duplicate,"Dynamic detection did not return to checking after skip.");
        // Long moving sequences exercise the original growing skip / shrinking check policy.
        detector=new();b[0]=0;detector.Observe(b,1,1,DuplicateFrameMode.Dynamic,false);int checks=0,bypassed=0;
        for(int i=1;i<=500;i++) { b[0]=(byte)i;var d=detector.Observe(b,1,1,DuplicateFrameMode.Dynamic,false);Require(!d.Duplicate,"Moving sequence incorrectly skipped.");if(d.Compared)checks++;if(d.Bypassed)bypassed++; }
        Require(checks==130 && bypassed==370,"The original adaptive schedule no longer matches its 500-frame golden trace.");
        var processor=new ScalingFrameProcessor(0);byte[] input=[1,2,3,255,5,7,11,255,13,17,19,255,23,29,31,255,37,41,43,255,47,53,59,255];
        NativeEffectStep[] plan=[new(){Effect=0,Width=6,Height=4}];
        long T(double seconds)=>(long)(seconds*Stopwatch.Frequency);
        bool Run(bool fresh,long time,double minimum=0,double maximum=0,uint w=3,uint h=2)
        {
            fixed(byte* source=input)return processor.Process(engine,(nint)source,w,h,fresh,plan,DuplicateFrameMode.Always,false,minimum,maximum,time);
        }
        Require(Run(true,0),"Initial GPU replay did not render.");var first=(byte[])processor.Pixels.Clone();
        Require(!Run(true,T(.01)),"Duplicate frame reached Vulkan.");
        Require(!Run(false,T(.02)),"Idle source reached Vulkan without a deadline.");
        input[0]=97;Require(Run(true,T(.03)) && !first.SequenceEqual(processor.Pixels),"Changed source was skipped.");
        plan=[new(){Effect=0,Width=9,Height=6}];Require(Run(false,T(.04)) && processor.Pixels.Length==9*6*4,"Resize/configuration with no capture update was skipped.");
        var wide=(byte[])processor.Pixels.Clone();Require(Run(true,T(.05),w:2,h:3) && !wide.SequenceEqual(processor.Pixels),"Same-byte-count source reshape was skipped.");
        Require(!Run(false,T(.06),minimum:60,maximum:10,w:2,h:3),"Minimum FPS exceeded maximum FPS clamp.");
        Require(Run(false,T(.151),minimum:60,maximum:10,w:2,h:3),"Minimum-rate cached refresh did not run.");
        Require(!Run(false,T(.152),minimum:60,maximum:10,w:2,h:3),"Forced refresh produced a catch-up burst.");
        // Parameter changes must invalidate output even though the original input bytes are duplicates.
        plan=[new(){Effect=3,Width=9,Height=6,Parameter1=.1f,Parameter2=.2f}];Require(Run(false,T(.16),w:2,h:3),"Effect switch was skipped.");
        var bicubic=(byte[])processor.Pixels.Clone();plan[0].Parameter1=.9f;Require(Run(false,T(.17),w:2,h:3) && !bicubic.SequenceEqual(processor.Pixels),"Parameter change was skipped.");
        plan=[new(){Effect=12,Width=9,Height=6}];plan[0].SetCrt(CrtParameters.Defaults());
        Require(Run(false,T(.18),w:2,h:3),"Configured CRT switch was skipped.");var crt=(byte[])processor.Pixels.Clone();
        plan[0].Extra[0]=1.2f;Require(Run(false,T(.19),w:2,h:3) && !crt.SequenceEqual(processor.Pixels),"Extended parameter change was skipped.");
        processor.Performance.Submit(2);processor.Performance.Submit(6);
        Require(processor.Performance.TotalReceived==4 && processor.Performance.TotalSkipped==1 && processor.Performance.TotalScaled==9 && processor.Performance.TotalForced==1,"Frame counters do not match actual processing decisions.");
        Require(processor.Performance.TrySnapshot(T(1),out var snapshot) && snapshot.Received==4 && snapshot.Scaled==9 && snapshot.Skipped==1 && snapshot.UiSubmitted==2 && snapshot.UiSubmitMilliseconds==4,"Performance snapshot is inaccurate.");
        Require(processor.Performance.TrySnapshot(T(2),out var idle) && idle.InputFps==0 && idle.ScaledFps==0 && idle.Skipped==0,"Idle statistics retained stale FPS.");
        var never=new ScalingFrameProcessor(0);NativeEffectStep[] unchanged=[new(){Effect=0,Width=6,Height=4}];
        fixed(byte* raw=input)
        {
            for(int i=0;i<3;i++)Require(never.Process(engine,(nint)raw,3,2,true,unchanged,DuplicateFrameMode.Never,false,0,0,T(i*.01)),"Never mode skipped a real Vulkan frame.");
            Require(never.Process(engine,(nint)raw,3,2,true,unchanged,DuplicateFrameMode.Always,false,0,0,T(.04)),"Changing detection mode did not reset its baseline.");
            Require(!never.Process(engine,(nint)raw,3,2,true,unchanged,DuplicateFrameMode.Always,false,0,0,T(.05)),"Always mode did not skip after baseline reset.");
        }
        Require(never.Performance.TotalScaled==4 && never.Performance.TotalSkipped==1,"Detection mode switch produced incorrect GPU call counts.");
        var predictionMetrics=new FramePerformance(0);predictionMetrics.Receive();predictionMetrics.Detect(prediction,1);
        Require(predictionMetrics.TrySnapshot(T(1),out var predictions) && predictions.PredictionMisses==1 && predictions.PredictionSamples==1 && predictions.Bypassed==1,"Prediction sample accounting is incorrect.");
        VerifyGpu(engine,input);
        var settings=new RuntimeSettings { GpuProfilingEnabled=true,DuplicateFrameDetection=DuplicateFrameMode.Always,StatisticsForDynamicDetection=true,MinimumFrameRate=30 };
        string json=JsonSerializer.Serialize(settings,SettingsJson.Default.RuntimeSettings);var loaded=JsonSerializer.Deserialize(json,SettingsJson.Default.RuntimeSettings)!;loaded.Validate();
        Require(loaded.GpuProfilingEnabled && loaded.DuplicateFrameDetection==DuplicateFrameMode.Always && loaded.MinimumFrameRate==30 && loaded.StatisticsForDynamicDetection,"Performance settings did not roundtrip.");
        string directory=Path.Combine(Path.GetTempPath(),"magpie-performance-"+Guid.NewGuid().ToString("N"));
        try
        {
            string path=Path.Combine(directory,"settings.json");settings.WriteFile(path);
            var disk=JsonSerializer.Deserialize(File.ReadAllText(path),SettingsJson.Default.RuntimeSettings)!;disk.Validate();
            Require(disk.GpuProfilingEnabled && disk.MinimumFrameRate==30 && disk.DuplicateFrameDetection==DuplicateFrameMode.Always && disk.StatisticsForDynamicDetection,"Performance settings disk roundtrip failed.");
        }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
        foreach(double invalid in new[]{-1d,1001,double.NaN}) { loaded.MinimumFrameRate=invalid;try { loaded.Validate();throw new InvalidOperationException("Invalid minimum FPS accepted."); } catch(InvalidDataException) { } }
        loaded.MinimumFrameRate=0;loaded.DuplicateFrameDetection=(DuplicateFrameMode)3;
        try { loaded.Validate();throw new InvalidOperationException("Invalid duplicate mode accepted."); } catch(InvalidDataException) { }
        Console.WriteLine("PROCESSING PASS: original dynamic detection policy, exact duplicates, real Vulkan replay, resize/parameter invalidation, minimum/max FPS, accurate counters and idle reset.");
    }
    private static unsafe void VerifyGpu(EngineHandle engine,byte[] input)
    {
        Require(Marshal.SizeOf<NativeGpuProfile>()==2440,"GPU profiling ABI size mismatch.");
        var processor=new ScalingFrameProcessor(0);
        NativeEffectStep[] plan=[new(){Effect=11,Width=6,Height=4},new(){Effect=9,Width=12,Height=8},new(){Effect=1,Width=18,Height=12}];
        bool Run(bool fresh,bool enabled,long time)
        {
            fixed(byte* source=input)return processor.Process(engine,(nint)source,3,2,fresh,plan,DuplicateFrameMode.Always,false,0,0,time,enabled);
        }
        Require(Run(true,false,0),"Unprofiled frame failed.");var expected=(byte[])processor.Pixels.Clone();
        Require(!Run(false,false,1),"Idle unprofiled frame rendered.");
        Require(NativeCore.GetGpuProfile(engine,out var capability)!=0,"GPU capability read failed.");
        bool supported=capability.Supported!=0;
        bool sampled=Run(false,true,2);var sample=processor.GpuProfile;
        Require(sample.Enabled && sample.Supported==supported && sampled==supported,"Profiler did not sample cached source exactly when supported.");
        Require(expected.SequenceEqual(processor.Pixels),"GPU timestamp instrumentation changed the pixels.");
        if(supported)
        {
            Require(sample.Valid && sample.Stages.Length==3 && sample.Stages.Select(s=>s.Passes.Length).SequenceEqual(new[]{6,6,1}),"Mixed GPU pass metadata is incorrect.");
            Require(sample.Describe(false).Contains("CuNNy") && sample.Describe(true).Contains("pass 6"),"GPU effect/pass descriptions are incomplete.");
        }
        else Require(sample.Status==2 && sample.Stages.Length==0,"Unsupported timestamps produced fake samples.");
        Require(!Run(false,true,3) && processor.GpuProfile.Sequence==sample.Sequence,"Idle duplicate produced another timestamp sample.");
        Require(!Run(false,false,4) && !processor.GpuProfile.Enabled,"Disabling profiler forced work or retained enabled state.");
        Require(processor.Performance.TotalForced==0,"Profiler-only refresh was counted as a minimum-rate refresh.");
        Require(processor.Performance.TrySnapshot(Stopwatch.Frequency,out var metrics) && metrics.GpuSamples==(supported?1:0),"GPU average counts include skipped frames.");
        Require(processor.Performance.TrySnapshot(Stopwatch.Frequency*2,out var idle) && idle.GpuSamples==0 && idle.GpuMilliseconds==0,"Idle window retained old GPU averages.");
        Console.WriteLine($"MANAGED GPU PROFILE PASS: supported={supported}, ABI=2440, 3 effects/13 passes, exact pixels, cached sample once, duplicate/off/idle counters.");
    }

}
