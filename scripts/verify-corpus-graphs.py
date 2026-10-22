#!/usr/bin/env python3
"""Run newly compilable original graphs with the production Vulkan engine on Linux.

This is a standalone test build, not registration of these effects in the UI.
Jinc and bilateral mean also have independent scalar image references.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import random
import subprocess
import sys
from magpiefx import parse

ROOT=Path(__file__).resolve().parents[1]
NAMES=['Ani4Kv2_ArtCNN_C4F32_i2','Anime4K/Anime4K_Denoise_Bilateral_Mean','Anime4K/Anime4K_Denoise_Bilateral_Median','Anime4K/Anime4K_Denoise_Bilateral_Mode','Anime4K/Anime4K_Upscale_Denoise_UL','Anime4K/Anime4K_Upscale_UL','Jinc','k7_modernAnime_FHD_x2','xBRZ/xBRZ_5x','xBRZ/xBRZ_6x']

def reference(name,pixels,w,h,ow,oh,parameters):
    def sample(x,y,c):return pixels[(max(0,min(h-1,y))*w+max(0,min(w-1,x)))*4+c]/255
    result=bytearray()
    for y in range(oh):
        for x in range(ow):
            for c in range(3):
                if name=='Jinc':
                    pcx,pcy=(x+.5)*w/ow,(y+.5)*h/oh;ix,iy=math.floor(pcx-.5),math.floor(pcy-.5)
                    wa,wb=parameters[0]*math.pi,parameters[1]*math.pi
                    total=value=0.
                    for dy in range(4):
                        for dx in range(4):
                            distance=math.hypot(pcx-(ix+.5+dx-1),pcy-(iy+.5+dy-1))
                            weight=wa*wb if distance==0 else math.sin(distance*wa)*math.sin(distance*wb)/(distance*distance)
                            # Preserve the original fourth-row src[2][3] repeat.
                            sx=ix+(2 if dy==3 and dx==1 else dx)-1
                            total+=weight;value+=weight*sample(sx,iy+dy-1,c)
                    value/=total
                    center=[sample(ix+dx,iy+dy,c) for dy in (0,1) for dx in (0,1)]
                    value+=(min(max(value,min(center)),max(center))-value)*parameters[2]
                else:
                    center=sample(x,y,c);scale=(center+.0001)*parameters[0];total=value=0.
                    for dy in range(-2,3):
                        for dx in range(-2,3):
                            # The original mean source subtracts an unsigned
                            # KERNELHALFSIZE when constructing spatial lengths.
                            # Negative offsets become ~2^32 and have exp weight
                            # zero. Preserve that source behavior in this port;
                            # do not compare against an ideal symmetric filter.
                            if dx<0 or dy<0:continue
                            v=sample(x+dx,y+dy,c);weight=math.exp(-.5*((v-center)/scale)**2)*math.exp(-.5*(dx*dx+dy*dy))
                            total+=weight;value+=weight*v
                    value/=total
                result.append(round(max(0,min(1,value))*255))
            result.append(255)
    return result

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--audit',type=Path,required=True)
    parser.add_argument('--default-header',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--effect',action='append',choices=NAMES)
    parser.add_argument('--cxx',default='c++')
    parser.add_argument('--reuse-binaries',action='store_true')
    args=parser.parse_args()
    if sys.platform!='linux':parser.error('Standalone engine compile currently requires Linux; other host paths are not claimed')
    args.audit=args.audit.resolve();args.output=args.output.resolve();args.output.mkdir(parents=True,exist_ok=True)
    audit=json.loads((args.audit/'results.json').read_text());items={e['name']:e for e in audit['effects']}
    prefix=args.default_header.read_text().split('inline constexpr uint32_t multiSampledCount=')[0]
    report={'passed':False,'state':'running','scope':'Standalone graphs through production Vulkan engine; not desktop runtime registration','effects':[],'reference_max_error_limit':1,'source_sha256':{str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in [Path(__file__).resolve(),ROOT/'tests/graph_fixture.cpp',ROOT/'src/Magpie.Native/engine.cpp',ROOT/'src/Magpie.Native/multipass.h']}}
    def save(): (args.output/'results.json').write_text(json.dumps(report,indent=2)+'\n')
    save()
    for index,name in enumerate(args.effect or NAMES):
        item=items[name];assert item['passed']
        original=(args.audit/item['log']).parent/'kernel.h'
        folder=args.output/f'{index:02d}';folder.mkdir(exist_ok=True)
        header=folder/'kernel.h';header.write_text(prefix+original.read_text())
        binary=folder/'graph_fixture'
        inputs=[header,ROOT/'src/Magpie.Native/engine.cpp',ROOT/'src/Magpie.Native/multipass.h',ROOT/'src/Magpie.Native/gpu_profile.h',ROOT/'src/Magpie.Native/error.h',ROOT/'src/Magpie.Native/include/magpie.h',ROOT/'tests/graph_fixture.cpp']
        signature={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs};signature_path=folder/'build-inputs.json'
        reuse=args.reuse_binaries and binary.is_file() and signature_path.is_file() and json.loads(signature_path.read_text())==signature
        if not reuse:
            command=[args.cxx,'-std=c++20','-O2','-Wall','-Wextra','-Werror','-Wno-missing-field-initializers','-I'+str(folder),'-I'+str(ROOT/'src/Magpie.Native/include'),str(ROOT/'src/Magpie.Native/engine.cpp'),str(ROOT/'tests/graph_fixture.cpp'),'-lvulkan','-o',str(binary)]
            with (folder/'build.log').open('w') as log:subprocess.run(command,stdout=log,stderr=subprocess.STDOUT,check=True,timeout=120)
            signature_path.write_text(json.dumps(signature,indent=2)+'\n')
        effect=parse((ROOT/f'src/Effects/{name}.hlsl').read_text(encoding='utf-8-sig'),name)
        defaults=[p.default for p in effect.parameters];pfile=folder/'parameters.txt';source=folder/'input.rgba';target=folder/'output.rgba'
        data={'name':name,'passed':False,'passes':len(effect.passes),'binary_sha256':hashlib.sha256(binary.read_bytes()).hexdigest(),'cases':[]};report['effects'].append(data);save()
        # Keep pathological CNN graphs small; reference filters include odd
        # sizes and exact integer-coordinate boundaries, not just constant input.
        sizes=[(1,1),(7,5),(17,13)] if name in ('Jinc','Anime4K/Anime4K_Denoise_Bilateral_Mean') else [(1,1),(7,5)]
        factor=int(effect.texture('OUTPUT').width.split('*')[-1].strip()) if '*' in effect.texture('OUTPUT').width else 1
        jobs=[]
        for w,h in sizes:
            outputs=[(w*factor,h*factor)] if name!='Jinc' else [(w*2+1,h*2+1),(w,h),(w*2,h*2),(w*3,h*3),(w+2,h+1),(max(1,w//2),max(1,h//2))]
            jobs.extend((w,h,ow,oh) for ow,oh in dict.fromkeys(outputs))
        for w,h,ow,oh in jobs:
            for pattern in ['constant','random']:
                rng=random.Random(513);pixels=bytes(119 if i%4==3 else 73 if pattern=='constant' else rng.randrange(256) for i in range(w*h*4));source.write_bytes(pixels)
                variants=[defaults]
                if name=='Jinc':variants+=[[.5,.825,0],[.65,.9,1]]
                if name.endswith('_Mean'):variants+=[[.01],[1.5]]
                for variant,parameters in enumerate(variants):
                    pfile.write_text(' '.join(map(str,parameters)))
                    run=subprocess.run([str(binary),str(w),str(h),str(ow),str(oh),str(source),str(target),str(pfile)],text=True,capture_output=True,timeout=60)
                    logpath=folder/f'{w}x{h}-{ow}x{oh}-{pattern}-{variant}.log';logpath.write_text(run.stdout+run.stderr)
                    if run.returncode or 'VUID-' in run.stdout+run.stderr:raise RuntimeError(name+': '+run.stdout+run.stderr)
                    actual=target.read_bytes();assert len(actual)==ow*oh*4
                    case={'input':[w,h],'output':[ow,oh],'pattern':pattern,'variant':variant,'passed':True,'gpu_log':str(logpath.relative_to(args.output))}
                    if name in ('Jinc','Anime4K/Anime4K_Denoise_Bilateral_Mean'):
                        expected=reference(name,pixels,w,h,ow,oh,parameters);error=max(abs(a-b) for a,b in zip(actual,expected));case['max_channel_error']=error
                        if error>1:case['passed']=False;data['cases'].append(case);save();raise AssertionError(f'{name} {w}x{h} {pattern} variant={variant}: CPU/GPU error={error}')
                    data['cases'].append(case);save()
        data['passed']=True;save();print(f'GRAPH PASS: {name}, {len(data["cases"])} cases',flush=True)
    report['state']='complete';report['passed']=all(e['passed'] for e in report['effects']);save()
    print(f'PASS: {len(report["effects"])} standalone original graphs; numerical references only for Jinc and bilateral mean.')

if __name__=='__main__':main()
