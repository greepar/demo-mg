#!/usr/bin/env python3
"""Independent scalar SSimDownscaler reference: cubic, moments, half/UNORM storage and reconstruction."""
import argparse
import json
import math
from pathlib import Path
import random
import struct
import subprocess
import tempfile
ROOT=Path(__file__).resolve().parents[1]
f32=lambda x:struct.unpack('<f',struct.pack('<f',x))[0]
half=lambda x:struct.unpack('<e',struct.pack('<e',x))[0]
quantize=lambda x:max(0,min(255,round(x*255)))

def cubic(x,c=.5):
    x=abs(x)
    if x<1:return ((2-c)*x-3+c)*x*x+1
    if x<2:return ((-c*x+5*c)*x-8*c)*x+4*c
    return 0.0

def reference(pixels,w,h,ow,oh,oversharp):
    source=[[f32(v/255) for v in pixels[i:i+3]] for i in range(0,len(pixels),4)]
    def sample(data,width,height,x,y):return data[max(0,min(height-1,y))*width+max(0,min(width-1,x))]
    # Pass 1: independently evaluate the 16 cubic taps instead of the shader's nine bilinear samples.
    post=[]
    for y in range(oh):
        py=(y+.5)*h/oh-.5;iy=math.floor(py)
        for x in range(ow):
            px=(x+.5)*w/ow-.5;ix=math.floor(px)
            color=[0.,0.,0.]
            for dy in range(-1,3):
                for dx in range(-1,3):
                    weight=cubic(px-ix-dx,.75)*cubic(py-iy-dy,.75)
                    tap=sample(source,w,h,ix+dx,iy+dy)
                    for c in range(3):color[c]+=weight*tap[c]
            post.append([f32(quantize(v)/255) for v in color])
    # Pass 2: source width x output height, vertically filtered second moments, stored in half.
    vertical=[]
    for y in range(oh):
        position=(y+.5)/oh;low=math.ceil((position-2/oh)*h-.5);high=math.floor((position+2/oh)*h-.5)
        weights=[(k,cubic(((k+.5)/h-position)*oh)) for k in range(low,high+1)];total=sum(v for _,v in weights)
        for x in range(w):
            value=[sum(weight*sample(source,w,h,x,k)[c]**2 for k,weight in weights)/total for c in range(3)]
            vertical.append([half(v) for v in value])
    # Pass 3: output width x output height, horizontally filtered half second moments.
    moments=[]
    for y in range(oh):
        for x in range(ow):
            position=(x+.5)/ow;low=math.ceil((position-2/ow)*w-.5);high=math.floor((position+2/ow)*w-.5)
            weights=[(k,cubic(((k+.5)/w-position)*ow)) for k in range(low,high+1)];total=sum(v for _,v in weights)
            moments.append([half(sum(weight*sample(vertical,w,oh,k,y)[c] for k,weight in weights)/total) for c in range(3)])
    def average(data,x,y,transform):
        value=None
        for dy,wy in [(-1,.5),(0,1.),(1,.5)]:
            for dx,wx in [(-1,.5),(0,1.),(1,.5)]:
                item=transform(sample(data,ow,oh,x+dx,y+dy));weight=wx*wy/4
                if value is None:value=[0.]*len(item)
                for c in range(len(item)):value[c]+=weight*item[c]
        return value
    # Pass 4: means and local variance ratio, all four channels stored in half (ratio is not alpha opacity).
    mr=[];luma=(.2126,.7152,.0722);sigma=10/(255*255)
    for y in range(oh):
        for x in range(ow):
            mean=average(post,x,y,lambda v:v);squared=average(post,x,y,lambda v:[a*a for a in v]);high=average(moments,x,y,lambda v:v)
            sl=sum(c*max(a-b*b,0) for c,a,b in zip(luma,squared,mean));sh=sum(c*max(a-b*b,0) for c,a,b in zip(luma,high,mean))
            ratio=max(0,min(1,sh/sl)) if sl>sh else math.sqrt((sh+sigma)/(sl+sigma))*oversharp
            mr.append([half(v) for v in [*mean,ratio]])
    # Pass 5: local means/ratios and correction of the quantized post-kernel.
    output=bytearray()
    for y in range(oh):
        for x in range(ow):
            value=average(mr,x,y,lambda v:[*(v[3]*a for a in v[:3]),*v[:3],v[3]])
            color=[value[c+3]+value[6]*post[y*ow+x][c]-value[c] for c in range(3)]
            output.extend([*(quantize(v) for v in color),255])
    return output

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--native-test',type=Path,required=True);parser.add_argument('--output',type=Path,default=ROOT/'artifacts/desktop/ssim-reference.json');args=parser.parse_args()
    args.output.parent.mkdir(exist_ok=True,parents=True);args.output.write_text(json.dumps({'passed':False,'state':'running'})+'\n')
    cases=[(1,1,1,1,1.,0),(7,5,3,2,1.,0),(7,5,3,2,1.,1),(7,5,3,2,3.,2),(11,9,5,4,1.7,1),(11,9,5,4,3.,2),(33,25,17,13,1.,1),(33,25,17,13,2.,2),(17,9,9,5,1.5,2),(7,5,7,5,1.,2),(3,2,5,3,1.,1),(13,17,4,7,1.,2)]
    reports=[]
    with tempfile.TemporaryDirectory(prefix='magpie-ssim-') as directory:
        source=Path(directory)/'input.rgba';target=Path(directory)/'output.rgba'
        for w,h,ow,oh,sharpness,pattern in cases:
            rng=random.Random(733+pattern)
            pixels=bytes((i*17)%256 if i%4==3 else 37 if pattern==0 else (i*31)%251 if pattern==1 else rng.randrange(256) for i in range(w*h*4))
            expected=reference(pixels,w,h,ow,oh,sharpness);source.write_bytes(pixels)
            result=subprocess.run([str(args.native_test.resolve()),'--ssim-fixture',str(w),str(h),str(ow),str(oh),str(sharpness),str(source),str(target)],cwd=ROOT,text=True,capture_output=True,timeout=60)
            if result.returncode:raise RuntimeError(result.stdout+result.stderr)
            actual=target.read_bytes();assert len(actual)==len(expected)
            errors=[abs(a-b) for a,b in zip(actual,expected)];maximum=max(errors);mean=sum(errors)/len(errors)
            row={'input':[w,h],'output':[ow,oh],'oversharp':sharpness,'pattern':pattern,'max_error':maximum,'mean_error':mean};reports.append(row)
            print(f'SSim {w}x{h} -> {ow}x{oh}, oversharp={sharpness}, pattern={pattern}: max={maximum}, mean={mean:.5f}',flush=True)
            assert maximum<=2 and mean<.2,row
    args.output.write_text(json.dumps({'passed':True,'effect':'SSimDownscaler','cases':reports,'reference':'independent scalar 16-tap cubic, separable second moments, half/UNORM storage, local variance and reconstruction'},indent=2)+'\n')
    print('PASS: 12 SSim cases, mixed-format five-pass graph, input/output-dependent texture dimensions, parameter range, odd/block boundaries and alpha.')

if __name__=='__main__':main()
