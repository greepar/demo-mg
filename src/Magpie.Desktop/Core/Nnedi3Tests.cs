using System.Text.Json;
namespace Magpie.Desktop.Core;

internal static class Nnedi3Tests
{
    internal const string OriginalMode="""{"scalingModes":[{"name":"NNEDI3 16/8x4","effects":[{"name":"NNEDI3\\NNEDI3_nns16_win8x4","scalingType":2,"scale":{"x":999,"y":777}}]}]}""";
    private static void Require(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
    internal static unsafe void Verify(EngineHandle engine)
    {
        var settings=new RuntimeSettings();
        var import=ScalingModeInterchange.Import(settings,OriginalMode,_=>{});
        Require(import==new ModeImportResult(1,0,0) && settings.Modes[0].IsAvailable,"Original NNEDI3 is still deferred.");
        settings.SelectedModeId=settings.Modes[0].Id;
        var plan=settings.ResolveChain(7,5,190,130);
        Require(plan.Length==1 && plan[0].Effect==13 && plan[0].Width==14 && plan[0].Height==10,"NNEDI3 fixed output was overridden by original scaling metadata.");
        string exported=ScalingModeInterchange.Export(settings.Modes);
        var restored=ScalingModeInterchange.Parse(exported).Single();
        Require(restored.Steps[0].Effect==13 && restored.Steps[0].OriginalScale?.X==999,"NNEDI3 original mode data did not roundtrip.");
        Require(EffectStageSettings.IntermediatePixelUnits(13)==1,"R16F doubled-height resource accounting differs from actual bytes.");
        byte[] source=new byte[7*5*4];for(int i=0;i<source.Length;i++)source[i]=i%4==3? (byte)255:(byte)(i*31%251);
        var processor=new ScalingFrameProcessor(0);
        bool Run(bool fresh,long time,NativeEffectStep[] graph)
        {
            fixed(byte* pixels=source)return processor.Process(engine,(nint)pixels,7,5,fresh,graph,DuplicateFrameMode.Always,false,0,0,time,true);
        }
        Require(Run(true,0,plan),"NNEDI3 AOT Vulkan processing did not execute.");
        byte[] expected=new byte[14*10*4];
        fixed(byte* pixels=source,target=expected)
            Require(NativeCore.ScaleExtended(engine,(nint)pixels,7,5,target,14,10,13,0,0)!=0,"Standalone NNEDI3 failed.");
        Require(processor.Pixels.SequenceEqual(expected),"NNEDI3 configured and legacy APIs differ.");
        Require(!Run(true,1,plan),"Duplicate NNEDI3 frame reached Vulkan.");
        var presentation=PresentationPlan.Build(settings,7,5,31,23,true);
        Require(presentation.Length==2 && presentation[^1].Effect==3 && Run(false,2,presentation),"Static NNEDI3 presentation resize did not re-render.");
        if(processor.GpuProfile.Supported)Require(processor.GpuProfile.Stages[0].Effect==13,"NNEDI3 profiling lost its effect identifier.");
        string directory=Path.Combine(Path.GetTempPath(),"magpie-nnedi-settings-"+Guid.NewGuid().ToString("N"));
        try
        {
            var profile=settings.CopyForProfile();profile.SelectedModeId=null;profile.Effect=13;
            settings.Profiles.Add(new ApplicationProfile { Name="NNEDI3",ExecutablePath=Path.Combine(directory,"application"),Settings=profile });
            string path=Path.Combine(directory,"settings.json");settings.WriteFile(path);
            var loaded=JsonSerializer.Deserialize(File.ReadAllText(path),SettingsJson.Default.RuntimeSettings)!;loaded.Validate();
            Require(loaded.ResolveChain(7,5,190,130)[0].Effect==13 && loaded.Profiles[0].Settings.ResolveChain(7,5,190,130)[0].Effect==13,"NNEDI3 defaults/profile/AOT disk roundtrip failed.");
        }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
        Console.WriteLine("NNEDI3 MANAGED PASS: original mode mapping/fixed dimensions/export, R16F byte budget, actual AOT Vulkan, duplicate/static resize, profile settings and disk restoration.");
    }
}
