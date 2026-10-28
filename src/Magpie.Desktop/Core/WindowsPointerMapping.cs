using System.Runtime.InteropServices;
namespace Magpie.Desktop.Core;

[StructLayout(LayoutKind.Sequential)]
internal struct NativePointerRect
{
    internal int Left,Top,Right,Bottom;
    internal readonly int Width=>checked(Right-Left);
    internal readonly int Height=>checked(Bottom-Top);
    internal readonly bool Contains(int x,int y)=>x>=Left&&x<Right&&y>=Top&&y<Bottom;
    internal NativePointerRect(int left,int top,int right,int bottom) {Left=left;Top=top;Right=right;Bottom=bottom;}
}
internal readonly record struct WindowsPointerMapping(NativePointerRect Source,NativePointerRect Destination,NativePointerRect Renderer)
{
    // Original CursorManager maps pixel centres, including both end pixels.
    // Outside the source, preserve unscaled distance and optionally skip bars.
    internal static int Axis(int point,int left,int right,int toLeft,int toRight,int outsideLeft,int outsideRight,MidpointRounding rounding=MidpointRounding.AwayFromZero)
    {
        if(right<=left||toRight<=toLeft)throw new ArgumentException("Empty pointer rectangle.");
        if(point<left)return checked(outsideLeft+point-left);
        if(point>=right)return checked(outsideRight+point-right);
        if(right-left==1)return toLeft;
        double delta=(double)(point-left)/(right-left-1)*(toRight-toLeft-1);
        return checked(toLeft+(int)Math.Round(delta,rounding));
    }
    internal (int X,int Y) ToSource(int x,int y)=>(
        Axis(x,Destination.Left,Destination.Right,Source.Left,Source.Right,Source.Left,Source.Right),
        Axis(y,Destination.Top,Destination.Bottom,Source.Top,Source.Bottom,Source.Top,Source.Bottom));
    internal (int X,int Y) ToOutput(int x,int y,bool skipBars=false)=>(
        Axis(x,Source.Left,Source.Right,Destination.Left,Destination.Right,skipBars?Renderer.Left:Destination.Left,skipBars?Renderer.Right:Destination.Right),
        Axis(y,Source.Top,Source.Bottom,Destination.Top,Destination.Bottom,skipBars?Renderer.Top:Destination.Top,skipBars?Renderer.Bottom:Destination.Bottom));
}
