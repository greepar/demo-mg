namespace Magpie.Desktop.Core;

internal static class WindowsMonitorClip
{
    // CursorManager::_ClipCursorForMonitors, in physical desktop pixels. A ray
    // tests whether another monitor exists past each renderer edge; separate
    // exit hit testing handles gaps between monitors, as in the original.
    internal static NativePointerRect? Compute(WindowsPointerMapping m,ReadOnlySpan<NativePointerRect> monitors,int x,int y,bool captured,bool focused)
    {
        if(monitors.IsEmpty)return null;
        var p=captured?m.ToOutput(x,y,true):(x,y);
        bool left=false,top=false,right=false,bottom=false;
        int minLeft=int.MaxValue,minTop=int.MaxValue,maxRight=int.MinValue,maxBottom=int.MinValue;
        foreach(var r in monitors)
        {
            if(r.Right<=r.Left||r.Bottom<=r.Top)continue;
            if(r.Top<=p.Item2&&r.Bottom>p.Item2)
            {
                left|=r.Left<m.Renderer.Left;right|=r.Right>m.Renderer.Right;
                minLeft=Math.Min(minLeft,r.Left);maxRight=Math.Max(maxRight,r.Right);
            }
            if(r.Left<=p.Item1&&r.Right>p.Item1)
            {
                top|=r.Top<m.Renderer.Top;bottom|=r.Bottom>m.Renderer.Bottom;
                minTop=Math.Min(minTop,r.Top);maxBottom=Math.Max(maxBottom,r.Bottom);
            }
        }
        var clip=new NativePointerRect(int.MinValue,int.MinValue,int.MaxValue,int.MaxValue);
        if(!left)
        {
            if(captured&&minLeft<m.Destination.Right)
                clip.Left=(minLeft<m.Destination.Left&&focused)||minLeft==m.Destination.Left?m.Source.Left:
                    m.ToSource(minLeft,p.Item2,WindowsPointerRound.Ceiling).X;
            else if(!captured&&focused&&m.Destination.Left!=m.Renderer.Left)clip.Left=m.Destination.Left;
        }
        if(!top)
        {
            if(captured&&minTop<m.Destination.Bottom)
                clip.Top=(minTop<m.Destination.Top&&focused)||minTop==m.Destination.Top?m.Source.Top:
                    m.ToSource(p.Item1,minTop,WindowsPointerRound.Ceiling).Y;
            else if(!captured&&focused&&m.Destination.Top!=m.Renderer.Top)clip.Top=m.Destination.Top;
        }
        if(!right)
        {
            if(focused)clip.Right=captured?m.Source.Right:m.Destination.Right;
            else if(captured&&m.Destination.Right==m.Renderer.Right)clip.Right=m.Source.Right;
            if(captured&&maxRight>m.Destination.Left)
                clip.Right=(maxRight>m.Destination.Right&&focused)||maxRight==m.Destination.Right?m.Source.Right:
                    m.ToSource(maxRight,p.Item2,WindowsPointerRound.Floor).X;
            else if(!captured&&focused&&m.Destination.Right!=m.Renderer.Right)clip.Right=m.Destination.Right;
        }
        if(!bottom)
        {
            if(captured&&maxBottom>m.Destination.Top)
                clip.Bottom=(maxBottom>m.Destination.Bottom&&focused)||maxBottom==m.Destination.Bottom?m.Source.Bottom:
                    m.ToSource(p.Item1,maxBottom,WindowsPointerRound.Floor).Y;
            else if(!captured&&focused&&m.Destination.Bottom!=m.Renderer.Bottom)clip.Bottom=m.Destination.Bottom;
        }
        return clip.Left==int.MinValue&&clip.Top==int.MinValue&&clip.Right==int.MaxValue&&clip.Bottom==int.MaxValue?null:clip;
    }
}
