#!/usr/bin/env python3
"""ScreenCast wire protocol plus real PipeWire transport, using only private synthetic sources."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import queue
import socket
import subprocess
import sys
import tempfile
import threading
import time

ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--fixture',default='artifacts/native/linux-x64/Release/wayland_capture_fixture')
p.add_argument('--output',default='artifacts/desktop/pipewire-capture')
p.add_argument('--private',action='store_true',help=argparse.SUPPRESS)
a=p.parse_args()
if not a.private:
    with tempfile.TemporaryDirectory(prefix='magpie-pipewire-') as temporary:
        folder=Path(temporary);folder.chmod(0o700)
        (folder/'magpie-fixture-private').touch()
        config=folder/'bus.conf'
        config.write_text('<busconfig><type>session</type><listen>unix:tmpdir=/tmp</listen><auth>EXTERNAL</auth><policy context="default"><allow own="*"/><allow send_destination="*"/><allow receive_sender="*"/></policy></busconfig>')
        env=dict(os.environ,MAGPIE_PIPEWIRE_FIXTURE_PRIVATE='1',XDG_RUNTIME_DIR=str(folder),PIPEWIRE_RUNTIME_DIR=str(folder),PIPEWIRE_REMOTE='magpie-fixture',PIPEWIRE_CONFIG_DIR=str(folder),PIPEWIRE_CONFIG_PREFIX='',XDG_CONFIG_HOME=str(folder/'config'),XDG_DATA_HOME=str(folder/'data'))
        for name in ('DISPLAY','WAYLAND_DISPLAY','WAYLAND_SOCKET','DBUS_SESSION_BUS_ADDRESS','PIPEWIRE_CORE','PIPEWIRE_CONFIG_NAME','PIPEWIRE_CONFIG_PATH'):
            env.pop(name,None)
        raise SystemExit(subprocess.call(['dbus-run-session','--config-file='+str(config),'--','/usr/bin/python3',str(Path(__file__).resolve()),*sys.argv[1:],'--private'],cwd=ROOT,env=env))

assert os.environ.get('MAGPIE_PIPEWIRE_FIXTURE_PRIVATE')=='1'
runtime=Path(os.environ['XDG_RUNTIME_DIR'])
assert (runtime/'magpie-fixture-private').is_file() and os.environ['PIPEWIRE_REMOTE']=='magpie-fixture'
assert all(name not in os.environ for name in ('DISPLAY','WAYLAND_DISPLAY','WAYLAND_SOCKET'))
from gi.repository import Gio,GLib
output=(ROOT/a.output).resolve();output.mkdir(parents=True,exist_ok=True)
fixture=(ROOT/a.fixture).resolve()
report={'passed':False,'private_bus':True,'private_pipewire':True,'gui_opened':False,'real_desktop_capture_tested':False,'native_input_delivery_tested':False,'session_manager_policy_tested':False,'synthetic_source':True,'portal_permission_ui_tested':False,'full_interaction_parity':False,'cases':[],'fixture_sha256':hashlib.sha256(fixture.read_bytes()).hexdigest()}
(output/'results.json').write_text(json.dumps(report,indent=2)+'\n')
# No hardware, RTKit, desktop portal, session-manager or device discovery modules.
config=runtime/'fixture.conf'
config.write_text('''context.properties = { core.daemon = true core.name = magpie-fixture support.dbus = false }
context.spa-libs = { support.* = support/libspa-support audio.convert.* = audioconvert/libspa-audioconvert }
context.modules = [
 { name = libpipewire-module-protocol-native }
 { name = libpipewire-module-client-node }
 { name = libpipewire-module-adapter }
 { name = libpipewire-module-link-factory }
 { name = libpipewire-module-access args = { access.force = unrestricted } }
]
''')

(runtime/'client.conf').write_text("""context.properties = { support.dbus = false }
context.spa-libs = { support.* = support/libspa-support audio.convert.* = audioconvert/libspa-audioconvert }
context.modules = [
 { name = libpipewire-module-protocol-native }
 { name = libpipewire-module-client-node }
 { name = libpipewire-module-adapter }
]
""")

class Child:
    def __init__(self,argument,label):
        self.lines=[];self.pending=queue.Queue();self.label=label
        self.log=(output/(label+'.log')).open('w')
        self.process=subprocess.Popen([str(fixture),argument],cwd=ROOT,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=self.log,text=True,bufsize=1,env=dict(os.environ,MAGPIE_PIPEWIRE_FIXTURE_TARGET_MODE=mode,MAGPIE_PIPEWIRE_FIXTURE_PIXEL_FORMAT=pixel_format))
        def read():
            for line in self.process.stdout:
                line=line.rstrip();self.lines.append(line);self.log.write(line+'\n');self.log.flush();self.pending.put(line)
            self.pending.put(None)
        self.thread=threading.Thread(target=read,daemon=True);self.thread.start()
    def send(self,line):
        self.process.stdin.write(line+'\n');self.process.stdin.flush()
    def expect(self,prefix,timeout=10):
        end=time.monotonic()+timeout
        while time.monotonic()<end:
            try:line=self.pending.get(timeout=min(.2,end-time.monotonic()))
            except queue.Empty:continue
            if line is None:raise AssertionError((self.label,'exited',self.process.poll(),self.lines))
            if line.startswith('ERROR') or line.startswith('START_ERROR'):raise AssertionError((self.label,line))
            if line.startswith(prefix):return line
        raise AssertionError((self.label,'timeout',prefix,self.lines))
    def command(self,line):
        self.send(line);return self.expect('ACK '+line.split()[0])
    def close(self):
        if self.process.poll() is None:
            try:self.send('stop');self.process.wait(timeout=5)
            except (BrokenPipeError,subprocess.TimeoutExpired):self.process.kill();self.process.wait(timeout=5)
        self.thread.join(timeout=2);self.process.stdin.close();self.process.stdout.close();self.log.close()

SERVICE='org.freedesktop.portal.Desktop';PATH='/org/freedesktop/portal/desktop';IFACE='org.freedesktop.portal.ScreenCast'
xml=Gio.DBusNodeInfo.new_for_xml('''<node><interface name="org.freedesktop.portal.ScreenCast">
<method name="CreateSession"><arg type="a{sv}" direction="in"/><arg type="o" direction="out"/></method>
<method name="SelectSources"><arg type="o" direction="in"/><arg type="a{sv}" direction="in"/><arg type="o" direction="out"/></method>
<method name="Start"><arg type="o" direction="in"/><arg type="s" direction="in"/><arg type="a{sv}" direction="in"/><arg type="o" direction="out"/></method>
<method name="OpenPipeWireRemote"><arg type="o" direction="in"/><arg type="a{sv}" direction="in"/><arg type="h" direction="out"/></method>
</interface></node>''').interfaces[0]
session_xml=Gio.DBusNodeInfo.new_for_xml('<node><interface name="org.freedesktop.portal.Session"><method name="Close"/><signal name="Closed"><arg type="a{sv}"/></signal></interface></node>').interfaces[0]
request_xml=Gio.DBusNodeInfo.new_for_xml('<node><interface name="org.freedesktop.portal.Request"><method name="Close"/><signal name="Response"><arg type="u"/><arg type="a{sv}"/></signal></interface></node>').interfaces[0]
objects=[];events=[];failures=[];session=None;source_node=source_serial=0;mode='serial'
conn=Gio.bus_get_sync(Gio.BusType.SESSION,None)

def close(c,sender,path,iface,member,params,invocation):
    events.append([iface,member,path]);invocation.return_value(None)

def method(c,sender,path,iface,member,params,invocation):
    global session
    try:
        values=params.unpack();events.append([member,values])
        if member=='OpenPipeWireRemote':
            assert values[0]==session
            with socket.socket(socket.AF_UNIX,socket.SOCK_STREAM) as remote:
                remote.connect(str(runtime/'magpie-fixture'))
                descriptors=Gio.UnixFDList.new();index=descriptors.append(remote.fileno())
                invocation.return_value_with_unix_fd_list(GLib.Variant('(h)',(index,)),descriptors)
            return
        options=values[-1];assert options['handle_token'].startswith('magpie')
        request='/org/freedesktop/portal/desktop/request/'+sender[1:].replace('.','_')+'/'+options['handle_token']
        objects.append(conn.register_object(request,request_xml,close,None,None))
        response={}
        if member=='CreateSession':
            session='/org/freedesktop/portal/desktop/session/'+sender[1:].replace('.','_')+'/'+options['session_handle_token']
            objects.append(conn.register_object(session,session_xml,close,None,None));response={'session_handle':GLib.Variant('s',session)}
        elif member=='SelectSources':
            assert values[0]==session and options['types']==3 and not options['multiple'] and options['cursor_mode']==2
        elif member=='Start':
            assert values[0]==session and values[1]==''
            properties={'source_type':GLib.Variant('u',2),'size':GLib.Variant('(ii)',(19,12))}
            if mode=='serial':properties['pipewire-serial']=GLib.Variant('t',source_serial)
            elif mode=='bad-serial':properties['pipewire-serial']=GLib.Variant('s',str(source_serial))
            response={'streams':GLib.Variant('a(ua{sv})',[(source_node+100000 if mode=='serial' else source_node,properties)])}
        else:raise AssertionError(member)
        # Response before method return exercises the actual capture request race.
        c.emit_signal(sender,request,'org.freedesktop.portal.Request','Response',GLib.Variant('(ua{sv})',(0,response)))
        invocation.return_value(GLib.Variant('(o)',(request,)))
    except Exception as error:
        failures.append(str(error));invocation.return_dbus_error('org.freedesktop.portal.Error.Failed',str(error))

portal=conn.register_object(PATH,xml,method,None,None)
reply=conn.call_sync('org.freedesktop.DBus','/org/freedesktop/DBus','org.freedesktop.DBus','RequestName',GLib.Variant('(su)',(SERVICE,0)),None,Gio.DBusCallFlags.NONE,3000,None)
assert reply.unpack()==(1,)
loop=GLib.MainLoop();threading.Thread(target=loop.run,daemon=True).start()


def frame(client):
    client.send('frame')
    # All frame replies share no common prefix; retrieve exactly one line.
    while True:
        try:line=client.pending.get(timeout=10)
        except queue.Empty:raise AssertionError(('frame timeout',client.lines))
        assert line is not None,('capture exited',client.process.poll(),client.lines)
        if line.startswith(('FRAME ','WAIT ','FRAME_ERROR ')):return line.split()
        assert not line.startswith('ERROR'),line

def wait_frame(client,width,height,colour):
    end=time.monotonic()+10
    while time.monotonic()<end:
        value=frame(client)
        assert value[0]!='FRAME_ERROR',value
        if value[0]=='FRAME':
            assert value[-1]=='valid',value
            if list(map(int,value[2:5]))==[width,height,colour]:return int(value[1])
        time.sleep(.025)
    raise AssertionError(('matching frame timeout',client.lines[-15:]))

children=[];server=None;server_log=(output/'pipewire.log').open('w')
try:
    server=subprocess.Popen(['pipewire','-c',config.name],cwd=ROOT,stdout=server_log,stderr=server_log)
    end=time.monotonic()+10
    while not (runtime/'magpie-fixture').is_socket():
        assert server.poll() is None,('private server exited',server.returncode)
        assert time.monotonic()<end,'private server startup timeout';time.sleep(.025)
    for mode,pixel_format in (('serial','BGRA'),('serial','RGBA'),('serial','BGRx'),('serial','RGBx'),('legacy','BGRA'),('bad-serial','BGRA')):
        label=mode+'-'+pixel_format
        events.clear();failures.clear()
        source=Child('--source',label+'-source');children.append(source)
        ready=source.expect('READY ').split();source_node,source_serial=map(int,ready[1:])
        client=Child('--capture',label+'-capture');children.append(client)
        if mode=='bad-serial':
            assert client.process.wait(timeout=10)==2
            client.thread.join(timeout=2)
            assert any('START_ERROR Invalid portal PipeWire serial' in line for line in client.lines),client.lines
            assert not any(e[0]=='OpenPipeWireRemote' for e in events),events
        else:
            client.expect('STARTED')
            initial=wait_frame(client,37,23,23);first_sequence=initial
            assert "FORMAT 37 23 "+pixel_format in source.lines,source.lines
            for colour in (41,73,137):
                source.command('colour '+str(colour));sequence=wait_frame(client,37,23,colour);assert sequence>initial;initial=sequence
            if mode=='serial':
                assert any(line.startswith('TARGET '+str(source_serial)+' ') for line in source.lines),source.lines
            else:
                # pw_stream_connect's legacy node targeting becomes node.target,
                # whose actual value is reported separately from object.serial.
                assert any(line.startswith('TARGET ') for line in source.lines),source.lines
            sizes=[]
            for width,height in ((81,47),(13,9),(37,23)):
                source.command('resize '+str(width)+' '+str(height))
                sequence=wait_frame(client,width,height,137);assert sequence>initial;initial=sequence
                sizes.append([width,height,sequence])
            source.command('pause');time.sleep(.15)
            for _ in range(3):frame(client);time.sleep(.025)
            paused=[frame(client) for _ in range(5)]
            assert all(v[0]=='WAIT' for v in paused) and len({v[1] for v in paused})==1,paused
            source.command('resume');sequence=wait_frame(client,37,23,137);assert sequence>initial;initial=sequence
            source.command('flags 2') # SPA_META_HEADER_FLAG_CORRUPTED
            time.sleep(.2)
            for _ in range(3):frame(client);time.sleep(.05)
            waiting=[frame(client) for _ in range(5)]
            assert all(v[0]=='WAIT' for v in waiting),waiting
            assert len({v[1] for v in waiting})==1,waiting
            source.command('flags 0');source.command('colour 191')
            resumed=wait_frame(client,37,23,191);assert resumed>initial
            if mode=='serial':
                conn.emit_signal(None,session,'org.freedesktop.portal.Session','Closed',GLib.Variant('(a{sv})',({},)));conn.flush_sync(None)
                time.sleep(.1);closed=frame(client);assert closed[0]=='FRAME_ERROR' and 'Screen sharing was stopped' in ' '.join(closed),closed
            else:
                client.send('cancel');client.expect('CANCELLED');closed=frame(client)
                assert closed[0]=='FRAME_ERROR' and 'Capture was stopped' in ' '.join(closed),closed
            report['cases'].append({'mode':mode,'pixel_format':pixel_format,'passed':True,'first_sequence':first_sequence,'sequence_before_corruption':initial,'resumed_sequence':resumed,'source_pause_resume':True,'header_corruption_pause_resume':True,'resize_frames':sizes,'lifecycle':'portal-closed' if mode=='serial' else 'cancel','source_node':source_node,'source_serial':source_serial,'portal_tuple_node':source_node+100000 if mode=='serial' else source_node,'target_events':[line for line in source.lines if line.startswith('TARGET ')]})
        client.close();source.close()
        assert not failures,failures
        assert any(e[0]=='org.freedesktop.portal.Session' and e[1]=='Close' for e in events),events
        assert client.process.returncode==(2 if mode=='bad-serial' else 0)
        assert source.process.returncode==0,source.lines
        if mode=='bad-serial':report['cases'].append({'mode':mode,'pixel_format':pixel_format,'passed':True,'remote_opened':False})
        for ident in objects:conn.unregister_object(ident)
        objects.clear()
    report['passed']=True
finally:
    for child in children:
        if not child.log.closed:child.close()
    if server:
        server.terminate()
        try:server.wait(timeout=5)
        except subprocess.TimeoutExpired:server.kill();server.wait(timeout=5)
    server_log.close();loop.quit()
    (output/'results.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
print('PIPEWIRE CAPTURE PASS: private ScreenCast D-Bus/FD handoff and real padded BGRA/RGBA/BGRx/RGBx transport, serial/legacy targeting, continuous changes/resize, retained ABI buffers, corrupt-frame pause/resume, session close and cleanup. No desktop/input/session-manager policy tested.')
