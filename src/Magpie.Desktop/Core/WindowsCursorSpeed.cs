using System.Runtime.InteropServices;
namespace Magpie.Desktop.Core;

internal interface IWindowsCursorSpeedApi
{
    int ReadSpeed();
    bool? ReadAcceleration();
    void WriteSpeed(int speed);
}
internal sealed class NativeWindowsCursorSpeed : IWindowsCursorSpeedApi
{
    [DllImport("user32",EntryPoint="SystemParametersInfoW",SetLastError=true)]
    private static extern bool Parameters(uint action,uint parameter,nint value,uint flags);
    public unsafe int ReadSpeed()
    {
        int speed=0;
        if(!Parameters(0x70,0,(nint)(&speed),0)||speed is <1 or >20)throw new InvalidOperationException("Cannot read Windows pointer speed: "+Marshal.GetLastPInvokeError());
        return speed;
    }
    public unsafe bool? ReadAcceleration()
    {
        int* values=stackalloc int[3];
        return Parameters(3,0,(nint)values,0)?values[2]!=0:null;
    }
    public void WriteSpeed(int speed)
    {
        if(speed is <1 or >20)throw new ArgumentOutOfRangeException(nameof(speed));
        // No SPIF_UPDATEINIFILE or SPIF_SENDCHANGE: temporary session setting.
        if(!Parameters(0x71,0,speed,0))throw new InvalidOperationException("Cannot set Windows pointer speed: "+Marshal.GetLastPInvokeError());
    }
}
internal sealed class WindowsCursorSpeed(IWindowsCursorSpeedApi api)
{
    private readonly object _mutex=new();
    private int _original,_applied;
    private bool _configured;
    private double _scale;
    private long _generation;
    internal bool OwnsChange {get {lock(_mutex)return _original!=0;}}
    internal long PendingRestore {get {lock(_mutex)return _original!=0?_generation:0;}}
    internal static int Compensate(int original,double scale,bool acceleration)
    {
        if(original is <1 or >20||!double.IsFinite(scale)||scale<=0)throw new ArgumentOutOfRangeException(nameof(scale));
        if(acceleration)return (int)Math.Clamp(Math.Round(original/scale,MidpointRounding.AwayFromZero),1,20);
        ReadOnlySpan<double> sensitivity=[.03125,.0625,.125,.25,.375,.5,.625,.75,.875,1,1.25,1.5,1.75,2,2.25,2.5,2.75,3,3.25,3.5];
        double desired=sensitivity[original-1]/scale;
        const double epsilon=2.2204460492503131e-16*100; // original FLOAT_EPSILON<double>
        int upper=0;while(upper<19&&sensitivity[upper]<desired-epsilon)upper++;
        if(upper>0&&Math.Abs(sensitivity[upper]-desired)>Math.Abs(sensitivity[upper-1]-desired))upper--;
        return upper+1;
    }
    internal void Update(bool enabled,double scale,bool moving)
    {
        lock(_mutex)
        {
            if(!enabled||moving) {RestoreCore();return;}
            if(_configured&&_scale==scale)return;
            RestoreCore();
            int original=api.ReadSpeed();bool acceleration=api.ReadAcceleration()??true;
            int wanted=Compensate(original,scale,acceleration);
            if(wanted!=original)
            {
                // Retain ownership if a setter throws after partially applying.
                checked {_generation++;}
                _original=original;_applied=wanted;api.WriteSpeed(wanted);
            }
            _scale=scale;_configured=true;
        }
    }
    internal void Restore() {lock(_mutex)RestoreCore();}
    internal void Restore(long generation) {lock(_mutex)if(_original!=0&&generation==_generation)RestoreCore();}
    private void RestoreCore()
    {
        if(_original!=0)
        {
            // Preserve a newer external preference; never continuously force
            // our chosen speed on an active user's control-panel change.
            if(api.ReadSpeed()==_applied)api.WriteSpeed(_original);
            _original=0;_applied=0;
        }
        _configured=false;
    }
}
internal static class WindowsCursorSpeedCleanup
{
    internal static void Schedule(WindowsCursorSpeed speed)
    {
        long generation=speed.PendingRestore;if(generation==0)return;
        _=Task.Run(async()=>
        {
            while(speed.PendingRestore==generation)
            {
                try {speed.Restore(generation);}
                catch(Exception error) {Console.Error.WriteLine("Retrying pointer speed restoration: "+error.Message);}
                if(speed.PendingRestore==generation)await Task.Delay(500);
            }
        });
    }
}
