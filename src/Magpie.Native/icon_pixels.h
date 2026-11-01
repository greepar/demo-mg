#pragma once
#include <cstdint>
#include <span>
#include <stdexcept>
#include <vector>
namespace magpie {
// IconHelper::HIcon2SoftwareBitmap rules, exposed as premultiplied RGBA.
inline std::vector<uint8_t> iconPixels(std::span<const uint8_t> colors,std::span<const uint8_t> mask) {
    if(colors.empty()||colors.size()%4||(!mask.empty()&&mask.size()!=colors.size()))throw std::invalid_argument("Invalid icon planes");
    bool alpha=false;for(size_t i=3;i<colors.size();i+=4)if(colors[i]){alpha=true;break;}
    std::vector<uint8_t> result(colors.size());
    for(size_t i=0;i<colors.size();i+=4) {
        uint8_t a=alpha?colors[i+3]:!mask.empty()&&mask[i]&&colors[i]==0&&colors[i+1]==0&&colors[i+2]==0?0:255;
        result[i]=uint8_t((uint32_t(colors[i+2])*a+127)/255);result[i+1]=uint8_t((uint32_t(colors[i+1])*a+127)/255);
        result[i+2]=uint8_t((uint32_t(colors[i])*a+127)/255);result[i+3]=a;
    }
    return result;
}
}
