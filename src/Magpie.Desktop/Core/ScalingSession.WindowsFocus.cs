using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    [DllImport("user32",EntryPoint="GetWindowLongPtrW",SetLastError=true)]
    private static extern nint OutputWindowStyle(nint window,int index);
    [DllImport("user32",EntryPoint="SetWindowLongPtrW",SetLastError=true)]
    private static extern nint SetOutputWindowStyle(nint window,int index,nint value);
    [DllImport("user32",EntryPoint="GetWindowThreadProcessId")]
    private static extern uint OutputWindowProcess(nint window,out uint process);
    [DllImport("user32",EntryPoint="GetForegroundWindow")]
    private static extern nint OutputForeground();
    [DllImport("user32",EntryPoint="SetWindowPos",SetLastError=true)]
    private static extern bool PositionOwnedOutput(nint window,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("user32",EntryPoint="SetForegroundWindow")]
    private static extern bool RestoreOutputSourceForeground(nint window);
    [DllImport("user32",EntryPoint="GetWindow")]
    private static extern nint OutputRelatedWindow(nint window,uint kind);
    [DllImport("user32",EntryPoint="IsWindowEnabled")]
    private static extern bool OutputSourceEnabled(nint window);
    [DllImport("user32",EntryPoint="SetPropW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern bool SetOutputShellProperty(nint window,string property,nint value);
    [DllImport("user32",EntryPoint="RemovePropW",CharSet=CharSet.Unicode)]
    private static extern nint RemoveOutputShellProperty(nint window,string property);
    [DllImport("kernel32",EntryPoint="GetCurrentThreadId")]
    private static extern uint OutputCurrentThread();
    [DllImport("user32",EntryPoint="AttachThreadInput")]
    private static extern bool OutputAttachInput(uint thread,uint other,bool attach);
    private bool _correctingWindowsFocus;
    private IDisposable? _windowsFocusPoll;
    private bool _windowsOutputNeedsLayerRestore=true;
    private int _windowsShellMarkResult;
    private void AssignWindowsOwnerBeforeShow(nint window)
    {
        if(_sourceSuspended||_stop.IsCancellationRequested)return;
        lock(_captureLock)
        {
            if(_capture is not {IsClosed:false,IsInvalid:false} capture)return;
            // Ownership is selected-source state, not presented-frame metadata.
            // The first accepted capture exists before the output's first Show.
            var source=ReadCapturedSource(capture);ulong sequence=NativeCore.CaptureFrameSequence(capture);
            if(source is not {Kind:1,WindowId:>0,ProcessId:>0}||sequence==0||
                NativeCore.CaptureSnapshotTitle(capture,source.WindowId,source.ProcessId,sequence,out _,5400)!=1)return;
            nint owner=unchecked((nint)source.WindowId);
            uint thread=OutputWindowProcess(owner,out uint process);
            if(thread==0||process!=source.ProcessId)return;
            bool changed=OutputWindowStyle(window,-8)!=owner;
            Marshal.SetLastPInvokeError(0);
            if(SetOutputWindowStyle(window,-8,owner)==0&&Marshal.GetLastPInvokeError()!=0)
                throw new InvalidOperationException("Cannot establish source ownership before showing output.");
            // Original WM_CREATE detaches the implicit input-queue attachment
            // of cross-thread owned windows; native source input stays native.
            if(changed)OutputAttachInput(OutputCurrentThread(),thread,false);
            _windowsOutputNeedsLayerRestore=true;
        }
    }
    private void DetachSuspendedWindowsOwner()
    {
        if(!OperatingSystem.IsWindows()||_output?.TryGetPlatformHandle() is not {HandleDescriptor:"HWND",Handle:not 0} handle)return;
        // Do not let restoring an owner automatically show a still-stale owned
        // window. The fresh-frame Show hook reattaches the selected source.
        Marshal.SetLastPInvokeError(0);
        if(SetOutputWindowStyle(handle.Handle,-8,0)==0&&Marshal.GetLastPInvokeError()!=0)
            throw new InvalidOperationException("Cannot detach the hidden source presentation.");
        _windowsOutputNeedsLayerRestore=true;
    }

    private void StartWindowsOutputPolling()
    {
        if(!OperatingSystem.IsWindows())return;
        _windowsFocusPoll?.Dispose();
        _windowsFocusPoll=DispatcherTimer.Run(()=>
        {
            try
            {
                if(_output is null||_stop.IsCancellationRequested) {EndWindowsPointer();return false;}
                UpdateWindowsOutputOwner();UpdateWindowsPointer();
            }
            catch(Exception error) {Console.Error.WriteLine("Output window policy: "+error.Message);Stop();}
            return true;
        },TimeSpan.FromMilliseconds(16));
    }

    private void UpdateWindowsOutputLayer(nint owner)
    {
        if(_output is null||_sourceSuspended||_stop.IsCancellationRequested)return;
        // Original _CalcTopmostState: inherit a topmost source; otherwise only
        // while that exact source is foreground, enabled, and has no popup.
        nint popup=OutputRelatedWindow(owner,6);
        nint output=_output.TryGetPlatformHandle()?.Handle??0;
        // GW_ENABLEDPOPUP may return the owner itself when none is found.
        // Our owned presentation is also not an application dialog.
        bool hasPopup=popup!=0&&popup!=owner&&popup!=output;
        bool wanted=(OutputWindowStyle(owner,-20)&8)!=0||(!RuntimeSettings.Current.DisableTopmost&&
            OutputForeground()==owner&&OutputSourceEnabled(owner)&&!hasPopup);
        bool nativeTopmost=output!=0&&(OutputWindowStyle(output,-20)&8)!=0;
        if(wanted==_output.Topmost&&wanted==nativeTopmost&&!_windowsOutputNeedsLayerRestore)return;
        if(wanted)
        {
            lock(_captureLock)
                if(_capture is not {IsClosed:false,IsInvalid:false} capture||
                    NativeCore.CaptureSnapshotTitle(capture,_lastSourceInfo.WindowId,_lastSourceInfo.ProcessId,_lastSourceSequence,out _,5400)!=1)return;
        }
        _output.Topmost=wanted;
        if(wanted)
        {
            // Hide/Show and fullscreen restoration can change native style or
            // ordering while Avalonia's Topmost property remains true.
            if(!PositionOwnedOutput(output,-1,0,0,0,0,0x0213))
                throw new InvalidOperationException("Cannot restore the native foreground output layer.");
            // Original full-screen branch also raises within the topmost band;
            // HWND_TOPMOST alone does not establish order among topmost peers.
            if(_output.WindowState==WindowState.FullScreen&&!PositionOwnedOutput(output,0,0,0,0,0,0x0213))
                throw new InvalidOperationException("Cannot restore fullscreen output above its topmost peers.");
            _windowsOutputNeedsLayerRestore=false;
        }
        // Demotion must preserve the owner's place instead of leaving the
        // presentation above an unrelated newly foreground application.
        if(!wanted&&_output.TryGetPlatformHandle() is {HandleDescriptor:"HWND"} handle)
        {
            lock(_captureLock)
                if(_capture is {IsClosed:false,IsInvalid:false} capture)
                {
                    int result=NativeCore.CaptureRestoreOutputLayer(capture,handle.Handle,_lastSourceInfo.WindowId,_lastSourceInfo.ProcessId);
                    if(result<0)throw NativeCore.Error();
                    _windowsOutputNeedsLayerRestore=result==0;
                }
        }
        // Avalonia marks fullscreen when the state changes. Re-notify after
        // Show/owner reconstruction too, once the correct native owner exists.
        lock(_captureLock)
            if(output!=0&&_capture is {IsClosed:false,IsInvalid:false} current)
            {
                _windowsShellMarkResult=NativeCore.CaptureMarkOutputFullscreen(current,output,_lastSourceInfo.WindowId,
                    _lastSourceInfo.ProcessId,_output.WindowState==WindowState.FullScreen?1:0);
                if(_windowsShellMarkResult<0)throw NativeCore.Error();
            }
    }

    private bool RestoreWindowsSourceFocus(nint window)
    {
        if(_correctingWindowsFocus||_stop.IsCancellationRequested||_sourceSuspended||OutputForeground()!=window||
            _lastSourceInfo is not {Kind:1,WindowId:>0,ProcessId:>0} source)return false;
        var handle=_output?.TryGetPlatformHandle();
        if(handle is not {HandleDescriptor:"HWND"}||handle.Handle!=window)return false;
        nint owner=unchecked((nint)source.WindowId);
        lock(_captureLock)
        {
            if(_capture is not {IsClosed:false,IsInvalid:false} capture||
                NativeCore.CaptureSnapshotTitle(capture,source.WindowId,source.ProcessId,_lastSourceSequence,out _,5400)!=1||
                OutputWindowProcess(owner,out uint process)==0||process!=source.ProcessId)return false;
        }
        try
        {
            _correctingWindowsFocus=true;
            return RestoreOutputSourceForeground(owner)&&OutputForeground()==owner;
        }
        finally {_correctingWindowsFocus=false;}
    }

    private void UpdateWindowsOutputOwner()
    {
        if(!OperatingSystem.IsWindows()||_output is null||_stop.IsCancellationRequested||_sourceSuspended||
            _lastSourceInfo is not {Kind:1,WindowId:>0,ProcessId:>0} source)return;
        var handle=_output.TryGetPlatformHandle();
        if(handle is not {HandleDescriptor:"HWND",Handle:not 0})return;
        nint owner=unchecked((nint)source.WindowId);
        if(OutputWindowStyle(handle.Handle,-8)==owner)
        {
            RestoreWindowsSourceFocus(handle.Handle);
            UpdateWindowsOutputLayer(owner);
            return;
        }
        // Revalidate the displayed capture epoch before using its HWND as an
        // owner; a numeric HWND/PID alone is not a lifetime guarantee.
        lock(_captureLock)
        {
            if(_capture is not {IsClosed:false,IsInvalid:false} capture||
                NativeCore.CaptureSnapshotTitle(capture,source.WindowId,source.ProcessId,_lastSourceSequence,out _,5400)!=1)return;
            uint thread=OutputWindowProcess(owner,out uint process);
            if(thread==0||process!=source.ProcessId)return;
            Marshal.SetLastPInvokeError(0);
            if(SetOutputWindowStyle(handle.Handle,-8,owner)==0&&Marshal.GetLastPInvokeError()!=0)
                throw new InvalidOperationException("Cannot establish output/source window ownership.");
            OutputAttachInput(OutputCurrentThread(),thread,false);
        }
        _windowsOutputNeedsLayerRestore=true;
        // Avalonia Show resets the native owner; restore the original owned
        // window relation after first presentation and every hide/show. Raise
        // only for the actual foreground source, without activating either.
        if(OutputForeground()==owner&&!PositionOwnedOutput(handle.Handle,0,0,0,0,0,0x0213))
            throw new InvalidOperationException("Cannot restore owned output above its foreground source.");
        RestoreWindowsSourceFocus(handle.Handle);
        UpdateWindowsOutputLayer(owner);
    }

    private void ConfigureWindowsOutputFocus(Window output)
    {
        if(!OperatingSystem.IsWindows())return;
        _windowsOutputNeedsLayerRestore=true;
        // Match original ScalingWindow: source keyboard focus survives both
        // initial showing and clicks on the presentation/overlay controls.
        Win32Properties.AddWindowStylesCallback(output,(style,extended)=>(style,
            (extended&~0x20u)|0x08000000u|(_windowsPointerLayered?0x80000u:0u)|(_windowsPointerPassthrough?0x20u:0u))); // WS_EX_NOACTIVATE
        Win32Properties.AddWndProcHookCallback(output,KeepWindowsSourceActive);
        var handle=output.TryGetPlatformHandle();
        if(handle is not {HandleDescriptor:"HWND",Handle:not 0})throw new InvalidOperationException("Windows output has no HWND for its activation policy.");
        // Same shell hint as original ScalingWindow::WM_CREATE, including its
        // owned UWP case; this changes only this presentation window.
        if(!SetOutputShellProperty(handle.Handle,"TreatAsDesktopFullscreen",1))
            throw new InvalidOperationException("Cannot set the scaling output's fullscreen shell hint.");
        // The styles callback maintains later style changes. Apply it to the
        // already-created HWND too; registering a callback alone does not.
        Marshal.SetLastPInvokeError(0);
        nint previous=OutputWindowStyle(handle.Handle,-20);
        if(previous==0&&Marshal.GetLastPInvokeError()!=0)throw new InvalidOperationException("Cannot read output activation style.");
        Marshal.SetLastPInvokeError(0);
        if(SetOutputWindowStyle(handle.Handle,-20,previous|0x08000000)==0&&Marshal.GetLastPInvokeError()!=0)
            throw new InvalidOperationException("Cannot preserve source focus for the output window.");
    }
    private nint KeepWindowsSourceActive(nint window,uint message,nint w,nint l,ref bool handled)
    {
        if(message==0x0082)RemoveOutputShellProperty(window,"TreatAsDesktopFullscreen"); // WM_NCDESTROY
        if(message==0x0018&&w!=0) // WM_SHOWWINDOW, before native visibility
        {
            try {AssignWindowsOwnerBeforeShow(window);}
            catch(Exception error) {Console.Error.WriteLine("Output source owner: "+error.Message);}
        }
        if(message==0x0021) // WM_MOUSEACTIVATE
        {
            handled=true;
            return 3; // MA_NOACTIVATE: process the click without activating.
        }
        // Avalonia 12.1.1 calls SetFocus on button-down even after
        // MA_NOACTIVATE. Match the original's unexpected-focus correction,
        // synchronously before subsequent keyboard messages are dispatched.
        if(message==0x0007) // WM_SETFOCUS
        {
            try {if(RestoreWindowsSourceFocus(window))handled=true;}
            catch(Exception error) {Console.Error.WriteLine("Output source focus: "+error.Message);}
        }
        return 0;
    }
}
