"""Pixel-domain AdaptiveSharpen 2015 equations; no generated shader execution."""
import math

OFFSETS=[(0,0),(-1,-1),(0,-1),(1,-1),(-1,0),(1,0),(-1,1),(0,1),(1,1),
         (0,-2),(-2,0),(2,0),(0,2),(0,3),(1,2),(-1,2),(3,0),(2,1),(2,-1),
         (-3,0),(-2,1),(-2,-1),(0,-3),(1,-2),(-1,-2)]


def reference(pixels,w,h,strength):
    def pixel(x,y):
        base=4*(max(0,min(h-1,y))*w+max(0,min(w-1,x)))
        return [pixels[base+c]/255 for c in range(3)]
    result=bytearray()
    for y in range(h):
        for x in range(w):
            colors=[pixel(x+dx,y+dy) for dx,dy in OFFSETS]
            luminance=[math.sqrt(sum(v*v for v in rgb)/3) for rgb in colors]
            blur=[(4*colors[0][c]+2*sum(colors[j][c] for j in (2,4,5,7))+sum(colors[j][c] for j in (1,3,6,8)))/16 for c in range(3)]
            edge=math.sqrt(sum((sum(abs(blur[c]-colors[j][c]) for j in range(9))+.25*sum(abs(blur[c]-colors[j][c]) for j in (9,10,11,12)))**2 for c in range(3)))/3
            edge*=min(.8+2.7*2**(-7.4*sum(blur)/3),3.2)
            center=luminance[0]
            def distance(i,j):return abs(luminance[i]-luminance[j])
            local=.03+4*(sum(distance(0,j) for j in (2,4,5,7))+.25*sum(distance(0,j) for j in (1,3,6,8)))
            weights=[]
            for i,near,diagonal in [(9,(2,24,23,22),(1,3)),(10,(20,19,21,4),(1,6)),(11,(17,5,18,16),(3,8)),(12,(13,15,7,14),(6,8))]:
                denominator=sum(distance(i,j) for j in near)+.5*sum(distance(i,j) for j in diagonal)
                weights.append(min(local/denominator,2) if denominator else 2)
            laplace=(.25*sum(luminance[j] for j in (2,4,5,7))+sum(luminance[j] for j in (1,3,6,8))+sum(luminance[j]*weights[i] for i,j in enumerate((9,10,11,12))))/(5+sum(weights))
            magnitude=.01+strength/(strength*1.5*edge**3.5+.5)-strength/(8192*(edge*2.2)**4.5+.5)
            difference=(center-laplace)*(magnitude*.8)
            ordered=sorted(luminance)
            upper=max((ordered[-2]+ordered[-1])/2,center)
            lower=min((ordered[0]+ordered[1])/2,center)
            upper_scale=max(1/(upper-center+.004),10)
            lower_scale=max(1/(center-lower+.016),10)
            positive,negative=max(difference,0),min(difference,0)
            difference=(1-.167)*math.tanh(positive*upper_scale)/upper_scale+.167*positive
            difference+=(1-.25)*math.tanh(negative*lower_scale)/lower_scale+.25*negative
            result.extend(max(0,min(255,round((v+difference)*255))) for v in colors[0])
            result.append(255)
    return bytes(result)
