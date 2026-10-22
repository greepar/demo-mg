#!/usr/bin/env python3
"""Independent scalar equations for original LumaSharpen and LCAS, using the production ABI."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import random
import subprocess
import tempfile
from effect_catalog import registry


def reference(name, pixels, w, h, ow, oh, values):
    def sample(x, y, channel):
        ix, iy = math.floor(x), math.floor(y)
        fx, fy = x-ix, y-iy
        def load(a, b):
            return pixels[4*(max(0,min(h-1,b))*w+max(0,min(w-1,a)))+channel]/255
        return ((load(ix,iy)*(1-fx)+load(ix+1,iy)*fx)*(1-fy)
                +(load(ix,iy+1)*(1-fx)+load(ix+1,iy+1)*fx)*fy)
    output = bytearray()
    for y in range(oh):
        for x in range(ow):
            px, py = (x+.5)*w/ow-.5, (y+.5)*h/oh-.5
            if name.endswith('LumaSharpen'):
                strength, limit, pattern, bias = values
                pattern = int(pattern)
                offsets = [[(bias/3,bias/3),(-bias/3,-bias/3)],
                           [(.5*bias,-.5*bias),(-.5*bias,-.5*bias),(.5*bias,.5*bias),(-.5*bias,.5*bias)],
                           [(.4*bias,-1.2*bias),(-1.2*bias,-.4*bias),(1.2*bias,.4*bias),(-.4*bias,1.2*bias)],
                           [(.5,-bias),(-bias,-.5),(bias,.5),(-.5,bias)]][pattern]
                original = [sample(px,py,c) for c in range(3)]
                blur = [sum(sample(px+dx,py+dy,c) for dx,dy in offsets)/len(offsets) for c in range(3)]
                # The source's scaled saturate formulation equals this symmetric
                # luma clamp for positive sharpClamp. At zero limit the added
                # delta is zero. No GPU/generated code is used for this oracle.
                delta = sum((original[c]-blur[c])*k for c,k in enumerate((.2126,.7152,.0722)))
                delta = max(-limit,min(limit,delta*strength*(1.5,1,.51,.666)[pattern]))
                color = [v+delta for v in original]
            else:
                peak = -values[0]/9
                color = []
                for c in range(3):
                    a,b,d,f,g,i,j,k = [sample(px+dx,py+dy,c) for dx,dy in
                                      [(-1,-1),(0,-1),(1,-1),(-1,0),(1,0),(-1,1),(0,1),(1,1)]]
                    center = sum(sample(px+dx,py+dy,c) for dx,dy in [(.25,.5),(-.25,-.5),(.5,-.25),(-.5,.25)])/4
                    low, high = min(a,b,d,f,g,i,j,k,center), max(a,b,d,f,g,i,j,k,center)
                    weight = math.sqrt(max(0,min(low,1-high)/high))*peak if high else 0
                    diagonal, cross = a+d+i+k, b+f+g+j
                    sharpened = ((diagonal+cross)*weight+5*center-(diagonal+2*cross+4*center)/4)/(1+8*weight)
                    color.append((sharpened+4*max(low,min(high,sharpened)))/5)
            output.extend(max(0,min(255,round(v*255))) for v in color)
            output.append(255)
    return bytes(output)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--native-test',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();binary=args.native_test.resolve();args.output.parent.mkdir(parents=True,exist_ok=True)
    effects={r['path']:r['id'] for r in registry()}
    report=dict(passed=False,state='running',native_test_sha256=hashlib.sha256(binary.read_bytes()).hexdigest(),max_error_limit=1,cases=[])
    def save():args.output.write_text(json.dumps(report,indent=2)+'\n')
    save()
    with tempfile.TemporaryDirectory(prefix='magpie-sharpen-reference-') as directory:
        folder=Path(directory);source,target,params=[folder/n for n in ('input.rgba','output.rgba','parameters.txt')]
        for name in ['Sharpen/LumaSharpen','Sharpen/LCAS']:
            variants=([[.65,.035,p,1] for p in range(4)]+[[3,1,p,2.37] for p in range(4)]+[[0,.035,1,0],[3,.005,3,6],[3,0,1,1],[0,0,0,0]]) if name.endswith('LumaSharpen') else [[0],[.5],[1]]
            for w,h in [(1,1),(7,5),(17,13),(33,25)]:
                outputs=[(w,h)] if name.endswith('LumaSharpen') else list(dict.fromkeys([(w,h),(2*w+1,2*h+1),(max(1,w//2),max(1,h//2))]))
                for ow,oh in outputs:
                    for pattern in ['black','white','constant','random','edge']:
                        rng=random.Random(739)
                        pixels=bytes(119 if c==3 else 0 if pattern=='black' else 255 if pattern=='white' else 73 if pattern=='constant' else rng.randrange(256) if pattern=='random' else (255 if (x>=w//2) != (y>=h//2) else 0) for y in range(h) for x in range(w) for c in range(4))
                        source.write_bytes(pixels)
                        for variant,values in enumerate(variants):
                            params.write_text(' '.join(map(str,values)))
                            run=subprocess.run([str(binary),'--original-effect-fixture',str(effects[name]),str(w),str(h),str(ow),str(oh),str(source),str(target),str(params)],capture_output=True,text=True,timeout=60)
                            if run.returncode or 'VUID-' in run.stdout+run.stderr:raise RuntimeError(run.stdout+run.stderr)
                            actual=target.read_bytes();expected=reference(name,pixels,w,h,ow,oh,values)
                            assert len(actual)==len(expected)
                            error=max(abs(a-b) for a,b in zip(actual,expected))
                            report['cases'].append(dict(effect=name,input=[w,h],output=[ow,oh],pattern=pattern,parameters=values,max_channel_error=error,passed=error<=1))
                            if error>1:
                                report['state']='failed';save();raise AssertionError(f'{name} {w}x{h}->{ow}x{oh} {pattern} variant={variant}: CPU/GPU error={error}')
            save()
    report.update(passed=True,state='complete');save()
    print(f'PASS: {len(report["cases"])} original LumaSharpen/LCAS independent CPU/Vulkan cases; max error={max(c["max_channel_error"] for c in report["cases"])}.')

if __name__=='__main__':main()
