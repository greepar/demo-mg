#pragma once
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <span>
#include <stdexcept>
#include <vector>

namespace magpie {
// CPU counterpart of CursorDrawer's Color/MaskedColor/Monochrome textures.
// Only this small cursor patch is composed here; scaling effects stay on Vulkan.
class CursorPixels {
    enum class Kind {Color,MaskedColor,Monochrome};
    Kind kind_=Kind::Color;
    uint32_t width_{},height_{};
    std::vector<std::array<uint8_t,4>> pixels_;
public:
    CursorPixels(uint32_t width,uint32_t height,std::span<const uint8_t> color,std::span<const uint8_t> mask):width_(width),height_(height) {
        if(!width||!height||width>4096||height>4096)throw std::invalid_argument("Invalid native cursor geometry");
        const size_t count=size_t(width)*height;
        if((!color.empty()&&color.size()!=count*4)||mask.size()!=count*(color.empty()?8:4))throw std::invalid_argument("Invalid native cursor bitmaps");
        bool alpha=false,invert=false;
        if(!color.empty())for(size_t i=0;i<count;++i)alpha|=color[i*4+3]!=0;
        if(!alpha)for(size_t i=0;i<count;++i) {
            if(color.empty())invert|=mask[i*4]!=0&&mask[(count+i)*4]!=0;
            else invert|=mask[i*4]!=0&&(color[i*4]!=0||color[i*4+1]!=0||color[i*4+2]!=0);
        }
        if(invert)kind_=color.empty()?Kind::Monochrome:Kind::MaskedColor;
        pixels_.resize(count);
        for(size_t i=0;i<count;++i) {
            auto& out=pixels_[i];
            if(color.empty()) {
                uint8_t a=mask[i*4],x=mask[(count+i)*4];
                if(invert)out={a,x,0,0};
                else if(a==0)out={x,x,x,255};
                else out={0,0,0,0};
            }else if(alpha) {
                uint8_t a=color[i*4+3];
                auto premultiply=[&](size_t channel){return uint8_t(std::lround(double(color[i*4+channel])*a/255.));};
                out={premultiply(2),premultiply(1),premultiply(0),a};
            }else if(invert)out={color[i*4+2],color[i*4+1],color[i*4],mask[i*4]};
            else if(mask[i*4]==0)out={color[i*4+2],color[i*4+1],color[i*4],255};
            else out={0,0,0,0};
        }
    }
    void composite(std::span<uint8_t> rgba,uint32_t width,uint32_t height,int32_t x,int32_t y,uint32_t drawWidth,uint32_t drawHeight,bool bilinear) const {
        if(!width||!height||uint64_t(width)*height>16777216||rgba.size()!=uint64_t(width)*height*4||
            !drawWidth||!drawHeight)throw std::invalid_argument("Invalid cursor composite surface");
        const int64_t left=std::max<int64_t>(0,x),top=std::max<int64_t>(0,y);
        const int64_t right=std::min<int64_t>(width,int64_t(x)+drawWidth),bottom=std::min<int64_t>(height,int64_t(y)+drawHeight);
        auto at=[&](int64_t cx,int64_t cy)->const auto& {
            return pixels_[size_t(std::clamp<int64_t>(cy,0,height_-1))*width_+size_t(std::clamp<int64_t>(cx,0,width_-1))];
        };
        for(int64_t row=top;row<bottom;++row)for(int64_t col=left;col<right;++col) {
            double u=(double(col-x)+.5)*width_/drawWidth,v=(double(row-y)+.5)*height_/drawHeight;
            const auto& point=at(int64_t(std::floor(u)),int64_t(std::floor(v)));
            auto* out=rgba.data()+(size_t(row)*width+size_t(col))*4;
            if(kind_==Kind::Monochrome)for(size_t c=0;c<3;++c)out[c]=uint8_t((out[c]&point[0])^point[1]);
            else if(kind_==Kind::MaskedColor)for(size_t c=0;c<3;++c)out[c]=point[3]>=128?uint8_t(out[c]^point[c]):point[c];
            else {
                std::array<double,4> sample{};
                if(!bilinear)for(size_t c=0;c<4;++c)sample[c]=point[c];
                else {
                    u-=.5;v-=.5;auto x0=int64_t(std::floor(u)),y0=int64_t(std::floor(v));
                    double fx=u-double(x0),fy=v-double(y0);
                    for(size_t c=0;c<4;++c)sample[c]=(at(x0,y0)[c]*(1-fx)+at(x0+1,y0)[c]*fx)*(1-fy)+
                        (at(x0,y0+1)[c]*(1-fx)+at(x0+1,y0+1)[c]*fx)*fy;
                }
                for(size_t c=0;c<3;++c)out[c]=uint8_t(std::clamp(std::lround(sample[c]+out[c]*(1-sample[3]/255.)),0l,255l));
            }
            out[3]=255;
        }
    }
};
}
