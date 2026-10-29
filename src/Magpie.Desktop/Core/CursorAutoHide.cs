namespace Magpie.Desktop.Core;

// CursorDrawer::_GetCursorState: a hidden source handle is not a shape change.
// Moving/resizing and leaving capture keep the pointer active; button presses
// alone do not reset the original idle timer.
internal sealed class CursorAutoHide
{
    private bool _seen;
    private int _x,_y;
    private nint _shape;
    private TimeSpan _active;
    internal void Reset()=>_seen=false;
    internal bool Visible(TimeSpan now,int x,int y,nint shape,bool captured,bool moving,bool enabled,double seconds)
    {
        if(!enabled) {Reset();return shape!=0;}
        bool active=!_seen||!captured||moving||x!=_x||y!=_y||(shape!=0&&shape!=_shape);
        if(active) {_active=now;_shape=shape;}
        _seen=true;_x=x;_y=y;
        return shape!=0&&(active||now-_active<=TimeSpan.FromSeconds(seconds));
    }
}
