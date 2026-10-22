#!/usr/bin/env python3
"""Stress real Vulkan chains across resizes against exact nearest-neighbor pixels."""
from pathlib import Path
import ctypes as C
import json
import random
import time

ROOT=Path(__file__).resolve().parents[1]
lib=C.CDLL(str(ROOT/'artifacts/native/linux-x64/Debug/libmagpie_native.so'))
class Step(C.Structure):
    _fields_=[('effect',C.c_uint32),('width',C.c_uint32),('height',C.c_uint32),('p1',C.c_float),('p2',C.c_float)]
lib.mp_create.restype=C.c_void_p
lib.mp_destroy.argtypes=[C.c_void_p]
lib.mp_last_error.restype=C.c_char_p
lib.mp_scale_chain.argtypes=[C.c_void_p,C.c_void_p,C.c_uint32,C.c_uint32,C.c_void_p,C.c_uint64,C.POINTER(Step),C.c_uint32]
engine=lib.mp_create()
if not engine:raise SystemExit(lib.mp_last_error().decode())
random.seed(2041)
def nearest(src,w,h,ow,oh):
    return bytes(src[(min(h-1,int((y+.5)*h/oh))*w+min(w-1,int((x+.5)*w/ow)))*4+c] for y in range(oh) for x in range(ow) for c in range(4))
started=time.monotonic()
try:
    for iteration in range(250):
        # Odd dimensions keep nearest samples away from integer texel boundaries.
        w,h=random.choice([3,5,7,11]),random.choice([3,5,7,11])
        src=bytes(random.randrange(256) if c%4!=3 else 255 for c in range(w*h*4))
        expected=src; iw,ih=w,h
        count=random.randrange(1,7)
        stages=(Step*count)()
        for index in range(count):
            ow,oh=random.choice([3,5,7,13,19]),random.choice([3,5,7,13])
            stages[index]=Step(0,ow,oh,0,0)
            expected=nearest(expected,iw,ih,ow,oh);iw,ih=ow,oh
        source=(C.c_uint8*len(src)).from_buffer_copy(src)
        output=(C.c_uint8*len(expected))()
        if not lib.mp_scale_chain(engine,source,w,h,output,len(output),stages,count):raise RuntimeError(lib.mp_last_error().decode())
        if bytes(output)!=expected:raise RuntimeError(f'Pixel mismatch in randomized chain {iteration}')
    elapsed=time.monotonic()-started
    result={'iterations':250,'stage_count_range':[1,6],'exact_pixel_comparison':True,'random_seed':2041,'elapsed_seconds':round(elapsed,3)}
    target=ROOT/'artifacts/desktop/native-stress.json';target.parent.mkdir(parents=True,exist_ok=True);target.write_text(json.dumps(result,indent=2))
    print('PASS',json.dumps(result))
finally:lib.mp_destroy(engine)
