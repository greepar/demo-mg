"""Compose preview layers from acknowledged xdg-popup positions, never global coordinates."""
import hashlib
import json
import re
from pathlib import Path


def popup_state(trace, token):
    marker='MAGPIE_UI_CAPTURE_BEGIN:'+token
    if trace.count(marker)!=1:raise ValueError('Missing or ambiguous UI capture protocol marker')
    surfaces={};popups={};geometry={};pending={}
    for line in trace.split(marker)[0].splitlines():
        if m:=re.search(r'xdg_wm_base@\d+\.get_xdg_surface\(new id xdg_surface@(\d+), wl_surface@(\d+)\)',line):
            surfaces[int(m[1])]={'wl':int(m[2]),'role':'unknown','acked':False}
        elif m:=re.search(r'xdg_surface@(\d+)\.get_toplevel\(',line):
            surfaces[int(m[1])]['role']='toplevel'
        elif m:=re.search(r'xdg_surface@(\d+)\.get_popup\(new id xdg_popup@(\d+), xdg_surface@(\d+),',line):
            surface,popup,parent=map(int,m.groups())
            surfaces[surface]['role']='popup'
            popups[popup]={'surface':surface,'parent':parent,'position':None,'committed':False}
        elif m:=re.search(r'xdg_popup@(\d+)\.configure\((-?\d+), (-?\d+), (\d+), (\d+)\)',line):
            popup=int(m[1]);popups[popup]['position']=tuple(map(int,m.groups()[1:]));popups[popup]['committed']=False
            surfaces[popups[popup]['surface']]['acked']=False
        elif m:=re.search(r'xdg_surface@(\d+)\.configure\((\d+)\)',line):
            pending[int(m[1])]=int(m[2])
        elif m:=re.search(r'xdg_surface@(\d+)\.ack_configure\((\d+)\)',line):
            surface,serial=map(int,m.groups())
            if pending.get(surface)==serial:surfaces[surface]['acked']=True
        elif m:=re.search(r'wl_surface@(\d+)\.commit\(\)',line):
            wl=int(m[1])
            for popup in popups.values():
                surface=surfaces[popup['surface']]
                if surface['wl']==wl and surface['acked'] and popup['position']:popup['committed']=True
        elif m:=re.search(r'xdg_surface@(\d+)\.set_window_geometry\((-?\d+), (-?\d+), (\d+), (\d+)\)',line):
            geometry[int(m[1])]=tuple(map(int,m.groups()[1:]))
        elif m:=re.search(r'xdg_popup@(\d+)\.(?:destroy|popup_done)\(',line):
            popups.pop(int(m[1]),None)
        elif m:=re.search(r'xdg_surface@(\d+)\.destroy\(',line):
            surfaces.pop(int(m[1]),None);geometry.pop(int(m[1]),None);pending.pop(int(m[1]),None)
    by_surface={p['surface']:p for p in popups.values()}
    def position(popup,seen):
        surface=popup['surface']
        if surface in seen:raise ValueError('Popup parent cycle')
        if not popup['committed']:raise ValueError('Popup configuration was not acknowledged and committed before capture')
        x,y,w,h=popup['position'];parent=popup['parent']
        if parent in by_surface:
            px,py,root=position(by_surface[parent],seen|{surface});return x+px,y+py,root
        if surfaces.get(parent,{}).get('role')!='toplevel':raise ValueError('Popup parent is not a live toplevel')
        return x,y,parent
    result=[]
    for popup_id,popup in popups.items():
        x,y,root=position(popup,set());w,h=popup['position'][2:]
        if root not in geometry:raise ValueError('Missing parent toplevel geometry')
        result.append({'popup_id':popup_id,'surface_id':popup['surface'],'parent_surface_id':popup['parent'],'root_surface_id':root,'x':x,'y':y,'width':w,'height':h,'parent_geometry':geometry[root],'acknowledged_and_committed':True})
    return result


def compose(target, trace):
    from PIL import Image
    target=Path(target);metadata_path=Path(str(target)+'.layers.json')
    metadata=json.loads(metadata_path.read_text())
    if not metadata['wayland']:return None
    scale=metadata['render_scaling']
    if scale<=0:raise ValueError('Invalid capture scale')
    native=popup_state(trace,metadata['capture_token'])
    layers=metadata['popups']
    if len(native)!=len(layers):raise ValueError(f'Native popup count {len(native)} differs from capture layers {len(layers)}')
    def local(name):
        if Path(name).name!=name:raise ValueError('Capture layer must be a sibling file')
        return target.parent/name
    base=Image.open(local(metadata['base'])).convert('RGBA')
    if base.size!=(metadata['width'],metadata['height']):raise ValueError('Base capture size mismatch')
    evidence=[];remaining=native[:]
    # Both lists are created parent-before-child. Equal-sized unrelated layers
    # would be ambiguous: reject rather than assign by guessed creation order.
    for layer in layers:
        matches=[p for p in remaining if abs(p['width']/scale-layer['width'])<=1 and abs(p['height']/scale-layer['height'])<=1]
        if len(matches)!=1:raise ValueError('Missing or ambiguous popup layer/geometry match')
        p=matches[0];remaining.remove(p)
        x,y=round(p['x']/scale),round(p['y']/scale)
        image=Image.open(local(layer['file'])).convert('RGBA')
        if image.size!=(layer['width'],layer['height']):raise ValueError('Popup capture size mismatch')
        gx,gy,pw,ph=p['parent_geometry']
        if gx or gy:raise ValueError('Nonzero parent window geometry origin requires an explicit capture transform')
        if abs(pw/scale-base.width)>1 or abs(ph/scale-base.height)>1:raise ValueError('Popup belongs to a different toplevel capture')
        if x<0 or y<0 or x+image.width>base.width+1 or y+image.height>base.height+1:raise ValueError('Native popup extends outside the application viewport')
        base.alpha_composite(image,(x,y))
        evidence.append(p|{'layer':layer['file'],'capture_x':x,'capture_y':y,'layer_sha256':hashlib.sha256(local(layer['file']).read_bytes()).hexdigest()})
    base.save(target)
    report={'passed':True,'method':'Acknowledged and committed xdg_popup.configure; parent-relative positions recursively accumulated. Rendered app layers composited, not a compositor framebuffer screenshot.','render_scaling':scale,'popups':evidence,'trace_sha256':hashlib.sha256(trace.encode()).hexdigest(),'capture_sha256':hashlib.sha256(target.read_bytes()).hexdigest()}
    Path(str(target)+'.wayland.json').write_text(json.dumps(report,indent=2)+'\n')
    return report
