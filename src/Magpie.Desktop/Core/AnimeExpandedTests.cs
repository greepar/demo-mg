using System.Text.Json;
namespace Magpie.Desktop.Core;
internal static class AnimeExpandedTests
{
    internal static readonly uint[] Effects=[15,16,17,18];
    internal static string OriginalModes=>"{\"scalingModes\":["+string.Join(',',Effects.Select(id=>"{\"name\":\""+EffectStageSettings.Name(id)+"\",\"effects\":[{\"name\":\""+ScalingModeInterchange.EffectPaths[id].Replace("\\","\\\\")+"\",\"scalingType\":2,\"scale\":{\"x\":99,\"y\":77}}]}"))+ "]}";
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
    internal static unsafe void Verify(EngineHandle engine)
    {
        var root=new RuntimeSettings();var import=ScalingModeInterchange.Import(root,OriginalModes,_=>{});
        Require(import==new ModeImportResult(4,0,0) && root.Modes.All(m=>m.IsAvailable),"Expanded Anime4K original modes are still deferred.");
        var roundtrip=ScalingModeInterchange.Parse(ScalingModeInterchange.Export(root.Modes));
        Require(roundtrip.Select(m=>m.Steps[0].Effect).SequenceEqual(Effects) && roundtrip.All(m=>m.Steps[0].OriginalScale?.X==99),"Expanded Anime4K original mode metadata did not roundtrip.");
        byte[] input=new byte[7*5*4];for(int i=0;i<input.Length;i++)input[i]=i%4==3?(byte)255:(byte)(i*31%251);
        var processor=new ScalingFrameProcessor(0);
        bool Run(NativeEffectStep[] graph,bool fresh,long time)
        {
            fixed(byte* pixels=input)return processor.Process(engine,(nint)pixels,7,5,fresh,graph,DuplicateFrameMode.Always,false,0,0,time,true);
        }
        foreach(uint effect in Effects)
        {
            var settings=new RuntimeSettings { Effect=effect };var plan=settings.ResolveChain(7,5,190,130);
            Require(plan.Length==1 && plan[0].Effect==effect && plan[0].Width==14 && plan[0].Height==10,"Expanded Anime4K fixed 2x planning failed.");
            Require(Run(plan,true,effect*2),"Expanded Anime4K model switch did not render.");
            byte[] expected=new byte[14*10*4];
            fixed(byte* source=input,target=expected)Require(NativeCore.ScaleExtended(engine,(nint)source,7,5,target,14,10,effect,0,0)!=0,"Expanded Anime4K standalone API failed.");
            Require(processor.Pixels.SequenceEqual(expected),"Expanded Anime4K configured/standalone pixels differ.");
            Require(!Run(plan,true,effect*2+1),"Expanded Anime4K duplicate reached Vulkan.");
            int passes=effect is 16 or 18?8:4;
            if(processor.GpuProfile.Supported)Require(processor.GpuProfile.Stages.Length==1 && processor.GpuProfile.Stages[0].Effect==effect && processor.GpuProfile.Stages[0].Passes.Length==passes,"Expanded Anime4K pass timings are incomplete.");
            var mode=root.Modes.Single(m=>m.Steps[0].Effect==effect);root.SelectedModeId=mode.Id;
            Require(root.ResolveChain(7,5,190,130)[0].Effect==effect,"Expanded Anime4K imported mode did not resolve.");
        }
        Require(Effects.Select(EffectStageSettings.IntermediatePixelUnits).SequenceEqual(new uint[]{8,28,4,28}),"Expanded Anime4K intermediate byte budgets differ from original textures.");
        var duplicate=root.CopyForProfile();duplicate.SelectedModeId=null;duplicate.Effect=16;
        root.Profiles.Add(new ApplicationProfile { Name="Anime4K VL",ExecutablePath=Path.GetFullPath("anime-vl"),Settings=duplicate });
        string directory=Path.Combine(Path.GetTempPath(),"magpie-anime-expanded-"+Guid.NewGuid().ToString("N"));
        try
        {
            string path=Path.Combine(directory,"settings.json");root.WriteFile(path);
            var loaded=JsonSerializer.Deserialize(File.ReadAllText(path),SettingsJson.Default.RuntimeSettings)!;loaded.Validate();
            Require(loaded.Modes.Count==4 && loaded.ResolveChain(7,5,190,130)[0].Effect==18 && loaded.Profiles[0].Settings.ResolveChain(7,5,190,130)[0].Effect==16,"Expanded Anime4K defaults/profile AOT disk recovery failed.");
        }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
        Console.WriteLine("ANIME EXPANDED PASS: Upscale L/VL and Denoise S/VL, fixed 2x, real Vulkan model switches, four/eight-pass timing, duplicate skip, original import/export, byte budgets and AOT profile persistence.");
    }
}
