#!/usr/bin/env python3
"""Test real GlobalShortcuts D-Bus messages with a private portal fixture; no UI/keyboard grabs."""
import argparse,json,os,pathlib,subprocess,sys,tempfile,time,threading,hashlib
ROOT=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--app',default='artifacts/desktop/linux-x64/Magpie.Desktop');p.add_argument('--output',default='artifacts/desktop/global-shortcuts');p.add_argument('--ui',action='store_true');p.add_argument('--private',action='store_true');a=p.parse_args()
if not a.private:
 with tempfile.TemporaryDirectory(prefix='magpie-shortcut-bus-') as temporary:
  root=pathlib.Path(temporary);root.chmod(0o700);config=root/'bus.conf';config.write_text('<busconfig><type>session</type><listen>unix:tmpdir=/tmp</listen><auth>EXTERNAL</auth><policy context="default"><allow own="*"/><allow send_destination="*"/><allow receive_sender="*"/></policy></busconfig>')
  env=dict(os.environ,MAGPIE_SHORTCUT_PRIVATE_SESSION='1',XDG_RUNTIME_DIR=str(root))
  for key in ('DISPLAY','WAYLAND_DISPLAY','WAYLAND_SOCKET','DBUS_SESSION_BUS_ADDRESS'):env.pop(key,None)
  raise SystemExit(subprocess.call(['dbus-run-session','--config-file='+str(config),'--','/usr/bin/python3',str(pathlib.Path(__file__).resolve()),*sys.argv[1:],'--private'],env=env,cwd=ROOT))
assert os.environ.get('MAGPIE_SHORTCUT_PRIVATE_SESSION')=='1'
from gi.repository import Gio,GLib
SERVICE='org.freedesktop.portal.Desktop';PATH='/org/freedesktop/portal/desktop';IFACE='org.freedesktop.portal.GlobalShortcuts'
conn=Gio.bus_get_sync(Gio.BusType.SESSION,None)
conn.call_sync('org.freedesktop.DBus','/org/freedesktop/DBus','org.freedesktop.DBus','RequestName',GLib.Variant('(su)',(SERVICE,0)),None,Gio.DBusCallFlags.NONE,3000,None)
xml='''<node><interface name="org.freedesktop.portal.GlobalShortcuts">
<method name="CreateSession"><arg type="a{sv}" direction="in"/><arg type="o" direction="out"/></method>
<method name="BindShortcuts"><arg type="o" direction="in"/><arg type="a(sa{sv})" direction="in"/><arg type="s" direction="in"/><arg type="a{sv}" direction="in"/><arg type="o" direction="out"/></method>
<property name="version" type="u" access="read"/>
<signal name="Activated"><arg type="o"/><arg type="s"/><arg type="t"/><arg type="a{sv}"/></signal>
<signal name="Deactivated"><arg type="o"/><arg type="s"/><arg type="t"/><arg type="a{sv}"/></signal>
<signal name="ShortcutsChanged"><arg type="o"/><arg type="a(sa{sv})"/></signal>
</interface></node>'''
request_xml=Gio.DBusNodeInfo.new_for_xml('<node><interface name="org.freedesktop.portal.Request"><method name="Close"/><signal name="Response"><arg type="u"/><arg type="a{sv}"/></signal></interface></node>').interfaces[0]
session_xml=Gio.DBusNodeInfo.new_for_xml('<node><interface name="org.freedesktop.portal.Session"><method name="Close"/><signal name="Closed"><arg type="a{sv}"/></signal></interface></node>').interfaces[0]
mode='success';session=None;bound=[];events=[];registered=[]
def close(conn,sender,path,iface,member,params,invocation):events.append((iface,member,path));invocation.return_value(None)
def response(path,code,values):conn.emit_signal(None,path,'org.freedesktop.portal.Request','Response',GLib.Variant('(ua{sv})',(code,values)))
def shortcuts(ids):return [(ident,{'description':GLib.Variant('s','System '+ident),'trigger_description':GLib.Variant('s','Custom '+ident)}) for ident in ids]
def method(connection,sender,path,iface,member,params,invocation):
 global session,bound
 values=params.unpack();opts=values[0] if member=='CreateSession' else values[-1]
 request='/org/freedesktop/portal/desktop/request/'+sender[1:].replace('.','_')+'/'+opts['handle_token']
 registered.append(conn.register_object(request,request_xml,close,None,None));events.append((member,values))
 if member=='CreateSession':
  session='/org/freedesktop/portal/desktop/session/'+sender[1:].replace('.','_')+'/'+opts['session_handle_token'];registered.append(conn.register_object(session,session_xml,close,None,None))
  # Intentionally emit before returning the method result: subscribe-before-call is required.
  response(request,0,{'session_handle':GLib.Variant('s',session)})
 else:
  requested_session,bindings,parent,_=values;assert requested_session==session and parent==''
  assert [i for i,_ in bindings]==['fullscreen','windowed','toolbar','screenshot']
  assert [v['preferred_trigger'] for _,v in bindings]==['ALT+SHIFT+a','ALT+SHIFT+q','ALT+SHIFT+d','ALT+SHIFT+s']
  assert all(v['description'] for _,v in bindings)
  bound=shortcuts(['fullscreen','windowed','toolbar','screenshot'] if mode!='partial' else ['windowed'])
  if mode=='cancel-response':response(request,1,{})
  elif mode=='reject':response(request,2,{})
  elif mode=='malformed':response(request,0,{'shortcuts':GLib.Variant('s','invalid')})
  elif mode=='invalid-actions':response(request,0,{'shortcuts':GLib.Variant('a(sa{sv})',shortcuts(['unexpected']))})
  elif mode not in ('cancel-pending','restart-pending'):
   response(request,0,{'shortcuts':GLib.Variant('a(sa{sv})',bound)})
   if a.ui:
    def ui_signals():
     emit('Activated','windowed');emit('Activated','windowed');emit('Deactivated','windowed');emit('Activated','windowed');return False
    GLib.timeout_add(500,ui_signals)
 invocation.return_value(GLib.Variant('(o)',(request,)))
