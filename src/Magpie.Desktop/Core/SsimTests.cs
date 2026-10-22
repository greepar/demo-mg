using System.Text.Json;
namespace Magpie.Desktop.Core;
internal static class SsimTests
{
    internal const string OriginalMode="""{"scalingModes":[{"name":"SSim downscale","effects":[{"name":"SSimDownscaler","scalingType":2,"scale":{"x":5,"y":4},"parameters":{"oversharp":2.5}}]}]}""";
    private static void Require(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
    internal static unsafe void Verify(EngineHandle engine)
    {
        var settings=new RuntimeSettings { Effect=14,SsimOversharp=1.7f,ScaleType=2,AbsoluteWidth=5,AbsoluteHeight=4 };
        var plan=settings.ResolveChain(11,9,100,100);
        Require(plan.Length==1 && plan[0].Effect==14 && plan[0].Width==5 && plan[0].Height==4 && plan[0].Parameter1==1.7f,"SSim default dimensions/parameter did not reach the graph.");
        Require(EffectStageSettings.IntermediateUnits(14,11,9,5,4)==188,"SSim mixed format/size resource budget is incorrect.");
        var processor=new ScalingFrameProcessor(0);byte[] source=new byte[11*9*4];
        for(int i=0;i<source.Length;i++)source[i]=i%4==3?(byte)255:(byte)(i*31%251);
        bool Run(bool fresh,long time,NativeEffectStep[] graph)
        {
            fixed(byte* pixels=source)return processor.Process(engine,(nint)pixels,11,9,fresh,graph,DuplicateFrameMode.Always,false,0,0,time,true);
        }
        Require(Run(true,0,plan),"SSim Vulkan frame did not execute.");
        byte[] expected=new byte[5*4*4];
        fixed(byte* pixels=source,target=expected)Require(NativeCore.ScaleExtended(engine,(nint)pixels,11,9,target,5,4,14,1.7f,0)!=0,"Standalone SSim failed.");
        Require(expected.SequenceEqual(processor.Pixels),"SSim configured and legacy paths differ.");
        Require(!Run(true,1,plan),"SSim duplicate frame rerendered.");
        settings.SsimOversharp=3;plan=settings.ResolveChain(11,9,100,100);
        Require(Run(false,2,plan),"SSim parameter change did not process the static source.");
        settings.AbsoluteWidth=7;settings.AbsoluteHeight=3;plan=settings.ResolveChain(11,9,100,100);
        Require(Run(false,3,plan) && processor.Pixels.Length==7*3*4,"SSim output-dependent intermediate resize failed.");
        if(processor.GpuProfile.Supported)Require(processor.GpuProfile.Stages[0].Effect==14,"SSim profile lost effect ID.");
        var root=new RuntimeSettings();var imported=ScalingModeInterchange.Import(root,OriginalMode,_=>{});
        Require(imported==new ModeImportResult(1,0,0),"SSim original JSON is still deferred.");
        root.SelectedModeId=root.Modes[0].Id;var mapped=root.ResolveChain(11,9,100,100);
        Require(mapped[0].Effect==14 && mapped[0].Width==5 && mapped[0].Parameter1==2.5f,"SSim imported parameter/size were lost.");
        var exported=ScalingModeInterchange.Parse(ScalingModeInterchange.Export(root.Modes)).Single();
        Require(exported.Steps[0].Effect==14 && exported.Steps[0].Parameter1==2.5f,"SSim original export roundtrip failed.");
        var copy=settings.CopyForProfile();copy.SsimOversharp=1;
        Require(settings.SsimOversharp==3,"SSim per-app parameter shares state.");
        root.Profiles.Add(new ApplicationProfile { Name="SSim",ExecutablePath=Path.GetFullPath("ssim-app"),Settings=copy });
        string directory=Path.Combine(Path.GetTempPath(),"magpie-ssim-settings-"+Guid.NewGuid().ToString("N"));
        try
        {
            string path=Path.Combine(directory,"settings.json");root.WriteFile(path);
            var loaded=JsonSerializer.Deserialize(File.ReadAllText(path),SettingsJson.Default.RuntimeSettings)!;loaded.Validate();
            Require(loaded.ResolveChain(11,9,100,100)[0].Parameter1==2.5f && loaded.Profiles[0].Settings.ResolveChain(11,9,100,100)[0].Parameter1==1,"SSim AOT defaults/profile disk restoration failed.");
        }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
        foreach(float invalid in new[]{.99f,3.01f,float.NaN,float.PositiveInfinity})
        {
            try { new RuntimeSettings { SsimOversharp=invalid }.Validate();throw new Exception("Invalid SSim parameter accepted."); }catch(InvalidDataException) { }
            try { new EffectStageSettings { Effect=14,Parameter1=invalid }.Validate();throw new Exception("Invalid SSim stage parameter accepted."); }catch(InvalidDataException) { }
        }
        Console.WriteLine("SSIM MANAGED PASS: real five-pass Vulkan, static parameter/size changes, duplicate detection, mixed texture byte budget, original import/export and independent profile AOT persistence.");
    }
}
