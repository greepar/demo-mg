#pragma once
#include "local_input_region.h"
#include <algorithm>
#include <cstdint>

namespace magpie {
struct InputPoint { double x{},y{}; };
struct InputMapping {
    LocalInputRegion destination,source,crop;
    bool map(double x,double y,bool dragging,InputPoint& result) const {
        if(!destination.valid()||!source.valid()||!crop.valid()||destination.width<=0||destination.height<=0||source.width<=0||source.height<=0||
            crop.width<=0||crop.height<=0||crop.x<0||crop.y<0||crop.x+crop.width>1||crop.y+crop.height>1||!std::isfinite(x)||!std::isfinite(y))return false;
        if(!dragging&&!destination.contains(x,y))return false;
        const double left=source.x+crop.x*source.width,top=source.y+crop.y*source.height;
        const double right=source.x+(crop.x+crop.width)*source.width,bottom=source.y+(crop.y+crop.height)*source.height;
        if(!std::isfinite(left)||!std::isfinite(top)||!std::isfinite(right)||!std::isfinite(bottom)||right<=left||bottom<=top)return false;
        const double u=std::clamp((x-destination.x)/destination.width,0.,1.);
        const double v=std::clamp((y-destination.y)/destination.height,0.,1.);
        // Input regions are half-open. A captured drag may reach or cross the
        // output's far edge; never map it onto a neighboring window/monitor.
        result={std::clamp(left+u*(right-left),left,std::nextafter(right,left)),
                std::clamp(top+v*(bottom-top),top,std::nextafter(bottom,top))};
        return true;
    }
};

struct WindowIdentity {
    uint64_t window{};
    uint32_t process{},thread{};
    bool matches(uint64_t liveWindow,uint32_t liveProcess,uint32_t liveThread) const {
        return window&&process&&thread&&window==liveWindow&&process==liveProcess&&thread==liveThread;
    }
};
}
