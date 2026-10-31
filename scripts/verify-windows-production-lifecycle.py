#!/usr/bin/env python3
"""Actual capture/Vulkan/Avalonia lifecycle on disposable hosted Windows only."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import struct
import sys
import tempfile
from contextlib import contextmanager

ROOT=Path(__file__).resolve().parents[1]
if sys.platform!='win32' or os.environ.get('GITHUB_ACTIONS')!='true' or os.environ.get('RUNNER_ENVIRONMENT')!='github-hosted':
    raise SystemExit('Windows production GUI testing is restricted to disposable GitHub-hosted Windows runners; no user-desktop fallback.')
folder=ROOT/'artifacts/platform-build-checks/win-x64/production-lifecycle';folder.mkdir(parents=True,exist_ok=True)
app=ROOT/'artifacts/desktop/win-x64/Magpie.Desktop.exe'
fixture=ROOT/'artifacts/native/win-x64/Release/Release/windows_capture_fixture.exe'
report={'passed':False,'commit':os.environ.get('GITHUB_SHA'),'disposable_hosted_runner':True,'synthetic_source':True,
        'production_capture_loop':False,'software_vulkan_tested':False,'hardware_vulkan_tested':False,
        'native_input_delivery_tested':False,'full_interaction_parity':False}
def save(): (folder/'results.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
save()

@contextmanager
def driver_registration(manifest):
    # Elevated Vulkan loaders ignore driver override environment variables.
    # This standard registry registration exists only in this disposable VM,
    # and restores the exact previous value without touching other drivers.
    import ctypes
    from ctypes import wintypes
    import winreg
    kernel=ctypes.WinDLL('kernel32',use_last_error=True);security=ctypes.WinDLL('advapi32',use_last_error=True)
    kernel.GetCurrentProcess.restype=wintypes.HANDLE
    kernel.CloseHandle.argtypes=[wintypes.HANDLE];kernel.CloseHandle.restype=wintypes.BOOL
    security.OpenProcessToken.argtypes=[wintypes.HANDLE,wintypes.DWORD,ctypes.POINTER(wintypes.HANDLE)];security.OpenProcessToken.restype=wintypes.BOOL
    security.GetTokenInformation.argtypes=[wintypes.HANDLE,ctypes.c_uint,ctypes.c_void_p,wintypes.DWORD,ctypes.POINTER(wintypes.DWORD)];security.GetTokenInformation.restype=wintypes.BOOL
    token=wintypes.HANDLE();elevated=wintypes.DWORD();returned=wintypes.DWORD()
    if not security.OpenProcessToken(kernel.GetCurrentProcess(),8,ctypes.byref(token)):raise ctypes.WinError(ctypes.get_last_error())
    try:
        if not security.GetTokenInformation(token,20,ctypes.byref(elevated),ctypes.sizeof(elevated),ctypes.byref(returned)):raise ctypes.WinError(ctypes.get_last_error())
    finally:kernel.CloseHandle(token)
    report['elevated_token']=bool(elevated.value)
    if not elevated.value:
        report['driver_registration']='process environment';save();yield;return
    path=r'SOFTWARE\Khronos\Vulkan\Drivers';access=winreg.KEY_QUERY_VALUE|winreg.KEY_SET_VALUE|winreg.KEY_WOW64_64KEY
    created=False
    try:
        with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE,path,0,winreg.KEY_QUERY_VALUE|winreg.KEY_WOW64_64KEY):pass
    except FileNotFoundError:created=True
    with winreg.CreateKeyEx(winreg.HKEY_LOCAL_MACHINE,path,0,access) as key:
        try:previous=winreg.QueryValueEx(key,str(manifest))
        except FileNotFoundError:previous=None
        winreg.SetValueEx(key,str(manifest),0,winreg.REG_DWORD,0)
        report.update(driver_registration='temporary HKLM x64 ICD',registry_value_restored=False);save()
        try:yield
        finally:
            if previous is None:winreg.DeleteValue(key,str(manifest))
            else:winreg.SetValueEx(key,str(manifest),0,previous[1],previous[0])
            report['registry_value_restored']=True;save()
    if created:
        try:winreg.DeleteKeyEx(winreg.HKEY_LOCAL_MACHINE,path,access=winreg.KEY_WOW64_64KEY)
        except OSError:pass # preserve a key if another legitimate value appeared

try:
    if not app.is_file() or not fixture.is_file():raise RuntimeError('Missing built AOT app or source fixture.')
    # Read the runner image's installed Chromium driver; never launch a browser,
    # change its files or ship this test-only dependency with the application.
    roots=[Path(r'C:\Program Files\Google\Chrome\Application'),Path(r'C:\Program Files (x86)\Google\Chrome\Application'),
           Path(r'C:\Program Files (x86)\Microsoft\Edge\Application'),Path(r'C:\Program Files\Microsoft\Edge\Application')]
    found=[]
    for root in roots:
        if root.is_dir():
            for manifest in root.rglob('vk_swiftshader_icd.json'):
                content=json.loads(manifest.read_text(encoding='utf-8'));library=content['ICD']['library_path']
                library=Path(library) if Path(library).is_absolute() else manifest.parent/library
                library=library.resolve()
                if library.is_file() and library.name.lower()=='vk_swiftshader.dll' and library.is_relative_to(root.resolve()):
                    image=library.read_bytes()
                    if len(image)<64 or image[:2]!=b'MZ':continue
                    header=struct.unpack_from('<I',image,0x3c)[0]
                    if header+6<=len(image) and image[header:header+4]==b'PE\0\0' and struct.unpack_from('<H',image,header+4)[0]==0x8664:
                        found.append((manifest.resolve(),library))
    if not found:raise RuntimeError('Hosted runner has no installed SwiftShader Vulkan ICD in Chrome/Edge; required test cannot run.')
    manifest,library=sorted(found,key=lambda item:str(item[0]))[-1]
    report['icd']={'manifest_document':json.loads(manifest.read_text(encoding='utf-8')),'manifest':str(manifest),'manifest_sha256':hashlib.sha256(manifest.read_bytes()).hexdigest(),
                   'library':str(library),'library_sha256':hashlib.sha256(library.read_bytes()).hexdigest()}
    report['app_sha256']=hashlib.sha256(app.read_bytes()).hexdigest();report['fixture_sha256']=hashlib.sha256(fixture.read_bytes()).hexdigest();save()
    with driver_registration(manifest),tempfile.TemporaryDirectory(prefix='magpie-windows-production-') as temporary:
        private=Path(temporary);(private/'local').mkdir();(private/'roaming').mkdir();(private/'cache').mkdir()
        env=dict(os.environ,MAGPIE_HOSTED_WINDOWS_LIFECYCLE='1',MAGPIE_HOSTED_WINDOWS_CAPTURE_FIXTURE='1',
                 VK_DRIVER_FILES=str(manifest),VK_ICD_FILENAMES=str(manifest),VK_LOADER_DEBUG='all',
                 LOCALAPPDATA=str(private/'local'),APPDATA=str(private/'roaming'),XDG_CONFIG_HOME=str(private/'config'),
                 XDG_CACHE_HOME=str(private/'cache'),XDG_DATA_HOME=str(private/'data'))
        for name in ('DISPLAY','WAYLAND_DISPLAY','WAYLAND_SOCKET','DBUS_SESSION_BUS_ADDRESS'):env.pop(name,None)
        result=subprocess.run([str(app),'--windows-source-lifecycle-test',str(fixture),str(folder)],cwd=ROOT,env=env,
                              text=True,encoding='utf-8',errors='replace',capture_output=True,timeout=240)
        (folder/'application.log').write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout,flush=True)
        if result.returncode:raise RuntimeError('Production app exited '+str(result.returncode)+': '+result.stderr[-6000:])
        if 'WINDOWS PRODUCTION LIFECYCLE PASS:' not in result.stdout or 'SMOKE PASS:' not in result.stdout:raise RuntimeError('Missing actual production lifecycle completion marker.')
        actual=json.loads((folder/'production.json').read_text(encoding='utf-8'))
        cases=actual['Cases'];expected={(backend,full) for backend in ('Gdi','GraphicsCapture') for full in (False,True)}
        if not actual['Passed'] or not actual['SoftwareVulkan'] or actual['MinimumFrameRate']!=30 or len(cases)!=4 or {(c['Backend'],c['Fullscreen']) for c in cases}!=expected:
            raise RuntimeError('Incomplete production lifecycle evidence.')
        for case in cases:
            if case['State']=='unavailable' and case['Backend']=='GraphicsCapture':continue
            if case['State']!='passed' or not all(case[key] for key in ('NativeOutputHidden','ForegroundPreserved','InitialForegroundPreserved','IdleGpuPreserved','SourceExitedCleanly','TopmostPreferenceHonored','SourceTopmostInherited','OtherForegroundPreserved','LayeredNativeMousePassed')) or case['NativeKeyboardChecks']!=3 or case['NativeOutputClickChecks']!=3 or case['ResumeHeldHiddenChecks']!=2:
                raise RuntimeError('Missing source/native-output/focus/idle checks: '+str(case))
            if not case['LayeredRepaintPassed'] or case['LayeredScreenPixel']!=(211|(47<<8)|(173<<16)):raise RuntimeError('Layered native output lost compositor pixels or stopped repainting.')
            if not all(case[key] for key in ('ScaledNativeMousePassed','NativeInvertCursorPassed','NativeCursorAutoHidePassed','NativePointerCleanupPassed')):raise RuntimeError('Scaled native pointer/cursor/idle-hide/cleanup acceptance is missing.')
            if case.get('NativeAutomaticChecks')!=3:raise RuntimeError('Actual foreground/rule/start, output mode and repeated automatic poll checks are missing.')
            if case.get('NativeCursorScaleChecks')!=4:raise RuntimeError('Actual cursor preset/custom/source-scale/interpolation checks are missing.')
            if case.get('NativeCursorSpeedChecks')!=4 or case.get('NativeOriginalPointerSpeed',0)<1 or case['NativeOriginalPointerSpeed']!=case.get('NativeRestoredPointerSpeed'):raise RuntimeError('Native pointer speed compensation/disable/reenable/restore acceptance is missing.')
            if case['NativeInvertCursorPixel']!=(232|(188<<8)|(146<<16)):raise RuntimeError('Actual XOR cursor compositor pixel is missing.')
            if not all(case[key] for key in ('NativeMonitorClipPassed','NativeToolbarExitPassed','NativeFullscreenBoundsPassed')):raise RuntimeError('Native monitor/toolbar/fullscreen coverage is missing.')
            if case['NativeMonitorCount']<1 or (case['Fullscreen'] and case['NativeMonitorCount']==1 and case['NativeBoundaryClampChecks']!=1):raise RuntimeError('Single-monitor native boundary clamp was not exercised.')
            if not (case['FirstSequence']<case['RestoredSequence']<case['ShownSequence']):raise RuntimeError('Restoration did not advance real capture sequences.')
            if not case['SourcePid'] or not case['SourceWindow'] or 'swiftshader' not in case['Device'].lower():raise RuntimeError('Missing actual source/software Vulkan identity.')
            if not case['SourceClosedWithoutError'] and not case['Fullscreen']:raise RuntimeError('Source closure failed.')
            if not case['StopWhileHidden'] and case['Fullscreen']:raise RuntimeError('Hidden stop failed.')
            screenshot=(folder/case['Screenshot']).resolve()
            if not screenshot.is_relative_to(folder.resolve()) or not screenshot.is_file() or screenshot.read_bytes()[:8]!=b'\x89PNG\r\n\x1a\n':raise RuntimeError('Missing actual screenshot artifact.')
        report.update(passed=True,production_capture_loop=True,software_vulkan_tested=True,native_source_keyboard_tested=True,
                      layered_cross_process_mouse_tested=True,scaled_pointer_mapping_tested=True,native_invert_cursor_tested=True,cases=cases,
                      wgc_runtime_tested=all(c['State']=='passed' for c in cases if c['Backend']=='GraphicsCapture'))
except subprocess.TimeoutExpired as error:
    def decode(value):return value.decode('utf-8',errors='replace') if isinstance(value,bytes) else value or ''
    (folder/'application.log').write_text(decode(error.stdout)+decode(error.stderr),encoding='utf-8')
    report['failure']='Production lifecycle timed out; parent-monitored source processes must exit with the app.';save();raise
except Exception as error:
    report['failure']=str(error);save();raise
finally:save()
print('PASS: Windows production lifecycle, keyboard, fresh layered rendering, scaled native mouse and actual XOR software cursor; broader real-app, IME, game, multimonitor and hardware Vulkan coverage remains incomplete.')
