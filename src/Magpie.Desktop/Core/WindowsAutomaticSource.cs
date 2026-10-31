using System.Runtime.InteropServices;
namespace Magpie.Desktop.Core;

internal interface IWindowsAutomaticSource
{
    ulong Foreground { get; }
    WindowsCaptureSelection? Read();
    bool Ready(WindowsCaptureSelection source);
    bool IsPopup(WindowsCaptureSelection source,ulong owner);
}

internal sealed class WindowsAutomaticSource : IWindowsAutomaticSource
{
    [DllImport("user32")] private static extern nint GetForegroundWindow();
    [DllImport("user32")] private static extern nint MonitorFromWindow(nint window,uint flags);
    [DllImport("user32",EntryPoint="SendMessageTimeoutW")] private static extern nint SendMessageTimeout(nint window,uint message,nint w,nint l,uint flags,uint timeout,out nuint result);
    [DllImport("user32")] private static extern nint GetWindow(nint window,uint command);
    [DllImport("user32")] private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(nint window,out uint process);
    public ulong Foreground=>unchecked((ulong)GetForegroundWindow());
    public WindowsCaptureSelection? Read()=>WindowsCaptureSelection.Read();
    public bool Ready(WindowsCaptureSelection source)
    {
        nint window=unchecked((nint)source.Source.WindowId);
        return GetWindowThreadProcessId(window,out uint process)==source.ThreadId&&process==source.Source.ProcessId&&
            MonitorFromWindow(window,0)!=0&&SendMessageTimeout(window,0,0,0,0x20,500,out _)!=0;
    }
    public bool IsPopup(WindowsCaptureSelection source,ulong owner)
    {
        nint window=unchecked((nint)source.Source.WindowId),parent=unchecked((nint)owner),current=window;
        for(int depth=0;depth<256&&(current=GetWindow(current,4))!=0;depth++)if(current==parent)return true;
        // Original fallback for games that disable their main window without
        // establishing native owner relationships; don't broaden this to all
        // same-process secondary windows.
        return !IsWindowEnabled(parent)&&GetWindowThreadProcessId(parent,out uint process)!=0&&
            process==source.Source.ProcessId&&GetWindow(parent,6)==0;
    }
}
