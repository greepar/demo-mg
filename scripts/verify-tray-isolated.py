#!/usr/bin/env python3
"""Exercise real Avalonia SNI/dbusmenu and window lifetime on a private bus/compositor.
The emulated tray host validates protocol behavior; this does not verify a real desktop panel.
"""
import argparse,json,os,pathlib,shutil,subprocess,sys,tempfile,time,threading,uuid,hashlib
ROOT=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--app',default='artifacts/desktop/linux-x64/Magpie.Desktop')
p.add_argument('--output',default='artifacts/desktop/tray-protocol')
p.add_argument('--case',action='append',choices=['lifecycle','hidden','fallback','disabled','unavailable-close'])
p.add_argument('--wayland-debug',action='store_true')
p.add_argument('--private',action='store_true',help=argparse.SUPPRESS)
a=p.parse_args()
if not a.private:
    # No service directories: framework Settings queries cannot autostart a portal,
    # keyring, accessibility daemon, or anything from the user's desktop session.
    with tempfile.TemporaryDirectory(prefix='magpie-tray-bus-') as temporary:
        runtime=pathlib.Path(temporary);runtime.chmod(0o700)
        config=runtime/'bus.conf'
        config.write_text('<busconfig><type>session</type><listen>unix:tmpdir=/tmp</listen><auth>EXTERNAL</auth><policy context="default"><allow own="*"/><allow send_destination="*"/><allow receive_sender="*"/></policy></busconfig>')
        env=dict(os.environ,MAGPIE_TRAY_PRIVATE_SESSION='1',XDG_RUNTIME_DIR=str(runtime),XDG_CURRENT_DESKTOP='MagpieTest',XDG_CONFIG_HOME=str(runtime/'config'),XDG_DATA_HOME=str(runtime/'data'),XDG_CACHE_HOME=str(runtime/'cache'))
        for k in ('DISPLAY','WAYLAND_DISPLAY','WAYLAND_SOCKET','DBUS_SESSION_BUS_ADDRESS','GNOME_KEYRING_CONTROL'):env.pop(k,None)
        raise SystemExit(subprocess.call(['dbus-run-session','--config-file='+str(config),'--','/usr/bin/python3',str(pathlib.Path(__file__).resolve()),*sys.argv[1:],'--private'],env=env,cwd=ROOT))
if os.environ.get('MAGPIE_TRAY_PRIVATE_SESSION')!='1':raise SystemExit('Private test session is required.')
from gi.repository import Gio,GLib
BUS='org.kde.StatusNotifierWatcher';WP='/StatusNotifierWatcher';ITEM='org.kde.StatusNotifierItem'
connection=Gio.bus_get_sync(Gio.BusType.SESSION,None)
items=[];host=True;registrations=[]
xml='''<node><interface name="org.kde.StatusNotifierWatcher">
<method name="RegisterStatusNotifierItem"><arg type="s" direction="in"/></method>
<method name="RegisterStatusNotifierHost"><arg type="s" direction="in"/></method>
<property name="RegisteredStatusNotifierItems" type="as" access="read"/>
<property name="IsStatusNotifierHostRegistered" type="b" access="read"/>
<property name="ProtocolVersion" type="i" access="read"/>
<signal name="StatusNotifierItemRegistered"><arg type="s"/></signal>
<signal name="StatusNotifierItemUnregistered"><arg type="s"/></signal>
<signal name="StatusNotifierHostRegistered"/><signal name="StatusNotifierHostUnregistered"/>
</interface></node>'''
def method(conn,sender,path,iface,name,params,invocation):
    value=params.unpack()[0]
    if name=='RegisterStatusNotifierItem':
        item=sender+value if value.startswith('/') else value+'/StatusNotifierItem'
        if item not in items:items.append(item)
        registrations.append(item)
        connection.emit_signal(None,WP,BUS,'StatusNotifierItemRegistered',GLib.Variant('(s)',(item,)))
    invocation.return_value(None)
def prop(conn,sender,path,iface,name):
    return {'RegisteredStatusNotifierItems':GLib.Variant('as',list(items)),'IsStatusNotifierHostRegistered':GLib.Variant('b',host),'ProtocolVersion':GLib.Variant('i',0)}[name]
