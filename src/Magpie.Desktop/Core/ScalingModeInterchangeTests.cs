using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Magpie.Desktop.Core;

internal static class ScalingModeInterchangeTests
{
    internal const string Unknown="""{"scalingModes":[{"name":"Future model","effects":[{"name":"Anime4K\\Unported_model","scalingType":0,"scale":{"x":1,"y":1},"parameters":{"strength":0.25},"future":{"formats":["RGBA16F",16]}}]}]}""";
    internal const string Bicubic="""{"scalingModes":[{"name":"Imported Bicubic","effects":[{"name":"Bicubic","scalingType":2,"scale":{"x":19,"y":13},"parameters":{"paramB":0.2,"paramC":0.6}}]}]}""";
    internal static string Fixture()
    {
        using var source=typeof(ScalingModeInterchangeTests).Assembly.GetManifestResourceStream("Magpie.Fixtures.OriginalScalingModes.json") ?? throw new InvalidDataException("Original fixture is missing.");
        using var reader=new StreamReader(source);return reader.ReadToEnd();
    }
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
    private static void Reject(Action action,string message)
    {
        try { action(); }catch(Exception error) when(error is InvalidDataException or JsonException or IOException or DecoderFallbackException or EncoderFallbackException) { return; }throw new InvalidOperationException(message);
    }
    internal static unsafe void Verify(EngineHandle engine)
    {
        Require(new RuntimeSettings { ScaleType=0,ScaleX=.5,ScaleY=.9 }.OutputSize(5,5,100,100)==(3u,5u),"Original float/lround half-pixel dimensions differ.");
        var settings=new RuntimeSettings();var fixture=Fixture();
        string directory=Path.Combine(Path.GetTempPath(),"magpie-mode-io-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            string path=Path.Combine(directory,"desktop-settings.json");
            var result=ScalingModeInterchange.Import(settings,fixture,s=>s.WriteFile(path));
            Require(result==new ModeImportResult(7,0,0),"Original seven presets were not imported as runnable modes.");
            Require(settings.Modes.SelectMany(m=>m.Steps).Select(s=>s.Effect).SequenceEqual(new uint[]{1,4,5,9,11,8,12,0}),"Original effect paths were mapped to incorrect shaders.");
            Require(settings.Modes[5].Steps[0].AdditionalParameters["interlace"]==0 && settings.Modes[5].Steps[0].Crt[0]==1.5f && settings.Modes[5].Steps[0].Crt[3]==0,"Original CRT overrides or ignored parameters were lost.");
            var beforeModes=settings.Modes;settings.SelectedModeId=settings.Modes[1].Id;
            var application=new ApplicationProfile { Name="Fixture app",ExecutablePath=Path.GetFullPath(Path.Combine(directory,"app")),Settings=settings.CopyForProfile() };settings.Profiles.Add(application);
            var second=ScalingModeInterchange.Import(settings,fixture,s=>s.WriteFile(path));
            Require(second.Renamed==7 && ReferenceEquals(beforeModes,settings.Modes) && ReferenceEquals(application.Settings.Modes,settings.Modes) && settings.SelectedModeId==settings.Modes[1].Id,"Append import broke names, mode references or active selection.");
            var roundtrip=ScalingModeInterchange.Parse(ScalingModeInterchange.Export(settings.Modes.Take(7)));
            for(int i=0;i<7;i++)
            {
                var expected=settings.Modes[i].Resolve(3,2,19,13);var actual=roundtrip[i].Resolve(3,2,19,13);
                Require(MemoryMarshal.AsBytes(expected.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(actual.AsSpan())),"Export/import changed an original preset's native chain.");
            }
            // Run all imported presets; compare nearest with CPU pixels and configured Bicubic with a manual native graph.
            byte[] pixels=[11,23,37,255,51,67,83,255,97,109,127,255,139,151,167,255,181,193,211,255,223,239,251,255];
            foreach(var mode in settings.Modes.Take(7))
            {
                var graph=mode.Resolve(3,2,19,13);
                // Metadata expectation above is independent; nearest below additionally has exact CPU pixels.
                var output=Render(engine,pixels,graph);Require(output.Length==(int)(graph[^1].Width*graph[^1].Height*4),"Imported preset did not render.");
                if(mode.Name=="Integer Scale 2x")
                    for(int y=0;y<4;y++)for(int x=0;x<6;x++)for(int c=0;c<4;c++)Require(output[(y*6+x)*4+c]==pixels[((y/2)*3+x/2)*4+c],"Imported 2x preset differs from independent CPU nearest pixels.");
            }
            var bicubic=ScalingModeInterchange.Parse(Bicubic)[0];
            NativeEffectStep[] manual=[new(){Effect=3,Width=19,Height=13,Parameter1=.2f,Parameter2=.6f}];
            Require(Render(engine,pixels,bicubic.Resolve(3,2,19,13)).SequenceEqual(Render(engine,pixels,manual)),"Imported Bicubic parameters did not reach the real GPU.");
            var unknown=ScalingModeInterchange.Parse(Unknown)[0];Require(!unknown.IsAvailable,"Unported effect was silently substituted.");
            settings.Modes.Add(unknown);settings.SelectedModeId=unknown.Id;
            try { settings.EnsureSelectedModeAvailable();throw new InvalidOperationException("Unavailable effect passed capture preflight."); }catch(InvalidOperationException error) when(error.Message.Contains("unavailable effects")) { }
            try { unknown.Resolve(3,2,19,13);throw new InvalidOperationException("Unavailable effect compiled to a fake chain."); }catch(InvalidOperationException error) when(error.Message.Contains("unavailable effects")) { }
            var unknownExport=ScalingModeInterchange.Export([unknown]);
            Require(JsonNode.DeepEquals(JsonNode.Parse(Unknown),JsonNode.Parse(unknownExport)),"Deferred effect parameters/extensions were not preserved exactly.");
            settings.WriteFile(path);var loaded=JsonSerializer.Deserialize(File.ReadAllText(path),SettingsJson.Default.RuntimeSettings)!;loaded.Validate();
            Require(!loaded.Modes[^1].IsAvailable && JsonNode.DeepEquals(JsonNode.Parse(unknownExport),JsonNode.Parse(ScalingModeInterchange.Export([loaded.Modes[^1]]))),"Deferred data did not survive AOT settings persistence.");
            string exportPath=Path.Combine(directory,"ScalingModes.json");
            ScalingModeInterchange.WriteLocalAsync(exportPath,ScalingModeInterchange.Export(settings.Modes)).GetAwaiter().GetResult();
            ScalingModeInterchange.WriteLocalAsync(exportPath,unknownExport).GetAwaiter().GetResult();
            using(var input=File.OpenRead(exportPath))Require(ScalingModeInterchange.ReadAsync(input).GetAwaiter().GetResult()==unknownExport,"Export overwrite retained stale bytes.");
            using(var stream=new MemoryStream())
            {
                stream.Write(new byte[10000]);ScalingModeInterchange.WriteAsync(stream,unknownExport).GetAwaiter().GetResult();
                Require(Encoding.UTF8.GetString(stream.ToArray())==unknownExport,"Provider stream export did not truncate/reset position.");
                byte[] before=stream.ToArray();Reject(()=>ScalingModeInterchange.WriteAsync(stream,"{}").GetAwaiter().GetResult(),"Malformed export was written.");
                Require(stream.ToArray().SequenceEqual(before),"Serialization failure damaged the export destination.");
            }
            var corrupt=unknown.Duplicate("Corrupt");corrupt.Steps[0].Crt=null!;
            Reject(()=>corrupt.Validate(),"Deferred metadata bypassed validation of editor backing values.");
            corrupt=unknown.Duplicate("Corrupt");corrupt.Steps[0].Parameter1=2;
            Reject(()=>corrupt.Validate(),"Deferred metadata accepted unsafe parameter controls.");
            var copied=unknown.Duplicate("Copy");copied.Steps[0].AdditionalParameters["local"]=3;
            Require(!unknown.Steps[0].AdditionalParameters.ContainsKey("local"),"Mode copy aliases additional parameter storage.");
            var empty=ScalingModeInterchange.Parse("{\"scalingModes\":[{\"name\":\"Identity\"}]}")[0];
            Require(!empty.IsAvailable && ScalingModeInterchange.Parse(ScalingModeInterchange.Export([empty]))[0].Steps.Count==0,"Original empty mode was not preserved as unavailable.");
            try { empty.Resolve(3,2,19,13);throw new InvalidOperationException("Empty mode produced an invented passthrough."); }catch(InvalidOperationException error) when(error.Message.Contains("no effects")) { }
            var withComments=ScalingModeInterchange.Parse("\uFEFF{/*comment*/\"scalingModes\":[{\"name\":\"Identity\",},],}");Require(withComments.Count==1,"Original import comments/trailing commas/BOM were rejected.");
            int previous=settings.Modes.Count;string existingFile=File.ReadAllText(path),selection=settings.SelectedModeId!;
            string blocked=Path.Combine(directory,"blocked");File.WriteAllText(blocked,"keep");
            Reject(()=>ScalingModeInterchange.Import(settings,Bicubic,s=>s.WriteFile(Path.Combine(blocked,"settings.json"))),"Failed persistence was accepted.");
            Require(settings.Modes.Count==previous && settings.SelectedModeId==selection && File.ReadAllText(path)==existingFile && File.ReadAllText(blocked)=="keep","Failed import partially changed settings or disk.");
            foreach(string invalid in new[]{"null","[]","{}","{\"scalingModes\":{}}","{\"scalingModes\":[null]}","{\"scalingModes\":[{\"name\":\"\"}]}","{\"scalingModes\":[],\"scalingModes\":[]}",Bicubic.Replace("19,","0,"),Bicubic.Replace("0.2","\"wrong\""),Bicubic.Replace("0.2","1e999"),Bicubic.Replace("\"paramB\":0.2","\"paramB\":0.2,\"paramB\":0.3")})
            {
                Reject(()=>ScalingModeInterchange.Import(settings,invalid,s=>s.WriteFile(path)),"Malformed import was accepted: "+invalid);
                Require(settings.Modes.Count==previous && File.ReadAllText(path)==existingFile,"Malformed import changed authoritative state.");
            }
            string tooManyEffects="{\"scalingModes\":[{\"name\":\"oversized\",\"effects\":["+string.Join(',',Enumerable.Repeat("{\"name\":\"Nearest\"}",17))+"]}]}";
            Reject(()=>ScalingModeInterchange.Parse(tooManyEffects),"Oversized chain accepted.");
            string tooManyModes="{\"scalingModes\":["+string.Join(',',Enumerable.Repeat("{\"name\":\"mode\"}",101))+"]}";
            Reject(()=>ScalingModeInterchange.Import(settings,tooManyModes,s=>s.WriteFile(path)),"Import exceeded runtime mode capacity.");
            string oversizedFile="{\"scalingModes\":["+string.Join(',',Enumerable.Repeat("{\"name\":\"mode\"}",257))+"]}";
            Reject(()=>ScalingModeInterchange.Parse(oversizedFile),"Oversized mode file accepted.");
            var fullStore=new RuntimeSettings { Modes=Enumerable.Range(0,100).Select(i=>new EffectModeSettings { Name="Mode "+i }).ToList() };
            Require(ScalingModeInterchange.Parse(ScalingModeInterchange.Export(ScalingModeInterchange.Builtins(fullStore).Concat(fullStore.Modes))).Count==107,"Full store export lost built-in or custom modes.");
            Reject(()=>ScalingModeInterchange.Parse(new string(' ',ScalingModeInterchange.MaximumBytes+1)),"Oversized document accepted.");
            Reject(()=>ScalingModeInterchange.ReadAsync(new MemoryStream(new byte[ScalingModeInterchange.MaximumBytes+1])).GetAwaiter().GetResult(),"Unbounded file stream was read.");
            Reject(()=>ScalingModeInterchange.ReadAsync(new MemoryStream(new byte[]{0xff,0xfe})).GetAwaiter().GetResult(),"Invalid UTF-8 was silently replaced.");
            var fractional=ScalingModeInterchange.Parse(Bicubic.Replace("\"x\":19","\"x\":19.5"))[0];
            Require(fractional.IsAvailable && fractional.Resolve(3,2,19,13)[0].Width==20,"Original fractional absolute size was not rounded away from zero.");
            using(var exportedFraction=JsonDocument.Parse(ScalingModeInterchange.Export([fractional])))Require(exportedFraction.RootElement.GetProperty("scalingModes")[0].GetProperty("effects")[0].GetProperty("scale").GetProperty("x").GetDouble()==19.5,"Fractional absolute metadata was lost.");
            var fractionalSettings=new RuntimeSettings { Modes=[fractional] };
            var restoredFractional=JsonSerializer.Deserialize(JsonSerializer.Serialize(fractionalSettings,SettingsJson.Default.RuntimeSettings),SettingsJson.Default.RuntimeSettings)!;restoredFractional.Validate();
            Require(restoredFractional.Modes[0].Steps[0].OriginalScale?.X==19.5f,"Original absolute metadata did not survive source-generated settings JSON.");
            var manyParameters=new EffectStageSettings { Effect=12,AdditionalParameters=Enumerable.Range(0,128).ToDictionary(i=>"extra"+i,i=>(float)i) };
            var maximumParameters=ScalingModeInterchange.Parse(ScalingModeInterchange.Export([new EffectModeSettings { Steps=[manyParameters] }]))[0];
            Require(maximumParameters.IsAvailable && maximumParameters.Steps[0].AdditionalParameters.Count==128,"Export of preserved parameters exceeded the import budget.");
            fractional.Steps[0].Width=21;Require(ScalingModeInterchange.Parse(ScalingModeInterchange.Export([fractional]))[0].Resolve(3,2,19,13)[0].Width==21,"Editing an imported absolute size exported stale dimensions.");
            var tiny=ScalingModeInterchange.Parse(Bicubic.Replace("\"scalingType\":2","\"scalingType\":1").Replace("\"x\":19","\"x\":0.05").Replace("\"y\":13","\"y\":0.05"))[0];
            var tinyGraph=tiny.Resolve(3,2,100,80);Require(tinyGraph[0].Width==5 && tinyGraph[0].Height==3,"Original small Fit factors were lost.");
            var fixedSize=ScalingModeInterchange.Parse("{\"scalingModes\":[{\"name\":\"Fixed CNN\",\"effects\":[{\"name\":\"FSRCNNX/FSRCNNX\",\"scalingType\":2,\"scale\":{\"x\":99.5,\"y\":37}}]}]}")[0];
            Require(fixedSize.IsAvailable && fixedSize.Resolve(3,2,100,80)[0].Width==6,"Fixed-output shader incorrectly applied its ignored scaling fields.");
            Require(ScalingModeInterchange.Parse(ScalingModeInterchange.Export([fixedSize]))[0].Steps[0].OriginalScale!.X==99.5f,"Ignored fixed-output scaling metadata was lost.");
            var crtInteger=ScalingModeInterchange.Parse(fixture.Replace("\"curvature\":0","\"curvature\":0.5"))[5];Require(crtInteger.Steps[0].Crt[3]==1,"Original integer parameter rounding differs.");
            Console.WriteLine("MODE INTERCHANGE PASS: original 7 presets, real Vulkan/CPU pixels, parameter graphs, JSON comments/BOM, append/conflicts, exact deferred export, AOT disk roundtrip, original empty-mode rejection, rollback and bounded invalid inputs.");
        }
        finally { Directory.Delete(directory,true); }
    }
    private static unsafe byte[] Render(EngineHandle engine,byte[] input,NativeEffectStep[] graph)
    {
        byte[] output=new byte[checked((int)(graph[^1].Width*graph[^1].Height*4))];
        fixed(byte* source=input,destination=output)fixed(NativeEffectStep* steps=graph)
            if(NativeCore.ScaleChain(engine,(nint)source,3,2,destination,(ulong)output.Length,steps,(uint)graph.Length)==0)throw NativeCore.Error();
        return output;
    }
}
