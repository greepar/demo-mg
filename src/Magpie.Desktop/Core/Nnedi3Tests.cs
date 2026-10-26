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
        byte[]? snapshotBeforeLegacy=null;
        byte[]? gpuCopyBeforeLegacy=null;
        string? diagnosticsBeforeLegacy=null;
        // The observed failure contains whole zero RGBA pixels. Preserve the
        // existing native buffer before the legacy call overwrites it, without
        // another GPU submission, to distinguish copy/visibility from rerender.
        bool invalidAlpha=false;for(int i=3;i<processor.Pixels.Length;i+=4)invalidAlpha|=processor.Pixels[i]!=255;
        if(invalidAlpha)
        {
            try {
                var snapshot=StageSnapshot.Read(engine).Single();snapshotBeforeLegacy=snapshot.Copy(engine);
                byte[] copy=new byte[snapshotBeforeLegacy.Length];
                fixed(byte* target=copy)
                    if(NativeCore.ProbeStageReadback(engine,snapshot.Serial,0,target,(ulong)copy.Length)!=1)throw NativeCore.Error();
                gpuCopyBeforeLegacy=copy;
            }
            catch(Exception failure){Console.Error.WriteLine("Could not preserve pre-legacy NNEDI3 native output: "+failure.Message);}
            try {
                diagnosticsBeforeLegacy=Path.Combine(Path.GetTempPath(),"magpie-nnedi3-mismatch-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(diagnosticsBeforeLegacy);
                File.WriteAllBytes(Path.Combine(diagnosticsBeforeLegacy,"input-7x5.rgba"),source);
                File.WriteAllBytes(Path.Combine(diagnosticsBeforeLegacy,"configured-14x10.rgba"),processor.Pixels);
                if(snapshotBeforeLegacy is not null)File.WriteAllBytes(Path.Combine(diagnosticsBeforeLegacy,"native-before-legacy-14x10.rgba"),snapshotBeforeLegacy);
                if(gpuCopyBeforeLegacy is not null)File.WriteAllBytes(Path.Combine(diagnosticsBeforeLegacy,"gpu-copy-before-legacy-14x10.rgba"),gpuCopyBeforeLegacy);
                Console.Error.WriteLine("NNEDI3 pre-legacy diagnostics: "+diagnosticsBeforeLegacy);
            }catch(Exception failure){Console.Error.WriteLine("Could not save pre-legacy NNEDI3 pixels: "+failure.Message);}
        }
        byte[] expected=new byte[14*10*4];
        fixed(byte* pixels=source,target=expected)
            Require(NativeCore.ScaleExtended(engine,(nint)pixels,7,5,target,14,10,13,0,0)!=0,"Standalone NNEDI3 failed.");
        if(!processor.Pixels.SequenceEqual(expected)||invalidAlpha)
        {
            int first=-1,differences=0,maximumDelta=0;
            for(int i=0;i<expected.Length;i++)if(processor.Pixels[i]!=expected[i])
            {
                if(first<0)first=i;differences++;maximumDelta=Math.Max(maximumDelta,Math.Abs(processor.Pixels[i]-expected[i]));
            }
            string details=first>=0?$"NNEDI3 configured and legacy APIs differ: {differences} channels, max delta {maximumDelta}, first byte {first} ({processor.Pixels[first]} vs {expected[first]}).":
                "NNEDI3 configured and legacy outputs agree but contain non-opaque alpha; the original shader always writes alpha 1.";
            try
            {
                string diagnostics=diagnosticsBeforeLegacy??Path.Combine(Path.GetTempPath(),"magpie-nnedi3-mismatch-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(diagnostics);
                File.WriteAllBytes(Path.Combine(diagnostics,"input-7x5.rgba"),source);
                File.WriteAllBytes(Path.Combine(diagnostics,"configured-14x10.rgba"),processor.Pixels);
                File.WriteAllBytes(Path.Combine(diagnostics,"legacy-14x10.rgba"),expected);
                if(snapshotBeforeLegacy is not null)File.WriteAllBytes(Path.Combine(diagnostics,"native-before-legacy-14x10.rgba"),snapshotBeforeLegacy);
                if(gpuCopyBeforeLegacy is not null)File.WriteAllBytes(Path.Combine(diagnostics,"gpu-copy-before-legacy-14x10.rgba"),gpuCopyBeforeLegacy);
                File.WriteAllText(Path.Combine(diagnostics,"comparison.txt"),details+"\nDevice: "+System.Runtime.InteropServices.Marshal.PtrToStringUTF8(NativeCore.DeviceName(engine))+"\n");
                Console.Error.WriteLine("NNEDI3 mismatch diagnostics: "+diagnostics);
            }
            catch(Exception failure) { Console.Error.WriteLine("Could not save NNEDI3 mismatch diagnostics: "+failure.Message); }
            throw new InvalidOperationException(details);
        }
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
