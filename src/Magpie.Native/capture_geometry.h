#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <limits>
#include <optional>
#include <stdexcept>

namespace magpie {
struct CaptureSize {uint32_t width{},height{};bool operator==(const CaptureSize&) const=default;};
struct CaptureContent {double x{},y{},width{},height{},scaleFactor{1},contentScale{1};};
struct CaptureGeometry {
    uint32_t x{},y{};CaptureSize crop,natural;
    bool metadata{},complete{};
};
inline CaptureSize captureSize(double width,double height) {
    width=std::ceil(width-1e-7);height=std::ceil(height-1e-7);
    if(!std::isfinite(width)||!std::isfinite(height)||width<1||height<1||width>16777216||height>16777216||width*height>16777216)
        throw std::runtime_error("Unsupported source dimensions");
    return {uint32_t(width),uint32_t(height)};
}
inline CaptureGeometry captureGeometry(CaptureSize buffer,std::optional<CaptureContent> content) {
    captureSize(buffer.width,buffer.height);
    if(!content)return {0,0,buffer,buffer,false,true};
    const auto& c=*content;
    if((c.width==0||c.height==0) && std::isfinite(c.width)&&std::isfinite(c.height)&&c.width>=0&&c.height>=0)return {0,0,{}, {},true,false};
    if(!std::isfinite(c.x)||!std::isfinite(c.y)||!std::isfinite(c.scaleFactor)||!std::isfinite(c.contentScale)||c.scaleFactor<=0||c.contentScale<=0||c.width<=0||c.height<=0)
        throw std::runtime_error("Invalid capture content geometry");
    CaptureGeometry result;result.metadata=true;
    result.natural=captureSize(c.width*c.scaleFactor/c.contentScale,c.height*c.scaleFactor/c.contentScale);
    double left=c.x*c.scaleFactor,top=c.y*c.scaleFactor,right=(c.x+c.width)*c.scaleFactor,bottom=(c.y+c.height)*c.scaleFactor;
    if(!std::isfinite(left)||!std::isfinite(top)||!std::isfinite(right)||!std::isfinite(bottom))throw std::runtime_error("Invalid capture content bounds");
    // An oversized content rectangle can occur while the old stream size is
    // still active. Request its natural size, but never read a partial frame.
    constexpr double epsilon=.01;
    if(left < -epsilon||top < -epsilon||right>buffer.width+epsilon||bottom>buffer.height+epsilon)return result;
    constexpr double rounding=1e-7;
    auto x=uint32_t(std::floor(std::max(0.0,left)+rounding)),y=uint32_t(std::floor(std::max(0.0,top)+rounding));
    auto endX=uint32_t(std::ceil(std::min(double(buffer.width),right)-rounding)),endY=uint32_t(std::ceil(std::min(double(buffer.height),bottom)-rounding));
    if(endX<=x||endY<=y)throw std::runtime_error("Empty capture content bounds");
    result.x=x;result.y=y;result.crop={endX-x,endY-y};result.complete=true;return result;
}
struct CaptureResizeRequest {uint64_t generation{},serial{};CaptureSize size;bool operator==(const CaptureResizeRequest&) const=default;};
// Caller holds the capture mutex. At most one async native configuration update
// is active; rapid samples replace desired_, not a queue of obsolete sizes.
class CaptureResize {
    uint64_t generation_{},serial_{};
    CaptureSize applied_,desired_;
    std::optional<CaptureResizeRequest> pending_;
public:
    void reset(uint64_t generation,CaptureSize size) {captureSize(size.width,size.height);generation_=generation;applied_=desired_=size;pending_.reset();}
    void observe(uint64_t generation,CaptureSize size) {if(generation!=generation_)return;captureSize(size.width,size.height);desired_=size;}
    std::optional<CaptureResizeRequest> next() {
        if(pending_||applied_==desired_)return {};
        if(serial_==std::numeric_limits<uint64_t>::max())throw std::runtime_error("Capture configuration sequence exhausted");
        pending_=CaptureResizeRequest{generation_,++serial_,desired_};return pending_;
    }
    bool complete(const CaptureResizeRequest& request) {if(!pending_||*pending_!=request)return false;applied_=request.size;pending_.reset();return true;}
    bool settled() const {return !pending_ && applied_==desired_;}
    bool ready(const CaptureGeometry& geometry) const {
        bool pixelBounds=geometry.crop.width+1>=applied_.width && applied_.width+1>=geometry.crop.width && geometry.crop.height+1>=applied_.height && applied_.height+1>=geometry.crop.height;
        return settled() && geometry.complete && pixelBounds && (!geometry.metadata||geometry.natural==applied_);
    }
};
}
