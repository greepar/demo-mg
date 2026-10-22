using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;
namespace Magpie.Desktop.Core;

[SupportedOSPlatform("macos")]
internal sealed class MacShortcutApi : IShortcutNativeApi
{
    private const string Carbon="/System/Library/Frameworks/Carbon.framework/Carbon",CoreFoundation="/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint Signature=0x4D675048; // MgPH, identifies only this registration set.
    private readonly Dictionary<int,nint> _registered=[];
    private static readonly List<MacShortcutApi> FailedCleanupRoots=[];
    private Action<int,bool>? _changed;
    private HotkeyHandler? _callback;
    private nint _handler,_layoutData,_carbon,_layoutProperty;
    private uint _keyboardType;
    public void Initialize(Action<int,bool> changed)
    {
        Dispatcher.UIThread.VerifyAccess();
        _carbon=NativeLibrary.Load(Carbon);_layoutProperty=Marshal.ReadIntPtr(NativeLibrary.GetExport(_carbon,"kTISPropertyUnicodeKeyLayoutData"));
        _layoutData=CopyCurrentLayout();_keyboardType=LMGetKbdType();_changed=changed;
        _callback=OnEvent;
        EventTypeSpec[] types=[new(0x6B657962,5),new(0x6B657962,6)]; // keyb / HotKeyPressed, HotKeyReleased.
        int status=InstallEventHandler(GetApplicationEventTarget(),_callback,(nuint)types.Length,types,0,out _handler);
        if(status!=0)throw new InvalidOperationException("Could not install the macOS hotkey handler (OSStatus "+status+").");
    }
    private nint CopyCurrentLayout()
    {
        nint source=TISCopyCurrentASCIICapableKeyboardLayoutInputSource();
        if(source==0)throw new InvalidOperationException("The macOS ASCII-capable keyboard layout is unavailable.");
        try
        {
            nint data=TISGetInputSourceProperty(source,_layoutProperty);
            if(data==0||CFDataGetLength(data)<1||CFDataGetLength(data)>1048576)throw new InvalidOperationException("The macOS Unicode keyboard layout data is unavailable.");
            nint copy=CFDataCreateCopy(0,data);if(copy==0)throw new OutOfMemoryException("Could not retain the keyboard layout.");return copy;
        }
        finally { CFRelease(source); }
    }
    public bool LayoutIsCurrent
    {
        get
        {
            nint current=CopyCurrentLayout();try { return CFEqual(current,_layoutData)!=0&&LMGetKbdType()==_keyboardType; }finally { CFRelease(current); }
        }
    }
    public NativeShortcutKey Resolve(string trigger)
    {
        var (symbol,modifiers)=NativeShortcutMapping.Parse(trigger);uint key;
        if(NativeShortcutMapping.Character(symbol) is { } character)
        {
            bool found=false;key=0;
            // Resolve logical characters from the user's layout, never assume ANSI/QWERTY.
            foreach(uint shift in new uint[]{0,2})
            {
                for(ushort candidate=0;candidate<128;candidate++)
                {
                    uint dead=0;char[] text=new char[4];
                    int status=UCKeyTranslate(CFDataGetBytePtr(_layoutData),candidate,3,shift,_keyboardType,1,ref dead,(nuint)text.Length,out nuint length,text);
                    if(status==0&&length==1&&char.ToLowerInvariant(text[0])==character)
                    {
                        key=candidate;if(shift!=0)modifiers|=4;found=true;break;
                    }
                }
                if(found)break;
            }
            if(!found)throw new InvalidDataException("The current macOS keyboard layout cannot map "+symbol+" to a global shortcut.");
        }
        else key=NativeShortcutMapping.MacSpecial(symbol);
        return new(key,modifiers,NativeShortcutMapping.Display(symbol,modifiers,true));
    }
    public void Register(int id,NativeShortcutKey key)
    {
        Dispatcher.UIThread.VerifyAccess();
        uint modifiers=0;
        if((key.Modifiers&1)!=0)modifiers|=0x800;
        if((key.Modifiers&2)!=0)modifiers|=0x1000;
        if((key.Modifiers&4)!=0)modifiers|=0x200;
        if((key.Modifiers&8)!=0)modifiers|=0x100;
        // Exclusive registration reports conflicts rather than silently sharing actions.
        int status=RegisterEventHotKey(key.Key,modifiers,new(Signature,(uint)id),GetApplicationEventTarget(),1,out nint reference);
        if(status!=0)throw new InvalidOperationException("Could not register "+key.Display+" (OSStatus "+status+"). Choose another combination; macOS may reserve this shortcut.");
        _registered.Add(id,reference);
    }
    public void Unregister(int id)
    {
        Dispatcher.UIThread.VerifyAccess();
        if(!_registered.TryGetValue(id,out nint reference))return;
        int status=UnregisterEventHotKey(reference);
        if(status!=0)throw new InvalidOperationException("Could not unregister a macOS shortcut (OSStatus "+status+").");
        _registered.Remove(id);
    }
    private int OnEvent(nint next,nint value,nint user)
    {
        uint kind=GetEventKind(value);
        if(kind is not (5 or 6))return -9874; // eventNotHandledErr.
        int status=GetEventParameter(value,0x2D2D2D2D,0x686B6964,0,8,out nuint actual,out EventHotKeyId key);
        if(status!=0||actual!=8||key.Signature!=Signature||!_registered.ContainsKey((int)key.Id))return -9874;
        _changed?.Invoke((int)key.Id,kind==5);return 0;
    }
    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        _changed=null;
        var failures=new List<Exception>();
        foreach(int id in _registered.Keys.ToArray())try { Unregister(id); }catch(Exception error) { failures.Add(error); }
        if(_handler!=0)
        {
            int status=RemoveEventHandler(_handler);
            if(status!=0)
            {
                // The OS may still invoke the unmanaged callback. Keep its delegate
                // and framework pinned, with action dispatch disabled, until exit.
                if(!FailedCleanupRoots.Contains(this))FailedCleanupRoots.Add(this);
                failures.Add(new InvalidOperationException("macOS hotkey handler removal failed: "+status));
                throw new AggregateException("macOS shortcut cleanup failed.",failures);
            }
            _handler=0;
        }
        _callback=null;
        if(_layoutData!=0) { CFRelease(_layoutData);_layoutData=0; }
        if(_carbon!=0) { NativeLibrary.Free(_carbon);_carbon=0; }
        if(failures.Count>0)throw new AggregateException("macOS shortcut cleanup failed.",failures);
    }

    [StructLayout(LayoutKind.Sequential)] private readonly record struct EventTypeSpec(uint EventClass,uint EventKind);
    [StructLayout(LayoutKind.Sequential)] private readonly record struct EventHotKeyId(uint Signature,uint Id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int HotkeyHandler(nint next,nint value,nint user);
    [DllImport(Carbon)] private static extern nint GetApplicationEventTarget();
    [DllImport(Carbon)] private static extern int InstallEventHandler(nint target,HotkeyHandler callback,nuint count,[In] EventTypeSpec[] types,nint user,out nint handler);
    [DllImport(Carbon)] private static extern int RemoveEventHandler(nint handler);
    [DllImport(Carbon)] private static extern int RegisterEventHotKey(uint key,uint modifiers,EventHotKeyId id,nint target,uint options,out nint reference);
    [DllImport(Carbon)] private static extern int UnregisterEventHotKey(nint reference);
    [DllImport(Carbon)] private static extern uint GetEventKind(nint value);
    [DllImport(Carbon)] private static extern int GetEventParameter(nint value,uint name,uint type,nint actualType,nuint bytes,out nuint actualSize,out EventHotKeyId result);
    [DllImport(Carbon)] private static extern nint TISCopyCurrentASCIICapableKeyboardLayoutInputSource();
    [DllImport(Carbon)] private static extern nint TISGetInputSourceProperty(nint source,nint property);
    [DllImport(Carbon)] private static extern byte LMGetKbdType();
    [DllImport(Carbon)] private static extern int UCKeyTranslate(nint layout,ushort key,ushort action,uint modifiers,uint keyboardType,uint options,ref uint deadState,nuint maxLength,out nuint length,[Out,MarshalAs(UnmanagedType.LPArray,ArraySubType=UnmanagedType.U2,SizeParamIndex=7)] char[] text);
    [DllImport(CoreFoundation)] private static extern nint CFDataCreateCopy(nint allocator,nint data);
    [DllImport(CoreFoundation)] private static extern nint CFDataGetLength(nint data);
    [DllImport(CoreFoundation)] private static extern nint CFDataGetBytePtr(nint data);
    [DllImport(CoreFoundation)] private static extern byte CFEqual(nint first,nint second);
    [DllImport(CoreFoundation)] private static extern void CFRelease(nint value);
}
