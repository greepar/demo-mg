namespace Magpie.Desktop.Core;

// Original Renderer::_ShouldAppendBicubic/_AppendBicubic and EffectDrawer's windowed Fit rule.
internal static class PresentationPlan
{
    internal static bool UnitScale(double x,double y)=>MathF.Abs((float)x-1)<1.1920928955078125e-5f && MathF.Abs((float)y-1)<1.1920928955078125e-5f;
    internal static (uint Width,uint Height) Viewport(uint sourceWidth,uint sourceHeight,uint clientWidth,uint clientHeight,bool windowed)
    {
        if(sourceWidth==0 || sourceHeight==0 || clientWidth is 0 or >16384 || clientHeight is 0 or >16384 || (ulong)clientWidth*clientHeight>16777216)throw new InvalidOperationException("Invalid presentation viewport.");
        if(!windowed)return(clientWidth,clientHeight);
        float fit=MathF.Min((float)clientWidth/sourceWidth,(float)clientHeight/sourceHeight);
        return ((uint)Math.Clamp(MathF.Round(sourceWidth*fit,MidpointRounding.AwayFromZero),1,clientWidth),(uint)Math.Clamp(MathF.Round(sourceHeight*fit,MidpointRounding.AwayFromZero),1,clientHeight));
    }
    internal static NativeEffectStep[] ForWindow(RuntimeSettings settings,uint width,uint height,uint clientWidth,uint clientHeight,bool windowed)
    {
        var viewport=Viewport(width,height,clientWidth,clientHeight,windowed);
        return Build(settings,width,height,viewport.Width,viewport.Height,windowed);
    }
    internal static NativeEffectStep[] Build(RuntimeSettings settings,uint width,uint height,uint viewportWidth,uint viewportHeight,bool windowed)
    {
        if(viewportWidth is 0 or >16384 || viewportHeight is 0 or >16384 || (ulong)viewportWidth*viewportHeight>16777216)throw new InvalidOperationException("Presentation viewport exceeds the 16-megapixel limit.");
        var steps=settings.ResolveChain(width,height,viewportWidth,viewportHeight,windowed);
        var last=steps[^1];
        bool append=windowed ? last.Width!=viewportWidth || last.Height!=viewportHeight : last.Width>viewportWidth || last.Height>viewportHeight;
        if(append)
        {
            var size=new RuntimeSettings { ScaleType=windowed?3:1 }.OutputSize(last.Width,last.Height,viewportWidth,viewportHeight);
            steps=[..steps,new NativeEffectStep { Effect=3,Width=size.Width,Height=size.Height,Parameter1=0,Parameter2=.5f }];
        }
        ulong total=0;
        foreach(var step in steps)
        {
            total+=(ulong)width*height+(ulong)step.Width*step.Height;
            total+=EffectStageSettings.IntermediateUnits(step.Effect,width,height,step.Width,step.Height);
            if(total>67108864)throw new InvalidOperationException("Effect chain exceeds the total frame resource limit including presentation.");
            width=step.Width;height=step.Height;
        }
        return steps;
    }
    internal static (long X,long Y) Destination(uint viewportWidth,uint viewportHeight,uint width,uint height,int alignment)
    {
        if(alignment is <0 or >8)throw new InvalidDataException("Invalid output alignment.");
        long horizontal=(long)viewportWidth-width,vertical=(long)viewportHeight-height;
        return((alignment%3) switch { 0=>0,1=>horizontal/2,_=>horizontal },(alignment/3) switch { 0=>0,1=>vertical/2,_=>vertical });
    }
    internal static (long X,long Y) Center(uint viewportWidth,uint viewportHeight,uint width,uint height)=>(((long)viewportWidth-width)/2,((long)viewportHeight-height)/2);
}
