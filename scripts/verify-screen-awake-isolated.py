#!/usr/bin/env python3
"""Actual Inhibit portal wire/lifetime tests on a private bus; no host power state or GUI."""
import argparse,hashlib,json,os,pathlib,subprocess,sys,tempfile,threading,time
ROOT=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--app',default='artifacts/desktop/linux-x64/Magpie.Desktop');p.add_argument('--output',default='artifacts/desktop/power-protocol');p.add_argument('--private',action='store_true');args=p.parse_args()
if not args.private:
 with tempfile.TemporaryDirectory(prefix='magpie-power-bus-') as temporary:
  folder=pathlib.Path(temporary);folder.chmod(0o700);config=folder/'bus.conf';config.write_text('<busconfig><type>session</type><listen>unix:tmpdir=/tmp</listen><auth>EXTERNAL</auth><policy context="default"><allow own="*"/><allow send_destination="*"/><allow receive_sender="*"/></policy></busconfig>')
  env=dict(os.environ,MAGPIE_POWER_PRIVATE_SESSION='1',XDG_RUNTIME_DIR=str(folder))
  for name in ['DISPLAY','WAYLAND_DISPLAY','WAYLAND_SOCKET','DBUS_SESSION_BUS_ADDRESS']:env.pop(name,None)
  raise SystemExit(subprocess.call(['dbus-run-session','--config-file='+str(config),'--','/usr/bin/python3',str(pathlib.Path(__file__).resolve()),*sys.argv[1:],'--private'],cwd=ROOT,env=env))
assert os.environ.get('MAGPIE_POWER_PRIVATE_SESSION')=='1'
from gi.repository import Gio,GLib
SERVICE='org.freedesktop.portal.Desktop';PATH='/org/freedesktop/portal/desktop';IFACE='org.freedesktop.portal.Inhibit'
portal_xml=Gio.DBusNodeInfo.new_for_xml('<node><interface name="org.freedesktop.portal.Inhibit"><method name="Inhibit"><arg type="s" direction="in"/><arg type="u" direction="in"/><arg type="a{sv}" direction="in"/><arg type="o" direction="out"/></method><property name="version" type="u" access="read"/></interface></node>').interfaces[0]
request_xml=Gio.DBusNodeInfo.new_for_xml('<node><interface name="org.freedesktop.portal.Request"><method name="Close"/><signal name="Response"><arg type="u"/><arg type="a{sv}"/></signal></interface></node>').interfaces[0]
mode='success';events=[];requests={};objects=[];pending=[];connections=[];failure=[]
def connect():
 c=Gio.DBusConnection.new_for_address_sync(os.environ['DBUS_SESSION_BUS_ADDRESS'],Gio.DBusConnectionFlags.AUTHENTICATION_CLIENT|Gio.DBusConnectionFlags.MESSAGE_BUS_CONNECTION,None,None);connections.append(c);return c
def own(c,yes):
 member='RequestName' if yes else 'ReleaseName';signature='(su)' if yes else '(s)';values=(SERVICE,0) if yes else (SERVICE,)
 c.call_sync('org.freedesktop.DBus','/org/freedesktop/DBus','org.freedesktop.DBus',member,GLib.Variant(signature,values),None,Gio.DBusCallFlags.NONE,3000,None)
def owner_changed(c,sender,path,iface,signal,params):
 name,old,new=params.unpack()
 if old and not new:
  for request,value in requests.items():
   if value['client']==old and value['active']:value['active']=False;events.append(['client-disconnected',request])
def close(c,sender,path,iface,member,params,invocation):
 try:
  assert path in requests and requests[path]['client']==sender
  events.append(['close',path,c.get_unique_name()])
  if mode=='close-error':invocation.return_dbus_error('org.freedesktop.portal.Error.Failed','fixture close error');return
  requests[path]['active']=False;invocation.return_value(None)
 except Exception as error:failure.append(str(error));invocation.return_dbus_error('org.freedesktop.portal.Error.Failed',str(error))
def inhibit(c,sender,path,iface,member,params,invocation):
 try:
  window,flags,options=params.unpack();assert window=='' and flags==12;assert options['reason']=='Magpie test — 常亮';assert options['handle_token'].startswith('magpie_power_')
  request='/org/freedesktop/portal/desktop/request/'+sender[1:].replace('.','_')+'/'+options['handle_token']
  objects.append((c,c.register_object(request,request_xml,close,None,None)));requests[request]=dict(client=sender,owner=c.get_unique_name(),active=True);events.append(['inhibit',request,c.get_unique_name(),flags,options['reason']])
  if mode in ('pending-cancel','pending-restart'):pending.append(invocation);return
  if mode=='reject':invocation.return_dbus_error('org.freedesktop.portal.Error.NotAllowed','fixture denied');return
  if mode in ['response-denied','response-success']:c.emit_signal(sender,request,'org.freedesktop.portal.Request','Response',GLib.Variant('(ua{sv})',(2 if mode=='response-denied' else 0,{})))
  invocation.return_value(GLib.Variant('(o)',('/unexpected/foreign/request' if mode=='wrong-path' else request,)))
 except Exception as error:failure.append(str(error));invocation.return_dbus_error('org.freedesktop.portal.Error.Failed',str(error))
def start_portal():
 c=connect();c.signal_subscribe('org.freedesktop.DBus','org.freedesktop.DBus','NameOwnerChanged','/org/freedesktop/DBus',None,Gio.DBusSignalFlags.NONE,owner_changed)
 objects.append((c,c.register_object(PATH,portal_xml,inhibit,lambda *unused:GLib.Variant('u',0 if mode=='version-zero' else 3),None)));own(c,True);return c
