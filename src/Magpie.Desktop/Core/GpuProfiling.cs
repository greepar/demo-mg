using System.Runtime.InteropServices;
using System.Text;
namespace Magpie.Desktop.Core;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeGpuProfile
{
    public uint Supported,Enabled,Status,TimestampBits,StageCount,PassCount;
    public ulong FrameSequence;
    public double TimestampPeriodNs,TotalMilliseconds;
    public fixed uint EffectIds[17];
    public fixed uint FirstPass[17];
    public fixed uint PassCounts[17];
    public fixed double StageMilliseconds[17];
    public fixed double PassMilliseconds[256];
}
internal sealed record GpuStageTiming(uint Effect,double Milliseconds,double[] Passes);
internal sealed record GpuProfileSnapshot(bool Supported,bool Enabled,uint Status,ulong Sequence,double TotalMilliseconds,GpuStageTiming[] Stages,string? Error=null)
{
    public bool Valid=>Status==1;
    public static GpuProfileSnapshot Off { get; }=new(false,false,0,0,0,[]);
    public static unsafe GpuProfileSnapshot Read(EngineHandle engine)
    {
        if(NativeCore.GetGpuProfile(engine,out var raw)==0)throw NativeCore.Error();
        if(raw.Supported>1 || raw.Enabled>1 || raw.Status>3 || raw.StageCount>17 || raw.PassCount>256)throw new InvalidDataException("Invalid GPU profiling ABI data.");
        if(raw.Status!=1)return new(raw.Supported!=0,raw.Enabled!=0,raw.Status,raw.FrameSequence,0,[]);
        if(raw.Supported!=1 || raw.Enabled!=1 || raw.TimestampBits is 0 or >64 || !double.IsFinite(raw.TimestampPeriodNs) || raw.TimestampPeriodNs<=0 || !double.IsFinite(raw.TotalMilliseconds) || raw.TotalMilliseconds<0 || raw.StageCount==0)throw new InvalidDataException("Invalid device timestamp sample.");
        var stages=new GpuStageTiming[raw.StageCount];uint next=0;
        for(int i=0;i<stages.Length;i++)
        {
            uint count=raw.PassCounts[i];
            if(raw.FirstPass[i]!=next || count==0 || count>raw.PassCount-next)throw new InvalidDataException("Invalid GPU pass mapping.");
            var passes=new double[count];
            for(int p=0;p<passes.Length;p++)
            {
                double ms=raw.PassMilliseconds[next+(uint)p];
                if(!double.IsFinite(ms) || ms<0)throw new InvalidDataException("Invalid GPU pass duration.");passes[p]=ms;
            }
            double stage=raw.StageMilliseconds[i];
            if(!double.IsFinite(stage) || stage<0 || Math.Abs(stage-passes.Sum())>1e-6)throw new InvalidDataException("Invalid GPU stage duration.");
            stages[i]=new(raw.EffectIds[i],stage,passes);next+=count;
        }
        if(next!=raw.PassCount || stages.Sum(s=>s.Milliseconds)>raw.TotalMilliseconds+1e-6)throw new InvalidDataException("Invalid GPU timing totals.");
        return new(true,true,1,raw.FrameSequence,raw.TotalMilliseconds,stages);
    }
    public string Describe(bool passes)
    {
        if(!Enabled)return "GPU profiler off.";
        if(Error is not null)return Error;
        if(Status==2 || !Supported)return "Vulkan timestamp queries are unavailable on this queue.";
        if(!Valid)return Error ?? "No valid GPU timing sample yet.";
        var text=new StringBuilder($"{Localization.Get("Overlay_Profiler_Timings_Total")}: {Stages.Sum(s=>s.Milliseconds):F3} ms\n");
        for(int i=0;i<Stages.Length;i++)
        {
            var stage=Stages[i];string name=EffectStageSettings.Name(stage.Effect);
            if(!passes)text.AppendLine($"{i+1}. {name}: {stage.Milliseconds:F3} ms");
            else for(int p=0;p<stage.Passes.Length;p++)
            {
                string description=PassExportCatalog.All.TryGetValue(stage.Effect,out var descriptions)&&p<descriptions.Length?" · "+descriptions[p].Name:"";
                text.AppendLine($"{i+1}. {name} / pass {p+1}{description}: {stage.Passes[p]:F3} ms");
            }
        }
        text.Append($"Final barrier/query tail: {Math.Max(0,TotalMilliseconds-Stages.Sum(s=>s.Milliseconds)):F3} ms\nApproximate GPU intervals include transfers, barriers and query overhead.");
        return text.ToString();
    }
}