portal_id=conn.register_object(PATH,Gio.DBusNodeInfo.new_for_xml(xml).interfaces[0],method,lambda *args:GLib.Variant('u',1),None)
loop=GLib.MainLoop();threading.Thread(target=loop.run,daemon=True).start()
def emit(signal,ident='fullscreen',target=None,connection=conn):connection.emit_signal(None,PATH,IFACE,signal,GLib.Variant('(osta{sv})',(target or session,ident,100,{})))
def state(path):
 try:return json.loads(path.read_text())
 except (FileNotFoundError,json.JSONDecodeError):return {}
def wait(path,predicate,process,label):
 deadline=time.monotonic()+8
 while time.monotonic()<deadline:
  value=state(path)
  if predicate(value):return value
  assert process.poll() is None,(label,process.returncode,value)
  time.sleep(.025)
 raise AssertionError((label,state(path)))
app=(ROOT/a.app).resolve();output=(ROOT/a.output).resolve();output.mkdir(parents=True,exist_ok=True);(output/'results.json').write_text('{"passed":false,"state":"running"}')
launch=['dotnet',str(app)] if app.suffix=='.dll' else [str(app)];results=[]
if a.ui:
 mode='success'
 result=subprocess.run([sys.executable,str(ROOT/'scripts/verify-desktop-isolated.py'),'--app',str(app),'--case','ShortcutPortal','--output',str(output/'ui')],cwd=ROOT,capture_output=True,text=True,timeout=35)
 (output/'ui.log').write_text(result.stdout+result.stderr)
 assert result.returncode==0,(result.stdout,result.stderr)
 ui=json.loads((output/'ui/results.json').read_text());assert 'SHORTCUT PORTAL UI PASS:' in ui[0]['log']
 (output/'results.json').write_text(json.dumps({'passed':True,'app_sha256':hashlib.sha256(app.read_bytes()).hexdigest(),'private_dbus':True,'private_wayland':True,'real_system_dialog_opened':False,'ui':ui,'events':events},indent=2)+'\n')
 print('PASS: actual Avalonia UI portal binding and activation on private D-Bus/Wayland.')
 raise SystemExit(0)