connection.register_object(WP,Gio.DBusNodeInfo.new_for_xml(xml).interfaces[0],method,prop,None)
def owner_changed(conn,sender,path,iface,signal,params):
    name,old,new=params.unpack()
    if old and not new:
        for item in list(items):
            if item.split('/')[0]==name:
                items.remove(item);connection.emit_signal(None,WP,BUS,'StatusNotifierItemUnregistered',GLib.Variant('(s)',(item,)))
connection.signal_subscribe('org.freedesktop.DBus','org.freedesktop.DBus','NameOwnerChanged','/org/freedesktop/DBus',None,Gio.DBusSignalFlags.NONE,owner_changed)
loop=GLib.MainLoop();threading.Thread(target=loop.run,daemon=True).start()
def call(service,path,iface,member,params=None):
    return connection.call_sync(service,path,iface,member,params,None,Gio.DBusCallFlags.NONE,3000,None)
def own():
    result=call('org.freedesktop.DBus','/org/freedesktop/DBus','org.freedesktop.DBus','RequestName',GLib.Variant('(su)',(BUS,0))).unpack()[0]
    assert result in (1,4),result
def unown():
    call('org.freedesktop.DBus','/org/freedesktop/DBus','org.freedesktop.DBus','ReleaseName',GLib.Variant('(s)',(BUS,)));items.clear()
def set_host(value):
    global host
    host=value
    connection.emit_signal(None,WP,BUS,'StatusNotifierHostRegistered' if value else 'StatusNotifierHostUnregistered',None)
def state(path):
    try:return json.loads(path.read_text())
    except (FileNotFoundError,json.JSONDecodeError):return {}
def wait_for(path,predicate,process,description,timeout=12):
    deadline=time.monotonic()+timeout
    while time.monotonic()<deadline:
        s=state(path)
        if s.get('Error'):raise AssertionError(s['Error'])
        if compositor.poll() is not None:raise AssertionError(f'Private compositor exited {compositor.returncode} during {description}')
        if predicate(s):return s
        if process.poll() is not None:raise AssertionError(f'{description}: process exited {process.returncode}; {s}')
        time.sleep(.05)
    diagnostic=ROOT/'.tools/diagnostics/dotnet-stack'
    if diagnostic.exists():
        with pathlib.Path(str(path)+'.stack.log').open('w') as log:
            subprocess.run([str(diagnostic),'report','--process-id',str(process.pid)],stdout=log,stderr=log,env=dict(os.environ,DOTNET_ROLL_FORWARD='LatestMajor'),timeout=10)
    raise AssertionError(f'{description}: timeout; {state(path)}')
def command(path,operation,process):
    key=uuid.uuid4().hex;temp=path.with_suffix('.request.tmp');temp.write_text(key+'\n'+operation+'\n');temp.replace(str(path)+'.command')
    return wait_for(path,lambda s:s.get('Ack')==key,process,operation)
def current_item():
    assert items, 'No registered item';service,path=items[-1].split('/',1);return service,'/'+path
def activate():
    service,path=current_item();call(service,path,ITEM,'Activate',GLib.Variant('(ii)',(0,0)))
def layout():
    service,path=current_item()
    menu=call(service,path,'org.freedesktop.DBus.Properties','Get',GLib.Variant('(ss)',(ITEM,'Menu'))).unpack()[0]
    tree=call(service,menu,'com.canonical.dbusmenu','GetLayout',GLib.Variant('(iias)',(0,-1,[]))).unpack()[1]
    rows=[]
    def walk(node):
        if isinstance(node,GLib.Variant):node=node.unpack()
        ident,properties,children=node
        if ident:rows.append((ident,properties))
        for child in children:walk(child)
    walk(tree);return service,menu,rows
def click(label):
    deadline=time.monotonic()+3
    while True:
        service,menu,rows=layout();matches=[i for i,props in rows if props.get('label')==label]
        if matches:ident=matches[0];break
        assert time.monotonic()<deadline, (label,rows)
        time.sleep(.05)
    call(service,menu,'com.canonical.dbusmenu','Event',GLib.Variant('(isvu)',(ident,'clicked',GLib.Variant('i',0),0)))
