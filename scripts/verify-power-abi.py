#!/usr/bin/env python3
"""Verify power P/Invoke contracts; optional real Windows SDK-header compilation."""
import argparse,hashlib,json,pathlib,re,subprocess
ROOT=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=pathlib.Path,required=True);p.add_argument('--windows-cxx',type=pathlib.Path);args=p.parse_args()
source=ROOT/'src/Magpie.Desktop/Core/NativeScreenAwake.cs';s=source.read_text();checks=[]
def check(name,value):
 assert value,name
 checks.append(name)
check('Windows REASON_CONTEXT 32-byte explicit union and offsets',all(v in s for v in ['LayoutKind.Explicit,Size=32','[FieldOffset(0)] public uint Version','[FieldOffset(4)] public uint Flags','[FieldOffset(8)] public nint SimpleReasonString']))
check('Windows simple reason flags and UTF16 lifetime',all(v in s for v in ['Marshal.StringToHGlobalUni(reason)','Version=0,Flags=1,SimpleReasonString=text','finally { Marshal.FreeHGlobal(text); }']))
check('Windows request handle is pointer-sized with last-error capture', '[DllImport("kernel32.dll",SetLastError=true)] private static extern nint PowerCreateRequest(ref WindowsPowerReasonContext context)' in s)
for name in ['PowerSetRequest','PowerClearRequest','CloseHandle']:
 check('Windows '+name+' BOOL32 and last error',bool(re.search(r'\[DllImport\("kernel32.dll",SetLastError=true\)\] \[return:MarshalAs\(UnmanagedType.Bool\)\] private static extern bool '+name+r'\(nint request',s)))
check('macOS IOReturn and assertion ID are 32-bit', 'extern int IOPMAssertionCreateWithName(nint type,uint level,nint name,out uint assertion)' in s and 'extern int IOPMAssertionRelease(uint assertion)' in s)
check('macOS CFString refs are pointer-sized, UTF8 and released',all(v in s for v in ['extern nint CFStringCreateWithCString(nint allocator,[MarshalAs(UnmanagedType.LPUTF8Str)] string text,uint encoding)','extern void CFRelease(nint value)','0x08000100','if(name!=0)CFRelease(name);if(type!=0)CFRelease(type)']))
check('macOS idle display assertion and enabled level follow Apple SDK', '"PreventUserIdleDisplaySleep"' in s and 'IOPMAssertionCreateWithName(type,255,name,out uint id)' in s)
report=dict(passed=True,target_runtime_tested=False,checks=checks,source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),windows_sdk_header_compilation=False)
args.output.parent.mkdir(parents=True,exist_ok=True)
if args.windows_cxx:
 probe=args.output.with_suffix('.cpp');probe.write_text('''#define _WIN32_WINNT 0x0A00
#include <windows.h>
#include <cstddef>
#include <type_traits>
static_assert(sizeof(REASON_CONTEXT)==32);
static_assert(offsetof(REASON_CONTEXT,Version)==0);
static_assert(offsetof(REASON_CONTEXT,Flags)==4);
static_assert(offsetof(REASON_CONTEXT,Reason.SimpleReasonString)==8);
static_assert(POWER_REQUEST_CONTEXT_VERSION==0);
static_assert(POWER_REQUEST_CONTEXT_SIMPLE_STRING==1);
static_assert(PowerRequestDisplayRequired==0 && PowerRequestSystemRequired==1);
static_assert(std::is_same_v<decltype(&PowerCreateRequest),HANDLE (WINAPI*)(PREASON_CONTEXT)>);
static_assert(std::is_same_v<decltype(&PowerSetRequest),BOOL (WINAPI*)(HANDLE,POWER_REQUEST_TYPE)>);
static_assert(std::is_same_v<decltype(&PowerClearRequest),BOOL (WINAPI*)(HANDLE,POWER_REQUEST_TYPE)>);
static_assert(std::is_same_v<decltype(&CloseHandle),BOOL (WINAPI*)(HANDLE)>);
''')
 run=subprocess.run([str(args.windows_cxx.resolve()),'-std=c++20','-fsyntax-only','-Wall','-Wextra','-Werror',str(probe.resolve())],capture_output=True,text=True)
 if run.returncode:raise RuntimeError(run.stdout+run.stderr)
 report['windows_sdk_header_compilation']=True
args.output.write_text(json.dumps(report,indent=2)+'\n');print('PASS: '+str(len(checks))+' power ABI checks'+(' and Windows SDK-header compilation' if args.windows_cxx else '')+'; target Windows/macOS runtime remains unverified.')