for mode in ['success','partial','cancel-response','reject','invalid-actions','cancel-pending','malformed','restart','restart-pending','missing-interface']:
 events.clear();registered.clear();session=None;bound=[]
 if mode=='missing-interface':conn.unregister_object(portal_id)
 receipt=output/(mode+'.json')
 for suffix in ('','.stop','.cancel'):pathlib.Path(str(receipt)+suffix).unlink(missing_ok=True)
 with (output/(mode+'.log')).open('w') as log:
  process=subprocess.Popen(launch+['--global-shortcut-probe',str(receipt)],cwd=ROOT,stdout=log,stderr=log)
  try:
   if mode in ('cancel-pending','restart-pending'):
    deadline=time.monotonic()+8
    while not any(e[0]=='BindShortcuts' for e in events):assert time.monotonic()<deadline;time.sleep(.025)
    if mode=='cancel-pending':pathlib.Path(str(receipt)+'.cancel').touch()
    else:conn.call_sync('org.freedesktop.DBus','/org/freedesktop/DBus','org.freedesktop.DBus','ReleaseName',GLib.Variant('(s)',(SERVICE,)),None,Gio.DBusCallFlags.NONE,3000,None)
   if mode in ('success','partial','restart'):
    initial=wait(receipt,lambda s:s.get('State')=='bound',process,'bound')
    assert len(initial['Bound'])==(1 if mode=='partial' else 4),initial
    assert all(b['TriggerDescription'].startswith('Custom') for b in initial['Bound'])
    emit('Activated','windowed');emit('Activated','windowed');emit('Deactivated','windowed');emit('Activated','windowed');emit('Activated','invalid');emit('Activated','windowed',session+'_wrong')
    spoof=Gio.DBusConnection.new_for_address_sync(os.environ['DBUS_SESSION_BUS_ADDRESS'],Gio.DBusConnectionFlags.AUTHENTICATION_CLIENT|Gio.DBusConnectionFlags.MESSAGE_BUS_CONNECTION,None,None)
    emit('Deactivated','windowed',connection=spoof);emit('Activated','windowed',connection=spoof);spoof.flush_sync(None);spoof.close_sync(None)
    wait(receipt,lambda s:s.get('Activations')==['windowed','windowed'],process,'activation dedup, deactivation and sender/session filtering')
    if mode!='partial':
     emit('Activated','screenshot');emit('Activated','screenshot');emit('Deactivated','screenshot')
     wait(receipt,lambda s:s.get('Activations')==['windowed','windowed','screenshot'],process,'screenshot action deduplication')
    changed=shortcuts(['toolbar']);conn.emit_signal(None,PATH,IFACE,'ShortcutsChanged',GLib.Variant('(oa(sa{sv}))',(session,changed)))
    wait(receipt,lambda s:len(s.get('Bound',[]))==1 and s['Bound'][0]['Id']=='toolbar',process,'changed bindings')
    emit('Activated','windowed');emit('Activated','toolbar');wait(receipt,lambda s:s.get('Activations')==(['windowed','windowed','toolbar'] if mode=='partial' else ['windowed','windowed','screenshot','toolbar']),process,'updated action filtering')
    if mode=='restart':conn.call_sync('org.freedesktop.DBus','/org/freedesktop/DBus','org.freedesktop.DBus','ReleaseName',GLib.Variant('(s)',(SERVICE,)),None,Gio.DBusCallFlags.NONE,3000,None)
    else:conn.emit_signal(None,session,'org.freedesktop.portal.Session','Closed',GLib.Variant('(a{sv})',({},)))
    wait(receipt,lambda s:len(s.get('Closed',[]))>0 and len(s.get('Bound',[]))==0,process,'session revoked')
    emit('Activated','toolbar');time.sleep(.1);assert state(receipt)['Activations']==(['windowed','windowed','toolbar'] if mode=='partial' else ['windowed','windowed','screenshot','toolbar'])
    pathlib.Path(str(receipt)+'.stop').touch()
   else:initial=wait(receipt,lambda s:s.get('State')=='failed',process,'failure handled');assert initial['Error']
   assert process.wait(timeout=8)==0
   if mode!='missing-interface':assert any(e[0]=='org.freedesktop.portal.Session' and e[1]=='Close' for e in events),events
   if mode=='cancel-pending':assert any(e[0]=='org.freedesktop.portal.Request' and e[1]=='Close' for e in events),events
   results.append({'case':mode,'passed':True,'initial':initial,'final':state(receipt),'events':events.copy()})
  finally:
   if process.poll() is None:process.terminate();process.wait(timeout=5)
 for ident in registered:conn.unregister_object(ident)
 if mode in ('restart','restart-pending'):conn.call_sync('org.freedesktop.DBus','/org/freedesktop/DBus','org.freedesktop.DBus','RequestName',GLib.Variant('(su)',(SERVICE,0)),None,Gio.DBusCallFlags.NONE,3000,None)
(output/'results.json').write_text(json.dumps({'passed':True,'app_sha256':hashlib.sha256(app.read_bytes()).hexdigest(),'private_bus_no_service_activation':True,'gui_opened':False,'real_desktop_shortcuts_registered':False,'cases':results},indent=2)+'\n')
print('PASS: 10 real D-Bus global shortcut session, response race, partial/rejected binding, activation filtering, reconfiguration, cancellation and cleanup cases; no GUI.')
