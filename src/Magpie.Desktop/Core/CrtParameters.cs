namespace Magpie.Desktop.Core;

internal static class CrtParameters
{
    internal readonly record struct Definition(string Label,float Default,float Minimum,float Maximum,decimal Step,bool Integer=false);
    // Original CRT_Geom.hlsl declaration order is also the configured native ABI order.
    internal static readonly Definition[] Definitions=GraphCatalog.Find(12)!.Parameters
        .Select(p=>new Definition(p.Label,p.Default,p.Minimum,p.Maximum,p.Step,p.Integer)).ToArray();
    internal static float[] Defaults()=>Definitions.Select(p=>p.Default).ToArray();
    internal static void Validate(float[]? values)
    {
        if(values is null || values.Length!=Definitions.Length)throw new InvalidDataException("Invalid CRT parameter count.");
        for(int i=0;i<values.Length;i++)
        {
            var p=Definitions[i];float value=values[i];
            if(!float.IsFinite(value) || value<p.Minimum || value>p.Maximum || p.Integer && MathF.Floor(value)!=value)
                throw new InvalidDataException("Invalid CRT parameter: "+p.Label);
        }
    }
}
