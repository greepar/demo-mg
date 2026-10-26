namespace Magpie.Desktop.Core;

internal static class GpuTimingPresentationTests
{
    internal static void Verify()
    {
        static void Check(bool ok,string message) { if(!ok)throw new InvalidOperationException("GPU timeline: "+message); }
        var sample=new GpuProfileSnapshot(true,true,1,1,16.001,[new(11,10.001,[1,2,0,.001,3,4]),new(0,5,[5])]);
        var effects=GpuTimingPresentation.Build(sample,true,false);
        Check(effects.ShowTimeline&&!effects.ShowPasses&&effects.Rows.Length==2&&effects.Segments.Length==2,"Ordinary mode exposed passes or lost effect timeline.");
        Check(effects.Total==15.001&&effects.Segments[0].Milliseconds==10.001&&effects.Segments[1].Milliseconds==5,"Query tail was counted as an algorithm or weights differ.");
        var passes=GpuTimingPresentation.Build(sample,true,true);
        Check(passes.ShowPasses&&passes.Rows.Length==8&&passes.Segments.Length==5,"Developer pass rows or skipped interval filtering differs.");
        Check(passes.Rows[1].Name==PassExportCatalog.All[11][0].Name&&passes.Rows[6].Key==new GpuTimingKey(0,5),"Original descriptions or pass order differ.");
        Check(passes.Segments.Select(s=>s.Key).SequenceEqual(new[]{new GpuTimingKey(0,0),new(0,1),new(0,4),new(0,5),new(1)}),"Pass/next-effect identities differ.");
        Check(passes.Tooltip(passes.Segments[0]).Contains("CuNNy")&&passes.Tooltip(passes.Segments[0]).Contains($"{1d:F3} ms")&&passes.Tooltip(passes.Segments[0]).EndsWith("7%",StringComparison.Ordinal),"Tooltip name, duration or rounded percentage differs.");
        Check(effects.Segments[0].Color!=effects.Segments[1].Color,"Adjacent effects use the same color.");
        Check(passes.Segments[^1].Color==effects.Segments[^1].Color,"View switch recolored the same effect.");
        Check(passes.Segments.Zip(passes.Segments.Skip(1)).All(p=>p.First.Color!=p.Second.Color),"Adjacent timeline colors are identical.");
        var single=sample with { Stages=[sample.Stages[0]],TotalMilliseconds=11.001 };
        Check(!GpuTimingPresentation.Build(single,false,true).ShowTimeline&&GpuTimingPresentation.Build(single,true,true).ShowTimeline,"Single effect bar visibility differs from original.");
        var zero=sample with { Stages=[new(0,0,[0]),new(0,0,[0])],TotalMilliseconds=0 };
        var empty=GpuTimingPresentation.Build(zero,false,false);
        Check(empty.ShowTimeline&&empty.Segments.Length==0&&empty.Total==0&&empty.Rows.Length==2,"Zero timing placeholder/duplicate effects are lost.");
        Check(empty.Rows[0].Key!=empty.Rows[1].Key&&empty.Percent(empty.Rows[0])==0,"Duplicate effects share an interaction identity or percentage is undefined.");
        Check(GpuTimingPresentation.Build(GpuProfileSnapshot.Off,true,true).Rows.Length==0,"Unsupported sample fabricated rows.");
        Console.WriteLine("GPU TIMELINE MODEL PASS: original mode gating, effect/pass order and descriptions, nonzero weights, query-tail separation, percentages, stable colors/identities, single-effect and zero/unavailable states.");
    }
}
