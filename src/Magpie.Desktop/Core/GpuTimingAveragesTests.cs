using System.Diagnostics;

namespace Magpie.Desktop.Core;

internal static class GpuTimingAveragesTests
{
    internal static void Verify()
    {
        static long T(double seconds)=>(long)(seconds*Stopwatch.Frequency);
        static GpuProfileSnapshot S(ulong sequence,double a,double b,uint effect=8)=>new(true,true,1,sequence,a+b+.5,[new(effect,a+b,[a,b])]);
        static void Check(bool value,string message) { if(!value)throw new InvalidOperationException("GPU averages: "+message); }
        var average=new GpuTimingAverages();
        var first=average.Push(S(1,2,4),T(0));
        Check(first.Stages[0].Passes.SequenceEqual(new[]{2d,4d}),"First sample was delayed.");
        Check(ReferenceEquals(first,average.Push(S(2,4,0),T(.1))),"Early sample was published.");
        Check(ReferenceEquals(first,average.Push(S(2,400,400),T(.4))),"Repeated sequence was counted or refreshed.");
        average.Push(S(3,8,.001),T(.5));
        var flushed=average.Push(S(4,100,100),T(.501));
        Check(flushed.Stages[0].Passes.SequenceEqual(new[]{6d,4d})&&flushed.TotalMilliseconds==10.5&&flushed.Sequence==3,"Strict 500ms flush, zero exclusion, retained skipped value, or prior-frame mean differs.");
        average.Push(S(5,200,0),T(.7));
        var second=average.Push(S(6,1,1),T(1.1));
        Check(second.Stages[0].Passes.SequenceEqual(new[]{150d,100d}),"Triggering sample was lost or skipped passes were averaged as zero.");
        Check(ReferenceEquals(second,average.Push(S(6,999,999),T(9))),"Idle refresh changed the sample.");
        var changed=average.Push(S(7,3,5,13),T(9.1));
        Check(changed.Stages[0].Effect==13&&changed.Stages[0].Passes[0]==3,"Effect change retained old statistics.");
        var shape=S(8,7,9,13) with { Stages=[new(13,7,[7])] ,TotalMilliseconds=7.5 };
        Check(average.Push(shape,T(9.2)).Stages[0].Passes.Length==1,"Pass mapping change did not reset.");
        Check(average.Push(S(1,10,20),T(9.3)).Stages[0].Passes[0]==10,"New engine sequence did not reset.");
        Check(!average.Push(GpuProfileSnapshot.Off,T(10)).Valid,"Disable retained old samples.");
        Check(average.Push(S(1,12,24),T(11)).Stages[0].Passes[0]==12,"Re-enable did not publish first sample.");
        Check(average.Push(S(2,14,28),T(1)).Stages[0].Passes[0]==14,"Clock rollback did not reset.");
        var input=S(3,18,36,13);var isolated=average.Push(input,T(2));input.Stages[0].Passes[0]=999;
        Check(isolated.Stages[0].Passes[0]==18,"Published first sample aliases caller arrays.");
        var unavailable=new GpuProfileSnapshot(false,true,2,4,0,[]);
        Check(!average.Push(unavailable,T(4)).Valid,"Unsupported state retained statistics.");
        Check(average.Push(S(5,20,40),T(5)).Stages[0].Passes[0]==20,"Support recovery retained stale statistics.");
        Console.WriteLine("GPU AVERAGES PASS: original strict 500ms cadence, first/prior/triggering samples, zero/threshold exclusion, duplicate/idle suppression, graph/pass/engine/disable/clock resets and query-tail separation.");
    }
}
