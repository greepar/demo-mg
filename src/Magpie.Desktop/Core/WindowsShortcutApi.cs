using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Threading;
namespace Magpie.Desktop.Core;

[SupportedOSPlatform("windows")]
internal sealed class WindowsShortcutApi(Window owner) : IShortcutNativeApi
{
    private nint _hwnd,_layout;
    private Action<int,bool>? _changed;
    private readonly HashSet<int> _registered=[];
    private bool _hooked;
    public void Initialize(Action<int,bool> changed)
    {
        Dispatcher.UIThread.VerifyAccess();
        _hwnd=owner.TryGetPlatformHandle()?.Handle??0;
        if(_hwnd==0)throw new InvalidOperationException("The native Windows application window is unavailable.");
        _layout=GetKeyboardLayout(0);_changed=changed;Win32Properties.AddWndProcHookCallback(owner,Message);_hooked=true;
    }
    private nint Message(nint hwnd,uint message,nint wParam,nint lParam,ref bool handled)
    {
        const uint Hotkey=0x0312;
        if(message==Hotkey&&_registered.Contains((int)wParam))
        {
            handled=true;
            // MOD_NOREPEAT delivers discrete presses. Each notification is complete.
            _changed?.Invoke((int)wParam,true);_changed?.Invoke((int)wParam,false);
        }
        return 0;
    }
    public bool LayoutIsCurrent=>GetKeyboardLayout(0)==_layout;
    public NativeShortcutKey Resolve(string trigger)
    {
        var (symbol,modifiers)=NativeShortcutMapping.Parse(trigger);uint key;
        if(NativeShortcutMapping.Character(symbol) is { } character)
        {
            short mapped=VkKeyScanExW(character,_layout);
            if(mapped==-1)throw new InvalidDataException("The current Windows keyboard layout cannot type "+symbol+".");
            key=(uint)mapped&0xFF;uint implicitModifiers=(uint)(mapped>>8)&0xFF;
            if((implicitModifiers&~7u)!=0)throw new InvalidDataException("Unsupported Windows keyboard modifier mapping.");
            if((implicitModifiers&1)!=0)modifiers|=4;
            if((implicitModifiers&2)!=0)modifiers|=2;
            if((implicitModifiers&4)!=0)modifiers|=1;
        }
        else key=NativeShortcutMapping.WindowsSpecial(symbol);
        // Win32 reserves F12 for debuggers; rejecting before registration is deterministic.
        if(key==0x7B)throw new InvalidDataException("Windows reserves F12 for the debugger; choose another global shortcut.");
        return new(key,modifiers,NativeShortcutMapping.Display(symbol,modifiers,false));
    }
    public void Register(int id,NativeShortcutKey key)
    {
        Dispatcher.UIThread.VerifyAccess();
        if(!RegisterHotKey(_hwnd,id,key.Modifiers|0x4000,key.Key))throw new Win32Exception(Marshal.GetLastPInvokeError(),"Could not register "+key.Display+"; another application or Windows may reserve it.");
        _registered.Add(id);
    }
    public void Unregister(int id)
    {
        Dispatcher.UIThread.VerifyAccess();
        if(!_registered.Contains(id))return;
        if(!UnregisterHotKey(_hwnd,id))throw new Win32Exception(Marshal.GetLastPInvokeError(),"Could not unregister a Windows shortcut.");
        _registered.Remove(id);
    }
    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        var failures=new List<Exception>();
        foreach(int id in _registered.ToArray())try { Unregister(id); }catch(Exception error) { failures.Add(error); }
        if(_hooked) { Win32Properties.RemoveWndProcHookCallback(owner,Message);_hooked=false; }
        _changed=null;
        if(failures.Count>0)throw new AggregateException("Windows shortcut cleanup failed.",failures);
    }
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(nint hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(nint hwnd,int id);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] private static extern short VkKeyScanExW(char character,nint layout);
}
