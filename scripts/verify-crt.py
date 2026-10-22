#!/usr/bin/env python3
"""Scalar CRT-Geom reference independent of GPU coordinates and shader execution."""
import argparse,hashlib,json,math,random,re,subprocess,tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def schema():
    source=(ROOT/'src/Effects/CRT/CRT_Geom.hlsl').read_text()
    declarations=re.findall(r'//!PARAMETER\n(.*?)(?:float|int) (\w+);',source,re.S)
    assert len(declarations)==15
    return [float(re.search(r'//!DEFAULT (\S+)',metadata)[1]) for metadata,_ in declarations]
def clamp(v,lo=0,hi=1):return min(hi,max(lo,v))
def reference(pixels,w,h,ow,oh,p):
    gamma,monitor,d,curvature,radius,corner,smooth,xt,yt,ox,oy,mask,sharp,weight,lum=p
    aspect=(1.,.75);sin=(math.sin(xt),math.sin(yt));cos=(math.cos(xt),math.cos(yt))
    def dot(a,b):return sum(x*y for x,y in zip(a,b))
    def backward(xy):
        a=dot(xy,xy)+d*d;b=2*(radius*(dot(xy,sin)-d*cos[0]*cos[1])-d*d);c=d*d+2*radius*d*cos[0]*cos[1]
        v=(-b-math.sqrt(max(0,b*b-4*a*c)))/(2*a)
        point=[(v*xy[i]+radius*sin[i])/radius for i in range(2)]
        tang=[sin[i]/cos[i] for i in range(2)];poc=[point[i]/cos[i] for i in range(2)]
        a=dot(tang,tang)+1;b=-2*dot(poc,tang);c=dot(poc,poc)-1
        v=(-b+math.sqrt(max(0,b*b-4*a*c)))/(2*a)
        uv=[(point[i]-v*sin[i])/cos[i] for i in range(2)]
        r=max(abs(radius*math.acos(clamp(v,-1,1))),1e-5)
        return [u*r/math.sin(r/radius) for u in uv]
    def forward(uv):
        r=max(abs(math.sqrt(dot(uv,uv))),1e-5);uv=[v*math.sin(r/radius)/r for v in uv];x=1-math.cos(r/radius)
        D=d/radius+x*cos[0]*cos[1]+dot(uv,sin)
        return [d*(uv[i]*cos[i]-x*sin[i])/D for i in range(2)]
    center=backward([-radius*v/(1+radius/d*cos[0]*cos[1]) for v in sin]);a=[.5*v for v in aspect]
    lo=[forward((-a[0],center[1]))[0]/aspect[0],forward((center[0],-a[1]))[1]/aspect[1]]
    hi=[forward((a[0],center[1]))[0]/aspect[0],forward((center[0],a[1]))[1]/aspect[1]]
    stretch=[(hi[i]+lo[i])*aspect[i]*.5 for i in range(2)]+[max(hi[i]-lo[i] for i in range(2))]
    def sample(x,y,c):
        ix=int(clamp(math.floor(x*w),0,w-1));iy=int(clamp(math.floor(y*h),0,h-1))
        return (pixels[(iy*w+ix)*4+c]/255)**gamma
    def beam(distance,color):
        width=2+2*color**4
        return (lum+1.4)*math.exp(-(distance/weight/math.sqrt(.5*width))**width)/(.6+.2*width)
    il=clamp(math.floor(h/1000),1,2);tx=sharp*w;result=bytearray()
    for y in range(oh):
        for x in range(ow):
            xy=[(x+.5)/ow,(y+.5)/oh]
            if curvature:
                xy=backward([(xy[i]-.5)*aspect[i]*stretch[2]+stretch[i] for i in range(2)])
                xy=[xy[0]/(ox/100)/aspect[0]+.5,xy[1]/(oy/100)/aspect[1]+.5]
            cd=[(xy[i]-.5)*[ox,oy][i]/100+.5 for i in range(2)];cd=[min(v,1-v)*aspect[i] for i,v in enumerate(cd)]
            dist=math.sqrt(sum((corner-min(v,corner))**2 for v in cd));cval=clamp((corner-dist)*smooth)
            ratio=[xy[0]*tx-.5,(xy[1]*h-.5)/il];uv=[v-math.floor(v) for v in ratio]
            xy=[(math.floor(ratio[0])+.5)/tx,(math.floor(ratio[1])*il+.5)/h]
            coeffs=[max(abs(math.pi*v),1e-5) for v in (1+uv[0],uv[0],1-uv[0],2-uv[0])]
            coeffs=[2*math.sin(v)*math.sin(v/2)/(v*v) for v in coeffs];total=sum(coeffs);coeffs=[v/total for v in coeffs]
            for c in range(3):
                col=clamp(sum(coeffs[t]*sample(xy[0]+(t-1)/tx,xy[1],c) for t in range(4)))**gamma
                col2=clamp(sum(coeffs[t]*sample(xy[0]+(t-1)/tx,xy[1]+il/h,c) for t in range(4)))**gamma
                filt=h/oh
                weights=(beam(uv[1],col)+beam(uv[1]+filt/3,col)+beam(abs(uv[1]-filt/3),col))/3
                weights2=(beam(1-uv[1],col2)+beam(abs(1-uv[1]-filt/3),col2)+beam(abs(1-uv[1]+filt/3),col2))/3
                v=(col*weights+col2*weights2)*cval
                v*=1-mask if (x%2==0 and c==1 or x%2==1 and c!=1) else 1
                result.append(round(clamp(v**(1/monitor))*255))
            result.append(255)
    return result

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--native-test',type=Path,required=True);parser.add_argument('--output',type=Path,default=Path('artifacts/desktop/crt-reference.json'));args=parser.parse_args()
    binary=args.native_test.resolve();defaults=schema();reports=[];base=ROOT/'artifacts/native-fixtures';base.mkdir(parents=True,exist_ok=True)
    variants=[{}, {3:0}, {7:.12,8:-.08,9:103,10:97}, {0:1.7,1:2.6,11:.1,12:2,13:.45,14:.2}, {2:2.2,4:3.5,5:.08,6:160}, {9:110,10:90,12:3,13:.18,11:0}]
    with tempfile.TemporaryDirectory(dir=base) as temporary:
        directory=Path(temporary);source=directory/'input.rgba';target=directory/'output.rgba';parameters=directory/'parameters.txt'
        for w,h,ow,oh in ((1,1,3,3),(7,5,19,13),(17,9,31,19),(17,9,10,6)):
            for variant,changes in enumerate(variants):
                p=defaults[:]
                for i,v in changes.items():p[i]=v
                # Use the same float32 values sent through the C ABI.
                import struct
                p=[struct.unpack('f',struct.pack('f',v))[0] for v in p]
                rng=random.Random(79);pixels=bytes(255 if i%4==3 else rng.randrange(256) for i in range(w*h*4))
                expected=reference(pixels,w,h,ow,oh,p);source.write_bytes(pixels);parameters.write_text(' '.join(map(str,p)))
                run=subprocess.run([str(binary),'--crt-fixture',str(w),str(h),str(ow),str(oh),str(source.relative_to(ROOT)),str(target.relative_to(ROOT)),str(parameters.relative_to(ROOT)),'configured'],cwd=ROOT,capture_output=True,text=True,timeout=30)
                if run.returncode:raise RuntimeError(run.stdout+run.stderr)
                actual=target.read_bytes();assert len(actual)==len(expected);errors=[abs(a-b) for a,b in zip(actual,expected)];error=max(errors)
                print(f'CRT {w}x{h}->{ow}x{oh} variant={variant}: max channel error={error}',flush=True)
                reports.append({'input':[w,h],'output':[ow,oh],'variant':variant,'max_channel_error':error})
                if error>1:
                    i=errors.index(error);raise AssertionError(f'CRT mismatch pixel={i//4%ow},{i//4//ow} c={i%4}: GPU={actual[i]}, CPU={expected[i]}')
    target=ROOT/args.output;target.parent.mkdir(parents=True,exist_ok=True);target.write_text(json.dumps({'passed':True,'native_test_sha256':hashlib.sha256(binary.read_bytes()).hexdigest(),'cases':reports,'reference':'independent scalar curvature/tilt, Lanczos scanlines, beam weights, mask and gamma'},indent=2))
if __name__=='__main__':main()
