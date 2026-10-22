#!/usr/bin/env python3
"""Scalar RAVU-Lite R2 reference using the original half-float DDS LUT."""
import argparse,json,math,random,struct,subprocess,tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
f32=lambda x:struct.unpack('<f',struct.pack('<f',x))[0]
def dot(a,b):
    value=f32(a[0]*b[0])
    for x,y in zip(a[1:],b[1:]):value=f32(x*y+value)
    return value

def reference(pixels,w,h):
    data=(ROOT/'src/Effects/RAVU/ravu_lite_lut2_f16.dds').read_bytes()
    assert data[:4]==b'DDS ' and struct.unpack_from('<2I',data,12)==(288,5) and struct.unpack_from('<I',data,84)[0]==113 and len(data)==128+5*288*8
    lut=list(struct.iter_unpack('<4e',data[128:]));rgb=[[f32(v/255) for v in pixels[i:i+3]] for i in range(0,len(pixels),4)]
    luma=[dot(v,[f32(c) for c in (.2126,.7152,.0722)]) for v in rgb]
    def sample(data,x,y):return data[max(0,min(h-1,y))*w+max(0,min(w-1,x))]
    predictions=[]
    for y in range(h):
        for x in range(w):
            p=[[sample(luma,x+dx-1,y+dy-1) for dy in range(3)] for dx in range(3)]
            gradients=[(p[1][0]-p[0][0],p[0][1]-p[0][0],0),(p[1][1]-p[0][1],(p[0][2]-p[0][0])/2,1),(p[1][2]-p[0][2],p[0][2]-p[0][1],0),((p[2][0]-p[0][0])/2,p[1][1]-p[1][0],1),((p[2][1]-p[0][1])/2,(p[1][2]-p[1][0])/2,2),((p[2][2]-p[0][2])/2,p[1][2]-p[1][1],1),(p[2][0]-p[1][0],p[2][1]-p[2][0],0),(p[2][1]-p[1][1],(p[2][2]-p[2][0])/2,1),(p[2][2]-p[1][2],p[2][2]-p[2][1],0)]
            tensor=[0.,0.,0.];weights=[f32(v) for v in (.1018680644198163,.11543163961422666,.13080118386382833)]
            for gx,gy,index in gradients:
                gx,gy=f32(gx),f32(gy)
                for i,v in enumerate((f32(gx*gx),f32(gx*gy),f32(gy*gy))):tensor[i]=f32(v*weights[index]+tensor[i])
            a,b,d=tensor;T=f32(a+d);D=f32(f32(a*d)-f32(b*b));delta=f32(math.sqrt(max(f32(f32(T*T)/4-D),0)))
            l1=f32(T/2+delta);l2=f32(T/2-delta);s1=f32(math.sqrt(max(l1,0)));s2=f32(math.sqrt(max(l2,0)))
            pi=f32(math.pi);theta=0 if abs(b)<f32(1.192092896e-7) else f32(math.fmod(f32(f32(math.atan2(f32(l1-a),b))+pi),pi))
            coherence_value=0 if f32(s1+s2)<f32(1.192092896e-7) else f32(f32(s1-s2)/f32(s1+s2))
            angle=math.floor(f32(f32(theta*24)/pi));strength=3 if s1>=f32(.05) else 2 if s1>=f32(.016) else 1 if s1>=f32(.004) else 0
            coherence=2 if coherence_value>=.5 else 1 if coherence_value>=.25 else 0
            row=max(0,min(287,(angle*4+strength)*3+coherence));result=[0.]*4
            pairs=[((0,0),(2,2)),((0,1),(2,1)),((0,2),(2,0)),((1,0),(1,2))]
            for column,(first,second) in enumerate(pairs):
                values=lut[row*5+column]
                for c in range(4):result[c]=f32(result[c]+f32(f32(p[first[0]][first[1]]*values[c])+f32(p[second[0]][second[1]]*values[3-c])))
            for c in range(4):result[c]=max(0,min(1,f32(result[c]+f32(p[1][1]*lut[row*5+4][c]))))
            predictions.append(result)
    u=[f32(f32(v)/f32(1.8556)) for v in (-.2126,-.7152,.9278)];v=[f32(f32(t)/f32(1.5748)) for t in (.7874,-.7152,-.0722)]
    output=bytearray();quantize=lambda x:max(0,min(255,round(x*255)))
    for y in range(h*2):
        for x in range(w*2):
            px=x/2-.25;py=y/2-.25;ix=math.floor(px);iy=math.floor(py);fx=px-ix;fy=py-iy
            taps=[sample(rgb,ix+dx,iy+dy) for dy in (0,1) for dx in (0,1)]
            color=[f32(f32(taps[0][c]*(1-fx)+taps[1][c]*fx)*(1-fy)+f32(taps[2][c]*(1-fx)+taps[3][c]*fx)*fy) for c in range(3)]
            U,V=dot(color,u),dot(color,v);Y=predictions[(y//2)*w+x//2][(x%2)*2+y%2]
            output.extend((quantize(f32(Y+f32(1.5748)*V)),quantize(f32(Y-f32(.187324)*U-f32(.468124)*V)),quantize(f32(Y+f32(1.8556)*U)),255))
    return output

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--native-test',required=True,type=Path);parser.add_argument('--output',type=Path,default=ROOT/'artifacts/desktop/ravu-reference.json');args=parser.parse_args()
    args.output.parent.mkdir(exist_ok=True,parents=True);args.output.write_text(json.dumps({'passed':False,'state':'running'})+'\n');reports=[]
    with tempfile.TemporaryDirectory(prefix='magpie-ravu-') as directory:
        source=Path(directory)/'input.rgba';target=Path(directory)/'output.rgba'
        for w,h in ((1,1),(7,5),(33,17),(3,2)):
            for pattern in range(3):
                rng=random.Random(811+pattern);pixels=bytes((i*17)%256 if i%4==3 else 37 if pattern==0 else (i*31)%251 if pattern==1 else rng.randrange(256) for i in range(w*h*4));source.write_bytes(pixels);expected=reference(pixels,w,h)
                run=subprocess.run([str(args.native_test.resolve()),'--effect-fixture','19',str(w),str(h),str(source),str(target)],cwd=ROOT,text=True,capture_output=True,timeout=60)
                if run.returncode:raise RuntimeError(run.stdout+run.stderr)
                actual=target.read_bytes();assert len(actual)==len(expected);errors=[abs(a-b) for a,b in zip(actual,expected)];maximum=max(errors);mean=sum(errors)/len(errors)
                print(f'RAVU {w}x{h} pattern={pattern}: max={maximum}, mean={mean:.5f}',flush=True);row={'width':w,'height':h,'pattern':pattern,'max_error':maximum,'mean_error':mean};reports.append(row)
                assert maximum<=2 and mean<.2,row
    args.output.write_text(json.dumps({'passed':True,'effect':'RAVU_Lite_R2','cases':reports,'reference':'independent scalar tensor/eigenvalue classification, original binary16 DDS LUT, paired 3x3 filter and bilinear chroma'},indent=2)+'\n');print('PASS: RAVU-Lite R2, 12 SOURCE/LUT CPU reference cases.')
if __name__=='__main__':main()
