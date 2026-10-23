#!/usr/bin/env python3
"""Check FileManager1 ShowItems wire messages on a private bus; never open a file manager."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
from urllib.parse import unquote,urlsplit

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--app',default='artifacts/desktop/linux-x64/Magpie.Desktop')
parser.add_argument('--output',default='artifacts/desktop/configuration-reveal')
parser.add_argument('--private',action='store_true',help=argparse.SUPPRESS)
args=parser.parse_args()
if not args.private:
    with tempfile.TemporaryDirectory(prefix='magpie-config-bus-') as temporary:
        folder=Path(temporary);folder.chmod(0o700)
        config=folder/'bus.conf'
        config.write_text('<busconfig><type>session</type><listen>unix:tmpdir=/tmp</listen><auth>EXTERNAL</auth><policy context="default"><allow own="*"/><allow send_destination="*"/><allow receive_sender="*"/></policy></busconfig>')
        env=dict(os.environ,MAGPIE_CONFIG_PRIVATE_SESSION='1',XDG_RUNTIME_DIR=str(folder))
        for key in ('DISPLAY','WAYLAND_DISPLAY','WAYLAND_SOCKET','DBUS_SESSION_BUS_ADDRESS'):env.pop(key,None)
        raise SystemExit(subprocess.call(['dbus-run-session','--config-file='+str(config),'--','/usr/bin/python3',str(Path(__file__).resolve()),*sys.argv[1:],'--private'],cwd=ROOT,env=env))
assert os.environ.get('MAGPIE_CONFIG_PRIVATE_SESSION')=='1'
from gi.repository import Gio,GLib
connection=Gio.bus_get_sync(Gio.BusType.SESSION,None)
name='org.freedesktop.FileManager1'
connection.call_sync('org.freedesktop.DBus','/org/freedesktop/DBus','org.freedesktop.DBus','RequestName',GLib.Variant('(su)',(name,0)),None,Gio.DBusCallFlags.NONE,3000,None)
xml=Gio.DBusNodeInfo.new_for_xml('<node><interface name="org.freedesktop.FileManager1"><method name="ShowItems"><arg type="as" direction="in"/><arg type="s" direction="in"/></method></interface></node>').interfaces[0]
events=[]
mode='success'
def call(conn,sender,path,iface,method,parameters,invocation):
    events.append(parameters.unpack())
    if mode=='reject':invocation.return_dbus_error('org.freedesktop.DBus.Error.Failed','fixture launch rejection')
    else:invocation.return_value(GLib.Variant('()',()))
registered=connection.register_object('/org/freedesktop/FileManager1',xml,call,None,None)
loop=GLib.MainLoop();thread=threading.Thread(target=loop.run,daemon=True);thread.start()
app=(ROOT/args.app).resolve();output=(ROOT/args.output).resolve();output.mkdir(parents=True,exist_ok=True)
results=[]
try:
    with tempfile.TemporaryDirectory(prefix='magpie-config-file-') as temporary:
        path=Path(temporary)/"folder space #? & 中文"/'desktop-settings.json';path.parent.mkdir();path.write_text('{}')
        for mode in ('success','reject','missing-service'):
            events.clear()
            if mode=='missing-service':connection.call_sync('org.freedesktop.DBus','/org/freedesktop/DBus','org.freedesktop.DBus','ReleaseName',GLib.Variant('(s)',(name,)),None,Gio.DBusCallFlags.NONE,3000,None)
            result=subprocess.run([str(app),'--configuration-reveal-probe',str(path)],cwd=ROOT,capture_output=True,text=True,timeout=12)
            (output/(mode+'.log')).write_text(result.stdout+result.stderr)
            assert result.returncode==(0 if mode=='success' else 1),(mode,result.returncode,result.stderr)
            if mode!='missing-service':
                assert len(events)==1 and len(events[0][0])==1 and events[0][1]=='',(mode,events)
                uri=urlsplit(events[0][0][0])
                assert uri.scheme=='file' and not uri.netloc and not uri.query and not uri.fragment and unquote(uri.path)==str(path),(mode,events,str(path))
            else:assert not events
            results.append({'case':mode,'passed':True,'events':events.copy()})
finally:
    connection.unregister_object(registered);loop.quit();thread.join(timeout=3)
(output/'results.json').write_text(json.dumps({'passed':True,'app_sha256':hashlib.sha256(app.read_bytes()).hexdigest(),'private_bus':True,'file_manager_opened':False,'cases':results},ensure_ascii=False,indent=2)+'\n')
print('PASS: 3 private-bus configuration reveal cases; UTF-8/URI escaping, rejection and missing service; no GUI.')
