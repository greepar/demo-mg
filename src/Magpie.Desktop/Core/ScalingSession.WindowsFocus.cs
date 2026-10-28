using System.Runtime.InteropServices;
using Avalonia.Controls;
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

    private void UpdateWindowsOutputOwner()
    {
        if(!OperatingSystem.IsWindows()||_output is null||_stop.IsCancellationRequested||_sourceSuspended||
            _lastSourceInfo is not {Kind:1,WindowId:>0,ProcessId:>0} source)return;
        var handle=_output.TryGetPlatformHandle();
        if(handle is not {HandleDescriptor:"HWND",Handle:not 0})return;
        nint owner=unchecked((nint)source.WindowId);
        if(OutputWindowStyle(handle.Handle,-8)==owner)return;
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
    }

    private static void ConfigureWindowsOutputFocus(Window output)
    {
        if(!OperatingSystem.IsWindows())return;
        // Match original ScalingWindow: source keyboard focus survives both
        // initial showing and clicks on the presentation/overlay controls.
        Win32Properties.AddWindowStylesCallback(output,static(style,extended)=>(style,extended|0x08000000)); // WS_EX_NOACTIVATE
        Win32Properties.AddWndProcHookCallback(output,KeepWindowsSourceActive);
        var handle=output.TryGetPlatformHandle();
        if(handle is not {HandleDescriptor:"HWND",Handle:not 0})throw new InvalidOperationException("Windows output has no HWND for its activation policy.");
        // The styles callback maintains later style changes. Apply it to the
        // already-created HWND too; registering a callback alone does not.
        Marshal.SetLastPInvokeError(0);
        nint previous=OutputWindowStyle(handle.Handle,-20);
        if(previous==0&&Marshal.GetLastPInvokeError()!=0)throw new InvalidOperationException("Cannot read output activation style.");
        Marshal.SetLastPInvokeError(0);
        if(SetOutputWindowStyle(handle.Handle,-20,previous|0x08000000)==0&&Marshal.GetLastPInvokeError()!=0)
            throw new InvalidOperationException("Cannot preserve source focus for the output window.");
    }
    private static nint KeepWindowsSourceActive(nint window,uint message,nint w,nint l,ref bool handled)
    {
        if(message==0x0021) // WM_MOUSEACTIVATE
        {
            handled=true;
            return 3; // MA_NOACTIVATE: process the click without activating.
        }
        return 0;
    }
}
