#!/usr/bin/env python3
"""Opt-in actual Win32 GDI/WGC fixture on disposable GitHub-hosted runners only."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT=Path(__file__).resolve().parents[1]
if sys.platform!='win32' or os.environ.get('GITHUB_ACTIONS')!='true' or os.environ.get('RUNNER_ENVIRONMENT')!='github-hosted':
    raise SystemExit('This visible source-window test is restricted to disposable GitHub-hosted Windows runners; no user-desktop fallback.')
binary=ROOT/'artifacts/native/win-x64/Release/Release/windows_capture_fixture.exe'
if not binary.is_file():raise SystemExit('Missing native Windows capture fixture: '+str(binary))
folder=ROOT/'artifacts/platform-build-checks/win-x64';folder.mkdir(parents=True,exist_ok=True)
env=dict(os.environ,MAGPIE_HOSTED_WINDOWS_CAPTURE_FIXTURE='1')
report={'passed':False,'commit':os.environ.get('GITHUB_SHA'),'run_id':os.environ.get('GITHUB_RUN_ID'),'disposable_hosted_runner':True,'capture_tested':'independent synthetic Win32 source process via actual GDI',
        'gpu_tested':False,'input_injected':False,'lifetime_event_injected':True,'real_application_compatibility_tested':False,'full_interaction_parity':False,
        'fixture_sha256':hashlib.sha256(binary.read_bytes()).hexdigest()}
try:
    result=subprocess.run([str(binary)],cwd=ROOT,env=env,text=True,encoding='utf-8',errors='replace',capture_output=True,timeout=45)
    output=result.stdout+result.stderr
    (folder/'windows-capture-fixture.log').write_text(output,encoding='utf-8')
    required=('WINDOWS GDI FIXTURE PASS:','actual out-of-context lifetime WinEvent','matching-tuple permanent rejection','independent recapture','waiting-buffer preservation','continuous grow/shrink/return','hide/show suspension without session failure','repeated hide/show fresh-frame guard','live source-state ABI','WINDOWS CURSOR INTERPOLATION PASS:')
    recovery='WINDOWS CAPTURE RECOVERY: backend=GDI title=0 cycles=16 passed'
    recovered=result.returncode==0 and recovery in result.stdout
    report.update(exit_code=result.returncode,passed=recovered and all(marker in result.stdout for marker in required),recovery_cycles_passed=16 if recovered else 0)
    print(output,flush=True)
except subprocess.TimeoutExpired as failure:
    def decode(value):return value.decode(errors='replace') if isinstance(value,bytes) else value or ''
    (folder/'windows-capture-fixture.log').write_text(decode(failure.stdout)+decode(failure.stderr),encoding='utf-8')
    report['failure']='timeout'
(folder/'windows-capture-fixture.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
if not report['passed']:raise SystemExit('Windows capture fixture did not produce successful actual source-window evidence.')

# A server runner may lack WGC. Only the native capability probe may skip this
# run; startup, device, pixel, geometry or lifecycle failures are failures.
wgc=dict(report,passed=False,capture_tested='independent synthetic Win32 source process via actual Windows Graphics Capture',gpu_tested=False,
         wgc_readback_tested=False,status='pending',exit_code=None,recovery_cycles_passed=0)
try:
    result=subprocess.run([str(binary),'--wgc'],cwd=ROOT,env=env,text=True,encoding='utf-8',errors='replace',capture_output=True,timeout=90)
    output=result.stdout+result.stderr
    (folder/'windows-wgc-fixture.log').write_text(output,encoding='utf-8')
    unavailable=result.returncode==77 and 'WINDOWS WGC UNAVAILABLE:' in result.stdout
    recovered=result.returncode==0 and 'WINDOWS CAPTURE RECOVERY: backend=WGC title=0 cycles=16 passed' in result.stdout
    passed=recovered and all(marker in result.stdout for marker in ('WINDOWS WGC FIXTURE PASS:',*required[1:]))
    wgc.update(exit_code=result.returncode,passed=passed,wgc_readback_tested=passed,recovery_cycles_passed=16 if recovered else 0,status='unavailable' if unavailable else 'passed' if passed else 'failed')
    print(output,flush=True)
except subprocess.TimeoutExpired as failure:
    def decode(value):return value.decode(errors='replace') if isinstance(value,bytes) else value or ''
    (folder/'windows-wgc-fixture.log').write_text(decode(failure.stdout)+decode(failure.stderr),encoding='utf-8')
    wgc.update(status='failed',failure='timeout')
(folder/'windows-wgc-fixture.json').write_text(json.dumps(wgc,indent=2)+'\n',encoding='utf-8')
if wgc['status']=='failed':raise SystemExit('Available Windows Graphics Capture failed the actual source-window fixture.')

caption=dict(wgc,passed=False,status='pending',title_bar_tested=False,wgc_readback_tested=False,exit_code=None,recovery_cycles_passed=0)
try:
    result=subprocess.run([str(binary),'--wgc-title-bar'],cwd=ROOT,env=env,text=True,encoding='utf-8',errors='replace',capture_output=True,timeout=90)
    output=result.stdout+result.stderr
    (folder/'windows-wgc-title-bar-fixture.log').write_text(output,encoding='utf-8')
    unavailable=result.returncode==77 and 'WINDOWS WGC UNAVAILABLE:' in result.stdout
    recovered=result.returncode==0 and 'WINDOWS CAPTURE RECOVERY: backend=WGC title=1 cycles=16 passed' in result.stdout
    passed=recovered and all(marker in result.stdout for marker in ('WINDOWS WGC TITLE BAR PASS:','WINDOWS WGC FIXTURE PASS:',*required[1:]))
    caption.update(exit_code=result.returncode,passed=passed,title_bar_tested=passed,wgc_readback_tested=passed,recovery_cycles_passed=16 if recovered else 0,status='unavailable' if unavailable else 'passed' if passed else 'failed')
    print(output,flush=True)
except subprocess.TimeoutExpired as failure:
    def decode(value):return value.decode(errors='replace') if isinstance(value,bytes) else value or ''
    (folder/'windows-wgc-title-bar-fixture.log').write_text(decode(failure.stdout)+decode(failure.stderr),encoding='utf-8')
    caption.update(status='failed',failure='timeout')
(folder/'windows-wgc-title-bar-fixture.json').write_text(json.dumps(caption,indent=2)+'\n',encoding='utf-8')
if caption['status']=='failed':raise SystemExit('Available WGC failed actual title bar capture.')
