#!/usr/bin/env python3
"""Native-host AOT/static-core verification. Never requests capture or opens UI."""
import argparse
import json
import os
from pathlib import Path
import platform
import subprocess
import sys

for stream in (sys.stdout,sys.stderr):
    if hasattr(stream,'reconfigure'):stream.reconfigure(encoding='utf-8',errors='replace')

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--rid',required=True,choices=['linux-x64','linux-arm64','win-x64','win-arm64','osx-x64','osx-arm64'])
args=parser.parse_args()
host={'linux':'linux','win32':'win','darwin':'osx'}.get(sys.platform)
arch={'x86_64':'x64','AMD64':'x64','aarch64':'arm64','arm64':'arm64'}.get(platform.machine())
if args.rid!=f'{host}-{arch}':raise SystemExit(f'Execute this gate on {args.rid}; current host is {host}-{arch}.')
build=ROOT/'artifacts/native'/args.rid/'Release'
output=ROOT/'artifacts/desktop'/args.rid
report_dir=ROOT/'artifacts/platform-checks'/args.rid
report_dir.mkdir(parents=True,exist_ok=True)
(report_dir/'results.json').write_text(json.dumps({'rid':args.rid,'passed':False,'state':'running','checks':[]},indent=2),encoding='utf-8')
commands=[
    [sys.executable,'scripts/verify-original-mode-fixture.py'],
    [sys.executable,'scripts/verify-precompiled-shaders.py'],
    ['dotnet','publish','src/Magpie.Desktop','-c','Release','-r',args.rid,'-o',str(output)],
    ['ctest','--test-dir',str(build),'-C','Release','--output-on-failure'],
    [str(output/('Magpie.Desktop.exe' if host=='win' else 'Magpie.Desktop')),'--core-smoke-test'],
    [sys.executable,'scripts/verify-power-abi.py','--output',str(report_dir/'power-abi.json')],
]
env=dict(os.environ,PYTHONUTF8='1',DOTNET_CLI_UI_LANGUAGE='en-US')
for key in ('DISPLAY','WAYLAND_DISPLAY','WAYLAND_SOCKET'):env.pop(key,None)
results=[]
for index,command in enumerate(commands):
    result=subprocess.run(command,cwd=ROOT,env=env,text=True,encoding='utf-8',errors='replace',capture_output=True)
    (report_dir/f'{index}.log').write_text(result.stdout+result.stderr,encoding='utf-8')
    print(result.stdout,flush=True)
    results.append({'command':command,'exit_code':result.returncode})
    if result.returncode:
        (report_dir/'results.json').write_text(json.dumps({'rid':args.rid,'passed':False,'checks':results},indent=2))
        print(result.stderr,file=sys.stderr);raise SystemExit(result.returncode)
files=[p.name for p in output.iterdir() if p.is_file()]
expected=['Magpie.Desktop.exe' if host=='win' else 'Magpie.Desktop']
if files!=expected:raise SystemExit(f'Single-executable packaging gate failed: {files}')
report={'rid':args.rid,'passed':True,'ui_opened':False,'capture_tested':False,'single_executable':True,'checks':results}
(report_dir/'results.json').write_text(json.dumps(report,indent=2))
print('PASS: '+args.rid+' native build, GPU regression, AOT ABI and single executable. Capture/UI are separate gates.')
