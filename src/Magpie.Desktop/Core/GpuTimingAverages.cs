using System.Diagnostics;

namespace Magpie.Desktop.Core;

// Mirrors OverlayDrawer::_DrawProfiler: publish the first sample immediately,
// then flush prior non-skipped pass samples only when a new frame arrives more
// than 500 ms later. UI refreshes of the same native sequence are not frames.
internal sealed class GpuTimingAverages
{
    private GpuProfileSnapshot _display=GpuProfileSnapshot.Off;
    private ulong _sequence;
    private long _updated;
    private double[][] _sums=[];
    private int[][] _counts=[];
    private double _tailSum;
    private int _tailCount;

    internal GpuProfileSnapshot Push(GpuProfileSnapshot sample,long now)
    {
        if(!sample.Valid)
        {
            Reset();
            return _display=sample;
        }
        bool sameShape=_display.Valid&&sample.Stages.Length==_display.Stages.Length&&
            sample.Stages.Select((s,i)=>s.Effect==_display.Stages[i].Effect&&s.UsesFP16==_display.Stages[i].UsesFP16&&s.Passes.Length==_display.Stages[i].Passes.Length).All(s=>s);
        if(!sameShape||sample.Sequence<_sequence||now<_updated)
        {
            Reset();_updated=now;_sequence=sample.Sequence;
            _sums=sample.Stages.Select(s=>new double[s.Passes.Length]).ToArray();
            _counts=sample.Stages.Select(s=>new int[s.Passes.Length]).ToArray();
            return _display=sample with { Stages=sample.Stages.Select(s=>s with { Passes=(double[])s.Passes.Clone() }).ToArray() };
        }
        if(sample.Sequence==_sequence)return _display;
        if(Stopwatch.GetElapsedTime(_updated,now)>TimeSpan.FromMilliseconds(500))
        {
            var stages=new GpuStageTiming[sample.Stages.Length];
            for(int i=0;i<stages.Length;i++)
            {
                var passes=new double[_counts[i].Length];
                for(int p=0;p<passes.Length;p++)
                {
                    passes[p]=_counts[i][p]>0?_sums[i][p]/_counts[i][p]:_display.Stages[i].Passes[p];
                    _sums[i][p]=0;_counts[i][p]=0;
                }
                stages[i]=new(sample.Stages[i].Effect,passes.Sum(),passes,sample.Stages[i].UsesFP16);
            }
            double tail=_tailCount>0?_tailSum/_tailCount:Math.Max(0,_display.TotalMilliseconds-_display.Stages.Sum(s=>s.Milliseconds));
            // These values describe preceding frames, not the triggering frame.
            _display=sample with { Sequence=_sequence,Stages=stages,TotalMilliseconds=stages.Sum(s=>s.Milliseconds)+tail };
            _tailSum=0;_tailCount=0;_updated=now;
        }
        for(int i=0;i<sample.Stages.Length;i++)
        for(int p=0;p<sample.Stages[i].Passes.Length;p++)
        {
            double value=sample.Stages[i].Passes[p];
            if(value>1e-3) { _sums[i][p]+=value;_counts[i][p]++; }
        }
        _tailSum+=Math.Max(0,sample.TotalMilliseconds-sample.Stages.Sum(s=>s.Milliseconds));_tailCount++;
        _sequence=sample.Sequence;
        return _display;
    }

    internal void Reset()
    {
        _display=GpuProfileSnapshot.Off;_sequence=0;_updated=0;
        _sums=[];_counts=[];_tailSum=0;_tailCount=0;
    }
}
