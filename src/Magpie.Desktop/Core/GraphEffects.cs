namespace Magpie.Desktop.Core;

internal sealed record GraphParameter(string Name,string Label,float Default,float Minimum,float Maximum,decimal Step=.01m,bool Integer=false);
internal static class GraphEffects
{
    internal static bool Contains(uint effect)=>effect>=24 && GraphCatalog.IsRegistered(effect);
    internal static bool HasParameters(uint effect)=>Contains(effect)&&Definitions(effect).Length>0;
    internal static GraphParameter[] Definitions(uint effect)=>Contains(effect)?GraphCatalog.Find(effect)!.Parameters:[];
    private static readonly IReadOnlyDictionary<string,GraphParameter> Known=GraphCatalog.All.Where(e=>Contains(e.Id)).SelectMany(e=>e.Parameters.Select(p=>new KeyValuePair<string,GraphParameter>(Key(e.Id,p),p))).ToDictionary();
    internal static float Get(EffectStageSettings stage,int slot)
    {
        var definition=Definitions(stage.Effect)[slot];
        return slot==0?stage.Parameter1:slot==1?stage.Parameter2:stage.AdditionalParameters.GetValueOrDefault(definition.Name,definition.Default);
    }
    internal static void Set(EffectStageSettings stage,int slot,float value)
    {
        if(slot==0)stage.Parameter1=value;
        else if(slot==1)stage.Parameter2=value;
        else stage.AdditionalParameters[Definitions(stage.Effect)[slot].Name]=value;
    }
    internal static void Reset(EffectStageSettings stage)
    {
        stage.Parameter1=.5f;stage.Parameter2=.33f;
        var definitions=Definitions(stage.Effect);
        for(int i=0;i<definitions.Length;i++)Set(stage,i,definitions[i].Default);
    }
    internal static void Validate(EffectStageSettings stage)
    {
        var definitions=Definitions(stage.Effect);
        for(int i=0;i<definitions.Length;i++)Check(definitions[i],Get(stage,i));
    }
    private static void Check(GraphParameter definition,float value)
    {
        if(!float.IsFinite(value)||value<definition.Minimum||value>definition.Maximum||(definition.Integer&&MathF.Floor(value)!=value))throw new InvalidDataException("Invalid graph parameter: "+definition.Name);
    }
    internal static string Key(uint effect,GraphParameter parameter)=>effect+"."+parameter.Name;
    internal static float Get(RuntimeSettings settings,uint effect,int slot)
    {
        var definition=Definitions(effect)[slot];return settings.GraphParameters.GetValueOrDefault(Key(effect,definition),definition.Default);
    }
    internal static void Validate(RuntimeSettings settings)
    {
        if(settings.GraphParameters is null||settings.GraphParameters.Count>Known.Count)throw new InvalidDataException("Invalid graph parameter settings.");
        foreach(var (key,value) in settings.GraphParameters)
        {
            if(!Known.TryGetValue(key,out var definition))throw new InvalidDataException("Unknown graph parameter setting: "+key);
            Check(definition,value);
        }
    }
    internal static EffectStageSettings Stage(RuntimeSettings settings)
    {
        var stage=new EffectStageSettings { Effect=settings.Effect,ScaleType=settings.ScaleType,ScaleX=settings.ScaleX,ScaleY=settings.ScaleY,Width=settings.AbsoluteWidth,Height=settings.AbsoluteHeight };
        Reset(stage);for(int i=0;i<Definitions(stage.Effect).Length;i++)Set(stage,i,Get(settings,stage.Effect,i));return stage;
    }
}
