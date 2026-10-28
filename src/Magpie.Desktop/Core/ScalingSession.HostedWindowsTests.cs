using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    [DllImport("user32",EntryPoint="GetForegroundWindow")]private static extern nint FixtureForeground();
    [DllImport("user32",EntryPoint="GetWindowThreadProcessId")]private static extern uint FixtureOwner(nint window,out uint process);
    [DllImport("user32",EntryPoint="ShowWindow")]private static extern bool FixtureShow(nint window,int command);
    [DllImport("user32",EntryPoint="SetForegroundWindow")]private static extern bool FixtureActivate(nint window);
    [DllImport("user32",EntryPoint="IsWindowVisible")]private static extern bool FixtureVisible(nint window);
    [DllImport("user32",EntryPoint="PostMessageW")]private static extern bool FixturePost(nint window,uint message,nuint w,nint l);
    [DllImport("user32",EntryPoint="GetPropW",CharSet=CharSet.Unicode)]private static extern nint FixtureProperty(nint window,string property);
    [DllImport("user32",EntryPoint="GetAsyncKeyState")]private static extern short FixtureKeyState(int key);
    [DllImport("user32",EntryPoint="SendInput",SetLastError=true)]private static extern unsafe uint FixtureSendInput(uint count,FixtureKeyboardInput* inputs,int size);
    [DllImport("user32",EntryPoint="GetCursorPos")]private static extern bool FixtureCursor(out FixturePoint point);
    [DllImport("user32",EntryPoint="SetCursorPos")]private static extern bool FixtureMoveCursor(int x,int y);
    [DllImport("user32",EntryPoint="ClientToScreen")]private static extern bool FixtureToScreen(nint window,ref FixturePoint point);
    [DllImport("user32",EntryPoint="GetClientRect")]private static extern bool FixtureClient(nint window,out FixtureRect rect);
    [DllImport("user32",EntryPoint="WindowFromPoint")]private static extern nint FixtureAtPoint(FixturePoint point);
    [DllImport("user32",EntryPoint="GetAncestor")]private static extern nint FixtureRoot(nint window,uint flag);
    [StructLayout(LayoutKind.Sequential)]private struct FixturePoint {public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct FixtureRect {public int Left,Top,Right,Bottom;}
    // This hosted fixture targets win-x64 only. INPUT includes the larger mouse
    // union; KEYBDINPUT starts at offset 8, not immediately after its DWORD tag.
    [StructLayout(LayoutKind.Explicit,Size=40)]
    private struct FixtureKeyboardInput
    {
        [FieldOffset(0)]public uint Type;
        [FieldOffset(8)]public ushort Key;
        [FieldOffset(12)]public uint Flags;
        [FieldOffset(20)]public uint MouseFlags;
    }

    private static unsafe void SendHostedClick(Process source,nint sourceWindow,nint output,FixturePoint point)
    {
        HostedWindowsLifecycle.Require();RequireOwnedSource(source,sourceWindow);
        nint foreground=FixtureForeground(),hit=FixtureAtPoint(point),root=FixtureRoot(hit,2);short button=FixtureKeyState(1);
        if(IntPtr.Size!=8||foreground!=sourceWindow||root!=output||(button&0x8000)!=0)
            throw new InvalidOperationException($"Hosted click requires its owned visible output: point={point.X},{point.Y}; source={sourceWindow}; foreground={foreground}; output={output}; hit={hit}; root={root}; leftState={button}.");
        FixtureKeyboardInput* inputs=stackalloc FixtureKeyboardInput[2];
        inputs[0]=new(){MouseFlags=2};inputs[1]=new(){MouseFlags=4};
        uint inserted=FixtureSendInput(2,inputs,sizeof(FixtureKeyboardInput));
        if(inserted!=2)
        {
            int error=Marshal.GetLastWin32Error();
            if(inserted==1)FixtureSendInput(1,inputs+1,sizeof(FixtureKeyboardInput));
            throw new InvalidOperationException($"Hosted mouse SendInput inserted {inserted}/2 events, error {error}.");
        }
    }
    private async Task VerifyHostedOutputClick(Process source,nint sourceWindow,Task run,Func<string?> failure)
    {
        HostedWindowsLifecycle.Require();RequireOwnedSource(source,sourceWindow);
        nint window=HostedOutputHandle();
        if(OutputWindowStyle(window,-8)!=sourceWindow)throw new InvalidOperationException("Output lost its native source owner.");
        if(FixtureProperty(window,"TreatAsDesktopFullscreen")!=1)throw new InvalidOperationException("Output lost the original fullscreen shell hint.");
        if(!FixtureClient(window,out var rect)||rect.Right<20||rect.Bottom<20||!FixtureCursor(out var original))
            throw new InvalidOperationException("Cannot locate owned output for native click test.");
        var point=new FixturePoint {X=rect.Right/2,Y=rect.Bottom-10};
        if(!FixtureToScreen(window,ref point))throw new InvalidOperationException("Cannot map owned output client point.");
        int presses=0,releases=0;
        EventHandler<PointerPressedEventArgs> pressed=(_,e)=> {if(e.GetCurrentPoint(_output).Properties.IsLeftButtonPressed)presses++;};
        EventHandler<PointerReleasedEventArgs> released=(_,_)=>releases++;
        _output!.AddHandler(InputElement.PointerPressedEvent,pressed,RoutingStrategies.Tunnel,handledEventsToo:true);
        _output.AddHandler(InputElement.PointerReleasedEvent,released,RoutingStrategies.Tunnel,handledEventsToo:true);
        try
        {
            if(!FixtureMoveCursor(point.X,point.Y))throw new InvalidOperationException("Cannot move fixture cursor.");
            SendHostedClick(source,sourceWindow,window,point);
            await WaitHosted(()=>presses==1&&releases==1,run,failure,"actual output pointer press/release");
            await Task.Delay(150);
            if(FixtureForeground()!=sourceWindow||(FixtureKeyState(1)&0x8000)!=0)
                throw new InvalidOperationException($"Click changed focus/button state: expectedSource={sourceWindow}; foreground={FixtureForeground()}; output={window}; leftState={FixtureKeyState(1)}.");
        }
        finally
        {
            _output?.RemoveHandler(InputElement.PointerPressedEvent,pressed);
            _output?.RemoveHandler(InputElement.PointerReleasedEvent,released);
            FixtureMoveCursor(original.X,original.Y);
        }
    }

    private static unsafe void SendHostedKey(Process source,nint window,ushort key)
    {
        HostedWindowsLifecycle.Require();RequireOwnedSource(source,window);
        if(IntPtr.Size!=8||FixtureForeground()!=window)throw new InvalidOperationException("Native keyboard test requires its owned foreground source on win-x64.");
        foreach(int held in new[]{0x10,0x11,0x12,0x5b,0x5c,(int)key})
            if((FixtureKeyState(held)&0x8000)!=0)throw new InvalidOperationException("Runner has a held key; refusing to modify its state.");
        FixtureKeyboardInput* inputs=stackalloc FixtureKeyboardInput[2];
        inputs[0]=new(){Type=1,Key=key};inputs[1]=new(){Type=1,Key=key,Flags=2};
        uint inserted=FixtureSendInput(2,inputs,sizeof(FixtureKeyboardInput));
        if(inserted!=2)
        {
            int error=Marshal.GetLastWin32Error();
            if(inserted==1)FixtureSendInput(1,inputs+1,sizeof(FixtureKeyboardInput));
            throw new InvalidOperationException($"Hosted SendInput inserted {inserted}/2 events, error {error}.");
        }
    }
    private static async Task VerifyHostedSourceKeyboard(Process source,nint window,Task run,Func<string?> failure)
    {
        // Escape/F12 must reach the foreground source as in the original;
        // the output's local convenience handlers must not steal those keys.
        foreach(ushort key in new ushort[]{0x42,0x1b,0x7b})
        {
            RequireOwnedSource(source,window);
            long down=(long)FixtureProperty(window,"MagpieFixtureKeyDown"),up=(long)FixtureProperty(window,"MagpieFixtureKeyUp");
            long chars=(long)FixtureProperty(window,"MagpieFixtureChar");
            SendHostedKey(source,window,key);
            await WaitHosted(()=>(long)FixtureProperty(window,"MagpieFixtureKeyDown")==down+1&&
                (long)FixtureProperty(window,"MagpieFixtureKeyUp")==up+1&&
                (key!=0x42||(long)FixtureProperty(window,"MagpieFixtureChar")>chars),run,failure,"native source keyboard delivery");
            RequireOwnedSource(source,window);
            if(FixtureForeground()!=window||(long)FixtureProperty(window,"MagpieFixtureLastKey")!=key||
                (FixtureKeyState(key)&0x8000)!=0)throw new InvalidOperationException("Source lost foreground/key identity or injected key remained held.");
            if(key==0x42&&(long)FixtureProperty(window,"MagpieFixtureLastChar") is not (0x62 or 0x42))
                throw new InvalidOperationException("Native source did not receive the expected translated B character.");
        }
    }

    private static void RequireOwnedSource(Process source,nint window)
    {
        if(source.HasExited||window==0||FixtureOwner(window,out var pid)==0||pid!=(uint)source.Id)
            throw new InvalidOperationException("Fixture source identity changed; refusing window operations.");
    }
    private async Task VerifyHostedWindowLayers(Process source,nint sourceWindow,Task run,Func<string?> failure,HostedLifecycleCase evidence)
    {
        HostedWindowsLifecycle.Require();RequireOwnedSource(source,sourceWindow);
        var global=RuntimeSettings.Current;bool disabled=global.DisableTopmost;double minimum=global.MinimumFrameRate;
        Process? other=null;nint otherWindow=0;
        bool NativeTopmost()=>_output is not null&&(OutputWindowStyle(HostedOutputHandle(),-20)&8)!=0;
        void SetSourceTopmost(bool value)
        {
            RequireOwnedSource(source,sourceWindow);
            if(!PositionOwnedOutput(sourceWindow,value?-1:-2,0,0,0,0,0x13))throw new InvalidOperationException("Cannot set owned fixture source topmost state.");
        }
        try
        {
            global.MinimumFrameRate=0;global.DisableTopmost=true;global.Save();
            await WaitHosted(()=>!_output!.Topmost&&!NativeTopmost(),run,failure,"disable topmost preference");
            SetSourceTopmost(true);
            await WaitHosted(()=>_output!.Topmost&&NativeTopmost(),run,failure,"inherit topmost source while automatic topmost is disabled");
            evidence.SourceTopmostInherited=true;
            SetSourceTopmost(false);
            await WaitHosted(()=>!_output!.Topmost&&!NativeTopmost(),run,failure,"return source/output to normal window layer");
            global.DisableTopmost=false;global.Save();
            await WaitHosted(()=>_output!.Topmost&&NativeTopmost(),run,failure,"foreground source automatic topmost restored");
            evidence.TopmostPreferenceHonored=true;
            var launch=new ProcessStartInfo(HostedWindowsLifecycle.Fixture) {UseShellExecute=false,CreateNoWindow=true};
            launch.ArgumentList.Add("--source");launch.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            other=Process.Start(launch)??throw new InvalidOperationException("Cannot create independent foreground fixture.");
            await WaitHosted(()=> {other.Refresh();otherWindow=other.MainWindowHandle;return otherWindow!=0;},run,failure,"independent foreground HWND");
            RequireOwnedSource(other,otherWindow);
            if(otherWindow==sourceWindow)throw new InvalidOperationException("Independent fixture reused the live selected source HWND.");
            FixtureActivate(otherWindow);
            await WaitHosted(()=>FixtureForeground()==otherWindow&&!_output!.Topmost&&!NativeTopmost(),run,failure,"independent foreground and output demotion");
            if(!FixtureClient(otherWindow,out var rect))throw new InvalidOperationException("Cannot locate independent foreground client.");
            var point=new FixturePoint {X=rect.Right/2,Y=rect.Bottom/2};
            if(!FixtureToScreen(otherWindow,ref point)||FixtureRoot(FixtureAtPoint(point),2)!=otherWindow)
                throw new InvalidOperationException("Scaling output obscures the independent foreground application.");
            long keys=(long)FixtureProperty(sourceWindow,"MagpieFixtureKeyDown");
            await Task.Delay(250);
            await VerifyHostedSourceKeyboard(other,otherWindow,run,failure);
            if(FixtureForeground()!=otherWindow||(long)FixtureProperty(sourceWindow,"MagpieFixtureKeyDown")!=keys||
                _lastSourceInfo.WindowId!=(ulong)sourceWindow||_lastSourceInfo.ProcessId!=(uint)source.Id)
                throw new InvalidOperationException("Independent foreground input was stolen or capture silently changed source.");
            RequireOwnedSource(source,sourceWindow);FixtureActivate(sourceWindow);
            await WaitHosted(()=>FixtureForeground()==sourceWindow&&_output!.Topmost&&NativeTopmost(),run,failure,"selected foreground source and output layer restored");
            await VerifyHostedSourceKeyboard(source,sourceWindow,run,failure);
            evidence.OtherForegroundPreserved=true;
        }
        finally
        {
            if(!source.HasExited)SetSourceTopmost(false);
            global.DisableTopmost=disabled;global.MinimumFrameRate=minimum;global.Save();
            if(other is not null)
            {
                if(!other.HasExited)
                {
                    if(otherWindow!=0) {RequireOwnedSource(other,otherWindow);FixturePost(otherWindow,0x10,0,0);}
                    try {await other.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));}
                    catch(TimeoutException){other.Kill(entireProcessTree:true);await other.WaitForExitAsync();}
                }
                int exit=other.ExitCode;other.Dispose();
                if(exit!=0)throw new InvalidOperationException("Independent foreground fixture did not exit cleanly.");
            }
        }
    }
    private static async Task WaitHosted(Func<bool> ready,Task? run,Func<string?> failure,string phase,int seconds=20)
    {
        var clock=Stopwatch.StartNew();
        while(!ready())
        {
            if(run?.IsCompleted==true)throw new InvalidOperationException(phase+": production loop ended: "+failure());
            if(clock.Elapsed>TimeSpan.FromSeconds(seconds))throw new InvalidOperationException("Timed out: "+phase+"; "+failure());
            await Task.Delay(25);
        }
    }
    private nint HostedOutputHandle()
    {
        var handle=_output?.TryGetPlatformHandle();
        if(handle is null||handle.HandleDescriptor!="HWND"||handle.Handle==0)throw new InvalidOperationException("Output has no native HWND.");
        if(FixtureOwner(handle.Handle,out var pid)==0||pid!=(uint)Environment.ProcessId)throw new InvalidOperationException("Output HWND is not owned by test application.");
        return handle.Handle;
    }
    private unsafe void CheckHostedPixels()
    {
        if(_bitmap is null)throw new InvalidOperationException("Production bitmap is missing.");
        using var pixels=_bitmap.Lock();
        var p=(byte*)pixels.Address+(_bitmap.PixelSize.Height/2)*pixels.RowBytes+(_bitmap.PixelSize.Width/2)*4;
        if(p[0]!=23||p[1]!=67||p[2]!=109||p[3]!=255)throw new InvalidOperationException($"Production Vulkan/bitmap centre differs from independent Win32 source paint: {p[0]},{p[1]},{p[2]},{p[3]}.");
    }
    private async Task<ulong> HostedGpuSerial(EngineHandle engine)
    {
        await _stageGate.WaitAsync();
        try {return StageSnapshot.Read(engine)[0].Serial;}
        finally {_stageGate.Release();}
    }
    internal static async Task VerifyHostedWindowsLifecycleAsync(MainWindow owner)
    {
        HostedWindowsLifecycle.Require();
        var report=new HostedLifecycleReport();
        void Save()=>File.WriteAllText(Path.Combine(HostedWindowsLifecycle.Output,"production.json"),JsonSerializer.Serialize(report,HostedLifecycleJson.Default.HostedLifecycleReport));
        Save();
        bool disable=RuntimeSettings.Current.DisableFP16,keepAwake=RuntimeSettings.Current.KeepScreenOn;
        double minimum=RuntimeSettings.Current.MinimumFrameRate;
        RuntimeSettings.Current.DisableFP16=true;RuntimeSettings.Current.KeepScreenOn=false;RuntimeSettings.Current.MinimumFrameRate=30;
        try
        {
            foreach(var backend in new[]{CaptureBackend.Gdi,CaptureBackend.GraphicsCapture})
            foreach(bool fullscreen in new[]{false,true})
            {
                var evidence=new HostedLifecycleCase {Backend=backend.ToString(),Fullscreen=fullscreen};report.Cases.Add(evidence);Save();
                if(!CaptureBackendPolicy.Supported(backend)) {evidence.State="unavailable";Save();continue;}
                var settings=new RuntimeSettings {Effect=0,CaptureBackend=backend,FrameRateLimiterEnabled=true,MaximumFrameRate=60};
                settings.ScreenshotDirectory=Path.Combine(HostedWindowsLifecycle.Output,backend+(fullscreen?"-fullscreen":"-windowed"));
                settings.ScreenshotTemplate="source";
                var session=new ScalingSession(settings);Task? run=null;string? failure=null;Process? source=null;
                void Phase(string value){evidence.Phase=value;Console.WriteLine("WINDOWS PRODUCTION PHASE: "+backend+" "+fullscreen+" "+value);Save();}
                session.Finished+=message=>failure=message;
                try
                {
                    var launch=new ProcessStartInfo(HostedWindowsLifecycle.Fixture) {UseShellExecute=false,CreateNoWindow=true};
                    launch.ArgumentList.Add("--source");launch.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    source=Process.Start(launch)??throw new InvalidOperationException("Cannot create owned source process.");
                    nint window=0;
                    await WaitHosted(()=> {source.Refresh();window=source.MainWindowHandle;return window!=0;},null,()=>failure,"owned source HWND");
                    RequireOwnedSource(source,window);FixtureActivate(window);
                    await WaitHosted(()=>FixtureForeground()==window,null,()=>failure,"owned source foreground");
                    evidence.SourcePid=(uint)source.Id;evidence.SourceWindow=(ulong)window;
                    Phase("initial production capture");run=session.RunAsync(owner,fullscreen,0);
                    await WaitHosted(()=>session._bitmap is not null&&session._lastSourceSequence>0,run,()=>failure,"initial production bitmap",30);
                    if(session._lastSourceInfo.WindowId!=(ulong)window||session._lastSourceInfo.ProcessId!=(uint)source.Id)throw new InvalidOperationException("Production capture selected a foreign source.");
                    lock(session._captureLock)
                        if(session._capture is not {} capture||NativeCore.CaptureInstanceBackend(capture)!=(uint)backend)throw new InvalidOperationException("Production capture substituted another backend.");
                    var engine=session._stageEngine??throw new InvalidOperationException("No actual production Vulkan engine.");
                    evidence.Device=Marshal.PtrToStringUTF8(NativeCore.DeviceName(engine))??"";
                    if(!evidence.Device.Contains("SwiftShader",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Expected the explicitly selected SwiftShader Vulkan test driver, got "+evidence.Device);
                    report.SoftwareVulkan=true;session.CheckHostedPixels();
                    await Task.Delay(200);
                    if(FixtureForeground()!=window)throw new InvalidOperationException("Initial output stole foreground focus from its source.");
                    evidence.InitialForegroundPreserved=true;
                    await session.VerifyHostedOutputClick(source,window,run,()=>failure);evidence.NativeOutputClickChecks++;
                    await VerifyHostedSourceKeyboard(source,window,run,()=>failure);evidence.NativeKeyboardChecks++;
                    Phase("foreground topmost policy and independent application");
                    await session.VerifyHostedWindowLayers(source,window,run,()=>failure,evidence);
                    evidence.SourceWidth=session._captureFrameWidth;evidence.SourceHeight=session._captureFrameHeight;evidence.FirstSequence=session._lastSourceSequence;
                    if(!fullscreen) {session._output!.Width+=80;session._output.Height+=40;await Task.Delay(200);}
                    var oldSize=session._output!.ClientSize;var oldPosition=session._output.Position;
                    foreach(bool minimize in new[]{true,false})
                    {
                        Phase(minimize?"minimize source":"hide source");
                        nint output=session.HostedOutputHandle();RequireOwnedSource(source,window);FixtureShow(window,minimize?6:0);
                        await WaitHosted(()=>session._sourceSuspended&&!session._output!.IsVisible&&!FixtureVisible(output),run,()=>failure,"native output hidden");
                        evidence.NativeOutputHidden=true;ulong paused=session._lastSourceSequence;
                        ulong gpu=await session.HostedGpuSerial(engine);await Task.Delay(200);
                        if(session._lastSourceSequence!=paused||await session.HostedGpuSerial(engine)!=gpu)throw new InvalidOperationException("Suspended production loop kept submitting frames/GPU work.");
                        evidence.IdleGpuPreserved=true;session.RequestScreenshot();
                        if(session.SaveScreenshot() is not null||Volatile.Read(ref session._screenshotPending)!=0)throw new InvalidOperationException("Hidden production source exported stale pixels.");
                        Phase(minimize?"restore source":"show source");
                        RequireOwnedSource(source,window);FixtureShow(window,minimize?9:8);FixtureActivate(window);
                        await WaitHosted(()=>!session._sourceSuspended&&session._output!.IsVisible&&session._lastSourceSequence>paused&&
                            session._output.WindowState==(fullscreen?WindowState.FullScreen:WindowState.Normal),run,()=>failure,"fresh production output restored",30);
                        await Task.Delay(200);
                        if(!FixtureVisible(session.HostedOutputHandle()))throw new InvalidOperationException("Restored output is not a visible native window.");
                        if(FixtureForeground()!=window)throw new InvalidOperationException("Restored output stole foreground focus from the source.");
                        evidence.ForegroundPreserved=true;session.CheckHostedPixels();
                        await session.VerifyHostedOutputClick(source,window,run,()=>failure);evidence.NativeOutputClickChecks++;
                        await VerifyHostedSourceKeyboard(source,window,run,()=>failure);evidence.NativeKeyboardChecks++;
                        if(!fullscreen&&(Math.Abs(session._output!.ClientSize.Width-oldSize.Width)>1||Math.Abs(session._output.ClientSize.Height-oldSize.Height)>1||session._output.Position!=oldPosition))
                            throw new InvalidOperationException("Windowed production recovery lost output geometry.");
                        if(minimize)evidence.RestoredSequence=session._lastSourceSequence;else evidence.ShownSequence=session._lastSourceSequence;
                    }
                    Phase("save actual output");
                    evidence.Screenshot=Path.GetRelativePath(HostedWindowsLifecycle.Output,session.SaveScreenshot()??throw new InvalidOperationException("Production screenshot failed."));
                    using(var picture=new RenderTargetBitmap(PixelSize.FromSize(session._output!.ClientSize,1)))
                    {picture.Render((Control)session._output.Content!);picture.Save(Path.Combine(settings.ScreenshotDirectory,"output.png"),PngBitmapEncoderOptions.Default);}
                    Phase(fullscreen?"stop while hidden":"source close while hidden");
                    nint finalOutput=session.HostedOutputHandle();RequireOwnedSource(source,window);FixtureShow(window,0);
                    await WaitHosted(()=>session._sourceSuspended&&!FixtureVisible(finalOutput),run,()=>failure,"final hidden source");
                    if(fullscreen) {session.Stop();evidence.StopWhileHidden=true;}
                    else {RequireOwnedSource(source,window);if(!FixturePost(window,0x0010,0,0))throw new InvalidOperationException("Cannot close owned source.");}
                    await run.WaitAsync(TimeSpan.FromSeconds(15));
                    if(failure is not null||session._output is not null)throw new InvalidOperationException("Production shutdown failed: "+failure);
                    if(!fullscreen)evidence.SourceClosedWithoutError=true;
                    evidence.State="passed";Phase("complete");
                    Console.WriteLine("WINDOWS PRODUCTION CASE PASS: "+backend+" fullscreen="+fullscreen);
                }
                catch(Exception error){evidence.State="failed";evidence.Error=error.ToString();Save();throw;}
                finally
                {
                    if(run is not null&&!run.IsCompleted) {session.Stop();await run.WaitAsync(TimeSpan.FromSeconds(15));}
                    if(source is not null)
                    {
                        if(!source.HasExited)
                        {
                            var window=unchecked((nint)evidence.SourceWindow);
                            if(window!=0) {RequireOwnedSource(source,window);FixturePost(window,0x0010,0,0);}
                            try {await source.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));}
                            catch(TimeoutException){source.Kill(entireProcessTree:true);await source.WaitForExitAsync();}
                        }
                        evidence.SourceExitedCleanly=source.ExitCode==0;
                        if(!evidence.SourceExitedCleanly&&evidence.State=="passed")throw new InvalidOperationException("Owned source exited abnormally during cleanup.");
                        source.Dispose();
                    }
                }
            }
            report.Passed=report.Cases.Count==4&&report.Cases.Where(c=>c.Backend=="Gdi").All(c=>c.State=="passed")&&report.Cases.All(c=>c.State is "passed" or "unavailable");
            if(!report.Passed)throw new InvalidOperationException("Incomplete production lifecycle matrix.");
            Console.WriteLine("WINDOWS PRODUCTION LIFECYCLE PASS: real owned Win32 source, native capture, SwiftShader Vulkan, Avalonia native output, initial/resumed foreground and native source keyboard, minimize/hide/fresh restore, geometry, idle GPU, screenshot and stop/source-close cleanup; mouse/IME/game/hardware Vulkan coverage remains incomplete.");
        }
        finally {RuntimeSettings.Current.DisableFP16=disable;RuntimeSettings.Current.KeepScreenOn=keepAwake;RuntimeSettings.Current.MinimumFrameRate=minimum;Save();}
    }
}
