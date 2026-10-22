using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
namespace Magpie.Desktop.Core;

internal interface IWindowsPowerApi
{
    nint Create(string reason);
    void Set(nint request,int kind);
    void Clear(nint request,int kind);
    void Close(nint request);
}
internal sealed class WindowsScreenAwakeLease(IWindowsPowerApi api,nint request) : IScreenAwakeLease
{
    private nint _request=request;
    private bool _displayRequired=true,_systemRequired=true;
    private readonly object _sync=new();
    public bool Active=>Volatile.Read(ref _request)!=0;
    public event Action<string>? Lost { add { } remove { } }
    internal static WindowsScreenAwakeLease Acquire(IWindowsPowerApi api,string reason)
    {
        nint handle=api.Create(reason);if(handle==0||handle==-1)throw new InvalidOperationException("Windows did not create a power request.");
        bool display=false,system=false;
        try { api.Set(handle,0);display=true;api.Set(handle,1);system=true;return new(api,handle); }
        catch(Exception acquisition)
        {
            try { api.Close(handle); } // Closing also removes partially acquired requests.
            catch(Exception cleanup)
            {
                ScreenAwakeCleanup.Schedule(new WindowsScreenAwakeLease(api,handle) { _displayRequired=display,_systemRequired=system });
                throw new AggregateException("Windows partial power request cleanup is pending.",acquisition,cleanup);
            }
            throw;
        }
    }
    public ValueTask DisposeAsync()
    {
        lock(_sync)
        {
            nint handle=_request;if(handle==0)return ValueTask.CompletedTask;
            List<Exception> errors=[];
            if(_systemRequired)try { api.Clear(handle,1);_systemRequired=false; }catch(Exception error) { errors.Add(error); }
            if(_displayRequired)try { api.Clear(handle,0);_displayRequired=false; }catch(Exception error) { errors.Add(error); }
            try { api.Close(handle);_request=0; }catch(Exception error) { errors.Add(error); }
            if(errors.Count>0)throw new AggregateException("Windows power request cleanup failed.",errors);
            return ValueTask.CompletedTask;
        }
    }
}

// REASON_CONTEXT contains a 24-byte union on supported 64-bit targets.
[StructLayout(LayoutKind.Explicit,Size=32)]
internal struct WindowsPowerReasonContext
{
    [FieldOffset(0)] public uint Version;
    [FieldOffset(4)] public uint Flags;
    [FieldOffset(8)] public nint SimpleReasonString;
}
[SupportedOSPlatform("windows")]
internal sealed class WindowsPowerApi : IWindowsPowerApi
{
    public nint Create(string reason)
    {
        nint text=Marshal.StringToHGlobalUni(reason);
        try
        {
            var context=new WindowsPowerReasonContext { Version=0,Flags=1,SimpleReasonString=text };
            nint request=PowerCreateRequest(ref context);if(request==0||request==-1)throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not create the display-awake request.");return request;
        }
        finally { Marshal.FreeHGlobal(text); }
    }
    public void Set(nint request,int kind) { if(!PowerSetRequest(request,kind))throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not keep the display/system awake."); }
    public void Clear(nint request,int kind) { if(!PowerClearRequest(request,kind))throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not clear the power request."); }
    public void Close(nint request) { if(!CloseHandle(request))throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not close the power request."); }
    [DllImport("kernel32.dll",SetLastError=true)] private static extern nint PowerCreateRequest(ref WindowsPowerReasonContext context);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool PowerSetRequest(nint request,int kind);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool PowerClearRequest(nint request,int kind);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint request);
}

internal interface IMacPowerApi { uint Create(string reason);void Release(uint assertion); }
internal sealed class MacScreenAwakeLease(IMacPowerApi api,uint assertion) : IScreenAwakeLease
{
    private uint _assertion=assertion;
    public bool Active=>Volatile.Read(ref _assertion)!=0;
    public event Action<string>? Lost { add { } remove { } }
    internal static MacScreenAwakeLease Acquire(IMacPowerApi api,string reason)
    {
        uint id=api.Create(reason);if(id==0)throw new InvalidOperationException("macOS returned an invalid power assertion.");return new(api,id);
    }
    public ValueTask DisposeAsync()
    {
        uint id=Volatile.Read(ref _assertion);if(id==0)return ValueTask.CompletedTask;
        api.Release(id);_assertion=0;return ValueTask.CompletedTask; // Retain an assertion whose release failed so cleanup can retry.
    }
}
[SupportedOSPlatform("macos")]
internal sealed class MacPowerApi : IMacPowerApi
{
    private const string IOKit="/System/Library/Frameworks/IOKit.framework/IOKit",CoreFoundation="/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    public uint Create(string reason)
    {
        nint type=0,name=0;
        try
        {
            // Apple documents that preventing idle display sleep also prevents idle system sleep.
            type=CFStringCreateWithCString(0,"PreventUserIdleDisplaySleep",0x08000100);name=CFStringCreateWithCString(0,reason,0x08000100);
            if(type==0||name==0)throw new OutOfMemoryException("Could not create the power assertion description.");
            int status=IOPMAssertionCreateWithName(type,255,name,out uint id);
            if(status!=0)throw new InvalidOperationException("Could not keep the display awake (IOKit 0x"+status.ToString("X8")+").");return id;
        }
        finally { if(name!=0)CFRelease(name);if(type!=0)CFRelease(type); }
    }
    public void Release(uint assertion) { int status=IOPMAssertionRelease(assertion);if(status!=0)throw new InvalidOperationException("Could not release the power assertion (IOKit 0x"+status.ToString("X8")+")."); }
    [DllImport(IOKit)] private static extern int IOPMAssertionCreateWithName(nint type,uint level,nint name,out uint assertion);
    [DllImport(IOKit)] private static extern int IOPMAssertionRelease(uint assertion);
    [DllImport(CoreFoundation)] private static extern nint CFStringCreateWithCString(nint allocator,[MarshalAs(UnmanagedType.LPUTF8Str)] string text,uint encoding);
    [DllImport(CoreFoundation)] private static extern void CFRelease(nint value);
}
