#!/usr/bin/env python3
"""Run UI checks in a private headless Wayland compositor, never on the user's display."""
import os
import argparse
import importlib.util
import json
import html
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parents[1]
# Use a fresh compositor per case: kiosk-shell is a test fixture, not a persistent desktop session.
selection = argparse.ArgumentParser(add_help=False)
selection.add_argument('--case', action='append')
selection.add_argument('--output', default='artifacts/desktop/review')
selected, remaining = selection.parse_known_args()
spec = importlib.util.spec_from_file_location('ui_checks', ROOT/'scripts/verify-desktop-ui.py')
checks = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checks)
cases = selected.case or [case for case in checks.CASES if case!="ShortcutPortal"]
if len(cases) > 1:
    destination = (ROOT/selected.output).resolve()
    destination.mkdir(parents=True,exist_ok=True)
    results = []
    for case in cases:
        result = subprocess.run([sys.executable, str(Path(__file__).resolve()), *remaining, '--output', str(destination), '--case', case], cwd=ROOT)
        if result.returncode:
            (destination/'partial-results.json').write_text(json.dumps(results,indent=2))
            raise SystemExit(result.returncode)
        results.extend(json.loads((destination/'results.json').read_text()))
    (destination/'results.json').write_text(json.dumps(results,indent=2))
    gallery='<!doctype html><meta charset="utf-8"><title>Isolated Wayland UI checks</title><style>body{font:16px system-ui;margin:24px}img{width:min(100%,1064px)}</style>'
    for result in results: gallery += '<h2>'+html.escape(result['case'])+'</h2><img src="'+html.escape(result['image'])+'">'
    (destination/'index.html').write_text(gallery)
    print(str(len(results))+' UI cases passed, each in its own private headless Wayland session.',flush=True)
    raise SystemExit(0)
local = ROOT / 'artifacts/test-tools/weston/root'
weston = local / 'usr/bin/weston'
if not weston.exists():
    resolved = shutil.which('weston')
    if not resolved:
        raise SystemExit('Weston is required. See docs/native-core-migration.md; no desktop fallback is permitted.')
    weston = Path(resolved)
with tempfile.TemporaryDirectory(prefix='magpie-wayland-') as temporary:
    runtime = Path(temporary)
    runtime.chmod(0o700)
    env = dict(os.environ)
    for key in ('DISPLAY','WAYLAND_DISPLAY','WAYLAND_SOCKET','XDG_SESSION_TYPE','DBUS_SESSION_BUS_ADDRESS'):
        env.pop(key, None)
    if cases==['ShortcutPortal']:
        if os.environ.get('MAGPIE_SHORTCUT_PRIVATE_SESSION')!='1' or not os.environ.get('DBUS_SESSION_BUS_ADDRESS'):
            raise SystemExit('ShortcutPortal requires the private shortcut protocol runner.')
        env['DBUS_SESSION_BUS_ADDRESS']=os.environ['DBUS_SESSION_BUS_ADDRESS']
    env.update(XDG_RUNTIME_DIR=str(runtime), WAYLAND_DISPLAY='magpie-test', XDG_SESSION_TYPE='wayland')
    # Keep both test data and desktop integration away from the user's session.
    # Exercise the actual Vulkan loader failure path only in this isolated test process.
    if cases==['ProfileGraphicsQueryFailure']:
        env['VK_DRIVER_FILES']=str(runtime/'missing-vulkan-driver.json')
        env.pop('VK_ICD_FILENAMES',None)
    env.update(XDG_CONFIG_HOME=str(runtime/'config'), XDG_DATA_HOME=str(runtime/'data'), XDG_CACHE_HOME=str(runtime/'cache'))
    args = [str(weston),'--no-config','--idle-time=0','--socket=magpie-test','--width=1920','--height=1200','--renderer=pixman']
    if local in weston.parents:
        lib = local/'usr/lib/x86_64-linux-gnu'
        env['LD_LIBRARY_PATH'] = str(lib)+':'+str(lib/'weston')+(':'+env['LD_LIBRARY_PATH'] if env.get('LD_LIBRARY_PATH') else '')
        env['WESTON_MODULE_MAP'] = ';'.join(p.name+'='+str(p) for p in lib.rglob('*.so'))
        args += ['--backend=headless','--shell=kiosk-shell.so']
    else:
        args += ['--backend=headless','--shell=kiosk-shell.so']
    # A kiosk forces independent toplevels fullscreen. Output sizing/lifetime
    # checks require a normal private window manager with independent windows.
    if any(any(word in case for word in ('Output','Window','Cropping','Tray','PresentationLifecycle')) for case in cases):
        args=[value for value in args if value not in ('--no-config','--shell=kiosk-shell.so')]
        config=runtime/'weston.ini'
        client=local/'usr/libexec/weston-desktop-shell'
        config.write_text('[shell]\nlocking=false\npanel-position=none\n'+('client='+str(client)+'\n' if local in weston.parents else ''))
        args+=['--shell=desktop-shell.so','--config='+str(config)]
    logfile = ROOT/('artifacts/desktop/isolated-weston-'+runtime.name+'.log')
    logfile.parent.mkdir(parents=True,exist_ok=True)
    with logfile.open('w') as log:
        server = subprocess.Popen(args,env=env,stdout=log,stderr=log)
        try:
            deadline=time.monotonic()+15
            while not (runtime/'magpie-test').exists():
                if server.poll() is not None or time.monotonic()>deadline:
                    raise SystemExit('Headless Wayland startup failed:\n'+logfile.read_text())
                time.sleep(.05)
            result=subprocess.run([sys.executable,'scripts/verify-desktop-ui.py',*sys.argv[1:]],cwd=ROOT,env=env)
            if result.returncode:raise SystemExit(result.returncode)
        finally:
            if server.poll() is None:
                server.terminate()
                try:server.wait(timeout=10)
                except subprocess.TimeoutExpired:server.kill();server.wait(timeout=5)
