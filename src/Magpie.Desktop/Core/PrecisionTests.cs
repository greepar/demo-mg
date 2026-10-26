namespace Magpie.Desktop.Core;

internal static class PrecisionTests
{
    internal static unsafe void Verify()
    {
        var root=RuntimeSettings.Current;bool previous=root.DisableFP16;
        string directory=Path.Combine(Path.GetTempPath(),"magpie-precision-"+Guid.NewGuid().ToString("N"));
        try
        {
            if(GraphCatalog.All.Count(e=>e.SupportsFP16)!=42)throw new InvalidOperationException("Original FP16 capabilities differ from the embedded corpus.");
            foreach(bool disabled in new[]{false,true})
            {
                root.DisableFP16=disabled;
                using var engine=NativeCore.CreateForSettings(new RuntimeSettings());if(engine.IsInvalid)throw NativeCore.Error();
                bool supported=NativeCore.FP16Supported(engine)!=0;
                foreach(var effect in GraphCatalog.All)
                    if((NativeCore.EffectUsesFP16(engine,effect.Id)!=0)!=(!disabled&&supported&&effect.SupportsFP16))throw new InvalidOperationException("Managed precision selection differs from the original effect/device capability.");
                var plan=new RuntimeSettings { Effect=6,ScaleType=0,ScaleX=2,ScaleY=2 }.ResolveChain(7,5,140,100);
                if(plan.Length!=2||plan[0].Effect!=4||plan[1].Effect!=5)throw new InvalidOperationException("Managed FSR composite did not resolve.");
                if(NativeCore.SetGpuProfiling(engine,1)==0)throw NativeCore.Error();
                byte[] input=new byte[7*5*4],middle=new byte[14*10*4],separate=new byte[middle.Length],combined=new byte[middle.Length];
                for(int i=0;i<input.Length;i++)input[i]=i%4==3?(byte)119:(byte)(i*71%251);
                fixed(byte* source=input,temp=middle,expected=separate,actual=combined)
                fixed(NativeEffectStep* steps=plan)
                {
                    if(NativeCore.ScaleExtended(engine,(nint)source,7,5,temp,14,10,4,plan[0].Parameter1,plan[0].Parameter2)==0||
                       NativeCore.ScaleExtended(engine,(nint)temp,14,10,expected,14,10,5,plan[1].Parameter1,plan[1].Parameter2)==0||
                       NativeCore.ScaleChain(engine,(nint)source,7,5,actual,(ulong)combined.Length,steps,2)==0)throw NativeCore.Error();
                }
                var sample=GpuProfileSnapshot.Read(engine);
                if(sample.Valid&&sample.Stages.Any(stage=>stage.UsesFP16!=(NativeCore.EffectUsesFP16(engine,stage.Effect)!=0)))throw new InvalidOperationException("Profile precision metadata differs from actual engine pipelines.");
                if(!combined.SequenceEqual(separate))throw new InvalidOperationException("FP16/disabled FSR combined output differs from separately executed EASU/RCAS.");
                string path=Path.Combine(directory,"settings.json");root.WriteFile(path);
                if(RuntimeSettings.ReadFile(path).DisableFP16!=disabled)throw new InvalidOperationException("FP16 preference did not persist through AOT settings.");
            }
        }
        finally { root.DisableFP16=previous;if(Directory.Exists(directory))Directory.Delete(directory,true); }
        Console.WriteLine("PRECISION MANAGED PASS: 42 original FP16 capabilities, actual device/automatic/disabled selection, exact separate/combined FSR EASU/RCAS output, FP32-only effects and AOT settings persistence.");
    }
}
