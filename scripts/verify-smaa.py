#!/usr/bin/env python3
"""Compare original SMAA CPU shader mathematics with real Vulkan for four presets.
CPU reference uses upstream HLSL math translated to C++/GLM, not production graph lowering.
"""
import argparse,json,hashlib,random,shutil,struct,subprocess,tempfile,os
from pathlib import Path
from smaa_reference import generate
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--native-test',required=True);p.add_argument('--output',default='artifacts/desktop/smaa-reference.json');p.add_argument('--cxx',default=os.environ.get('CXX','c++'));a=p.parse_args()
output=(ROOT/a.output).resolve();output.parent.mkdir(parents=True,exist_ok=True)
precision=subprocess.check_output([str((ROOT/a.native_test).resolve()),'--sampler-precision'],text=True).strip();device,bits=precision.rsplit('|',1);bits=int(bits);assert bits>0
report={'device':device,'device_subtexel_precision_bits':bits,'canonical_sampler_bits':8,'passed':False,'state':'running','reference':'Original SMAA.hlsli mathematics on CPU/C++/GLM; independent texture filtering and storage quantization','max_error_limit':3,'mean_error_limit':0.3,'cases':[]}
output.write_text(json.dumps(report,indent=2)+'\n')
with tempfile.TemporaryDirectory(prefix='magpie-smaa-') as temporary:
 work=Path(temporary);cpp=work/'reference.cpp';cpu=work/('reference.exe' if os.name=='nt' else 'reference');generate(cpp)
 includes=[ROOT/'tests/reference']
 if os.environ.get('MAGPIE_GLM_INCLUDE_DIR'):includes.append(Path(os.environ['MAGPIE_GLM_INCLUDE_DIR']).resolve())
 if os.environ.get('VULKAN_SDK'):includes.append(Path(os.environ['VULKAN_SDK'])/('Include' if os.name=='nt' else 'include'))
 if Path(a.cxx).name.lower() in ('cl','cl.exe'):
  command=[a.cxx,'/nologo','/std:c++20','/O2','/EHsc',*['/I'+str(p) for p in includes],str(cpp),'/Fe:'+str(cpu)]
 else:command=[a.cxx,'-std=c++20','-O2',*['-I'+str(p) for p in includes],str(cpp),'-o',str(cpu)]
 compile_result=subprocess.run(command,cwd=work,capture_output=True,text=True,encoding="utf-8",errors="replace")
 (output.parent/'smaa-cpu-build.log').write_text(compile_result.stdout+compile_result.stderr)
 if compile_result.returncode:raise SystemExit('SMAA CPU reference compile failed: '+compile_result.stderr[:1500])
 # Deliberately parse these legacy DDS payloads separately from the production DDS decoder.
 area=(ROOT/'src/Effects/SMAA/AreaTex.dds').read_bytes();search=(ROOT/'src/Effects/SMAA/SearchTex.dds').read_bytes()
 assert len(area)==128+160*560*4 and struct.unpack_from('<2I',area,12)==(560,160) and struct.unpack_from('<5I',area,88)==(32,255,65280,16711680,4278190080)
 assert len(search)==128+64*16 and struct.unpack_from('<2I',search,12)==(16,64) and struct.unpack_from('<2I',search,88)==(8,255)
 (work/'area.bin').write_bytes(area[128:]);(work/'search.bin').write_bytes(search[128:])
 cpu_env=dict(os.environ)
 for name in ('MAGPIE_SMAA_CPU_BLEND','MAGPIE_SMAA_CPU_EDGE','MAGPIE_SMAA_CPU_A'):cpu_env.pop(name,None)
 rng=random.Random(251012)
 fixtures=[]
 for w,h in [(1,1),(2,3),(7,5),(17,13),(41,35),(65,33)]:
  for pattern in ['constant','diagonal','stairs','checker','random','alpha']:
   pixels=[]
   for y in range(h):
    for x in range(w):
     if pattern=='constant':rgb=[37,113,197]
     elif pattern=='diagonal':rgb=[255,230,171] if x*h>y*w else [4,25,76]
     elif pattern=='stairs':rgb=[190,73,21] if (x//3+y//4)%2 else [10,100,235]
     elif pattern=='checker':rgb=[255,255,255] if (x+y)%2 else [0,0,0]
     else:rgb=[rng.randrange(256) for _ in range(3)]
     pixels.extend(rgb+[rng.randrange(256) if pattern=='alpha' else 255])
   fixtures.append((w,h,pattern,bytes(pixels)))
 for preset in range(4):
  for w,h,pattern,pixels in fixtures:
   src,dst,ref=work/'input.bin',work/'gpu.bin',work/'cpu.bin';src.write_bytes(pixels)
   subprocess.run([str((ROOT/a.native_test).resolve()),'--smaa-fixture',str(20+preset),str(w),str(h),str(src),str(dst)],check=True,capture_output=True)
   subprocess.run([str(cpu),str(preset),str(w),str(h),str(src),str(work/'area.bin'),str(work/'search.bin'),str(ref),'8'],check=True,capture_output=True,env=cpu_env)
   actual,expected=dst.read_bytes(),ref.read_bytes();assert len(actual)==len(expected)==w*h*4
   errors=[abs(x-y) for x,y in zip(actual,expected)];maximum=max(errors);mean=sum(errors)/len(errors)
   case={'preset':['Low','Medium','High','Ultra'][preset],'width':w,'height':h,'pattern':pattern,'max_error':maximum,'mean_error':mean,'passed':maximum<=3 and mean<=.3}
   report['cases'].append(case)
   if not case['passed']:
    stem=f"smaa-{preset}-{w}x{h}-{pattern}";shutil.copy2(src,output.parent/(stem+'-input.rgba'));shutil.copy2(dst,output.parent/(stem+'-gpu.rgba'));shutil.copy2(ref,output.parent/(stem+'-cpu.rgba'))
 report['passed']=all(c['passed'] for c in report['cases']);report['state']='complete'
 report['source_sha256']={str(path.relative_to(ROOT)):hashlib.sha256(path.read_bytes()).hexdigest() for path in [ROOT/'src/Effects/SMAA/SMAA.hlsli',ROOT/'src/Effects/SMAA/AreaTex.dds',ROOT/'src/Effects/SMAA/SearchTex.dds',ROOT/'scripts/smaa_reference.py',ROOT/'tests/reference/smaa_cpu.h']}
 output.write_text(json.dumps(report,indent=2)+'\n')
 print(f"SMAA CPU/Vulkan: {len(report['cases'])} cases, max={max(c['max_error'] for c in report['cases'])}, mean={max(c['mean_error'] for c in report['cases']):.5f}, passed={report['passed']}")
 if not report['passed']:raise SystemExit(1)
