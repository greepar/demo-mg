#!/usr/bin/env python3
"""Scalar FSRCNNX reference: 5x5 luma features, learned PReLU, residual mapping, YUV output."""
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
F32=struct.Struct('<f');F16=struct.Struct('<e')
def f32(x):return F32.unpack(F32.pack(x))[0]
def half(x):return F16.unpack(F16.pack(x))[0]
def numbers(text):return [f32(float(v.strip())) for v in text.split(',')]
OFFSETS={'tl':(-1,-1),'ml':(-1,0),'bl':(-1,1),'tc':(0,-1),'mc':(0,0),'bc':(0,1),'tr':(1,-1),'mr':(1,0),'br':(1,1)}

def parse(name):
    source=(ROOT/f'src/Effects/FSRCNNX/{name}.hlsl').read_text(encoding='utf-8-sig');graph=[]
    for number,body in enumerate(re.split(r'^//!PASS\s+\d+\s*$',source,flags=re.M)[1:],1):
        outputs=[v.strip() for v in re.search(r'^//!OUT (.+)$',body,re.M)[1].split(',')]
        variables=dict(re.findall(r'MF4\s+(\w+)\s*=\s*(\w+)\.SampleLevel',body))
        ops=[]
        for target,bias1,bias2 in re.findall(r'MF4\s+(target[12]|c[12]|result)\s*=\s*(?:MF4\(([^)]+)\)|\{([^}]+)\})\s*;',body):
            bias=numbers(bias1 or bias2)
            if number==1:
                terms=[(int(x)-2,int(y)-2,numbers(weights)) for x,y,weights in re.findall(target+r'\s*=\s*mad\(src\[(\d)\]\[(\d)\],\s*MF4\((.*?)\),\s*'+target+r'\);',body)]
                if len(terms)!=25:raise ValueError('Expected original 5x5 feature extraction')
            else:
                terms=[(variable,numbers(weights)) for variable,weights in re.findall(target+r'\s*=\s*MulAdd\((\w+),\s*MF4x4\((.*?)\),\s*'+target+r'\);',body)]
                if len(terms) not in (2,18):raise ValueError(f'Unexpected convolution shape: {target} {len(terms)}')
            activation=re.search(target+r'\s*=\s*max\('+target+r',\s*0\)\s*\+\s*MF4\((.*?)\)\s*\*\s*min\('+target+r',\s*0\)',body)
            skip=re.search(target+r'\s*\+=\s*(featureMap[12])\.SampleLevel',body)
            ops.append((target,bias,terms,numbers(activation[1]) if activation else None,skip[1] if skip else None))
        if not ops:raise ValueError('Missing network operations')
        graph.append((outputs,variables,ops))
    if len(graph)!=6:raise ValueError('Unexpected network depth')
    rgb2uv=numbers(re.search(r'MF2x3 rgb2uv\s*=\s*\{([^}]+)\}',source)[1])
    yuv2rgb=numbers(re.search(r'MF3x3 yuv2rgb\s*=\s*\{([^}]+)\}',source)[1])
    return graph,rgb2uv,yuv2rgb

