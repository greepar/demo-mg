using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    [DllImport("user32",EntryPoint="GetSystemMetrics")]private static extern int HostedDesktopMetric(int index);
    private static unsafe void MoveHostedMouseInput(Process source,nint sourceWindow,nint output,FixturePoint point)
    {
        HostedWindowsLifecycle.Require();RequireOwnedSource(source,sourceWindow);
        if(FixtureForeground()!=sourceWindow||FixtureRoot(FixtureAtPoint(point),2)!=output)
            throw new InvalidOperationException("Native mouse entry requires the owned foreground source and its visible output.");
        int left=HostedDesktopMetric(76),top=HostedDesktopMetric(77),width=HostedDesktopMetric(78),height=HostedDesktopMetric(79);
        if(width<=0||height<=0||width>32768||height>32768||point.X<left||point.X>=left+width||point.Y<top||point.Y>=top+height)
            throw new InvalidOperationException("Hosted desktop cannot represent the exact requested absolute mouse pixel.");
        // Pixel-centre normalization for the real absolute mouse event. A plain
        // SetCursorPos warp does not establish mouse input as the last device.
        FixtureKeyboardInput input=new() {MouseX=(int)(((long)(point.X-left)*65536+32768)/width),
            MouseY=(int)(((long)(point.Y-top)*65536+32768)/height),MouseFlags=0xc001};
        if(FixtureSendInput(1,&input,sizeof(FixtureKeyboardInput))!=1)
            throw new InvalidOperationException("Cannot deliver native mouse entry movement: "+Marshal.GetLastWin32Error());
    }
    private async Task VerifyHostedScaledPointer(Process source,nint sourceWindow,Task run,Func<string?> failure,HostedLifecycleCase evidence)
    {
        HostedWindowsLifecycle.Require();RequireOwnedSource(source,sourceWindow);
        if(_image is null||_bitmap is null||!FixtureClient(sourceWindow,out var client)||!FixtureCursor(out var original))
            throw new InvalidOperationException("Missing actual source/image for native scaled pointer acceptance.");
        if((FixtureKeyState(1)&0x8000)!=0)throw new InvalidOperationException("Held runner button prevents scaled native pointer acceptance.");
        var dest=_image.PointToScreen(default);
        int dx=(_bitmap.PixelSize.Width-1)*3/4,dy=(_bitmap.PixelSize.Height-1)*3/4;
        var visual=new FixturePoint {X=dest.X+dx,Y=dest.Y+dy};
        if(WindowsPointerOnOverlay(visual.X,visual.Y))throw new InvalidOperationException("Scaled pointer test point overlaps a real local overlay.");
        var origin=new FixturePoint();
        if(!FixtureToScreen(sourceWindow,ref origin))throw new InvalidOperationException("Cannot query native source origin.");
        // Independent expected endpoint-centre mapping, not the production
        // mapper or its returned target. Native geometry is read from Win32.
        var physical=new FixturePoint {
            X=origin.X+(int)Math.Round((double)dx/(_bitmap.PixelSize.Width-1)*(client.Right-1),MidpointRounding.AwayFromZero),
            Y=origin.Y+(int)Math.Round((double)dy/(_bitmap.PixelSize.Height-1)*(client.Bottom-1),MidpointRounding.AwayFromZero)};
        bool buttonDown=false;
        try
        {
            if((long)FixtureProperty(sourceWindow,"MagpieFixtureCursorDisplayCount")<1000)
                throw new InvalidOperationException("Owned source did not establish its visible-cursor display count.");
            MoveHostedMouseInput(source,sourceWindow,HostedOutputHandle(),visual);
            await WaitHosted(()=>FixtureCursor(out var at)&&at.X==visual.X&&at.Y==visual.Y,run,failure,"exact native absolute mouse entry");
            _windowsPointerSuppressed=false;
            try {await WaitHosted(()=>_windowsPointerCaptured&&_windowsSystemCursorHidden&&_windowsCursorOverlay is {IsVisible:true},run,failure,"production native virtual cursor",10);}
            catch(Exception error)
            {
                FixtureCursor(out var now);var cursor=new WindowsCursorInfo {Size=(uint)Marshal.SizeOf<WindowsCursorInfo>()};ReadWindowsCursor(ref cursor);
                var m=_windowsPointerMapping;
                throw new InvalidOperationException($"Native pointer status={_windowsPointerStatus}; captured={_windowsPointerCaptured}; hidden={_windowsSystemCursorHidden}; passthrough={_windowsPointerPassthrough}; overlay={_windowsCursorOverlay?.IsVisible}; pointer={now.X},{now.Y}; expectedPointer={physical.X},{physical.Y}; inputVisual={visual.X},{visual.Y}; cursorFlags={cursor.Flags}; beforeHide={_windowsCursorBeforeHide}; afterHide={_windowsCursorAfterHide}; cursor={cursor.Cursor}; sourceShowCount={(long)FixtureProperty(sourceWindow,"MagpieFixtureCursorDisplayCount")-1000}; sourceCursorUpdates={FixtureProperty(sourceWindow,"MagpieFixtureCursorUpdates")}; sourceRect={m.Source.Left},{m.Source.Top},{m.Source.Right},{m.Source.Bottom}; destRect={m.Destination.Left},{m.Destination.Top},{m.Destination.Right},{m.Destination.Bottom}; imageBounds={_image.Bounds}; bitmap={_bitmap.PixelSize}; hitOutput={NativeCore.WindowsPointerWindow(HostedOutputHandle(),visual.X,visual.Y,0)}; expectedOutput={HostedOutputHandle()}; hitSource={NativeCore.WindowsPointerWindow(HostedOutputHandle(),physical.X,physical.Y,1)}; expectedSource={sourceWindow}.",error);
            }
            if(!FixtureCursor(out var actual)||actual.X!=physical.X||actual.Y!=physical.Y)
                throw new InvalidOperationException($"Scaled pointer landed at {actual.X},{actual.Y}, expected independent source pixel {physical.X},{physical.Y}.");
            const uint inverse=232u|(188u<<8)|(146u<<16);
            await WaitHosted(()=>ReadHostedScreenPixel(visual)==inverse,run,failure,"actual monochrome XOR cursor on scaled Vulkan pixels",5);
            evidence.NativeInvertCursorPassed=true;evidence.NativeInvertCursorPixel=ReadHostedScreenPixel(visual);
            Directory.CreateDirectory(_settings.ScreenshotDirectory);
            using(var image=new RenderTargetBitmap(PixelSize.FromSize(_output!.ClientSize,1)))
            {image.Render((Control)_output.Content!);image.Save(Path.Combine(_settings.ScreenshotDirectory,"native-pointer.png"),PngBitmapEncoderOptions.Default);}
            long down=(long)FixtureProperty(sourceWindow,"MagpieFixtureMouseDown"),up=(long)FixtureProperty(sourceWindow,"MagpieFixtureMouseUp");
            long drag=(long)FixtureProperty(sourceWindow,"MagpieFixtureDrag"),wheel=(long)FixtureProperty(sourceWindow,"MagpieFixtureWheel");
            SendHostedSourceMouse(source,sourceWindow,physical,2);buttonDown=true;
            await WaitHosted(()=>(long)FixtureProperty(sourceWindow,"MagpieFixtureMouseDown")==down+1,run,failure,"scaled real source press");
            var moved=new FixturePoint {X=physical.X+7,Y=physical.Y+5};
            if(!FixtureMoveCursor(moved.X,moved.Y))throw new InvalidOperationException("Cannot move native captured source drag.");
            await WaitHosted(()=>(long)FixtureProperty(sourceWindow,"MagpieFixtureDrag")>drag,run,failure,"scaled real source drag");
            SendHostedSourceMouse(source,sourceWindow,moved,4);buttonDown=false;
            await WaitHosted(()=>(long)FixtureProperty(sourceWindow,"MagpieFixtureMouseUp")==up+1,run,failure,"scaled real source release");
            long coordinates=(long)FixtureProperty(sourceWindow,"MagpieFixtureMousePoint");
            if((short)coordinates!=moved.X-origin.X||(short)(coordinates>>16)!=moved.Y-origin.Y)
                throw new InvalidOperationException("Scaled source drag changed independent source client coordinates.");
            SendHostedSourceMouse(source,sourceWindow,moved,0x800,120);
            await WaitHosted(()=>(long)FixtureProperty(sourceWindow,"MagpieFixtureWheel")==wheel+1,run,failure,"scaled real source wheel");
            var movedVisual=new FixturePoint {
                X=dest.X+(int)Math.Round((double)(moved.X-origin.X)/(client.Right-1)*(_bitmap.PixelSize.Width-1),MidpointRounding.AwayFromZero),
                Y=dest.Y+(int)Math.Round((double)(moved.Y-origin.Y)/(client.Bottom-1)*(_bitmap.PixelSize.Height-1),MidpointRounding.AwayFromZero)};
            await WaitHosted(()=>ReadHostedScreenPixel(movedVisual)==inverse,run,failure,"native software cursor follows actual source drag",5);
            if(FixtureForeground()!=sourceWindow||(FixtureKeyState(1)&0x8000)!=0||(long)FixtureProperty(sourceWindow,"MagpieFixtureWheelDelta")!=120)
                throw new InvalidOperationException("Scaled native source mouse lost foreground or input state.");
            await VerifyHostedCursorAutoHide(source,sourceWindow,run,failure,evidence,origin,client,moved,movedVisual);
            await VerifyHostedCursorScaling(source,sourceWindow,run,failure,evidence,client,movedVisual);
            await VerifyHostedMonitorAndToolbar(source,sourceWindow,run,failure,evidence,origin,client,moved);
            evidence.ScaledNativeMousePassed=true;
        }
        catch(Exception)
        {
            var cursor=new WindowsCursorInfo {Size=(uint)Marshal.SizeOf<WindowsCursorInfo>()};ReadWindowsCursor(ref cursor);
            Console.Error.WriteLine($"SCALED POINTER FAILURE STATE: status={_windowsPointerStatus}; cursorFlags={cursor.Flags}; beforeHide={_windowsCursorBeforeHide}; afterHide={_windowsCursorAfterHide}; captured={_windowsPointerCaptured}; hidden={_windowsSystemCursorHidden}; sourceCount={(long)FixtureProperty(sourceWindow,"MagpieFixtureCursorDisplayCount")-1000}; sourceCursorUpdates={FixtureProperty(sourceWindow,"MagpieFixtureCursorUpdates")}.");
            throw;
        }
        finally
        {
            if(buttonDown)ReleaseHostedSourceMouse();
            _windowsPointerSuppressed=true;EndWindowsPointer();
            FixtureMoveCursor(original.X,original.Y);
        }
        if(_windowsPointerCaptured||_windowsPointerPassthrough||_windowsSystemCursorHidden||_windowsCursorOverlay is {IsVisible:true}||
            _windowsAppliedClip is not null||(OutputWindowStyle(HostedOutputHandle(),-20)&0x20)!=0)
            throw new InvalidOperationException("Stopping virtual pointer left system cursor, clip, overlay or passthrough state behind.");
        await VerifyHostedOutputClick(source,sourceWindow,run,failure);
        CheckHostedPixels();evidence.NativePointerCleanupPassed=true;
    }
    private async Task VerifyHostedCursorAutoHide(Process source,nint window,Task run,Func<string?> failure,HostedLifecycleCase evidence,
        FixturePoint origin,FixtureRect client,FixturePoint current,FixturePoint currentVisual)
    {
        HostedWindowsLifecycle.Require();RequireOwnedSource(source,window);
        var previous=(_settings.AutoHideCursorEnabled,_settings.AutoHideCursorDelay);
        const uint background=23u|(67u<<8)|(109u<<16),inverse=232u|(188u<<8)|(146u<<16);
        try
        {
            _settings.AutoHideCursorEnabled=true;_settings.AutoHideCursorDelay=.5;_windowsCursorAutoHide.Reset();
            await WaitHosted(()=>_windowsPointerCaptured&&_windowsSystemCursorHidden&&_windowsCursorOverlay is {IsVisible:false}&&
                ReadHostedScreenPixel(currentVisual)==background,run,failure,"idle software cursor actually disappears from screen",5);
            var moved=new FixturePoint {X=current.X+3,Y=current.Y+2};
            var dest=_image!.PointToScreen(default);
            var visual=new FixturePoint {
                X=dest.X+(int)Math.Round((double)(moved.X-origin.X)/(client.Right-1)*(_bitmap!.PixelSize.Width-1),MidpointRounding.AwayFromZero),
                Y=dest.Y+(int)Math.Round((double)(moved.Y-origin.Y)/(client.Bottom-1)*(_bitmap!.PixelSize.Height-1),MidpointRounding.AwayFromZero)};
            if(!FixtureMoveCursor(moved.X,moved.Y))throw new InvalidOperationException("Cannot move owned source cursor to wake it.");
            await WaitHosted(()=>_windowsCursorOverlay is {IsVisible:true}&&ReadHostedScreenPixel(visual)==inverse,
                run,failure,"native source movement redraws the actual XOR cursor",5);
            await WaitHosted(()=>_windowsPointerCaptured&&_windowsCursorOverlay is {IsVisible:false}&&ReadHostedScreenPixel(visual)==background,
                run,failure,"software cursor hides again after movement",5);
            if(FixtureForeground()!=window)throw new InvalidOperationException("Idle cursor changed source activation.");
            evidence.NativeCursorAutoHidePassed=true;
        }
        finally
        {
            (_settings.AutoHideCursorEnabled,_settings.AutoHideCursorDelay)=previous;_windowsCursorAutoHide.Reset();
            FixtureMoveCursor(current.X,current.Y);
        }
    }
    private async Task VerifyHostedCursorScaling(Process source,nint window,Task run,Func<string?> failure,HostedLifecycleCase evidence,
        FixtureRect client,FixturePoint visual)
    {
        HostedWindowsLifecycle.Require();RequireOwnedSource(source,window);
        var previous=(_settings.CursorScaling,_settings.CustomCursorScaleFactor,_settings.CursorInterpolation,_settings.AutoHideCursorEnabled);
        const uint background=23u|(67u<<8)|(109u<<16),inverse=232u|(188u<<8)|(146u<<16);
        try
        {
            _settings.AutoHideCursorEnabled=false;
            var origin=_output!.PointToScreen(default);var dest=_image!.PointToScreen(default);
            int imageWidth=_bitmap!.PixelSize.Width,imageHeight=_bitmap.PixelSize.Height;
            foreach(var option in new[]{(5,1f,0,2f),(5,1f,1,2f),(7,1.5f,1,1.5f),
                (6,1f,1,((float)imageWidth/client.Right+(float)imageHeight/client.Bottom)/2)})
            {
                (_settings.CursorScaling,_settings.CustomCursorScaleFactor,_settings.CursorInterpolation)=(option.Item1,option.Item2,option.Item3);
                static int Round(float value)=>(int)MathF.Round(value,MidpointRounding.AwayFromZero);
                int width=Round(32*option.Item4),height=Round(32*option.Item4);
                int left=origin.X+Round((visual.X-origin.X)-3*option.Item4),top=origin.Y+Round((visual.Y-origin.Y)-3*option.Item4);
                int clippedWidth=Math.Min(left+width,dest.X+imageWidth)-Math.Max(left,dest.X);
                int clippedHeight=Math.Min(top+height,dest.Y+imageHeight)-Math.Max(top,dest.Y);
                bool PixelsMatch()
                {
                    if(!_windowsPointerCaptured||_windowsCursorBitmap?.PixelSize!=new PixelSize(clippedWidth,clippedHeight))return false;
                    // Independent native fixture mask is a 10x10 inversion
                    // cross at (3,3). Probe the real screen, not the bitmap.
                    for(int offset=-8;offset<=22;++offset)
                    {
                        var point=new FixturePoint {X=visual.X+offset,Y=visual.Y};
                        if(point.X<dest.X||point.X>=dest.X+imageWidth)return false;
                        int sx=(int)Math.Floor(((double)point.X-left+.5)*32/width),sy=(int)Math.Floor(((double)point.Y-top+.5)*32/height);
                        bool invert=(sx==3&&sy>=0&&sy<10)||(sy==3&&sx>=0&&sx<10);
                        if(ReadHostedScreenPixel(point)!=(invert?inverse:background))return false;
                    }
                    return true;
                }
                await WaitHosted(PixelsMatch,run,failure,"actual scaled cursor bounds/hotspot/XOR pixels for choice "+option.Item1+" interpolation "+option.Item3,5);
                evidence.NativeCursorScaleChecks++;
            }
        }
        finally
        {
            (_settings.CursorScaling,_settings.CustomCursorScaleFactor,_settings.CursorInterpolation,_settings.AutoHideCursorEnabled)=previous;
            _windowsCursorAutoHide.Reset();
        }
    }
}
