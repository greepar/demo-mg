namespace Magpie.Desktop.Core;

internal readonly record struct InitialWindowSize(uint Width,uint Height,uint MinimumWidth,uint MinimumHeight);
internal static class WindowSizing
{
    // Original InitialWindowedScaleFactor enum order. Values are renderer pixels, before DIP conversion.
    internal static float Factor(int selection,float custom)=>selection switch { 0=>0,1=>1.25f,2=>1.5f,3=>1.75f,4=>2,5=>3,6=>custom,_=>throw new InvalidDataException("Invalid initial window scale.") };
    internal static InitialWindowSize Initial(RuntimeSettings settings,uint sourceWidth,uint sourceHeight,uint workWidth,uint workHeight,double renderScale,uint decorationWidth=0,uint decorationHeight=0,uint frameWidth=0,uint frameHeight=0)
    {
        if(sourceWidth is 0 or >16384 || sourceHeight is 0 or >16384 || (ulong)sourceWidth*sourceHeight>16777216 || workWidth<=decorationWidth || workHeight<=decorationHeight || !double.IsFinite(renderScale) || renderScale<=0 || renderScale>16)throw new InvalidDataException("Invalid window sizing geometry.");
        float factor=Factor(settings.InitialWindowedScaleFactor,settings.CustomInitialWindowedScaleFactor);
        if(!float.IsFinite(factor) || factor!=0 && (factor<1 || factor>10))throw new InvalidDataException("Invalid initial window factor.");
        double aspect=(float)sourceHeight/sourceWidth;
        double width,height;
        if(factor==0)
        {
            if((float)workHeight/workWidth>aspect) { width=Math.Max((long)workWidth*3/4,(long)sourceWidth*4/3);height=Round((float)width*(float)aspect); }
            else { height=Math.Max((long)workHeight*3/4,(long)sourceHeight*4/3);width=Round((float)height/(float)aspect); }
        }
        else { width=Round(sourceWidth*factor);height=Round((float)width*(float)aspect); }
        // Capture APIs may not expose source frame bounds. In that case use captured content as a documented fallback.
        double padding=Round(100*(float)renderScale);
        double frameMinimumWidth=(frameWidth==0?sourceWidth:frameWidth)+padding,frameMinimumHeight=(frameHeight==0?sourceHeight:frameHeight)+padding;
        double minWidth,minHeight;
        if(sourceWidth>sourceHeight) { minHeight=frameMinimumHeight;minWidth=Round((float)minHeight/(float)aspect); }
        else { minWidth=frameMinimumWidth;minHeight=Round((float)minWidth*(float)aspect); }
        if(minWidth<frameMinimumWidth) { minWidth=frameMinimumWidth;minHeight=Round((float)minWidth*(float)aspect); }
        if(minHeight<frameMinimumHeight) { minHeight=frameMinimumHeight;minWidth=Round((float)minHeight/(float)aspect); }
        if(width<minWidth || height<minHeight) { width=minWidth;height=minHeight; }
        double maximumWidth=Math.Min(workWidth-decorationWidth,16384u),maximumHeight=Math.Min(workHeight-decorationHeight,16384u);
        // The original source-plus-margin minimum is useful only when it fits.
        // Desktop/window captures can already fill the display (or come from a
        // larger monitor). Keep those sessions windowed with room for decorations
        // and let the presentation pipeline downscale instead of refusing to start.
        bool fitLargeSource=minWidth>maximumWidth || minHeight>maximumHeight || minWidth*minHeight>16777216;
        if(fitLargeSource)
        {
            maximumWidth=Math.Max(1,Math.Floor(maximumWidth*.8));
            maximumHeight=Math.Max(1,Math.Floor(maximumHeight*.8));
        }
        if(width>maximumWidth || height>maximumHeight)
        {
            double fittedHeight=Round((float)maximumWidth*(float)aspect);
            if(fittedHeight<maximumHeight) { width=maximumWidth;height=Math.Max(1,fittedHeight); }
            else { height=maximumHeight;width=Math.Clamp(Round((float)height/(float)aspect),1,maximumWidth); }
        }
        if(width*height>16777216)
        {
            width=Math.Min(width,Math.Floor(Math.Sqrt(16777216/aspect)));height=Math.Max(1,Round((float)width*(float)aspect));
            while(width>0 && (height>maximumHeight || width*height>16777216)) { --width;height=Math.Max(1,Round((float)width*(float)aspect)); }
        }
        if(fitLargeSource)
        {
            minWidth=Math.Min(width,Math.Max(1,Round(320*(float)renderScale)));
            minHeight=Math.Min(height,Math.Max(1,Round(200*(float)renderScale)));
        }
        if(width<minWidth || height<minHeight || width>maximumWidth || height>maximumHeight || width*height>16777216)throw new InvalidOperationException("The output window does not fit the display or rendering limits.");
        return new((uint)width,(uint)height,(uint)minWidth,(uint)minHeight);
    }
    private static double Round(double number)=>Math.Round(number,MidpointRounding.AwayFromZero);
}
