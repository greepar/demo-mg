using System.Text.Json;
namespace Magpie.Desktop.Core;
internal static class SmaaTests
{
    internal const string OriginalMode="""{"scalingModes":[{"name":"SMAA chain","effects":[{"name":"SMAA\\SMAA_High","scalingType":2,"scale":{"x":999,"y":777}},{"name":"SMAA\\SMAA_Ultra"}]}]}""";
    private static void Require(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
    internal static unsafe void Verify(EngineHandle engine)
    {
        byte[] input=new byte[7*5*4];for(int i=0;i<input.Length;i++)input[i]=i%4==3?(byte)255:(byte)(i*31%251);
        var processor=new ScalingFrameProcessor(0);
        bool Run(bool fresh,long time,NativeEffectStep[] graph)
        {
            fixed(byte* pixels=input)return processor.Process(engine,(nint)pixels,7,5,fresh,graph,DuplicateFrameMode.Always,false,0,0,time,true);
        }
        for(uint effect=20;effect<=23;effect++)
        {
            var settings=new RuntimeSettings { Effect=effect,ScaleType=2,AbsoluteWidth=999,AbsoluteHeight=777 };
            var plan=settings.ResolveChain(7,5,190,130);
            Require(plan.Length==1&&plan[0].Effect==effect&&plan[0].Width==7&&plan[0].Height==5,"SMAA same-size output did not resolve.");
            Require(ScalingModeInterchange.HasFixedOutput(effect)&&!EffectStageSettings.IsFixed2x(effect),"SMAA was misclassified as 2x.");
            ulong expected=((ulong)7*5*(effect>=22?12u:6u)+359424+3)/4;
            Require(EffectStageSettings.IntermediateUnits(effect,7,5,7,5)==expected,"SMAA intermediate/DDS byte budget incorrect.");
            Require(Run(true,effect*10,plan),"SMAA GPU model switch did not execute.");
            byte[] direct=new byte[input.Length];
            fixed(byte* source=input,target=direct)Require(NativeCore.ScaleExtended(engine,(nint)source,7,5,target,7,5,effect,0,0)!=0,"SMAA direct path failed.");
            Require(processor.Pixels.SequenceEqual(direct),"SMAA managed and direct configured outputs differ.");
            Require(!Run(true,effect*10+1,plan),"SMAA duplicate invoked Vulkan again.");
            var presentation=PresentationPlan.Build(settings,7,5,31,23,true);
            Require(Run(false,effect*10+2,presentation),"SMAA static presentation resize did not render.");
            Require(Run(false,effect*10+3,plan)&&processor.Pixels.SequenceEqual(direct),"SMAA DDS resources were lost after chain resize.");
            if(processor.GpuProfile.Supported)Require(processor.GpuProfile.Stages[0].Effect==effect&&processor.GpuProfile.Stages[0].Passes.Length==3,"SMAA GPU pass timing missing.");
            byte[] invalid=new byte[8*5*4];
            fixed(byte* source=input,target=invalid)Require(NativeCore.ScaleExtended(engine,(nint)source,7,5,target,8,5,effect,0,0)==0,"SMAA accepted invalid output dimensions.");
        }
        var root=new RuntimeSettings();Require(ScalingModeInterchange.Import(root,OriginalMode,_=>{})==new ModeImportResult(1,0,0),"Original SMAA modes are still deferred.");
        root.SelectedModeId=root.Modes[0].Id;var chain=root.ResolveChain(7,5,190,130);
        Require(chain.Length==2&&chain.All(step=>step.Width==7&&step.Height==5),"Imported SMAA dimensions are incorrect.");
        Require(Run(false,999,chain),"Two SMAA effects did not run in sequence.");
        var exported=ScalingModeInterchange.Parse(ScalingModeInterchange.Export(root.Modes)).Single();
        Require(exported.Steps[0].Effect==22&&exported.Steps[0].OriginalScale?.X==999&&exported.Steps[1].Effect==23,"Original SMAA metadata was not preserved.");
        root.Profiles.Add(new ApplicationProfile { Name="SMAA",ExecutablePath=Path.GetFullPath("smaa-app"),Settings=new RuntimeSettings { Effect=21 } });
        string directory=Path.Combine(Path.GetTempPath(),"magpie-smaa-"+Guid.NewGuid().ToString("N"));
        try
        {
            string path=Path.Combine(directory,"settings.json");root.WriteFile(path);
            var loaded=JsonSerializer.Deserialize(File.ReadAllText(path),SettingsJson.Default.RuntimeSettings)!;loaded.Validate();
            Require(loaded.ResolveChain(7,5,190,130)[0].Effect==22&&loaded.Profiles[0].Settings.ResolveChain(7,5,190,130)[0].Effect==21,"SMAA AOT profile/mode restore failed.");
        }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
        Console.WriteLine("SMAA MANAGED PASS: four original three-pass presets, two embedded DDS tables, same-size enforcement, typed intermediates and byte budgets, duplicates/static presentation changes, original import/export, chain execution, GPU timing and AOT profile persistence.");
    }
}
