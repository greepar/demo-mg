#!/usr/bin/env python3
"""Independent scalar NNEDI3 nns16/win8x4 reference, including R16F inter-pass storage."""
import argparse
import json
import math
from pathlib import Path
import random
import re
import struct
import subprocess
import tempfile
ROOT=Path(__file__).resolve().parents[1]
f32=lambda x:struct.unpack('<f',struct.pack('<f',x))[0]
half=lambda x:struct.unpack('<e',struct.pack('<e',x))[0]

def graph():
    source=(ROOT/'src/Effects/NNEDI3/NNEDI3_nns16_win8x4.hlsl').read_text()
    functions=re.findall(r'float nnedi3\(vec4 samples\[8\]\) \{(.*?)\n\}',source,re.S)
    assert len(functions)==2
    result=[]
    for body in functions:
        body=body[body.index('\n\tsum1 = W('):]
        neurons=[]
        matches=re.findall(r'sum1\s*=\s*(.*?);\s*sum2\s*=\s*(.*?);\s*WS\((-?\d+),\s*(-?\d+)\);',body,re.S)
        assert len(matches)==16
        for first,second,bias1,bias2 in matches:
            def weights(text):
                terms=re.findall(r'W\((\d),\s*(-?\d+),\s*(-?\d+),\s*(-?\d+),\s*(-?\d+)\)',text)
                assert [int(t[0]) for t in terms]==list(range(8))
                return [[struct.unpack('<f',struct.pack('<i',int(v)))[0] for v in t[1:]] for t in terms]
            decode=lambda x:struct.unpack('<f',struct.pack('<i',int(x)))[0]
            neurons.append((weights(first),weights(second),decode(bias1),decode(bias2)))
        result.append(neurons)
    return result

def predict(neurons,samples):
    total=squares=0.0
    for sample in samples:
        # Match float32 dot/accumulation but keep this independent of shader dispatch and shared memory.
        total=f32(total+f32(f32(f32(sample[0]+sample[1])+sample[2])+sample[3]))
        squares=f32(squares+f32(sum(f32(v*v) for v in sample)))
    mean=f32(total/32);variance=f32(f32(squares/32)-f32(mean*mean))
    inverse=f32(1/math.sqrt(variance)) if variance>=f32(1.192092896e-7) else 0.0
    deviation=f32(variance*inverse);vsum=wsum=0.0
    for w1,w2,b1,b2 in neurons:
        def product(weights):
            dots=[f32(sum(f32(a*b) for a,b in zip(sample,weight))) for sample,weight in zip(samples,weights)]
            value=dots[0]
            for dot in dots[1:]:value=f32(value+dot)
            return value
        first=f32(math.exp(f32(f32(product(w1)*inverse)+b1)))
        second=f32(f32(product(w2)*inverse)+b2)
        wsum=f32(wsum+first)
        vsum=f32(vsum+f32(first*f32(second/f32(1+abs(second)))))
    return max(0,min(1,f32(mean+f32(f32(f32(5*vsum)/wsum)*deviation))))

