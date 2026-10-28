using Avalonia;
namespace Magpie.Desktop.Core;

internal enum ToolbarState { Off,AlwaysShow,AutoHide }
internal static class ToolbarVisibility
{
    internal static ToolbarState Initial(RuntimeSettings settings,bool windowed)=>windowed?settings.WindowedInitialToolbarState:settings.FullscreenInitialToolbarState;
    internal static ToolbarState Next(ToolbarState state)=>(ToolbarState)(((int)state+1)%3);
    internal static double Alpha(ToolbarState state,Rect bounds,Point? pointer,bool itemActive)
    {
        if(state==ToolbarState.Off)return 0;
        if(state==ToolbarState.AlwaysShow||itemActive)return 1;
        if(pointer is not {} point)return 0;
        double x=Math.Max(Math.Max(bounds.Left-point.X,point.X-bounds.Right),0);
        double y=Math.Max(Math.Max(bounds.Top-point.Y,point.Y-bounds.Bottom),0);
        // OverlayDrawer::_CalcToolbarAlpha: 10 DIP plateau, 40 DIP fade.
        return (40-Math.Clamp(Math.Sqrt(x*x+y*y)-10,0,40))/40;
    }
}
