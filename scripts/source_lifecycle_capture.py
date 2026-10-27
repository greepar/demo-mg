"""Verify source hide/resume from independent Wayland client protocol events."""
import re

PHASES=('initial','hide1','resume1','hide2','resume2','hide3')

def verify_source_lifecycle(log):
    generations={};surfaces={};xdg={};tops={};outputs=set();alive=set();acked=set();pending=set()
    phase=None;events=[];after_stop=[];popups={}
    def token(surface):return str(surface[0])+':'+str(surface[1])
    def active():return sorted(token(t) for t in outputs&alive)
    for line in log.splitlines():
        marker=re.search(r'MAGPIE_SOURCE_PHASE_(BEGIN|END):(\w+)',line)
        if marker:
            action,name=marker.groups()
            if action=='BEGIN':
                if phase is not None or len(events)>=len(PHASES) or name!=PHASES[len(events)]:raise ValueError('Missing, duplicate or unordered lifecycle phase')
                phase={'name':name,'active_before':active(),'mapped':[],'destroyed':[],
                       'popups_before':sorted(token(p) for p,parent in popups.items() if p in alive and parent in outputs),
                       'popups_destroyed':[]}
            else:
                if phase is None or phase['name']!=name:raise ValueError('Unpaired source lifecycle phase')
                phase['active_after']=active();events.append(phase);phase=None
            continue
        match=re.search(r'wl_compositor@\d+\.create_surface\(new id wl_surface@(\d+)\)',line)
        if match:
            ident=int(match[1]);generations[ident]=generations.get(ident,0)+1;surfaces[ident]=(ident,generations[ident]);alive.add(surfaces[ident])
        match=re.search(r'get_xdg_surface\(new id xdg_surface@(\d+), wl_surface@(\d+)\)',line)
        if match:xdg[int(match[1])]=surfaces.get(int(match[2]))
        match=re.search(r'xdg_surface@(\d+)\.get_toplevel\(new id xdg_toplevel@(\d+)\)',line)
        if match:tops[int(match[2])]=xdg.get(int(match[1]))
        match=re.search(r'xdg_surface@(\d+)\.get_popup\(new id xdg_popup@\d+, xdg_surface@(\d+),',line)
        if match and xdg.get(int(match[1])) is not None:popups[xdg[int(match[1])]]=xdg.get(int(match[2]))
        match=re.search(r'xdg_toplevel@(\d+)\.set_title\("Magpie ·',line)
        if match and tops.get(int(match[1])) is not None:outputs.add(tops[int(match[1])])
        match=re.search(r'xdg_surface@(\d+)\.ack_configure\(\d+\)',line)
        if match and xdg.get(int(match[1])) is not None:acked.add(xdg[int(match[1])])
        match=re.search(r'wl_surface@(\d+)\.attach\(wl_buffer@\d+,',line)
        if match and surfaces.get(int(match[1])) is not None:pending.add(surfaces[int(match[1])])
        match=re.search(r'wl_surface@(\d+)\.commit\(\)',line)
        if match:
            surface=surfaces.get(int(match[1]))
            if surface in outputs and surface in pending:
                if surface not in acked:raise ValueError('Output buffer committed before configure acknowledgement')
                if phase is not None:phase['mapped'].append(token(surface))
                if len(events)==len(PHASES):after_stop.append(token(surface))
                pending.discard(surface)
        match=re.search(r'wl_surface@(\d+)\.destroy\(\)',line)
        if match:
            surface=surfaces.get(int(match[1]))
            if surface in outputs and phase is not None:phase['destroyed'].append(token(surface))
            if surface in popups and phase is not None:phase['popups_destroyed'].append(token(surface))
            alive.discard(surface)
    if phase is not None or tuple(e['name'] for e in events)!=PHASES:raise ValueError('Incomplete source lifecycle protocol evidence')
    for event in events:
        if event['name'].startswith('hide'):
            if not set(event['popups_before']).issubset(event['popups_destroyed']):raise ValueError('Hidden source retained a native popup')
            if event['name']=='hide1' and not event['popups_before']:raise ValueError('Missing native export popup closure evidence')
            if not event['active_before'] or not set(event['active_before']).issubset(event['destroyed']) or event['active_after']:
                raise ValueError('Hidden source retained a native output surface: '+event['name'])
        elif not set(event['mapped']).intersection(event['active_after']):
            raise ValueError('Restored source never committed a configured output buffer: '+event['name'])
    if after_stop:raise ValueError('Stopped hidden source mapped an output again')
    return {'passed':True,'private_wayland':True,'phases':events,'maps_after_stop':after_stop,'source_visibility_injected':True,'real_windows_input_tested':False}