loop=GLib.MainLoop();threading.Thread(target=loop.run,daemon=True).start();server=start_portal()
def state(path):
 try:return json.loads(path.read_text())
 except (FileNotFoundError,json.JSONDecodeError):return {}
def wait(path,predicate,process,label):
 end=time.monotonic()+10
 while time.monotonic()<end:
  value=state(path)
  if predicate(value):return value
  if process.poll() is not None:raise AssertionError(label+': process exited '+str(process.returncode)+' '+str(value))
  time.sleep(.02)
 raise AssertionError(label+': timed out '+str(state(path)))
output=(ROOT/args.output).resolve();output.mkdir(parents=True,exist_ok=True);app=(ROOT/args.app).resolve();launch=['dotnet',str(app)] if app.suffix=='.dll' else [str(app)];results=[]
for mode in ['success','response-success','toggle','spoof','reject','response-denied','wrong-path','version-zero','pending-cancel','pending-restart','restart','close-error']:
 events.clear();requests.clear();failure.clear();pending.clear();receipt=output/(mode+'.json')
 for suffix in ['','.stop','.cancel','.disable']:pathlib.Path(str(receipt)+suffix).unlink(missing_ok=True)
 with (output/(mode+'.log')).open('w') as log:
  process=subprocess.Popen(launch+['--screen-awake-probe',str(receipt)],cwd=ROOT,stdout=log,stderr=log)
  try:
   if mode in ['pending-cancel','pending-restart']:
    end=time.monotonic()+8
    while not any(e[0]=='inhibit' for e in events):assert time.monotonic()<end;time.sleep(.02)
    if mode=='pending-cancel':pathlib.Path(str(receipt)+'.cancel').touch()
    else:own(server,False)
    initial=wait(receipt,lambda s:s.get('State') in ['cancelled','failed'],process,'pending request cancelled')
   elif mode in ['reject','response-denied','wrong-path','version-zero']:
    initial=wait(receipt,lambda s:s.get('State')=='failed' or (s.get('State')=='lost' and bool(s.get('Warning'))),process,'rejection handled');assert initial['Error'] or initial['Warning'];pathlib.Path(str(receipt)+'.stop').touch()
   else:
    initial=wait(receipt,lambda s:s.get('State')=='active' and s.get('Frames',0)>=4,process,'active reusable inhibition');assert len(requests)==1
    if mode=='toggle':
     pathlib.Path(str(receipt)+'.disable').touch();wait(receipt,lambda s:s.get('State')=='disabled' and not s.get('Active'),process,'disable releases request');assert not any(v['active'] for v in requests.values())
     pathlib.Path(str(receipt)+'.disable').unlink();wait(receipt,lambda s:s.get('State')=='active' and s.get('Attempts')==2,process,'reenable acquires once')
    if mode=='spoof':
     attacker=connect();request=next(iter(requests));attacker.emit_signal(None,request,'org.freedesktop.portal.Request','Response',GLib.Variant('(ua{sv})',(2,{})));attacker.emit_signal(None,'/org/freedesktop/DBus','org.freedesktop.DBus','NameOwnerChanged',GLib.Variant('(sss)',(SERVICE,server.get_unique_name(),'')));attacker.flush_sync(None);time.sleep(.15);assert state(receipt)['Active'] and state(receipt)['Attempts']==1
    if mode=='restart':
     previous_owner=server.get_unique_name();own(server,False);wait(receipt,lambda s:not s.get('Active') and bool(s.get('Warning')),process,'portal ownership lost');server=start_portal();recovered=wait(receipt,lambda s:s.get('Active') and s.get('Attempts')==2,process,'new portal ownership recovered');assert recovered['Warning'] is None
     assert len({v['owner'] for v in requests.values()})==2 and server.get_unique_name()!=previous_owner
    pathlib.Path(str(receipt)+'.stop').touch()
   assert process.wait(timeout=10)==0
   end=time.monotonic()+3
   while any(v['active'] for v in requests.values()) and time.monotonic()<end:time.sleep(.02)
   assert not failure,failure
   assert not any(v['active'] for v in requests.values()),events
   if mode!='version-zero':assert any(e[0]=='close' for e in events),events
   if mode=='wrong-path':assert not any(e[0]=='close' and e[1]=='/unexpected/foreign/request' for e in events)
   if mode=='close-error':assert any(e[0]=='client-disconnected' for e in events),events
   results.append(dict(case=mode,passed=True,initial=initial,final=state(receipt),events=events.copy()))
  finally:
   if process.poll() is None:process.kill();process.wait(timeout=5)
   if mode=='pending-restart':own(server,True)
 for c,identifier in objects:
  try:c.unregister_object(identifier)
  except Exception:pass
 objects.clear()
 # Keep the current server interface for the next case after unregistering per-request objects.
 objects.append((server,server.register_object(PATH,portal_xml,inhibit,lambda *unused:GLib.Variant('u',0 if mode=='version-zero' else 3),None)))
 print('PASS '+mode,flush=True)
report=dict(passed=True,private_bus=True,system_inhibition_tested=False,gui_opened=False,app_sha256=hashlib.sha256(app.read_bytes()).hexdigest(),cases=results)
(output/'results.json').write_text(json.dumps(report,indent=2)+'\n');print('PASS: '+str(len(results))+' private Inhibit portal cases; no host power or GUI changes.')