def reference(network,pixels,w,h):
    graph,rgb2uv,yuv2rgb=network
    original=[[f32(v/255) for v in pixels[i:i+4]] for i in range(0,len(pixels),4)]
    def dot(a,b):
        result=f32(a[0]*b[0])
        for x,y in zip(a[1:],b[1:]):result=f32(x*y+result)
        return result
    luma=[dot(pixel[:3],[f32(.299),f32(.587),f32(.114)]) for pixel in original]
    textures={}
    def sample(image,x,y):return image[min(h-1,max(0,y))*w+min(w-1,max(0,x))]
    for number,(outputs,variables,ops) in enumerate(graph,1):
        results={name:[] for name,_,_,_,_ in ops}
        for y in range(h):
            for x in range(w):
                local={}
                for name,bias,terms,activation,skip in ops:
                    value=bias[:]
                    for term in terms:
                        if number==1:
                            dx,dy,weights=term;source=sample(luma,x+dx,y+dy)
                            for channel in range(4):value[channel]=f32(source*weights[channel]+value[channel])
                        else:
                            variable,weights=term
                            if variable in local:vector=local[variable]
                            else:
                                dx,dy=OFFSETS[variable[:-1]];vector=sample(textures[variables[variable]],x+dx,y+dy)
                            for row in range(4):
                                for channel in range(4):value[channel]=f32(vector[row]*weights[row*4+channel]+value[channel])
                    if skip:
                        vector=sample(textures[skip],x,y);value=[f32(a+b) for a,b in zip(value,vector)]
                    if activation:value=[f32(v*slope) if v<0 else v for v,slope in zip(value,activation)]
                    local[name]=value;results[name].append(value)
        if number<6:
            for i,output in enumerate(outputs):textures[output]=[[half(v) for v in vector] for vector in results[f'target{i+1}']]
            continue
        result=bytearray();features=results['result']
        for y in range(2*h):
            for x in range(2*w):
                sx,sy=x*.5-.25,y*.5-.25;ix,iy=math.floor(sx),math.floor(sy);fx,fy=sx-ix,sy-iy
                rgb=[]
                for c in range(3):
                    a,b,c0,d=[sample(original,ix+dx,iy+dy)[c] for dx,dy in ((0,0),(1,0),(0,1),(1,1))]
                    rgb.append(f32((a*(1-fx)+b*fx)*(1-fy)+(c0*(1-fx)+d*fx)*fy))
                uv=[dot(rgb,row) for row in (rgb2uv[:3],rgb2uv[3:])]
                yy=features[(y//2)*w+x//2][(x%2)*2+y%2]
                for channel in range(3):result.append(round(min(1,max(0,dot([yy,*uv],yuv2rgb[channel*3:channel*3+3])))*255))
                result.append(255)
        return result
    raise ValueError('Missing output')

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--native-test',required=True,type=Path);parser.add_argument('--output',type=Path,default=Path('artifacts/desktop/fsrcnnx-reference.json'));args=parser.parse_args()
    binary=args.native_test.resolve();reports=[];base=ROOT/'artifacts/native-fixtures';base.mkdir(parents=True,exist_ok=True)
    with tempfile.TemporaryDirectory(dir=base) as temporary:
        directory=Path(temporary);source=directory/'input.rgba';destination=directory/'output.rgba'
        for mode,name in ((9,'FSRCNNX'),(10,'FSRCNNX_LineArt')):
            network=parse(name)
            for w,h in ((1,1),(7,5),(33,25)):
                for pattern in range(3):
                    rng=random.Random(2931+pattern)
                    pixels=bytes(255 if i%4==3 else 37 if pattern==0 else i*31%251 if pattern==1 else rng.randrange(256) for i in range(w*h*4))
                    expected=reference(network,pixels,w,h);source.write_bytes(pixels)
                    run=subprocess.run([str(binary),'--effect-fixture',str(mode),str(w),str(h),str(source.relative_to(ROOT)),str(destination.relative_to(ROOT))],cwd=ROOT,capture_output=True,text=True,timeout=30)
                    if run.returncode:raise RuntimeError(run.stdout+run.stderr)
                    actual=destination.read_bytes()
                    if len(actual)!=len(expected):raise AssertionError('Incomplete CNN output')
                    errors=[abs(a-b) for a,b in zip(actual,expected)];error=max(errors)
                    print(f'{name} {w}x{h} pattern={pattern}: max channel error={error}',flush=True)
                    reports.append({'effect':name,'width':w,'height':h,'pattern':pattern,'max_channel_error':error})
                    if error>1:
                        index=errors.index(error);raise AssertionError(f'FSRCNNX mismatch at {index//4%(w*2)},{index//4//(w*2)} channel={index%4}: GPU={actual[index]}, CPU={expected[index]}')
    target=ROOT/args.output;target.parent.mkdir(parents=True,exist_ok=True);target.write_text(json.dumps({'passed':True,'native_test':str(binary),'native_test_sha256':hashlib.sha256(binary.read_bytes()).hexdigest(),'cases':reports,'reference':'scalar direct 5x5/3x3/1x1 convolutions, original PReLU, RGBA16F stores, residual and original YUV matrices'},indent=2))
    print('PASS: original FSRCNNX / LineArt six-pass CNN scalar CPU references.')
if __name__=='__main__':main()
