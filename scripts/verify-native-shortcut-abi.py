#!/usr/bin/env python3
"""Check native hotkey interop declarations against Apple SDK LP64/Win32 ABI facts.
This is a source/metadata check, not a target-system compilation or keyboard test.
"""
import argparse,hashlib,json,pathlib,re
ROOT=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',default='artifacts/desktop/native-shortcut-abi.json');a=p.parse_args()
mac=ROOT/'src/Magpie.Desktop/Core/MacShortcutApi.cs';win=ROOT/'src/Magpie.Desktop/Core/WindowsShortcutApi.cs'
m=mac.read_text();w=win.read_text();checks=[]
def check(label,condition):
 assert condition,label
 checks.append(label)
# Apple SDK: MacTypes.h typedefs unsigned long UniCharCount, ByteCount, ItemCount;
# UInt32 and OptionBits stay 32-bit; Events.h uses UInt8 LMGetKbdType.
check('Carbon UCKeyTranslate LP64 count and UTF16 output',bool(re.search(r'extern int UCKeyTranslate\(nint layout,ushort key,ushort action,uint modifiers,uint keyboardType,uint options,ref uint deadState,nuint maxLength,out nuint length,\[Out,MarshalAs\(UnmanagedType.LPArray,ArraySubType=UnmanagedType.U2,SizeParamIndex=7\)\] char\[\] text\)',m)))
check('Carbon GetEventParameter LP64 ByteCount',bool(re.search(r'extern int GetEventParameter\(nint value,uint name,uint type,nint actualType,nuint bytes,out nuint actualSize,out EventHotKeyId result\)',m)))
check('Carbon InstallEventHandler LP64 ItemCount',bool(re.search(r'extern int InstallEventHandler\(nint target,HotkeyHandler callback,nuint count,\[In\] EventTypeSpec\[\] types,nint user,out nint handler\)',m)))
check('Carbon hotkey identity and event structs have two UInt32 fields','record struct EventHotKeyId(uint Signature,uint Id)' in m and 'record struct EventTypeSpec(uint EventClass,uint EventKind)' in m)
check('Carbon event callbacks are Cdecl','[UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int HotkeyHandler(nint next,nint value,nint user)' in m)
check('Carbon pressed/released kinds match SDK 5 and 6','new(0x6B657962,5),new(0x6B657962,6)' in m and 'kind==5' in m)
check('Carbon keyboard type is UInt8','extern byte LMGetKbdType()' in m)
check('CoreFoundation CFIndex is pointer-sized','extern nint CFDataGetLength(nint data)' in m)
check('Windows Unicode character mapping','[DllImport("user32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] private static extern short VkKeyScanExW(char character,nint layout)' in w)
check('Windows BOOL result and last-error preservation',bool(re.search(r'\[DllImport\("user32.dll",SetLastError=true\)\] \[return:MarshalAs\(UnmanagedType.Bool\)\] private static extern bool RegisterHotKey\(nint hwnd,int id,uint modifiers,uint key\)',w)))
report={'passed':True,'target_runtime_tested':False,'checks':checks,'sources_sha256':{str(f.relative_to(ROOT)):hashlib.sha256(f.read_bytes()).hexdigest() for f in [mac,win]},'reference':'Apple SDK CarbonEventsCore.h/CarbonEvents.h/Events.h/MacTypes.h/UnicodeUtilities.h; Microsoft RegisterHotKey/VkKeyScanExW declarations'}
out=(ROOT/a.output).resolve();out.parent.mkdir(parents=True,exist_ok=True);out.write_text(json.dumps(report,indent=2)+'\n')
print(f'PASS: {len(checks)} native shortcut ABI declarations; target runtime remains unverified.')