def reference(neurons,pixels,w,h):
    rgb=[[f32(v/255) for v in pixels[i:i+3]] for i in range(0,len(pixels),4)]
    ycoef=[f32(v) for v in (.2126,.7152,.0722)]
    luma=[f32(sum(f32(v*c) for v,c in zip(color,ycoef))) for color in rgb]
    def clamped(values,width,height,x,y):return values[max(0,min(height-1,y))*width+max(0,min(width-1,x))]
    # Original HLSL declares x/y as uint in group_base + x/y - border.
    # Preserve unsigned wrap before float conversion; a wrapped left/top coordinate
    # is clamped at the far edge. Do not silently reinterpret the source as signed.
    intermediate=[]
    for y in range(h):
        original=[];predicted=[]
        for x in range(w):
            samples=[[clamped(luma,w,h,(x+dx-3)&0xffffffff,(y+dy-1)&0xffffffff) for dy in range(4)] for dx in range(8)]
            original.append(half(luma[y*w+x]));predicted.append(half(predict(neurons[0],samples)))
        intermediate.extend(original);intermediate.extend(predicted)
    output=bytearray()
    ucoef=[f32(f32(n)/f32(1.8556)) for n in (-.2126,-.7152,.9278)]
    vcoef=[f32(f32(n)/f32(1.5748)) for n in (.7874,-.7152,-.0722)]
    quantize=lambda v:max(0,min(255,round(v*255)))
    for y in range(h*2):
        for x in range(w*2):
            if x%2==0:Y=intermediate[y*w+x//2]
            else:
                sx=x//2
                samples=[[clamped(intermediate,w,h*2,(sx+(i//2)-1)&0xffffffff,(y+(i%2)*4+j-3)&0xffffffff) for j in range(4)] for i in range(8)]
                Y=predict(neurons[1],samples)
            fx=x*.5-.25;fy=y*.5-.25;ix,iy=math.floor(fx),math.floor(fy);fx-=ix;fy-=iy
            taps=[clamped(rgb,w,h,ix+dx,iy+dy) for dy in (0,1) for dx in (0,1)]
            color=[]
            for channel in range(3):
                top=f32(taps[0][channel]*(1-fx)+taps[1][channel]*fx)
                bottom=f32(taps[2][channel]*(1-fx)+taps[3][channel]*fx)
                color.append(f32(top*(1-fy)+bottom*fy))
            U=f32(sum(f32(a*b) for a,b in zip(color,ucoef)));V=f32(sum(f32(a*b) for a,b in zip(color,vcoef)))
            output.extend((quantize(f32(Y+f32(f32(1.5748)*V))),quantize(f32(f32(Y-f32(f32(.187324)*U))-f32(f32(.468124)*V))),quantize(f32(Y+f32(f32(1.8556)*U))),255))
    return output

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--native-test',required=True,type=Path);parser.add_argument('--output',type=Path,default=ROOT/'artifacts/desktop/nnedi3-reference.json');args=parser.parse_args()
    args.output.parent.mkdir(exist_ok=True,parents=True);args.output.write_text(json.dumps({'passed':False,'state':'running'})+'\n')
    models=graph();reports=[]
    with tempfile.TemporaryDirectory(prefix='magpie-nnedi3-') as directory:
        source=Path(directory)/'input.rgba';target=Path(directory)/'output.rgba'
        for w,h,pattern in [(1,1,p) for p in range(3)]+[(7,5,p) for p in range(3)]+[(33,9,1),(33,9,2),(2,3,2)]:
            rng=random.Random(397+pattern)
            pixels=bytes((i*17)%256 if i%4==3 else 37 if pattern==0 else (i*31)%251 if pattern==1 else rng.randrange(256) for i in range(w*h*4))
            expected=reference(models,pixels,w,h);source.write_bytes(pixels)
            run=subprocess.run([str(args.native_test.resolve()),'--effect-fixture','13',str(w),str(h),str(source),str(target)],cwd=ROOT,text=True,capture_output=True,timeout=60)
            if run.returncode:raise RuntimeError(run.stdout+run.stderr)
            actual=target.read_bytes();assert len(actual)==len(expected)
            errors=[abs(a-b) for a,b in zip(actual,expected)];maximum=max(errors);mean=sum(errors)/len(errors)
            reports.append({'width':w,'height':h,'pattern':pattern,'max_error':maximum,'mean_error':mean})
            print(f'NNEDI3 {w}x{h} pattern {pattern}: max={maximum}, mean={mean:.5f}',flush=True)
            assert maximum<=2 and mean<.2,reports[-1]
    args.output.write_text(json.dumps({'passed':True,'effect':'NNEDI3_nns16_win8x4','cases':reports,'reference':'scalar float32 neural luma prediction, R16F intermediate and independent bilinear chroma reconstruction'},indent=2)+'\n')
    print('PASS: 9 NNEDI3 cases, original 16-neuron weights, R16F, 2D shared-memory dispatch, source/edge/block boundaries and alpha.')

if __name__=='__main__':main()
