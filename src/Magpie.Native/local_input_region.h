#pragma once
#include <cmath>

namespace magpie {
struct LocalInputRegion {
    double x=0,y=0,width=0,height=0;
    bool valid() const {
        return std::isfinite(x)&&std::isfinite(y)&&std::isfinite(width)&&std::isfinite(height)&&width>=0&&height>=0&&std::isfinite(x+width)&&std::isfinite(y+height);
    }
    bool contains(double px,double py) const {
        return valid()&&width>0&&height>0&&px>=x&&py>=y&&px<x+width&&py<y+height;
    }
};
}
