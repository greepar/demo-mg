namespace Magpie.Desktop.Core;

internal readonly record struct GpuTimingKey(int Stage,int Pass=-1);
internal sealed record GpuTimingItem(GpuTimingKey Key,string Name,double Milliseconds,int Color,bool Indented=false);
internal sealed record GpuTimingPresentation(GpuTimingItem[] Rows,GpuTimingItem[] Segments,double Total,bool ShowTimeline,bool ShowPasses)
{
    internal static GpuTimingPresentation Build(GpuProfileSnapshot sample,bool passes,bool developer)
    {
        if(!sample.Valid)return new([],[],0,false,false);
        passes&=developer&&sample.Stages.Any(s=>s.Passes.Length>1);
        var rows=new List<GpuTimingItem>();var segments=new List<GpuTimingItem>();int previousTimelineColor=-1;
        for(int stage=0;stage<sample.Stages.Length;stage++)
        {
            var timing=sample.Stages[stage];string name=EffectStageSettings.Name(timing.Effect)+(developer&&timing.UsesFP16?" (FP16)":"");
            int effectColor=stage%7;
            var effect=new GpuTimingItem(new(stage),name,timing.Milliseconds,effectColor);
            rows.Add(effect);
            bool expanded=passes&&timing.Passes.Length>1;
            if(!expanded)segments.Add(effect);
            if(timing.Passes.Length==1)previousTimelineColor=effectColor;
            for(int pass=0;pass<timing.Passes.Length&&timing.Passes.Length>1;pass++)
            {
                int passColor=(previousTimelineColor+1)%7;
                if(pass+1==timing.Passes.Length&&stage+1<sample.Stages.Length&&sample.Stages[stage+1].Passes.Length==1&&passColor==(stage+1)%7)
                    passColor=(passColor+1)%7;
                previousTimelineColor=passColor;
                if(!expanded)continue;
                string description=PassExportCatalog.All.TryGetValue(timing.Effect,out var descriptions)&&pass<descriptions.Length?descriptions[pass].Name:"Pass "+(pass+1);
                var item=new GpuTimingItem(new(stage,pass),description,timing.Passes[pass],passColor,true);
                rows.Add(item);segments.Add(item);
            }
        }
        return new(rows.ToArray(),segments.Where(s=>s.Milliseconds>1e-3).ToArray(),sample.Stages.Sum(s=>s.Milliseconds),sample.Stages.Length>1||passes,passes);
    }

    internal double Percent(GpuTimingItem item)=>Total>0?item.Milliseconds/Total*100:0;
    internal string Tooltip(GpuTimingItem item)
    {
        string name=item.Indented&&Rows.Count(r=>!r.Indented)>1?Rows.First(r=>r.Key==new GpuTimingKey(item.Key.Stage)).Name+" / "+item.Name:item.Name;
        return $"{name}\n{item.Milliseconds:F3} ms\n{Math.Round(Percent(item),MidpointRounding.AwayFromZero):F0}%";
    }
}
