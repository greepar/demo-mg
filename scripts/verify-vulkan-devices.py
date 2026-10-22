#!/usr/bin/env python3
"""Check UUID identity after a real driver visibility change in a fresh process."""
import argparse,ctypes as C,hashlib,json,os,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
LIBRARY=ROOT/'artifacts/native/linux-x64/Debug/libmagpie_native.so'
class Info(C.Structure):
    _fields_=[('identifier',C.c_char*33),('name',C.c_char*256),('type',C.c_uint32),('vendor',C.c_uint32),('device',C.c_uint32)]
def api():
    lib=C.CDLL(str(LIBRARY));lib.mp_devices_create.restype=C.c_void_p
    lib.mp_devices_destroy.argtypes=[C.c_void_p];lib.mp_devices_count.argtypes=[C.c_void_p];lib.mp_devices_count.restype=C.c_uint32
    lib.mp_devices_get.argtypes=[C.c_void_p,C.c_uint32,C.POINTER(Info)]
    lib.mp_create_for_device.argtypes=[C.c_char_p];lib.mp_create_for_device.restype=C.c_void_p
    lib.mp_device_identifier.argtypes=[C.c_void_p];lib.mp_device_identifier.restype=C.c_char_p
    lib.mp_destroy.argtypes=[C.c_void_p];lib.mp_last_error.restype=C.c_char_p
    lib.mp_scale.argtypes=[C.c_void_p,C.c_void_p,C.c_uint32,C.c_uint32,C.c_void_p,C.c_uint32,C.c_uint32,C.c_uint32,C.c_float]
    return lib
def catalog(lib):
    snapshot=lib.mp_devices_create()
    if not snapshot:raise RuntimeError(lib.mp_last_error().decode())
    result=[]
    try:
        for i in range(lib.mp_devices_count(snapshot)):
            info=Info()
            if not lib.mp_devices_get(snapshot,i,C.byref(info)):raise RuntimeError(lib.mp_last_error().decode())
            result.append({'id':info.identifier.decode(),'name':info.name.decode(),'type':info.type})
        return result
    finally:lib.mp_devices_destroy(snapshot)
def exercise(lib,identifier):
    engine=lib.mp_create_for_device(identifier.encode())
    if not engine:raise RuntimeError(lib.mp_last_error().decode())
    try:
        actual=lib.mp_device_identifier(engine).decode()
        if identifier and identifier!=actual:raise AssertionError('Stored UUID selected another device')
        source=(C.c_uint8*4)(19,83,157,255);output=(C.c_uint8*16)()
        if not lib.mp_scale(engine,source,1,1,output,2,2,0,0):raise RuntimeError(lib.mp_last_error().decode())
        if bytes(output)!=bytes(source)*4:raise AssertionError('Selected device dispatch differs')
        return actual
    finally:lib.mp_destroy(engine)
def child():
    before=json.loads(sys.stdin.read());lib=api();after=catalog(lib)
    if not after:raise AssertionError('Filtered driver exposed no usable devices')
    visible={d['id'] for d in after};checks=[]
    for original_index,device in enumerate(before):
        identifier=device['id']
        if identifier in visible:
            exercise(lib,identifier);new_index=next(i for i,d in enumerate(after) if d['id']==identifier)
            checks.append({'uuid':identifier,'before_index':original_index,'after_index':new_index,'selected_identity_preserved':True})
        else:
            engine=lib.mp_create_for_device(identifier.encode())
            if engine:
                lib.mp_destroy(engine);raise AssertionError('Missing device silently fell back after driver filtering')
            message=lib.mp_last_error().decode()
            if 'unavailable' not in message:raise AssertionError('Missing device diagnostic was lost')
            checks.append({'uuid':identifier,'before_index':original_index,'missing_rejected':True,'diagnostic':message})
    actual=exercise(lib,'')
    print(json.dumps({'devices':after,'checks':checks,'automatic_uuid':actual,'automatic_dispatch':True}))
def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--driver',type=Path);parser.add_argument('--child',action='store_true');parser.add_argument('--output',type=Path,default=Path('artifacts/desktop/devices-driver-visibility.json'));args=parser.parse_args()
    if args.child:child();return
    if sys.platform!='linux' or args.driver is None or not args.driver.is_file():raise SystemExit('Run on Linux with --driver /absolute/path/to/a/software/icd.json')
    before=catalog(api());env=dict(os.environ,VK_DRIVER_FILES=str(args.driver.resolve()));env.pop('VK_ICD_FILENAMES',None)
    run=subprocess.run([sys.executable,str(Path(__file__).resolve()),'--child'],input=json.dumps(before),env=env,cwd=ROOT,capture_output=True,text=True,timeout=60)
    if run.returncode:raise RuntimeError(run.stdout+run.stderr)
    filtered=json.loads(run.stdout)
    if not any(c.get('missing_rejected') for c in filtered['checks']):raise AssertionError('Driver did not hide any previously available device')
    if not any(c.get('after_index')!=c['before_index'] for c in filtered['checks'] if c.get('selected_identity_preserved')):raise AssertionError('Device enumeration indices did not change')
    report={'passed':True,'before':before,'filtered':filtered,'driver':str(args.driver.resolve()),'shared_library_sha256':hashlib.sha256(LIBRARY.read_bytes()).hexdigest(),'physical_hotplug_tested':False}
    target=ROOT/args.output;target.parent.mkdir(parents=True,exist_ok=True);target.write_text(json.dumps(report,indent=2))
    print('PASS: missing UUID rejected; surviving UUID retained identity after list indices changed; Automatic dispatch recovered.')
if __name__=='__main__':main()
