using System.Runtime.InteropServices;
using System.Text.Json;

namespace Magpie.Desktop.Core;

// Headless numerical probe. It never loads settings, starts capture or opens UI.
internal static class Nnedi3Stress
{
    internal static unsafe void Run(int iterations,string directory)
    {
        if(iterations is <1 or >10000)throw new ArgumentOutOfRangeException(nameof(iterations));
        Directory.CreateDirectory(directory);
        (uint W,uint H)[] sizes=[(7,5),(31,17),(33,9),(65,3),(1,1),(17,17),(32,8)];
        int completed=0,engines=0;string device="";
        while(completed<iterations)
        {
            using var engine=NativeCore.Create();if(engine.IsInvalid)throw NativeCore.Error();engines++;
            device=Marshal.PtrToStringUTF8(NativeCore.DeviceName(engine))??"unknown";
            for(int batch=0;batch<16&&completed<iterations;batch++,completed++)
            {
                var (w,h)=sizes[completed%sizes.Length];uint ow=w*2,oh=h*2;
                byte[] source=new byte[checked((int)(w*h*4))],warm=new byte[source.Length];
                for(int i=0;i<source.Length;i++) {source[i]=i%4==3?(byte)255:(byte)((i*31+completed*17)%251);warm[i]=(byte)(255-source[i]);}
                byte[] configured=new byte[checked((int)(ow*oh*4))],legacy=new byte[configured.Length],primed=new byte[configured.Length];
                NativeEffectStep step=new(){Effect=13,Width=ow,Height=oh};
                if(NativeCore.SetGpuProfiling(engine,completed%2)!=1)throw NativeCore.Error();
                // Force graph/non-graph transitions without changing output size,
                // reusing the same output allocation with visibly different data.
                fixed(byte* input=warm,output=primed)
                    if(NativeCore.ScaleExtended(engine,(nint)input,w,h,output,ow,oh,3,.2f,.7f)!=1)throw NativeCore.Error();
                fixed(byte* input=source,output=configured)
                    if(NativeCore.ScaleChain(engine,(nint)input,w,h,output,(ulong)configured.Length,&step,1)!=1)throw NativeCore.Error();
                // Capture the existing native output before legacy rerenders it.
                // This is a CPU copy, with no extra GPU dispatch or fence.
                var stage=StageSnapshot.Read(engine).Single();byte[] snapshot=stage.Copy(engine);
                ulong timing=GpuProfileSnapshot.Read(engine).Sequence;
                byte[] gpuCopy=new byte[configured.Length];
                fixed(byte* output=gpuCopy)
                    if(NativeCore.ProbeStageReadback(engine,stage.Serial,0,output,(ulong)gpuCopy.Length)!=1)throw NativeCore.Error();
                if(StageSnapshot.Read(engine).Single().Serial!=stage.Serial)throw new InvalidOperationException("Diagnostic GPU copy changed the shader execution identity.");
                if(GpuProfileSnapshot.Read(engine).Sequence!=timing)throw new InvalidOperationException("Diagnostic GPU copy fabricated a shader timing sample.");
                if(batch==0)
                {
                    byte[] sentinel=Enumerable.Repeat((byte)123,gpuCopy.Length).ToArray();
                    fixed(byte* target=sentinel) {
                        if(NativeCore.ProbeStageReadback(engine,stage.Serial+1,0,target,(ulong)sentinel.Length)!=0||
                            NativeCore.ProbeStageReadback(engine,stage.Serial,0,target,(ulong)sentinel.Length-1)!=0||
                            NativeCore.ProbeStageReadback(engine,stage.Serial,99,target,(ulong)sentinel.Length)!=0)
                            throw new InvalidOperationException("Readback probe accepted stale, length or stage arguments.");
                    }
                    if(sentinel.Any(value=>value!=123))throw new InvalidOperationException("Rejected diagnostic readback modified caller bytes.");
                }
                fixed(byte* input=source,output=legacy)
                    if(NativeCore.ScaleExtended(engine,(nint)input,w,h,output,ow,oh,13,0,0)!=1)throw NativeCore.Error();
                bool alpha=true;for(int i=3;i<configured.Length;i+=4)alpha&=configured[i]==255&&legacy[i]==255;
                if(!configured.AsSpan().SequenceEqual(snapshot)||!configured.AsSpan().SequenceEqual(gpuCopy)||!configured.AsSpan().SequenceEqual(legacy)||!alpha)
                {
                    string failure=Path.Combine(directory,"failure-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(failure);
                    foreach(var (name,pixels) in new[]{("input",source),("primed",primed),("configured",configured),("native-snapshot",snapshot),("gpu-copy",gpuCopy),("legacy",legacy)})
                        File.WriteAllBytes(Path.Combine(failure,name+".rgba"),pixels);
                    WriteReport(Path.Combine(failure,"report.json"),false,completed,engines,device,w,h,
                        configured.AsSpan().SequenceEqual(snapshot),configured.AsSpan().SequenceEqual(legacy),alpha,configured.AsSpan().SequenceEqual(gpuCopy));
                    throw new InvalidOperationException("NNEDI3 stress failed at iteration "+completed+"; diagnostics: "+failure);
                }
            }
        }
        WriteReport(Path.Combine(directory,"report.json"),true,completed,engines,device,0,0,true,true,true,true);
        Console.WriteLine($"NNEDI3 STRESS PASS: {completed} iterations, {engines} engines, seven input sizes, graph/allocation reuse, profiling on/off, exact configured/native snapshot/separate GPU copy/legacy pixels and opaque alpha; device={device}.");
    }
    private static void WriteReport(string path,bool passed,int iterations,int engines,string device,uint width,uint height,bool snapshot,bool legacy,bool alpha,bool gpuCopy)
    {
        using var stream=File.Create(path);using var writer=new Utf8JsonWriter(stream,new(){Indented=true});
        writer.WriteStartObject();writer.WriteBoolean("passed",passed);writer.WriteNumber("iterations",iterations);writer.WriteNumber("engines",engines);
        writer.WriteString("device",device);writer.WriteNumber("input_width",width);writer.WriteNumber("input_height",height);
        writer.WriteBoolean("configured_matches_native_snapshot",snapshot);writer.WriteBoolean("configured_matches_legacy",legacy);
        writer.WriteBoolean("configured_matches_separate_gpu_copy",gpuCopy);
        writer.WriteBoolean("alpha_is_opaque",alpha);writer.WriteBoolean("intermittent_issue_resolved",false);writer.WriteEndObject();
    }
}
