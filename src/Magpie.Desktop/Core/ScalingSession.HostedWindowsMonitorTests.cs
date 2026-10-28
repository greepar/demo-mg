using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
namespace Magpie.Desktop.Core;
internal sealed partial class ScalingSession
{
    [StructLayout(LayoutKind.Sequential)]private struct HostedMonitorInfo {public uint Size;public NativePointerRect Monitor,Work;public uint Flags;}
    [DllImport("user32",EntryPoint="MonitorFromWindow")]private static extern nint HostedWindowMonitor(nint window,uint flags);
    [DllImport("user32",EntryPoint="GetMonitorInfoW")]private static extern bool HostedMonitorBounds(nint monitor,ref HostedMonitorInfo info);
    private async Task VerifyHostedMonitorAndToolbar(Process source,nint window,Task run,Func<string?> failure,HostedLifecycleCase evidence,
        FixturePoint clientOrigin,FixtureRect client,FixturePoint current)
    {
        HostedWindowsLifecycle.Require();RequireOwnedSource(source,window);
        var output=_output??throw new InvalidOperationException("Native output closed before monitor test.");
        evidence.NativeMonitorCount=HostedDesktopMetric(80);
        var nativeSource=new NativePointerRect(clientOrigin.X,clientOrigin.Y,clientOrigin.X+client.Right,clientOrigin.Y+client.Bottom);
        if(evidence.Fullscreen)
        {
            var monitor=new HostedMonitorInfo {Size=(uint)Marshal.SizeOf<HostedMonitorInfo>()};
            var handle=HostedOutputHandle();var zero=new FixturePoint();
            if(!HostedMonitorBounds(HostedWindowMonitor(handle,2),ref monitor)||!FixtureClient(handle,out var area)||!FixtureToScreen(handle,ref zero))
                throw new InvalidOperationException("Cannot query actual fullscreen client/monitor geometry.");
            var actual=new NativePointerRect(zero.X,zero.Y,zero.X+area.Right,zero.Y+area.Bottom);
            if(!actual.Equals(monitor.Monitor)||(OutputWindowStyle(handle,-16)&0xcf0000)!=0)
                throw new InvalidOperationException($"Logical fullscreen does not cover its real monitor without a caption: client={actual.Left},{actual.Top},{actual.Right},{actual.Bottom}; monitor={monitor.Monitor.Left},{monitor.Monitor.Top},{monitor.Monitor.Right},{monitor.Monitor.Bottom}; style={OutputWindowStyle(handle,-16):x}.");
            evidence.NativeFullscreenBoundsPassed=true;
            if(output.Screens.All.Count==1)
            {
                await WaitHosted(()=>ReadWindowsClip(out var clip)&&clip.Equals(nativeSource),run,failure,"real single-monitor source boundaries");
                if(!FixtureMoveCursor(nativeSource.Right+30,nativeSource.Bottom+30)||!FixtureCursor(out var clamped)||
                    clamped.X!=nativeSource.Right-1||clamped.Y!=nativeSource.Bottom-1)
                    throw new InvalidOperationException("Actual native clip did not prevent leaving the fullscreen source bottom/right.");
                if(!FixtureMoveCursor(current.X,current.Y))throw new InvalidOperationException("Cannot restore owned source pointer after boundary probe.");
                evidence.NativeBoundaryClampChecks++;
            }
        }
        else
        {
            evidence.NativeFullscreenBoundsPassed=true;
            var monitor=new HostedMonitorInfo {Size=(uint)Marshal.SizeOf<HostedMonitorInfo>()};
            if(!HostedMonitorBounds(HostedWindowMonitor(HostedOutputHandle(),2),ref monitor))throw new InvalidOperationException("Cannot query windowed monitor.");
            var renderer=_windowsPointerMapping.Renderer;
            if(renderer.Left>monitor.Monitor.Left&&renderer.Top>monitor.Monitor.Top&&renderer.Right<monitor.Monitor.Right&&renderer.Bottom<monitor.Monitor.Bottom)
            {
                var desktop=new NativePointerRect(HostedDesktopMetric(76),HostedDesktopMetric(77),
                    HostedDesktopMetric(76)+HostedDesktopMetric(78),HostedDesktopMetric(77)+HostedDesktopMetric(79));
                await WaitHosted(()=>_windowsAppliedClip is null&&ReadWindowsClip(out var clip)&&clip.Equals(desktop),run,failure,"windowed source releases its native drag clip");
            }
        }
        evidence.NativeMonitorClipPassed=true;
        var pin=_toolbarPin??throw new InvalidOperationException("Actual toolbar pin is missing.");
        var pinPoint=pin.PointToScreen(new Point(pin.Bounds.Width/2,pin.Bounds.Height/2));
        var dest=_image!.PointToScreen(default);
        if(evidence.Fullscreen&&dest.Y-output.PointToScreen(default).Y<=_toolbar!.Bounds.Height*output.RenderScaling)
            throw new InvalidOperationException("Wide-source test did not produce the intended large letterbox.");
        if(pinPoint.X<dest.X||pinPoint.Y<dest.Y||pinPoint.X>=dest.X+_bitmap!.PixelSize.Width||pinPoint.Y>=dest.Y+_bitmap.PixelSize.Height)
            throw new InvalidOperationException("Image-relative toolbar entry is unreachable from the real captured source.");
        var entry=new FixturePoint {
            X=clientOrigin.X+(int)Math.Round((double)(pinPoint.X-dest.X)/(_bitmap.PixelSize.Width-1)*(client.Right-1),MidpointRounding.AwayFromZero),
            Y=clientOrigin.Y+(int)Math.Round((double)(pinPoint.Y-dest.Y)/(_bitmap.PixelSize.Height-1)*(client.Bottom-1),MidpointRounding.AwayFromZero)};
        if(!FixtureMoveCursor(entry.X,entry.Y))throw new InvalidOperationException("Cannot approach local toolbar with native source pointer.");
        await WaitHosted(()=>!_windowsPointerCaptured&&!_windowsSystemCursorHidden&&_toolbar!.Opacity>0&&
            (OutputWindowStyle(HostedOutputHandle(),-20)&0x20)==0,run,failure,"virtual pointer reveals toolbar and restores local native input");
        if(_windowsAppliedClip is not null)throw new InvalidOperationException("Toolbar handoff retained the source's monitor clip.");
        if(!FixtureCursor(out var physical)||FixtureRoot(FixtureAtPoint(physical),2)!=HostedOutputHandle())
            throw new InvalidOperationException("Toolbar handoff did not restore the pointer to the owned native output.");
        bool pinned=pin.IsChecked==true;long presses=(long)FixtureProperty(window,"MagpieFixtureMouseDown"),releases=(long)FixtureProperty(window,"MagpieFixtureMouseUp");
        var state=_toolbarState;
        try
        {
            SendHostedClick(source,window,HostedOutputHandle(),physical);
            await WaitHosted(()=>(pin.IsChecked==true)!=pinned,run,failure,"real local toolbar pin click after virtual pointer handoff");
            if((long)FixtureProperty(window,"MagpieFixtureMouseDown")!=presses||(long)FixtureProperty(window,"MagpieFixtureMouseUp")!=releases)
                throw new InvalidOperationException("Local toolbar click leaked into native source input.");
        }
        finally {SetToolbarState(state);}
        evidence.NativeToolbarExitPassed=true;
    }
}
