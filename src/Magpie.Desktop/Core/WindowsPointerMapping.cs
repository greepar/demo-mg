using System.Runtime.InteropServices;
namespace Magpie.Desktop.Core;

internal static class WindowsCursorVisibility
{
    // Some desktops report suppression as the result of our global hide. Only
    // compensate an observed SHOWING -> SUPPRESSED transition caused by that
    // call; pre-existing touch/pen suppression and application hiding stay hidden.
    internal static bool Draw(uint flags,bool ownedHide,uint before,uint after)=>
        flags==1||(ownedHide&&flags==2&&before==1&&after==2);
}
internal enum WindowsPointerRound { Nearest,Floor,Ceiling }

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
    internal static int Axis(int point,int left,int right,int toLeft,int toRight,int outsideLeft,int outsideRight,WindowsPointerRound rounding=WindowsPointerRound.Nearest)
    {
        if(right<=left||toRight<=toLeft)throw new ArgumentException("Empty pointer rectangle.");
        if(point<left)return checked(outsideLeft+point-left);
        if(point>=right)return checked(outsideRight+point-right);
        if(right-left==1)return toLeft;
        double delta=(double)(point-left)/(right-left-1)*(toRight-toLeft-1);
        return checked(toLeft+(int)(rounding switch {WindowsPointerRound.Floor=>Math.Floor(delta),WindowsPointerRound.Ceiling=>Math.Ceiling(delta),_=>Math.Round(delta,MidpointRounding.AwayFromZero)}));
    }
    internal (int X,int Y) ToSource(int x,int y,WindowsPointerRound rounding=WindowsPointerRound.Nearest)=>(
        Axis(x,Destination.Left,Destination.Right,Source.Left,Source.Right,Source.Left,Source.Right,rounding),
        Axis(y,Destination.Top,Destination.Bottom,Source.Top,Source.Bottom,Source.Top,Source.Bottom,rounding));
    internal (int X,int Y) ToOutput(int x,int y,bool skipBars=false)=>(
        Axis(x,Source.Left,Source.Right,Destination.Left,Destination.Right,skipBars?Renderer.Left:Destination.Left,skipBars?Renderer.Right:Destination.Right),
        Axis(y,Source.Top,Source.Bottom,Destination.Top,Destination.Bottom,skipBars?Renderer.Top:Destination.Top,skipBars?Renderer.Bottom:Destination.Bottom));
}
