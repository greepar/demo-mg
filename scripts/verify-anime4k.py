#!/usr/bin/env python3
"""Independent scalar CNN reference: direct clamped taps, signed half stores, depth-to-space."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import random
import re
import struct
import subprocess
import tempfile

ROOT=Path(__file__).resolve().parents[1]
MODELS=((7,'Anime4K_Upscale_S'),(8,'Anime4K_Upscale_Denoise_L'),(15,'Anime4K_Upscale_L'),(16,'Anime4K_Upscale_VL'),(17,'Anime4K_Upscale_Denoise_S'),(18,'Anime4K_Upscale_Denoise_VL'))
F32=struct.Struct('<f');F16=struct.Struct('<e')
def f32(v):return F32.unpack(F32.pack(v))[0]
def half(v):return F16.unpack(F16.pack(v))[0]
def numbers(text):return [f32(float(v.strip())) for v in text.split(',')]
def parse(name):
    source=(ROOT/f'src/Effects/Anime4K/{name}.hlsl').read_text(encoding='utf-8-sig')
    graph=[]
    for body in re.split(r'^//!PASS\s+\d+\s*$',source,flags=re.M)[1:]:
        outputs=[v.strip() for v in re.search(r'^//!OUT (.+)$',body,re.M)[1].split(',')]
        variables={}
        for variable,resource,position in re.findall(r'MF4\s+([a-i][0-9]*)\s*=\s*([A-Za-z_]\w*)\.SampleLevel\(sam,\s*(pos[^\n;]*),\s*0\)',body):
            position=position.replace(' ','')
            if position=='pos':delta=[0,0]
            elif position=='pos-inputPt':delta=[-1,-1]
            elif position=='pos+inputPt':delta=[1,1]
            else:
                offset=re.fullmatch(r'pos\+(?:MF2|float2)\(([^)]+)\)',position)
                if not offset:raise ValueError('Unsupported sample coordinates '+position)
                delta=[]
                for axis,text in zip(('x','y'),offset[1].split(',')):
                    if text=='0':delta.append(0)
                    elif text=='inputPt.'+axis:delta.append(1)
                    elif text=='-inputPt.'+axis:delta.append(-1)
                    else:raise ValueError('Unsupported source sample offset '+text)
                if len(delta)!=2:raise ValueError('Invalid source sample offset')
            variables[variable]=(resource,*delta)
        targets=[]
        for target,bias_brace,bias_constructor in re.findall(r'MF4\s+(result|target\d+)\s*=\s*(?:\{([^}]+)\}|MF4\(([^)]+)\))',body):
            bias=bias_brace or bias_constructor
            terms=[]
            for expr,rows,weights in re.findall(r'\b'+target+r'\s*=\s*MulAdd\((.*?),\s*MF([34])x4\((.*?)\),\s*'+target+r'\);',body,re.S):
                if 'src[' in expr:
                    offsets=re.search(r'src\[i\s*([+-]\s*\d+)?\]\[j\s*([+-]\s*\d+)?\]',expr)
                    dx,dy=[int(v.replace(' ','')) if v else 0 for v in offsets.groups()]
                    resource='INPUT';activation=0 if not expr.startswith('max') else -1 if '-src' in expr else 1
                    if resource=='INPUT' and int(rows)==4:resource=re.search(r'//!IN ([A-Za-z_]\w*)',body)[1]
                else:
                    variable=re.search(r'\b(n?[a-i][0-9]*)\b',expr)[1]
                    negative=variable.startswith('n') or expr.startswith('max(-')
                    variable=variable.removeprefix('n');resource,dx,dy=variables[variable];activation=-1 if negative else 1
                terms.append((resource,dx,dy,activation,int(rows),numbers(weights)))
            if len(terms) not in (9,18,28,36):raise ValueError(f'Unexpected convolution term count: {target} {len(terms)}')
            targets.append((numbers(bias),terms))
        if not targets:raise ValueError('Missing convolution targets')
        if outputs!=['OUTPUT'] and len(targets)!=len(outputs):raise ValueError('Resource graph/target count mismatch')
        graph.append((outputs,targets))
    if len(graph) not in (4,8):raise ValueError('Unexpected network depth')
    return graph

def reference(graph,pixels,w,h,fp16=False):
    accumulate=half if fp16 else f32
    resources={'INPUT':[[f32(v/255) for v in pixels[i:i+4]] for i in range(0,len(pixels),4)]}
    def sample(resource,x,y):return resources[resource][min(h-1,max(0,y))*w+min(w-1,max(0,x))]
    for outputs,targets in graph:
        images=[]
        for bias,terms in targets:
            image=[]
            for y in range(h):
                for x in range(w):
                    result=[half(v) for v in bias] if fp16 else bias[:]
                    for resource,dx,dy,activation,rows,weights in terms:
                        vector=sample(resource,x+dx,y+dy)
                        for row in range(rows):
                            source_value=half(vector[row]) if fp16 else vector[row]
                            value=source_value if activation==0 else max(0,source_value*activation)
                            for channel in range(4):
                                weight=half(weights[row*4+channel]) if fp16 else weights[row*4+channel]
                                result[channel]=half(half(value*weight)+result[channel]) if fp16 else accumulate(value*weight+result[channel])
                    image.append([half(v) for v in result] if outputs!=['OUTPUT'] else result)
            images.append(image)
        if outputs!=['OUTPUT']:
            resources.update(zip(outputs,images));continue
        result=bytearray()
        for y in range(2*h):
            for x in range(2*w):
                sx,sy=x*.5-.25,y*.5-.25;ix,iy=math.floor(sx),math.floor(sy);fx,fy=sx-ix,sy-iy
                index=(y//2)*w+x//2;subpixel=(y%2)*2+x%2
                for channel in range(3):
                    a,b,c,d=[sample('INPUT',ix+dx,iy+dy)[channel] for dx,dy in ((0,0),(1,0),(0,1),(1,1))]
                    base=(a*(1-fx)+b*fx)*(1-fy)+(c*(1-fx)+d*fx)*fy
                    residual=images[channel if len(images)==3 else 0][index][subpixel]
                    color=half(f32(base+residual)) if fp16 else f32(base+residual)
                    result.append(round(min(1,max(0,color))*255))
                result.append(255)
        return result
    raise ValueError('Missing final output')

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--native-test',required=True,type=Path)
    parser.add_argument('--output',default='artifacts/desktop/anime4k-reference.json',type=Path)
    parser.add_argument('--fp16',action='store_true')
    args=parser.parse_args();reports=[]
    target=ROOT/args.output;target.parent.mkdir(parents=True,exist_ok=True);target.write_text(json.dumps({"passed":False,"state":"running"})+"\n")
    binary=args.native_test.resolve();base=ROOT/'artifacts/native-fixtures';base.mkdir(parents=True,exist_ok=True)
    with tempfile.TemporaryDirectory(dir=base) as temporary:
        directory=Path(temporary);input_path=directory/'input.rgba';output_path=directory/'output.rgba'
        for mode,name in MODELS:
            graph=parse(name)
            for w,h in ((1,1),(7,5),(17,9)):
                for pattern in range(3):
                    rng=random.Random(2093+pattern)
                    pixels=bytes(255 if i%4==3 else 37 if pattern==0 else (i*31)%251 if pattern==1 else rng.randrange(256) for i in range(w*h*4))
                    expected=reference(graph,pixels,w,h,args.fp16);input_path.write_bytes(pixels)
                    command=[str(binary),'--anime-fixture',str(mode),str(w),str(h),str(input_path.relative_to(ROOT)),str(output_path.relative_to(ROOT))]
                    if args.fp16:
                        parameters=directory/'parameters.txt';parameters.write_text('')
                        command=[str(binary),'--original-effect-fixture-fp16',str(mode),str(w),str(h),str(2*w),str(2*h),str(input_path),str(output_path),str(parameters)]
                    run=subprocess.run(command,cwd=ROOT,capture_output=True,text=True,timeout=30)
                    if run.returncode or 'VUID-' in run.stdout+run.stderr:raise RuntimeError(run.stdout+run.stderr)
                    actual=output_path.read_bytes()
                    if len(actual)!=len(expected):raise AssertionError('Incomplete CNN output')
                    errors=[abs(a-b) for a,b in zip(actual,expected)];error=max(errors)
                    print(f'{name} {w}x{h} pattern={pattern}: max channel error={error}',flush=True)
                    reports.append({'effect':name,'width':w,'height':h,'pattern':pattern,'max_channel_error':error})
                    if error>1:
                        index=errors.index(error);raise AssertionError(f'CNN reference mismatch at {index//4%(w*2)},{index//4//(w*2)} channel {index%4}: GPU={actual[index]}, CPU={expected[index]}')
    target=ROOT/args.output;target.parent.mkdir(parents=True,exist_ok=True);target.write_text(json.dumps({'passed':True,'native_test':str(binary),'native_test_sha256':hashlib.sha256(binary.read_bytes()).hexdigest(),'cases':reports,'precision':'fp16' if args.fp16 else 'fp32','reference':'scalar original coefficients, direct clamped taps, '+('binary16 weights/activations, separate RNE multiply/add' if args.fp16 else 'float32 accumulate')+'/half intermediate, original RGB skip connection'},indent=2))
    print('PASS: six original Anime4K models, four/eight-pass CPU references, 54 cases.')
if __name__=='__main__':main()
