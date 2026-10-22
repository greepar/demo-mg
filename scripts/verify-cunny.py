#!/usr/bin/env python3
"""Independent scalar reference for original CuNNy 4x12 NVL (including UNORM stores)."""
import argparse, hashlib, json, math, random, re, struct, subprocess, tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def f32(v):return struct.unpack('f',struct.pack('f',v))[0]
def numbers(s):return [f32(float(v.strip())) for v in s.split(',')]
def quantize(v):return round(min(1,max(0,v))*255)
def parse():
    source=(ROOT/'src/Effects/CuNNy2/CuNNy-4x12-NVL.hlsl').read_text()
    graph=[]
    for body in re.split(r'^//!PASS \d+\s*$',source,flags=re.M)[1:]:
        maps=dict(re.findall(r'#define L(\d)\(x, y\) V[34]\(O\((\w+), x, y\)(?:\.rgb)?\)',body))
        ops=[]
        # Process scalar equations in source order; sample variables are overwritten in later channel groups.
        for statement in re.findall(r'(?:s\d_\d_\d = L\d\([^;]+|r\d = (?:V4\([^;]+|MulAdd\([^;]+|max\([^;]+)|T\d\[gxy\] = r\d);',body):
            statement=statement.removesuffix(';')
            if m:=re.fullmatch(r'(s\d_\d_\d) = L(\d)\(([^)]+)\)',statement):
                dx,dy=map(float,m[3].split(','));ops.append(('sample',m[1],maps[m[2]],int(dx),int(dy)))
            elif m:=re.fullmatch(r'(r\d) = V4\(([^)]+)\)',statement):ops.append(('bias',m[1],numbers(m[2])))
            elif m:=re.fullmatch(r'(r\d) = MulAdd\((s\d_\d_\d), M(?:3x4|4)\(([^)]+)\), (r\d)\)',statement):
                assert m[1]==m[4];ops.append(('mad',m[1],m[2],numbers(m[3])))
            elif m:=re.fullmatch(r'(r\d) = max\(\1, 0.0\)',statement):ops.append(('relu',m[1]))
            elif m:=re.fullmatch(r'(T\d)\[gxy\] = (r\d)',statement):ops.append(('store',m[1],m[2]))
            else:raise ValueError(statement)
        assert sum(op[0]=='mad' for op in ops)==(27 if len(graph)==0 else 81)
        graph.append(ops)
    assert len(graph)==6
    return graph

def reference(graph,pixels,w,h):
    original=[[f32(v/255) for v in pixels[i:i+4]] for i in range(0,len(pixels),4)]
    textures={'INPUT':original}
    def sample(name,x,y):return textures[name][max(0,min(h-1,y))*w+max(0,min(w-1,x))]
    for index,ops in enumerate(graph):
        output={name:[] for kind,name,*_ in ops if kind=='store'}
        features=[]
        for y in range(h):
            for x in range(w):
                values={'r0':[0.]*4,'r1':[0.]*4,'r2':[0.]*4}
                for kind,name,*args in ops:
                    if kind=='sample':texture,dx,dy=args;values[name]=sample(texture,x+dx,y+dy)
                    elif kind=='bias':values[name]=args[0][:]
                    elif kind=='mad':
                        variable,weights=args;vector=values[variable];acc=values[name]
                        for row in range(len(weights)//4):
                            for col in range(4):acc[col]=f32(vector[row]*weights[row*4+col]+acc[col])
                    elif kind=='relu':values[name]=[max(0,v) for v in values[name]]
                    elif kind=='store':output[name].append([f32(quantize(v)/255) for v in values[args[0]]])
                if index==5:features.append([values['r0'],values['r1'],values['r2']])
        if index<5:textures.update(output);continue
        result=bytearray()
        for y in range(h*2):
            for x in range(w*2):
                sx,sy=x*.5-.25,y*.5-.25;ix,iy=math.floor(sx),math.floor(sy);fx,fy=sx-ix,sy-iy
                taps=[sample('INPUT',ix+dx,iy+dy) for dy in (0,1) for dx in (0,1)]
                for c in range(3):
                    top=f32(taps[0][c]*(1-fx)+taps[1][c]*fx);bottom=f32(taps[2][c]*(1-fx)+taps[3][c]*fx)
                    base=f32(top*(1-fy)+bottom*fy);residual=features[(y//2)*w+x//2][c][(y%2)*2+x%2]
                    result.append(quantize(f32(base+residual)))
                result.append(255)
        return result

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--native-test',type=Path,required=True);parser.add_argument('--output',type=Path,default=Path('artifacts/desktop/cunny-reference.json'));args=parser.parse_args()
    binary=args.native_test.resolve();graph=parse();reports=[];base=ROOT/'artifacts/native-fixtures';base.mkdir(exist_ok=True,parents=True)
    with tempfile.TemporaryDirectory(dir=base) as temp:
        source=Path(temp)/'input.rgba';target=Path(temp)/'output.rgba'
        for w,h in ((1,1),(7,5),(17,9)):
            for pattern in range(3):
                rng=random.Random(211+pattern);pixels=bytes(255 if i%4==3 else 37 if pattern==0 else i*31%251 if pattern==1 else rng.randrange(256) for i in range(w*h*4))
                expected=reference(graph,pixels,w,h);source.write_bytes(pixels)
                run=subprocess.run([str(binary),'--effect-fixture','11',str(w),str(h),str(source.relative_to(ROOT)),str(target.relative_to(ROOT))],cwd=ROOT,capture_output=True,text=True,timeout=60)
                if run.returncode:raise RuntimeError(run.stdout+run.stderr)
                actual=target.read_bytes();assert len(actual)==len(expected)
                errors=[abs(a-b) for a,b in zip(actual,expected)];error=max(errors)
                print(f'CuNNy 4x12 {w}x{h} pattern={pattern}: max channel error={error}',flush=True)
                reports.append({'width':w,'height':h,'pattern':pattern,'max_channel_error':error})
                if error>1:
                    i=errors.index(error);raise AssertionError(f'CuNNy mismatch byte={i}: GPU={actual[i]}, CPU={expected[i]}')
    target=ROOT/args.output;target.parent.mkdir(parents=True,exist_ok=True);target.write_text(json.dumps({'passed':True,'native_test_sha256':hashlib.sha256(binary.read_bytes()).hexdigest(),'cases':reports,'reference':'Original 3x3 RGB / feature convolutions, ordered float32 MulAdd, ReLU and RGBA8 UNORM stores, RGB residual + bilinear skip'},indent=2))
if __name__=='__main__':main()
