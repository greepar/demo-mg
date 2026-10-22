using System.Diagnostics;
namespace Magpie.Desktop.Core;

internal static class PresentationPlanTests
{
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
    internal static unsafe void Verify(EngineHandle engine)
    {
        Require(PresentationPlan.Viewport(100,50,103,70,true)==(103u,52u),"Free window resizing distorted the source aspect ratio.");
        Require(PresentationPlan.Viewport(2,3,960,540,true)==(360u,540u),"Portrait letterboxing is incorrect.");
        Require(PresentationPlan.Viewport(2,3,960,540,false)==(960u,540u),"Fullscreen renderer was incorrectly constrained to source aspect ratio.");
        var settings=new RuntimeSettings { Effect=1 };
        var fullscreen=PresentationPlan.Build(settings,100,50,101,51,false);
        var windowed=PresentationPlan.Build(settings,100,50,101,51,true);
        Require(fullscreen.Length==1 && fullscreen[0].Width==101 && fullscreen[0].Height==51 && windowed.Length==1 && windowed[0].Width==101 && windowed[0].Height==51,"Unit Fit dimensions differ from the original half-pixel case.");
        // This viewport deliberately differs from the source's aspect ratio.
        Require(PresentationPlan.Build(settings,100,50,103,70,true).Length==1 && PresentationPlan.Build(settings,100,50,103,70,true)[0].Height==70,"Windowed unit Fit did not become Fill.");
        Require(PresentationPlan.Build(settings,100,50,103,70,false)[0].Height==52,"Fullscreen Fit lost aspect ratio.");
        settings.ScaleX=settings.ScaleY=1.000001;
        Require(PresentationPlan.Build(settings,100,50,103,70,true).Length==1,"Original float epsilon around unit Fit was ignored.");
        settings.ScaleX=settings.ScaleY=.5;
        var reduced=PresentationPlan.Build(settings,100,50,103,70,true);
        Require(reduced.Length==2 && reduced[0].Width==52 && reduced[0].Height==26 && reduced[1].Width==103 && reduced[1].Height==70,"Non-unit Fit was silently converted to Fill or not resampled.");
        settings=new RuntimeSettings { Effect=9 };
        var cnn=PresentationPlan.Build(settings,3,2,19,13,true);
        Require(cnn.Length==2 && cnn[0].Width==6 && cnn[1].Effect==3 && cnn[1].Parameter1==0 && cnn[1].Parameter2==.5f && cnn[1].Width==19 && cnn[1].Height==13,"CNN presentation resampler is incorrect.");
        Require(PresentationPlan.Build(settings,3,2,6,4,true).Length==1,"Matching output was resampled twice.");
        Require(PresentationPlan.Build(settings,3,2,19,13,false).Length==1,"Fullscreen magnified undersized CNN output.");
        var downsampled=PresentationPlan.Build(settings,3,2,5,3,false);
        Require(downsampled.Length==2 && downsampled[1].Width==5 && downsampled[1].Height==3,"Fullscreen oversized output was not fitted.");
        settings=new RuntimeSettings { Effect=0,ScaleType=2,AbsoluteWidth=20,AbsoluteHeight=3 };
        var oneAxis=PresentationPlan.Build(settings,3,2,9,9,false);
        Require(oneAxis.Length==2 && oneAxis[1].Width==9 && oneAxis[1].Height==1,"Fullscreen overflow on one axis did not append a Fit resampler.");
        Require(PresentationPlan.Center(10,8,5,3)==(2L,2L),"Destination centering introduced a half pixel.");
        var mode=new EffectModeSettings { Name="Sixteen effects",Steps=Enumerable.Range(0,16).Select(_=>new EffectStageSettings { Effect=0 }).ToList() };
        settings=new RuntimeSettings { Modes=[mode],SelectedModeId=mode.Id };
        var maximum=PresentationPlan.Build(settings,3,2,7,5,true);Require(maximum.Length==17 && mode.Steps.Count==16,"Presentation stage replaced a user stage or exceeded model limits.");
        byte[] source=[11,23,37,255,51,67,83,255,97,109,127,255,139,151,167,255,181,193,211,255,223,239,251,255];
        var processor=new ScalingFrameProcessor(0);
        bool Run(NativeEffectStep[] graph,bool fresh,long now)
        {
            fixed(byte* pixels=source)return processor.Process(engine,(nint)pixels,3,2,fresh,graph,DuplicateFrameMode.Always,false,0,0,now,true);
        }
        Require(Run(maximum,true,0),"Seventeen-stage GPU plan did not render.");
        var first=(byte[])processor.Pixels.Clone();
        NativeEffectStep[] reference=[new(){Effect=3,Width=7,Height=5,Parameter1=0,Parameter2=.5f}];
        Require(first.SequenceEqual(Render(engine,source,reference)),"GPU presentation pixels differ from an independent native Bicubic request.");
        if(processor.GpuProfile.Supported)Require(processor.GpuProfile.Valid && processor.GpuProfile.Stages.Length==17,"Managed profiling lost the presentation stage.");
        Require(!Run(maximum,true,1),"Static presentation output was unnecessarily rendered.");
        var resized=PresentationPlan.Build(settings,3,2,8,6,true);Require(Run(resized,false,2) && processor.Pixels.Length==8*6*4,"Idle window resize did not rebuild presentation.");
        var removed=PresentationPlan.Build(settings,3,2,3,2,true);Require(removed.Length==16 && Run(removed,false,3) && processor.Pixels.SequenceEqual(source),"Removing the automatic pass did not release/rebuild the output graph.");
        Require(Run(maximum,false,4) && processor.Pixels.SequenceEqual(first),"Re-adding the automatic pass changed pixels.");
        var realCnn=new RuntimeSettings { Effect=11 };var cnnGraph=PresentationPlan.Build(realCnn,3,2,19,13,true);
        Require(Render(engine,source,cnnGraph).SequenceEqual(RenderSequential(engine,source,cnnGraph)),"CNN plus presentation differs from sequential GPU execution.");
        var largeMode=new EffectModeSettings { Steps=Enumerable.Range(0,16).Select(_=>new EffectStageSettings { Effect=0 }).ToList() };
        var large=new RuntimeSettings { Modes=[largeMode],SelectedModeId=largeMode.Id };
        try { PresentationPlan.Build(large,1400,1400,5000,3000,true);throw new InvalidOperationException("Presentation memory budget was ignored."); }catch(InvalidOperationException error) when(error.Message.Contains("resource limit")) { }
        Console.WriteLine("PRESENTATION PASS: original Fit/Fill/overflow rules, B=0/C=0.5 GPU pixels, 16+1 stages/timestamps, duplicate/idle resize, append/remove/re-add, memory budget and integer centering.");
        NativeCore.SetGpuProfiling(engine,0);
    }
    private static unsafe byte[] Render(EngineHandle engine,byte[] pixels,NativeEffectStep[] graph,uint width=3,uint height=2)
    {
        var last=graph[^1];byte[] output=new byte[checked((int)(last.Width*last.Height*4))];
        fixed(byte* source=pixels,destination=output)fixed(NativeEffectStep* stages=graph)
            if(NativeCore.ScaleChain(engine,(nint)source,width,height,destination,(ulong)output.Length,stages,(uint)graph.Length)==0)throw NativeCore.Error();
        return output;
    }
    private static byte[] RenderSequential(EngineHandle engine,byte[] pixels,NativeEffectStep[] graph)
    {
        uint width=3,height=2;
        foreach(var stage in graph) { pixels=Render(engine,pixels,[stage],width,height);width=stage.Width;height=stage.Height; }
        return pixels;
    }
}
