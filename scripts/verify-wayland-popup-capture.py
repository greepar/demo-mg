#!/usr/bin/env python3
"""Protocol-state and composition failure tests for the Wayland UI evidence path."""
import json
from pathlib import Path
import tempfile
from PIL import Image
from wayland_popup_capture import popup_state,compose

TRACE='''xdg_wm_base@1.get_xdg_surface(new id xdg_surface@2, wl_surface@3)
xdg_surface@2.get_toplevel(new id xdg_toplevel@4)
xdg_surface@2.set_window_geometry(0, 0, 100, 80)
xdg_wm_base@1.get_xdg_surface(new id xdg_surface@5, wl_surface@6)
xdg_surface@5.get_popup(new id xdg_popup@7, xdg_surface@2, xdg_positioner@8)
xdg_popup@7.configure(30, 10, 20, 16)
xdg_surface@5.configure(11)
xdg_surface@5.ack_configure(11)
wl_surface@6.commit()
MAGPIE_UI_CAPTURE_BEGIN:test
xdg_popup@7.destroy()
'''

def main():
    rejected=0
    def reject(call):
        nonlocal rejected
        try:call()
        except ValueError:rejected+=1;return
        raise AssertionError('Invalid popup evidence accepted')
    one=popup_state(TRACE,'test');assert len(one)==1 and one[0]['x']==30
    reject(lambda:popup_state(TRACE,'absent'))
    reject(lambda:popup_state(TRACE+'MAGPIE_UI_CAPTURE_BEGIN:test','test'))
    reject(lambda:popup_state(TRACE.replace('xdg_surface@5.ack_configure(11)',''),'test'))
    reject(lambda:popup_state(TRACE.replace('ack_configure(11)','ack_configure(12)'),'test'))
    reject(lambda:popup_state(TRACE.replace('wl_surface@6.commit()',''),'test'))
    reject(lambda:popup_state(TRACE.replace('xdg_surface@2, xdg_positioner','xdg_surface@99, xdg_positioner'),'test'))
    nested='''xdg_wm_base@1.get_xdg_surface(new id xdg_surface@9, wl_surface@10)
xdg_surface@9.get_popup(new id xdg_popup@12, xdg_surface@5, xdg_positioner@13)
xdg_popup@12.configure(5, 3, 10, 8)
xdg_surface@9.configure(20)
xdg_surface@9.ack_configure(20)
wl_surface@10.commit()
'''
    trace=TRACE.replace('MAGPIE_UI_CAPTURE_BEGIN:test',nested+'MAGPIE_UI_CAPTURE_BEGIN:test')
    two=popup_state(trace,'test');assert (two[1]['x'],two[1]['y'])==(35,13)
    moved='''xdg_popup@7.configure(40, 20, 20, 16)
xdg_surface@5.configure(21)
xdg_surface@5.ack_configure(21)
wl_surface@6.commit()
'''
    assert popup_state(TRACE.replace('MAGPIE_UI_CAPTURE_BEGIN:test',moved+'MAGPIE_UI_CAPTURE_BEGIN:test'),'test')[0]['x']==40
    assert popup_state(TRACE.replace('MAGPIE_UI_CAPTURE_BEGIN:test','xdg_popup@7.destroy()\nMAGPIE_UI_CAPTURE_BEGIN:test'),'test')==[]
    with tempfile.TemporaryDirectory(prefix='magpie-popup-parser-') as folder:
        path=Path(folder)/'frame.png'
        Image.new('RGBA',(100,80),'white').save(str(path)+'.base.png')
        Image.new('RGBA',(20,16),'red').save(str(path)+'.popup.png')
        data={'capture_token':'test','wayland':True,'render_scaling':1,'base':'frame.png.base.png','width':100,'height':80,'popups':[{'file':'frame.png.popup.png','width':20,'height':16}]}
        meta=Path(str(path)+'.layers.json')
        meta.write_text(json.dumps(data));report=compose(path,TRACE)
        assert report['passed'] and Image.open(path).getpixel((30,10))==(255,0,0,255) and Image.open(path).getpixel((29,10))==(255,255,255,255)
        reject(lambda:compose(path,TRACE.replace('configure(30, 10','configure(90, 10')))
        reject(lambda:compose(path,trace.replace('configure(5, 3, 10, 8)','configure(5, 3, 20, 16)')))
        data['popups'][0]['file']='../outside.png';meta.write_text(json.dumps(data));reject(lambda:compose(path,TRACE))
        data['popups'][0]['file']='frame.png.popup.png';data['render_scaling']=2;meta.write_text(json.dumps(data));reject(lambda:compose(path,TRACE))
    print(f'PASS: acknowledged/committed popup state, nesting, reposition, destruction, exact composite pixels; {rejected} invalid evidence cases rejected.')

if __name__=='__main__':main()
