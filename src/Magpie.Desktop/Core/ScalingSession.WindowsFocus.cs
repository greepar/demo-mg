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
    private bool _correctingWindowsFocus;
    private IDisposable? _windowsFocusPoll;

    private void StartWindowsOutputPolling()
    {
        if(!OperatingSystem.IsWindows())return;
        _windowsFocusPoll?.Dispose();
        _windowsFocusPoll=DispatcherTimer.Run(()=>
        {
            if(_output is null||_stop.IsCancellationRequested)return false;
            try {UpdateWindowsOutputOwner();}
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
        if(wanted==_output.Topmost)return;
        if(wanted)
        {
            lock(_captureLock)
                if(_capture is not {IsClosed:false,IsInvalid:false} capture||
                    NativeCore.CaptureSnapshotTitle(capture,_lastSourceInfo.WindowId,_lastSourceInfo.ProcessId,_lastSourceSequence,out _,5400)!=1)return;
        }
        _output.Topmost=wanted;
        // Demotion must preserve the owner's place instead of leaving the
        // presentation above an unrelated newly foreground application.
        if(!wanted&&_output.TryGetPlatformHandle() is {HandleDescriptor:"HWND"} handle&&
            !PositionOwnedOutput(handle.Handle,owner,0,0,0,0,0x0213))
            throw new InvalidOperationException("Cannot return scaling output to its source's window layer.");
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
            if(OutputWindowProcess(owner,out uint process)==0||process!=source.ProcessId)return;
            Marshal.SetLastPInvokeError(0);
            if(SetOutputWindowStyle(handle.Handle,-8,owner)==0&&Marshal.GetLastPInvokeError()!=0)
                throw new InvalidOperationException("Cannot establish output/source window ownership.");
        }
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
        // Match original ScalingWindow: source keyboard focus survives both
        // initial showing and clicks on the presentation/overlay controls.
        Win32Properties.AddWindowStylesCallback(output,static(style,extended)=>(style,extended|0x08000000)); // WS_EX_NOACTIVATE
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
