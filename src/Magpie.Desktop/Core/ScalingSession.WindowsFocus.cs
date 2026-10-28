using System.Runtime.InteropServices;
using Avalonia.Controls;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    [DllImport("user32",EntryPoint="GetWindowLongPtrW",SetLastError=true)]
    private static extern nint OutputWindowStyle(nint window,int index);
    [DllImport("user32",EntryPoint="SetWindowLongPtrW",SetLastError=true)]
    private static extern nint SetOutputWindowStyle(nint window,int index,nint value);

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
