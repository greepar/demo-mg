using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    [DllImport("user32",EntryPoint="SetLayeredWindowAttributes",SetLastError=true)]
    private static extern bool FixtureLayered(nint window,uint color,byte alpha,uint flags);
    [DllImport("user32",EntryPoint="GetDC")]
    private static extern nint FixtureScreenDc(nint window);
    [DllImport("user32",EntryPoint="ReleaseDC")]
    private static extern int FixtureReleaseDc(nint window,nint dc);
    [DllImport("gdi32",EntryPoint="GetPixel")]
    private static extern uint FixtureScreenPixel(nint dc,int x,int y);
    [DllImport("dwmapi",EntryPoint="DwmFlush")]
    private static extern int FixtureCompositorFlush();

    private static uint ReadHostedScreenPixel(FixturePoint point)
    {
        HostedWindowsLifecycle.Require();
        nint dc=FixtureScreenDc(0);
        if(dc==0)throw new InvalidOperationException("Cannot inspect hosted compositor pixel.");
        try
        {
            uint value=FixtureScreenPixel(dc,point.X,point.Y);
            if(value==uint.MaxValue)throw new InvalidOperationException("Hosted compositor GetPixel failed.");
            return value;
        }
        finally {FixtureReleaseDc(0,dc);}
    }

    private static unsafe void SendHostedSourceMouse(Process source,nint window,FixturePoint point,uint flags,int data=0)
    {
        HostedWindowsLifecycle.Require();RequireOwnedSource(source,window);
        nint foreground=FixtureForeground(),root=FixtureRoot(FixtureAtPoint(point),2);
        if(IntPtr.Size!=8||foreground!=window||root!=window)
            throw new InvalidOperationException($"Layered presentation did not pass the physical pointer to its exact owned source: point={point.X},{point.Y}; expected={window}; foreground={foreground}; hit={DescribeFixtureWindow(root)}.");
        FixtureKeyboardInput input=new(){MouseFlags=flags,MouseData=data};
        if(FixtureSendInput(1,&input,sizeof(FixtureKeyboardInput))!=1)
            throw new InvalidOperationException("Native source mouse SendInput failed: "+Marshal.GetLastWin32Error());
    }

    // This tests the actual rendering/hit-test prerequisite, not a source-input
    // implementation. There is deliberately no user-desktop or normal-session
    // entry point: pointer warping, software cursors and original capture policy
    // must be implemented before automatic passthrough can be enabled.
    private async Task VerifyHostedLayeredMouse(Process source,nint sourceWindow,Task run,Func<string?> failure,HostedLifecycleCase evidence)
    {
        HostedWindowsLifecycle.Require();RequireOwnedSource(source,sourceWindow);
        nint output=HostedOutputHandle();
        if(_output!.Content is not Grid grid||!FixtureClient(output,out var client)||
            !FixtureWindowRect(sourceWindow,out var originalSource)||!FixtureCursor(out var originalCursor))
            throw new InvalidOperationException("Cannot locate own production output/source for layered acceptance.");
        if((FixtureKeyState(1)&0x8000)!=0)throw new InvalidOperationException("Held runner button prevents native mouse acceptance.");
        nint originalStyle=OutputWindowStyle(output,-20);
        if((originalStyle&0x80020)!=0)throw new InvalidOperationException("Unexpected original layered/passthrough output style.");
        double scale=_output.RenderScaling;
        int top=Math.Max(client.Bottom/2,(int)Math.Ceiling((_toolbar?.Bounds.Bottom??0)*scale)+30);
        var local=new FixturePoint {X=client.Right/2,Y=top};
        if(local.X<40||local.Y+40>=client.Bottom)throw new InvalidOperationException("Output has no unobstructed client area for layered acceptance.");
        var point=local;
        if(!FixtureToScreen(output,ref point))throw new InvalidOperationException("Cannot map owned output point.");
        var marker=new Border {Width=48/scale,Height=48/scale,Background=new SolidColorBrush(Color.FromRgb(211,47,173)),
            HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,
            Margin=new Thickness((local.X-16)/scale,(local.Y-16)/scale,0,0),IsHitTestVisible=false};
        bool buttonDown=false,styleChanged=false;
        try
        {
            // Put only our disposable source under this point. Source geometry
            // changes still pass through the real capture epoch, never an
            // injected bitmap/visibility/source metadata fixture.
            if(!FixtureClient(sourceWindow,out var sourceClient))throw new InvalidOperationException("Cannot locate native source client.");
            var sourceCentre=new FixturePoint {X=sourceClient.Right/2,Y=sourceClient.Bottom/2};
            if(!FixtureToScreen(sourceWindow,ref sourceCentre)||!PositionOwnedOutput(sourceWindow,0,
                originalSource.Left+point.X-sourceCentre.X,originalSource.Top+point.Y-sourceCentre.Y,0,0,0x15))
                throw new InvalidOperationException("Cannot align own native source under presentation.");
            ulong oldSequence=_lastSourceSequence;
            await WaitHosted(()=>_lastSourceSequence>oldSequence,run,failure,"source geometry accepted before mouse test");
            grid.Children.Add(marker);
            if(!FixtureMoveCursor(point.X,point.Y))throw new InvalidOperationException("Cannot move hosted pointer.");
            await Task.Delay(300);FixtureCompositorFlush();
            const uint expectedPixel=211u|(47u<<8)|(173u<<16);
            uint before=ReadHostedScreenPixel(point);
            if(before!=expectedPixel||FixtureRoot(FixtureAtPoint(point),2)!=output)
                throw new InvalidOperationException($"Hosted output marker is not natively visible/hittable before layering: pixel={before:x}, expected={expectedPixel:x}.");
            Marshal.SetLastPInvokeError(0);
            if(SetOutputWindowStyle(output,-20,OutputWindowStyle(output,-20)|0x80020)==0&&Marshal.GetLastPInvokeError()!=0)
                throw new InvalidOperationException("Cannot apply original layered/passthrough output style.");
            styleChanged=true;
            if(!FixtureLayered(output,0,255,2))throw new InvalidOperationException("Cannot set opaque native layered output: "+Marshal.GetLastWin32Error());
            await Task.Delay(300);FixtureCompositorFlush();
            uint during=ReadHostedScreenPixel(point);
            if(during!=expectedPixel)throw new InvalidOperationException($"Layered output lost actual compositor content: pixel={during:x}, expected={expectedPixel:x}.");
            evidence.LayeredScreenPixel=during;
            // A retained pre-style desktop image is insufficient: verify new
            // Avalonia drawing still reaches DWM while mouse-transparent.
            marker.Background=new SolidColorBrush(Color.FromRgb(69,197,83));
            await WaitHosted(()=>ReadHostedScreenPixel(point)==(69u|(197u<<8)|(83u<<16)),run,failure,"fresh layered compositor repaint",5);
            marker.Background=new SolidColorBrush(Color.FromRgb(211,47,173));
            await WaitHosted(()=>ReadHostedScreenPixel(point)==expectedPixel,run,failure,"second layered compositor repaint",5);
            evidence.LayeredRepaintPassed=true;
            long down=(long)FixtureProperty(sourceWindow,"MagpieFixtureMouseDown"),up=(long)FixtureProperty(sourceWindow,"MagpieFixtureMouseUp");
            long drag=(long)FixtureProperty(sourceWindow,"MagpieFixtureDrag"),wheel=(long)FixtureProperty(sourceWindow,"MagpieFixtureWheel");
            // Separate genuine down/move/up exercises native source capture;
            // two posted mouse messages cannot satisfy this acceptance.
            SendHostedSourceMouse(source,sourceWindow,point,2);buttonDown=true;
            await WaitHosted(()=>(long)FixtureProperty(sourceWindow,"MagpieFixtureMouseDown")==down+1,run,failure,"native source mouse down");
            var moved=new FixturePoint {X=point.X+12,Y=point.Y+12};
            if(!FixtureMoveCursor(moved.X,moved.Y))throw new InvalidOperationException("Cannot move physical source drag.");
            await WaitHosted(()=>(long)FixtureProperty(sourceWindow,"MagpieFixtureDrag")>drag,run,failure,"native source drag with capture");
            SendHostedSourceMouse(source,sourceWindow,moved,4);buttonDown=false;
            await WaitHosted(()=>(long)FixtureProperty(sourceWindow,"MagpieFixtureMouseUp")==up+1,run,failure,"native source mouse up");
            if(!FixtureClient(sourceWindow,out sourceClient))throw new InvalidOperationException("Source geometry disappeared after drag.");
            long coordinates=(long)FixtureProperty(sourceWindow,"MagpieFixtureMousePoint");
            if((short)coordinates!=sourceClient.Right/2+12||(short)(coordinates>>16)!=sourceClient.Bottom/2+12)
                throw new InvalidOperationException("Native source drag did not preserve physical client coordinates.");
            SendHostedSourceMouse(source,sourceWindow,moved,0x800,120);
            await WaitHosted(()=>(long)FixtureProperty(sourceWindow,"MagpieFixtureWheel")==wheel+1,run,failure,"native source wheel");
            if((long)FixtureProperty(sourceWindow,"MagpieFixtureWheelDelta")!=120||FixtureForeground()!=sourceWindow||
                (FixtureKeyState(1)&0x8000)!=0||(OutputWindowStyle(output,-20)&0x80020)!=0x80020)
                throw new InvalidOperationException("Native source mouse changed focus, button, wheel or passthrough state.");
            if(ReadHostedScreenPixel(point)!=expectedPixel)throw new InvalidOperationException("Native mouse acceptance lost rendered output.");
            evidence.LayeredNativeMousePassed=true;
        }
        finally
        {
            if(buttonDown)ReleaseHostedSourceMouse();
            if(styleChanged&&_output is not null)
            {
                Marshal.SetLastPInvokeError(0);
                if(SetOutputWindowStyle(output,-20,OutputWindowStyle(output,-20)&~(nint)0x80020)==0&&Marshal.GetLastPInvokeError()!=0)
                    throw new InvalidOperationException("Cannot restore normal output hit-testing after layered acceptance.");
            }
            grid.Children.Remove(marker);
            if(!source.HasExited)
            {
                RequireOwnedSource(source,sourceWindow);
                if(!PositionOwnedOutput(sourceWindow,0,originalSource.Left,originalSource.Top,0,0,0x15))
                    throw new InvalidOperationException("Cannot restore disposable source position.");
            }
            FixtureMoveCursor(originalCursor.X,originalCursor.Y);
        }
        await Task.Delay(200);
        await VerifyHostedOutputClick(source,sourceWindow,run,failure);
        CheckHostedPixels();
    }

    private static unsafe void ReleaseHostedSourceMouse()
    {
        // Only release a down inserted by this explicit disposable fixture.
        HostedWindowsLifecycle.Require();
        FixtureKeyboardInput input=new(){MouseFlags=4};
        if(FixtureSendInput(1,&input,sizeof(FixtureKeyboardInput))!=1)
            throw new InvalidOperationException("Cannot release fixture mouse button.");
    }
}
