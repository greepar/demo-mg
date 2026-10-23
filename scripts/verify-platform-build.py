#!/usr/bin/env python3
"""Native-host compiler, AOT/static packaging and non-GPU interaction checks; no UI/consent."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
import sys

for stream in (sys.stdout,sys.stderr):
    if hasattr(stream,'reconfigure'):stream.reconfigure(encoding='utf-8',errors='replace')

ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--rid',required=True);p.add_argument('--output',type=Path);args=p.parse_args()
host={'linux':'linux','win32':'win','darwin':'osx'}.get(sys.platform)
arch={'x86_64':'x64','AMD64':'x64','aarch64':'arm64','arm64':'arm64'}.get(platform.machine())
if args.rid!=f'{host}-{arch}':raise SystemExit(f'Expected native host {args.rid}, found {host}-{arch}.')
output=args.output.resolve() if args.output else ROOT/'artifacts/desktop'/args.rid
build=ROOT/'artifacts/native'/args.rid/'Release'
report_dir=ROOT/'artifacts/platform-build-checks'/args.rid;report_dir.mkdir(parents=True,exist_ok=True)
name='Magpie.Desktop.exe' if host=='win' else 'Magpie.Desktop'
commands=[
    [sys.executable,'scripts/verify-original-mode-fixture.py'],
    [sys.executable,'scripts/verify-precompiled-shaders.py'],
    ['dotnet','publish','src/Magpie.Desktop','-c','Release','-r',args.rid,'-o',str(output)],
    ['ctest','--test-dir',str(build),'-C','Release','--output-on-failure','--no-tests=error','-R','^(capture_resize_geometry|capture_frame_mailbox|capture_source_metadata)$'],
    [str(output/name),'--interaction-model-test'],
    [sys.executable,'scripts/verify-power-abi.py','--output',str(report_dir/'power-abi.json')],
]
report={'rid':args.rid,'passed':False,'state':'running','checks':[],'gpu_tested':False,'ui_tested':False,'capture_tested':False,'native_input_delivery_tested':False,'full_interaction_parity':False}
def save():
    (report_dir/'results.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
save();env=dict(os.environ,PYTHONUTF8='1',DOTNET_CLI_UI_LANGUAGE='en-US')
for key in ('DISPLAY','WAYLAND_DISPLAY','WAYLAND_SOCKET','DBUS_SESSION_BUS_ADDRESS'):env.pop(key,None)
for index,command in enumerate(commands):
    print('RUN: '+str(command),flush=True)
    result=subprocess.run(command,cwd=ROOT,env=env,text=True,encoding='utf-8',errors='replace',capture_output=True)
    (report_dir/f'{index}.log').write_text(result.stdout+result.stderr,encoding='utf-8')
    print(result.stdout,flush=True)
    report['checks'].append({'command':command,'exit_code':result.returncode});save()
    if not result.returncode and index==3 and '100% tests passed, 0 tests failed out of 3' not in result.stdout:
        result.returncode=1;result.stderr+='\nThe gate requires all three non-GPU native tests.\n'
    if not result.returncode and index==4 and 'INTERACTION MODEL PASS:' not in result.stdout:
        result.returncode=1;result.stderr+='\nThe executable did not report successful interaction-model execution.\n'
    if result.returncode:
        report['checks'][-1]['exit_code']=result.returncode
        report['state']='failed';save();print(result.stderr,file=sys.stderr);raise SystemExit(result.returncode)
files=sorted(p.name for p in output.iterdir() if p.is_file())
if files!=[name]:
    report['state']='failed';report['packaging_files']=files;save();raise SystemExit('Expected exactly one executable, got '+str(files))
if host=='osx':
    result=subprocess.run(['otool','-L',str(output/name)],capture_output=True,text=True)
    (report_dir/'native-dependencies.log').write_text(result.stdout+result.stderr,encoding='utf-8')
    if result.returncode or any(value in result.stdout.lower() for value in ('libmoltenvk','libmagpie_native','libskiasharp','libavalonianative')):
        report['state']='failed';save();raise SystemExit('macOS native dependencies must be embedded, not shipped as extra dynamic libraries.')
report.update(passed=True,state='complete',single_executable=True,app_sha256=hashlib.sha256((output/name).read_bytes()).hexdigest())
save();print('PASS: native SDK/compiler, AOT interaction models and single executable. GPU, actual input and UI remain separate unverified gates.',flush=True)
