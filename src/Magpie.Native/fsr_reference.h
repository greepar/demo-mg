#pragma once
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <vector>

// Scalar CPU reference: directly samples the 12 taps; no shader gather, bit mapping or dispatch code.
inline std::vector<uint8_t> easuReference(const std::vector<uint8_t>& input,int w,int h,int ow,int oh) {
    std::vector<uint8_t> output(size_t(ow)*oh*4);
    constexpr int dx[]={0,1,-1,0,0,-1,1,2,2,1,1,0};
    constexpr int dy[]={-1,-1,1,1,0,0,1,1,0,0,2,2};
    for(int y=0;y<oh;++y) for(int x=0;x<ow;++x) {
        double sx=(x+.5)*w/ow-.5,sy=(y+.5)*h/oh-.5;
        int ix=int(std::floor(sx)),iy=int(std::floor(sy));
        double px=sx-ix,py=sy-iy;
        std::array<double,3> samples[12];double luma[12];
        for(int i=0;i<12;++i) {
            for(int c=0;c<3;++c) samples[i][c]=input[(std::clamp(iy+dy[i],0,h-1)*w+std::clamp(ix+dx[i],0,w-1))*4+c]/255.0;
            luma[i]=samples[i][2]*.5+samples[i][0]*.5+samples[i][1];
        }
        double dirx=0,diry=0,length=0;
        auto direction=[&](double weight,int a,int b,int c,int d,int e) {
            double deltaX=luma[d]-luma[b],deltaY=luma[e]-luma[a];
            double denX=std::max(std::abs(luma[d]-luma[c]),std::abs(luma[c]-luma[b]));
            double denY=std::max(std::abs(luma[e]-luma[c]),std::abs(luma[c]-luma[a]));
            double lx=denX>0?std::clamp(std::abs(deltaX)/denX,0.0,1.0):0;
            double ly=denY>0?std::clamp(std::abs(deltaY)/denY,0.0,1.0):0;
            dirx+=deltaX*weight;diry+=deltaY*weight;length+=(lx*lx+ly*ly)*weight;
        };
        direction((1-px)*(1-py),0,5,4,9,3);
        direction(px*(1-py),1,4,9,8,6);
        direction((1-px)*py,4,2,3,6,11);
        direction(px*py,9,3,6,7,10);
        double norm=dirx*dirx+diry*diry;
        if(norm<1.0/32768) { dirx=1; }
        else { norm=1/std::sqrt(norm);dirx*=norm;diry*=norm; }
        length=length*.5;length*=length;
        double stretch=(dirx*dirx+diry*diry)/std::max(std::abs(dirx),std::abs(diry));
        double lengthX=1+(stretch-1)*length,lengthY=1-.5*length;
        double lobe=.5+(.25-.04-.5)*length,clip=1/lobe;
        std::array<double,3> accumulated{};double total=0;
        for(int i=0;i<12;++i) {
            double ox=dx[i]-px,oy=dy[i]-py;
            double vx=(ox*dirx+oy*diry)*lengthX,vy=(-ox*diry+oy*dirx)*lengthY;
            double distance=std::min(vx*vx+vy*vy,clip);
            double wb=.4*distance-1,wa=lobe*distance-1;
            double weight=(25.0/16*wb*wb-9.0/16)*wa*wa;
            for(int c=0;c<3;++c)accumulated[c]+=samples[i][c]*weight;
            total+=weight;
        }
        for(int c=0;c<3;++c) {
            double lo=std::min({samples[4][c],samples[9][c],samples[3][c],samples[6][c]});
            double hi=std::max({samples[4][c],samples[9][c],samples[3][c],samples[6][c]});
            output[(y*ow+x)*4+c]=uint8_t(std::round(std::clamp(accumulated[c]/total,lo,hi)*255));
        }
        output[(y*ow+x)*4+3]=255;
    }
    return output;
}
