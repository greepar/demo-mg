using System.Text.Json;
namespace Magpie.Desktop.Core;

internal static class GraphFilterTests
{
    internal const string OriginalMode="""{"scalingModes":[{"name":"Filter chain","effects":[{"name":"Anime4K\\Anime4K_Denoise_Bilateral_Mean","scalingType":2,"scale":{"x":999,"y":777},"parameters":{"intensitySigma":1.5}},{"name":"Jinc","scalingType":0,"scale":{"x":2,"y":2},"parameters":{"windowSinc":0.65,"sinc":0.9,"ARStrength":1,"future":0.3}}]}]}""";
    private static void Require(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
    internal static unsafe void Verify(EngineHandle engine)
    {
        byte[] input=new byte[7*5*4];for(int i=0;i<input.Length;i++)input[i]=i%4==3?(byte)119:(byte)(i*31%251);
        var processor=new ScalingFrameProcessor(0);
        bool Run(NativeEffectStep[] graph,long tick,bool fresh=false)
        {
            fixed(byte* source=input)return processor.Process(engine,(nint)source,7,5,fresh,graph,DuplicateFrameMode.Always,false,0,0,tick,true);
        }
        foreach(uint effect in new[]{24u,25u})
        {
            var settings=new RuntimeSettings { Effect=effect,ScaleType=0,ScaleX=2,ScaleY=2 };
            var plan=settings.ResolveChain(7,5,190,130);
            Require(plan[0].Effect==effect&&plan[0].Width==(effect==24?14u:7u)&&plan[0].Height==(effect==24?10u:5u),"Filter output dimensions incorrect.");
            Require(EffectStageSettings.IntermediateUnits(effect,7,5,plan[0].Width,plan[0].Height)==1,"Filter descriptor padding budget missing.");
            Require(Run(plan,100+effect,true),"Registered filter graph did not execute.");
            byte[] direct=new byte[plan[0].Width*plan[0].Height*4];
            fixed(byte* source=input,target=direct)Require(NativeCore.ScaleExtended(engine,(nint)source,7,5,target,plan[0].Width,plan[0].Height,effect,plan[0].Parameter1,plan[0].Parameter2)!=0,"Direct registered filter failed.");
            Require(processor.Pixels.SequenceEqual(direct),"Configured/default filter parameters differ.");
            Require(!Run(plan,200+effect,true),"Duplicate filter frame reached GPU.");
            var definition=GraphEffects.Definitions(effect)[0];settings.GraphParameters[GraphEffects.Key(effect,definition)]=effect==24?.65f:1.5f;
            Require(Run(settings.ResolveChain(7,5,190,130),300+effect),"Static filter parameter change did not rerender.");
            if(processor.GpuProfile.Supported)Require(processor.GpuProfile.Stages[0].Passes.Length==1,"Filter pass timing missing.");
            var copy=settings.CopyForProfile();copy.GraphParameters[GraphEffects.Key(effect,definition)]=definition.Default;
            Require(settings.GraphParameters[GraphEffects.Key(effect,definition)]!=definition.Default,"Filter profile copy shares parameter dictionary.");
            settings.GraphParameters[GraphEffects.Key(effect,definition)]=float.NaN;
            try { settings.Validate();throw new InvalidOperationException("Invalid filter setting accepted."); }catch(InvalidDataException) { }
        }
        var root=new RuntimeSettings();Require(ScalingModeInterchange.Import(root,OriginalMode,_=>{})==new ModeImportResult(1,0,0),"Original filters remain deferred.");
        root.SelectedModeId=root.Modes.Single().Id;var graph=root.ResolveChain(7,5,190,130);
        Require(graph.Length==2&&graph[0].Width==7&&graph[0].Parameter1==1.5f&&graph[1].Width==14&&graph[1].Extra[0]==1,"Original graph parameters lost in ABI resolution.");
        Require(Run(graph,999),"Imported mean/Jinc chain failed.");
        var exported=ScalingModeInterchange.Parse(ScalingModeInterchange.Export(root.Modes)).Single();
        Require(exported.Steps[0].OriginalScale?.X==999&&GraphEffects.Get(exported.Steps[1],2)==1&&exported.Steps[1].AdditionalParameters["future"]==.3f,"Original filter export lost scale, parameters or unknown field.");
        root.Profiles.Add(new ApplicationProfile { Name="Jinc",ExecutablePath=Path.GetFullPath("filter-app"),Settings=new RuntimeSettings { Effect=24,GraphParameters=new() { ["24.ARStrength"] = .1f } } });
        string directory=Path.Combine(Path.GetTempPath(),"magpie-filters-"+Guid.NewGuid().ToString("N"));
        try
        {
            string path=Path.Combine(directory,"settings.json");root.WriteFile(path);
            var loaded=JsonSerializer.Deserialize(File.ReadAllText(path),SettingsJson.Default.RuntimeSettings)!;loaded.Validate();
            Require(loaded.ResolveChain(7,5,190,130)[1].Extra[0]==1&&loaded.Profiles[0].Settings.ResolveChain(7,5,19,13)[0].Extra[0]==.1f,"AOT filter profile restore failed.");
        }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
        Console.WriteLine("FILTER MANAGED PASS: registered Jinc/mean parameters, padding budgets, static parameter rerender, duplicate skip, direct/configured GPU paths, original modes and unknown parameters, independent profiles and AOT disk restore.");
    }
}
