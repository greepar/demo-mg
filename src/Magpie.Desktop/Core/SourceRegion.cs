namespace Magpie.Desktop.Core;

// Coordinates are physical pixels in the delivered RGBA frame, independent of desktop position/DPI.
internal readonly record struct SourceRegion(uint X,uint Y,uint Width,uint Height)
{
    internal static SourceRegion Resolve(RuntimeSettings settings,uint width,uint height)
    {
        settings.ValidateCropping();
        if(width==0 || height==0 || width>16384 || height>16384 || (ulong)width*height>16777216)throw new InvalidOperationException("Invalid capture dimensions.");
        if(!settings.IsCroppingEnabled)return new(0,0,width,height);
        // Match the original float coordinates and lround of rectangle edges, including right/bottom ties.
        long left=Round(settings.CroppingLeft),top=Round(settings.CroppingTop);
        long right=Round((float)width-settings.CroppingRight),bottom=Round((float)height-settings.CroppingBottom);
        if(right-left<64 || bottom-top<64 || right>width || bottom>height)
            throw new InvalidOperationException(Localization.Get("Message_InvalidCropping"));
        return new((uint)left,(uint)top,(uint)(right-left),(uint)(bottom-top));
    }
    private static long Round(float value)=>(long)MathF.Round(value,MidpointRounding.AwayFromZero);
    internal void Validate(uint sourceWidth,uint sourceHeight)
    {
        if(Width==0 || Height==0 || (ulong)X+Width>sourceWidth || (ulong)Y+Height>sourceHeight)
            throw new ArgumentException("Crop region is outside the capture frame.");
    }
    internal unsafe void Copy(nint source,uint sourceWidth,uint sourceHeight,Span<byte> destination)
    {
        Validate(sourceWidth,sourceHeight);
        int rowBytes=checked((int)Width*4);
        if(source==0 || destination.Length!=checked(rowBytes*(int)Height))throw new ArgumentException("Invalid crop buffer.");
        for(uint y=0;y<Height;y++)
        {
            var row=new ReadOnlySpan<byte>((byte*)source+checked(((long)(Y+y)*sourceWidth+X)*4),rowBytes);
            row.CopyTo(destination.Slice(checked((int)y*rowBytes),rowBytes));
        }
    }
}