def validate_icon():
    service,path=current_item()
    props=call(service,path,'org.freedesktop.DBus.Properties','GetAll',GLib.Variant('(s)',(ITEM,))).unpack()[0]
    assert props['Id']=='Magpie' and props['Status']=='Active',props
    pixmaps=props['IconPixmap'];assert pixmaps
    for width,height,data in pixmaps:assert width>1 and height>1 and len(data)==width*height*4 and any(data)
    _,_,rows=layout();labels=[props.get('label') for _,props in rows if 'label' in props]
    assert labels==['Main window','Scale after 3s (Fullscreen)','Scale after 3s (Windowed)','Exit'],labels
    return {'id':props['Id'],'pixmaps':[[w,h,len(data)] for w,h,data in pixmaps],'menu_labels':labels}
own()
output=(ROOT/a.output).resolve();output.mkdir(parents=True,exist_ok=True)
(output/'results.json').write_text(json.dumps({'passed':False,'state':'running'}))
app=(ROOT/a.app).resolve();launch=['dotnet',str(app)] if app.suffix=='.dll' else [str(app)]
results=[]
with tempfile.TemporaryDirectory(prefix='magpie-tray-wayland-') as temporary:
    runtime=pathlib.Path(temporary);runtime.chmod(0o700)
    env=dict(os.environ,XDG_RUNTIME_DIR=str(runtime),WAYLAND_DISPLAY='magpie-tray',XDG_SESSION_TYPE='wayland',XDG_CONFIG_HOME=str(runtime/'config'),XDG_DATA_HOME=str(runtime/'data'),XDG_CACHE_HOME=str(runtime/'cache'))
    for k in ('DISPLAY','WAYLAND_SOCKET'):env.pop(k,None)
    if a.wayland_debug:env['WAYLAND_DEBUG']='1'
    local=ROOT/'artifacts/test-tools/weston/root';weston=local/'usr/bin/weston'
    if not weston.exists():weston=pathlib.Path(shutil.which('weston') or '/missing-weston')
    if local in weston.parents:
        lib=local/'usr/lib/x86_64-linux-gnu';env['LD_LIBRARY_PATH']=str(lib)+':'+str(lib/'weston')+(':'+env['LD_LIBRARY_PATH'] if env.get('LD_LIBRARY_PATH') else '')
        env['WESTON_MODULE_MAP']=';'.join(x.name+'='+str(x) for x in lib.rglob('*.so'))
    config=runtime/'weston.ini'
    client=local/'usr/libexec/weston-desktop-shell'
    config.write_text('[shell]\nlocking=false\npanel-position=none\n'+('client='+str(client)+'\n' if local in weston.parents else ''))
    with (output/'weston.log').open('w') as log:
        compositor=subprocess.Popen([str(weston),'--config='+str(config),'--idle-time=0','--socket=magpie-tray','--width=1280','--height=900','--renderer=pixman','--backend=headless','--shell=desktop-shell.so'],env=env,stdout=log,stderr=log)
        try:
            deadline=time.monotonic()+15
            while not (runtime/'magpie-tray').exists():
                assert compositor.poll() is None and time.monotonic()<deadline,'Private Weston startup failed';time.sleep(.05)
            for case in a.case or ['lifecycle','hidden','fallback','disabled','unavailable-close']:
                items.clear();host=case not in ('fallback','unavailable-close');receipt=output/(case+'.json');receipt.unlink(missing_ok=True);pathlib.Path(str(receipt)+'.command').unlink(missing_ok=True)
                args=launch+['--tray-integration-test',str(receipt)]
                if case in ('hidden','fallback','disabled'):args+=['--autostart']
                if case=='disabled':args+=['--tray-disabled']
                with (output/(case+'.log')).open('w') as applog:
                    process=subprocess.Popen(args,cwd=ROOT,env=env,stdout=applog,stderr=applog)
                    try:
                        if case in ('lifecycle','hidden'):
                            initial=wait_for(receipt,lambda s:s.get('Ready') and (s.get('Hidden') if case=='hidden' else s.get('Visible')),process,'registered tray')
                            if case=='hidden':assert not initial['Visible'] and initial['Hidden'] and initial['Opened']==0,initial
                            else:assert initial['Visible'] and initial['Opened']==1,initial
                            icon=validate_icon()
                            if case=='lifecycle':command(receipt,'close',process);wait_for(receipt,lambda s:s.get('Hidden') and not s.get('Visible'),process,'close hides')
                            activate();wait_for(receipt,lambda s:s.get('Visible') and not s.get('Hidden'),process,'tray activation restores')
                            command(receipt,'close',process);click('Main window')
                            wait_for(receipt,lambda s:s.get('Visible') and not s.get('Hidden'),process,'dbusmenu main window restores')
                            command(receipt,'minimize',process);activate()
                            wait_for(receipt,lambda s:s.get('Visible') and not s.get('Hidden'),process,'activate minimized window')
                            command(receipt,'delay5',process)
                            # AboutToShow makes native menus refresh their persisted countdown.
                            service,menu,_=layout();call(service,menu,'com.canonical.dbusmenu','AboutToShow',GLib.Variant('(i)',(0,)))
                            click('Scale after 5s (Fullscreen)');click('Scale after 5s (Windowed)')
                            wait_for(receipt,lambda s:s.get('Starts')==['fullscreen:5','windowed:5'],process,'actual dbusmenu start callbacks')
                            command(receipt,'close',process);set_host(False)
                            wait_for(receipt,lambda s:s.get('Visible') and not s.get('Ready') and not s.get('Hidden'),process,'host loss recovers window')
                            set_host(True);wait_for(receipt,lambda s:s.get('Ready'),process,'host restored')
                            command(receipt,'close',process);unown()
                            wait_for(receipt,lambda s:s.get('Visible') and not s.get('Ready'),process,'watcher loss recovers window')
                            own();wait_for(receipt,lambda s:s.get('Ready'),process,'watcher restart re-registers')
                            command(receipt,'close',process);command(receipt,'disable',process)
                            wait_for(receipt,lambda s:s.get('Visible') and not s.get('Enabled') and not s.get('Ready'),process,'settings disable restores window')
                            command(receipt,'enable',process);wait_for(receipt,lambda s:s.get('Ready'),process,'settings re-enable')
                            command(receipt,'screenshot',process)
                            click('Exit');assert process.wait(timeout=10)==0
                            assert state(receipt)['Exited']
                            results.append({'case':case,'passed':True,'initial':initial,'icon':icon,'final':state(receipt)})
                        else:
                            visible=wait_for(receipt,lambda s:s.get('Visible'),process,'fallback visible',timeout=8)
                            assert not visible['Ready'] and not visible['Hidden'] and visible['Opened']==1,visible
                            if case=='disabled':assert not visible['Enabled']
                            else:assert visible['Enabled']
                            commandfile=pathlib.Path(str(receipt)+".command");commandfile.write_text(chr(10).join([uuid.uuid4().hex,"close",""]))
                            try:assert process.wait(timeout=10)==0
                            except subprocess.TimeoutExpired:
                                diagnostic=ROOT/'.tools/diagnostics/dotnet-stack'
                                if diagnostic.exists():
                                    with (output/(case+'-stack.log')).open('w') as stacklog:
                                        subprocess.run([str(diagnostic),'report','--process-id',str(process.pid)],stdout=stacklog,stderr=stacklog,env=dict(env,DOTNET_ROLL_FORWARD='LatestMajor'),timeout=10)
                                raise
                            results.append({'case':case,'passed':True,'initial':visible,'final':state(receipt)})
                    finally:
                        if process.poll() is None:process.terminate();process.wait(timeout=5)
        finally:
            compositor.terminate();compositor.wait(timeout=10)
(output/'results.json').write_text(json.dumps({'passed':True,'app_sha256':hashlib.sha256(app.read_bytes()).hexdigest(),'private_dbus':True,'private_bus_service_activation_disabled':True,'private_wayland':True,'real_panel_render_tested':False,'capture_portal_called':False,'cases':results},indent=2)+'\n')
print(f'PASS: {len(results)} real SNI/dbusmenu/window lifetime cases on private D-Bus and Wayland.')
