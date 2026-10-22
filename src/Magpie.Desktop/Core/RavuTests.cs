using System.Text.Json;
namespace Magpie.Desktop.Core;
internal static class RavuTests
{
    internal const string OriginalMode="""{"scalingModes":[{"name":"RAVU Lite R2","effects":[{"name":"RAVU\\RAVU_Lite_R2","scalingType":2,"scale":{"x":999,"y":777}}]}]}""";
    private static void Require(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
    internal static unsafe void Verify(EngineHandle engine)
    {
        var settings=new RuntimeSettings { Effect=19 };
        var plan=settings.ResolveChain(7,5,190,130);
        Require(plan[0].Effect==19 && plan[0].Width==14 && plan[0].Height==10,"RAVU fixed output did not resolve.");
        Require(EffectStageSettings.IntermediateUnits(19,7,5,14,10)==2880 && EffectStageSettings.IntermediateUnits(19,33,17,66,34)==2880,"SOURCE byte budget incorrectly depends on captured dimensions.");
        byte[] input=new byte[7*5*4];for(int i=0;i<input.Length;i++)input[i]=i%4==3?(byte)255:(byte)(i*31%251);
        var processor=new ScalingFrameProcessor(0);
        bool Run(bool fresh,long time,NativeEffectStep[] graph)
        {
            fixed(byte* pixels=input)return processor.Process(engine,(nint)pixels,7,5,fresh,graph,DuplicateFrameMode.Always,false,0,0,time,true);
        }
        Require(Run(true,0,plan),"RAVU with SOURCE did not execute.");
        byte[] expected=new byte[14*10*4];
        fixed(byte* source=input,target=expected)Require(NativeCore.ScaleExtended(engine,(nint)source,7,5,target,14,10,19,0,0)!=0,"RAVU direct API failed.");
        Require(processor.Pixels.SequenceEqual(expected),"RAVU configured output differs from direct SOURCE path.");
        Require(!Run(true,1,plan),"RAVU duplicate frame rerendered.");
        var presentation=PresentationPlan.Build(settings,7,5,31,23,true);
        Require(Run(false,2,presentation),"RAVU static resize did not update presentation.");
        Require(Run(false,3,plan) && processor.Pixels.SequenceEqual(expected),"RAVU LUT was lost after changing/restoring the chain.");
        if(processor.GpuProfile.Supported)Require(processor.GpuProfile.Stages[0].Effect==19 && processor.GpuProfile.Stages[0].Passes.Length==1,"RAVU pass timing was lost.");
        var root=new RuntimeSettings();Require(ScalingModeInterchange.Import(root,OriginalMode,_=>{})==new ModeImportResult(1,0,0),"Original RAVU mode is still deferred.");
        var imported=root.Modes[0];root.SelectedModeId=imported.Id;
        Require(root.ResolveChain(7,5,190,130)[0].Effect==19 && imported.IsAvailable,"RAVU import is not runnable.");
        var exported=ScalingModeInterchange.Parse(ScalingModeInterchange.Export(root.Modes)).Single();
        Require(exported.Steps[0].Effect==19 && exported.Steps[0].OriginalScale?.X==999,"RAVU original metadata did not roundtrip.");
        root.Profiles.Add(new ApplicationProfile { Name="RAVU",ExecutablePath=Path.GetFullPath("ravu-app"),Settings=settings.CopyForProfile() });
        string directory=Path.Combine(Path.GetTempPath(),"magpie-ravu-"+Guid.NewGuid().ToString("N"));
        try
        {
            string path=Path.Combine(directory,"settings.json");root.WriteFile(path);
            var loaded=JsonSerializer.Deserialize(File.ReadAllText(path),SettingsJson.Default.RuntimeSettings)!;loaded.Validate();
            Require(loaded.ResolveChain(7,5,190,130)[0].Effect==19 && loaded.Profiles[0].Settings.ResolveChain(7,5,190,130)[0].Effect==19,"RAVU defaults/profile AOT disk restore failed.");
        }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
        Console.WriteLine("RAVU MANAGED PASS: embedded DDS SOURCE GPU execution/reuse, fixed dimensions and constant asset budget, duplicate/static chain changes, original mode mapping and AOT profile persistence.");
    }
}
