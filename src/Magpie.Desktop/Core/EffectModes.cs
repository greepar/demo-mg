using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Magpie.Desktop.Core;

[System.Runtime.CompilerServices.InlineArray(16)]
internal struct NativeEffectParameters { private float _element0; }
[StructLayout(LayoutKind.Sequential)]
internal struct NativeEffectStep
{
    public uint Effect, Width, Height;
    public float Parameter1, Parameter2;
    public NativeEffectParameters Extra;
    public void SetCrt(float[] values) { for(int i=0;i<values.Length;i++)Extra[i]=values[i]; }
}
internal sealed record OriginalEffectScale(int ScalingType,float X,float Y);
internal sealed class EffectStageSettings
{
    private JsonElement? _deferredEffect;
    private bool _deferredValidated;
    public JsonElement? DeferredEffect { get=>_deferredEffect;set { _deferredEffect=value;_deferredValidated=false; } }
    public OriginalEffectScale? OriginalScale { get; set; }
    public Dictionary<string,float> AdditionalParameters { get; set; } = new(StringComparer.Ordinal);
    [JsonIgnore] public string DisplayName=>DeferredEffect is {} raw ? raw.GetProperty("name").GetString()! : Name(Effect);
    public float[] Crt { get; set; } = CrtParameters.Defaults();
    public uint Effect { get; set; } = 1;
    public float Parameter1 { get; set; } = .5f;
    public float Parameter2 { get; set; } = .33f;
    public int ScaleType { get; set; } = 0;
    public double ScaleX { get; set; } = 1;
    public double ScaleY { get; set; } = 1;
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public static string Name(uint effect) => effect switch { 0 => "Nearest", 1 => "Lanczos", 2 => "Bilinear", 3 => "Bicubic", 4 => "FSR_EASU", 5 => "FSR_RCAS", 6 => "FSR", 7 => "Anime4K_Upscale_S", 8 => "Anime4K_Upscale_Denoise_L", 9 => "FSRCNNX", 10 => "FSRCNNX_LineArt", 11 => "CuNNy-4x12-NVL", 12 => "CRT-Geom", 13 => "NNEDI3 nns16 win8x4",14 => "SSimDownscaler",15 => "Anime4K_Upscale_L",16 => "Anime4K_Upscale_VL",17 => "Anime4K_Upscale_Denoise_S",18 => "Anime4K_Upscale_Denoise_VL",19 => "RAVU Lite R2",20=>"SMAA Low",21=>"SMAA Medium",22=>"SMAA High",23=>"SMAA Ultra",24=>"Jinc",25=>"Anime4K Denoise Bilateral Mean", _ => GraphCatalog.Find(effect) is { Registered:true } model ? model.Path[(model.Path.LastIndexOf('/')+1)..] : throw new InvalidDataException("Unknown effect.") };
    internal static bool IsSameSize(uint effect)=>GraphCatalog.FixedScale(effect)==1;
    internal static bool IsFixed2x(uint effect)=>GraphCatalog.FixedScale(effect)==2;
    internal static uint IntermediatePixelUnits(uint effect)=>(uint)IntermediateUnits(effect,1,1,1,1);
    internal static ulong IntermediateUnits(uint effect,uint width,uint height,uint outputWidth,uint outputHeight)=>GraphCatalog.ResourceUnits(effect,width,height,outputWidth,outputHeight);
    public EffectStageSettings Clone() { var copy=(EffectStageSettings)MemberwiseClone();copy.Crt=(float[])Crt.Clone();copy.DeferredEffect=DeferredEffect?.Clone();copy.AdditionalParameters=new(AdditionalParameters,StringComparer.Ordinal);return copy; }
    private RuntimeSettings Dimensions() => new() { ScaleType = ScaleType, ScaleX = ScaleX, ScaleY = ScaleY, AbsoluteWidth = Width, AbsoluteHeight = Height };
    public void Validate()
    {
        if(OriginalScale is {} scale && (scale.ScalingType is <0 or >3 || !float.IsFinite(scale.X) || !float.IsFinite(scale.Y) || scale.X<=0 || scale.Y<=0))throw new InvalidDataException("Invalid original scaling metadata.");
        if(AdditionalParameters is null || AdditionalParameters.Count>128 || AdditionalParameters.Any(p=>string.IsNullOrWhiteSpace(p.Key) || p.Key.Length>128 || p.Key.Any(char.IsControl) || !float.IsFinite(p.Value)))throw new InvalidDataException("Invalid additional effect parameters.");
        bool firstDeclared=GraphEffects.Contains(Effect)&&GraphEffects.Definitions(Effect).Length>0;
        bool secondDeclared=GraphEffects.Contains(Effect)&&GraphEffects.Definitions(Effect).Length>1;
        if (!GraphCatalog.IsRegistered(Effect) || !float.IsFinite(Parameter1) || (!firstDeclared && (Effect==14 ? Parameter1 is <1 or >3 : Parameter1 is <0 or >1)) || !float.IsFinite(Parameter2) || (!secondDeclared && Parameter2 is <0 or >1))
            throw new InvalidDataException("Invalid effect parameters.");
        GraphEffects.Validate(this);
        CrtParameters.Validate(Crt);
        Dimensions().Validate();
        if(DeferredEffect is {} deferred && !_deferredValidated) { ScalingModeInterchange.ValidateDeferred(deferred);_deferredValidated=true; }
    }
    public NativeEffectStep Resolve(uint width, uint height, uint viewportWidth, uint viewportHeight,bool windowed=false)
    {
        Validate();
        if(DeferredEffect.HasValue)throw new InvalidOperationException("Effect cannot run in this build: "+DisplayName+". Its original configuration has been preserved.");
        uint fixedScale=GraphCatalog.FixedScale(Effect);
        var size = IsSameSize(Effect) ? (Width:width,Height:height) : fixedScale>0 ? new RuntimeSettings { ScaleType=0,ScaleX=fixedScale,ScaleY=fixedScale }.OutputSize(width,height,viewportWidth,viewportHeight) : Dimensions().OutputSize(width, height, viewportWidth, viewportHeight,windowed);
        var result=new NativeEffectStep { Effect = Effect, Width = size.Width, Height = size.Height, Parameter1 = Parameter1, Parameter2 = Parameter2 };
        if(Effect==12)result.SetCrt(Crt);
        for(int i=2;i<GraphEffects.Definitions(Effect).Length;i++)result.Extra[i-2]=GraphEffects.Get(this,i);
        return result;
    }
}
internal sealed class EffectModeSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New mode";
    public List<EffectStageSettings> Steps { get; set; } = [new() { ScaleType = 1 }];
    public void Validate()
    {
        if (!Guid.TryParseExact(Id, "N", out _) || string.IsNullOrWhiteSpace(Name) || Name.Length > 128 || Name.Any(char.IsControl) || Steps is null || Steps.Count > 16)
            throw new InvalidDataException("Invalid scaling mode.");
        foreach (var step in Steps) (step ?? throw new InvalidDataException("Null effect stage.")).Validate();
    }
    [JsonIgnore] public bool IsAvailable=>Steps.Count>0 && Steps.All(s=>!s.DeferredEffect.HasValue);
    public void EnsureAvailable()
    {
        Validate();if(Steps.Count==0)throw new InvalidOperationException("Scaling mode has no effects. Add an effect before starting.");
        if(!IsAvailable)throw new InvalidOperationException("Scaling mode contains unavailable effects: "+string.Join(", ",Steps.Where(s=>s.DeferredEffect.HasValue).Select(s=>s.DisplayName)));
    }
    public EffectModeSettings Duplicate(string name) => new() { Name = name, Steps = Steps.Select(s => s.Clone()).ToList() };
    public NativeEffectStep[] Resolve(uint width, uint height, uint viewportWidth, uint viewportHeight,bool windowed=false)
    {
        EnsureAvailable();
        var resolved = new NativeEffectStep[Steps.Count];
        ulong total = 0;
        for (int i = 0; i < Steps.Count; i++)
        {
            var step = Steps[i].Resolve(width, height, viewportWidth, viewportHeight,windowed);
            total += (ulong)width * height + (ulong)step.Width * step.Height;
            total+=EffectStageSettings.IntermediateUnits(step.Effect,width,height,step.Width,step.Height);
            if (total > 67108864) throw new InvalidOperationException("Effect chain exceeds the total frame resource limit.");
            resolved[i] = step; width = step.Width; height = step.Height;
        }
        return resolved;
    }
}
