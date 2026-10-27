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
    report['icd']={'manifest':str(manifest),'manifest_sha256':hashlib.sha256(manifest.read_bytes()).hexdigest(),
                   'library':str(library),'library_sha256':hashlib.sha256(library.read_bytes()).hexdigest()}
    report['app_sha256']=hashlib.sha256(app.read_bytes()).hexdigest();report['fixture_sha256']=hashlib.sha256(fixture.read_bytes()).hexdigest();save()
    with tempfile.TemporaryDirectory(prefix='magpie-windows-production-') as temporary:
        private=Path(temporary);(private/'local').mkdir();(private/'roaming').mkdir();(private/'cache').mkdir()
        env=dict(os.environ,MAGPIE_HOSTED_WINDOWS_LIFECYCLE='1',MAGPIE_HOSTED_WINDOWS_CAPTURE_FIXTURE='1',
                 VK_DRIVER_FILES=str(manifest),VK_ICD_FILENAMES=str(manifest),
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
            if case['State']!='passed' or not all(case[key] for key in ('NativeOutputHidden','ForegroundPreserved','IdleGpuPreserved','SourceExitedCleanly')):
                raise RuntimeError('Missing source/native-output/focus/idle checks: '+str(case))
            if not (case['FirstSequence']<case['RestoredSequence']<case['ShownSequence']):raise RuntimeError('Restoration did not advance real capture sequences.')
            if not case['SourcePid'] or not case['SourceWindow'] or 'swiftshader' not in case['Device'].lower():raise RuntimeError('Missing actual source/software Vulkan identity.')
            if not case['SourceClosedWithoutError'] and not case['Fullscreen']:raise RuntimeError('Source closure failed.')
            if not case['StopWhileHidden'] and case['Fullscreen']:raise RuntimeError('Hidden stop failed.')
            screenshot=(folder/case['Screenshot']).resolve()
            if not screenshot.is_relative_to(folder.resolve()) or not screenshot.is_file() or screenshot.read_bytes()[:8]!=b'\x89PNG\r\n\x1a\n':raise RuntimeError('Missing actual screenshot artifact.')
        report.update(passed=True,production_capture_loop=True,software_vulkan_tested=True,cases=cases,
                      wgc_runtime_tested=all(c['State']=='passed' for c in cases if c['Backend']=='GraphicsCapture'))
except subprocess.TimeoutExpired as error:
    def decode(value):return value.decode('utf-8',errors='replace') if isinstance(value,bytes) else value or ''
    (folder/'application.log').write_text(decode(error.stdout)+decode(error.stderr),encoding='utf-8')
    report['failure']='Production lifecycle timed out; parent-monitored source processes must exit with the app.';save();raise
except Exception as error:
    report['failure']=str(error);save();raise
finally:save()
print('PASS: actual Windows production source/capture/software-Vulkan/Avalonia restore and focus matrix; hardware Vulkan and input delivery remain separate unverified gates.')
