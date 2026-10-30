namespace Magpie.Desktop.Core;
internal static class CursorPresentation
{
    internal static float Scale(RuntimeSettings settings,NativePointerRect source,NativePointerRect destination)=>settings.CursorScaling switch
    {
        0=>.5f,1=>.75f,2=>1,3=>1.25f,4=>1.5f,5=>2,
        6=>((float)destination.Width/source.Width+(float)destination.Height/source.Height)/2,
        7=>settings.CustomCursorScaleFactor,
        _=>throw new InvalidDataException("Invalid cursor scaling preference.")
    };
    internal static NativePointerRect Bounds(RuntimeSettings settings,NativeCursorShape shape,WindowsPointerMapping mapping,int x,int y)
    {
        float scale=Scale(settings,mapping.Source,mapping.Destination);
        static int Round(float value)=>checked((int)MathF.Round(value,MidpointRounding.AwayFromZero));
        // The original rounds in renderer-local coordinates, including hotspots.
        int left=checked(mapping.Renderer.Left+Round((x-mapping.Renderer.Left)-shape.HotX*scale));
        int top=checked(mapping.Renderer.Top+Round((y-mapping.Renderer.Top)-shape.HotY*scale));
        return new(left,top,checked(left+Round(shape.Width*scale)),checked(top+Round(shape.Height*scale)));
    }
}
