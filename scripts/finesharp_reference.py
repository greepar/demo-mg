"""Independent pixel-domain FineSharp equations, with binary16 texture stores per pass.

This does not parse or execute generated shader code. The five source stages are
expressed as image operations: Gaussian, median, nonlinear detail, repair and YUV.
"""
import math
import struct

F32=struct.Struct('<f')
F16=struct.Struct('<e')
def f32(value):return F32.unpack(F32.pack(value))[0]
def half(value):return F16.unpack(F16.pack(f32(value)))[0]
def add(a,b):return f32(a+b)
def mul(a,b):return f32(a*b)
def dot(a,b):return add(add(mul(a[0],b[0]),mul(a[1],b[1])),mul(a[2],b[2]))


def reference(pixels,w,h,parameters):
    strength,equalization,final,repair=map(f32,parameters)
    kb,kr=map(f32,(.114,.299) if h<=576 else (.0722,.2126))
    kg=f32(1-kr-kb)
    matrix=[(kr,kg,kb),tuple(f32(v/(2*(1-kb))) for v in (-kr,-kg,1-kb)),tuple(f32(v/(2*(1-kr))) for v in (1-kr,-kg,-kb))]
    source=[]
    for i in range(w*h):
        rgb=[f32(pixels[4*i+c]/255) for c in range(3)]
        source.append([dot(rgb,matrix[0]),add(dot(rgb,matrix[1]),.5),add(dot(rgb,matrix[2]),.5)])
    def at(image,x,y):return image[max(0,min(h-1,y))*w+max(0,min(w-1,x))]
    cross=[(0,-1),(-1,0),(1,0),(0,1)]
    corners=[(-1,-1),(1,-1),(-1,1),(1,1)]
    def gaussian(image,x,y,channel):
        center=at(image,x,y)[channel]
        axis=[at(image,x+dx,y+dy)[channel] for dx,dy in cross]
        diagonal=[at(image,x+dx,y+dy)[channel] for dx,dy in corners]
        # Preserve the original two doublings and accumulation order.
        value=add(center,center)
        value=add(value,add(add(add(axis[0],axis[1]),axis[2]),axis[3]))
        value=add(value,value)
        value=add(value,add(add(add(diagonal[0],diagonal[1]),diagonal[2]),diagonal[3]))
        return mul(value,.0625)
    first=[]
    for y in range(h):
        for x in range(w):
            yuv=at(source,x,y)
            first.append([half(gaussian(source,x,y,0)),half(yuv[1]),half(yuv[2]),half(yuv[0])])
    second=[]
    for y in range(h):
        for x in range(w):
            values=[at(first,x+dx,y+dy)[0] for dx,dy in corners+cross]
            values.append(gaussian(first,x,y,0))
            second.append([half(sorted(values)[4]),*at(first,x,y)[1:]])
    differences=[]
    for p in second:
        delta=f32(p[3]-p[0]);squared=mul(delta,delta)
        nonlinear=f32(math.pow(f32(abs(delta)/f32(1.49/255)),f32(1/1.272)))
        sd=mul(mul(mul(math.copysign(1,delta) if delta else 0,f32(strength/255)),nonlinear),f32(squared/add(squared,f32(add(strength,.1)/(255*255)))))
        differences.append([sd])
    third=[]
    for y in range(h):
        for x in range(w):
            p=at(second,x,y);value=f32(add(p[3],at(differences,x,y)[0])-mul(equalization,gaussian(differences,x,y,0)))
            third.append([half(value),p[1],p[2],half(value)])
    fourth=[]
    for y in range(h):
        for x in range(w):
            p=at(third,x,y)
            neighborhood=[at(third,x+dx,y+dy)[3] for dy in range(-1,2) for dx in range(-1,2)]
            total=0
            for value in neighborhood:total=add(total,value)
            value=add(p[3],mul(f32(9.9),f32(p[3]-f32(total/9))))
            # Preserve the original partial sorting network. It does not
            # always put the mathematically second-largest value in slot 8;
            # replacing it by a full sort changes the original effect.
            ordered=neighborhood.copy()
            pairs=[(0,1),(2,3),(4,5),(6,7),(0,2),(4,6),(0,4),(1,3),(5,6),(3,7),(0,8),(7,8),
                   (1,2),(3,4),(5,6),(1,3),(1,5),(2,6),(4,5),(1,7),(6,7)]
            for left,right in pairs:ordered[left],ordered[right]=min(ordered[left],ordered[right]),max(ordered[left],ordered[right])
            value=max(value,min(ordered[1],p[3]));value=min(value,max(ordered[7],p[3]))
            fourth.append([half(value),p[1],p[2],p[3]])
    inverse=[(1,0,f32(2*(1-kr))),(1,f32(-2*(1-kb)*kb/kg),f32(-2*kr*(1-kr)/kg)),(1,f32(2*(1-kb)),0)]
    result=bytearray()
    for y in range(h):
        for x in range(w):
            p=at(fourth,x,y)
            values=[at(fourth,x+dx,y+dy)[0] for dx,dy in cross]
            edge=abs(f32(add(add(add(values[0],values[1]),values[2]),values[3])-mul(4,p[0])))
            mix=mul(final,f32(1-max(0,min(1,mul(edge,repair)))))
            luma=add(p[3],mul(mix,f32(p[0]-p[3])))
            yuv=[luma,f32(p[1]-.5),f32(p[2]-.5)]
            result.extend(max(0,min(255,round(dot(yuv,row)*255))) for row in inverse)
            result.append(255)
    return bytes(result)
