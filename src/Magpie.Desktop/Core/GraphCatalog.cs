namespace Magpie.Desktop.Core;

internal sealed record GraphTexture(uint WidthAxis,uint WidthFactor,uint HeightAxis,uint HeightFactor,uint Bytes);
internal sealed record GraphDescriptor(uint Id,string Path,bool Registered,bool Graph,bool SupportsFP16,uint FixedScale,uint Passes,GraphParameter[] Parameters,GraphTexture[] Textures);
internal static partial class GraphCatalog
{
    private static class Index { internal static readonly IReadOnlyDictionary<uint,GraphDescriptor> ById=All.ToDictionary(e=>e.Id); }
    internal static GraphDescriptor? Find(uint id)=>Index.ById.GetValueOrDefault(id);
    internal static uint FixedScale(uint id)=>Find(id) is { Registered:true } item?item.FixedScale:0;
    internal static bool IsRegistered(uint id)=>Find(id)?.Registered==true;
    private static ulong Axis(uint axis,uint width,uint height,uint outputWidth,uint outputHeight)=>axis switch { 0=>width,1=>height,2=>outputWidth,3=>outputHeight,4=>1,_=>throw new InvalidDataException("Unknown texture dimension axis.") };
    internal static ulong ResourceUnits(uint id,uint width,uint height,uint outputWidth,uint outputHeight)
    {
        if(Find(id) is not { Registered:true,Graph:true } effect)return 0;
        if(effect.Textures.Length==0)return 1; // Native descriptor padding image.
        ulong bytes=0;
        foreach(var texture in effect.Textures)
        {
            ulong w=Axis(texture.WidthAxis,width,height,outputWidth,outputHeight)*texture.WidthFactor;
            ulong h=Axis(texture.HeightAxis,width,height,outputWidth,outputHeight)*texture.HeightFactor;
            if(w==0 || h==0 || w>uint.MaxValue || h>uint.MaxValue || w>16777216/h)throw new InvalidOperationException("Effect intermediate exceeds its texture resource limit.");
            bytes=checked(bytes+w*h*texture.Bytes);
        }
        return (bytes+3)/4;
    }
}
