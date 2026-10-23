#pragma once
#include "source_info.h"
#include "capture_epoch.h"
#include <cstdint>
#include <limits>
#include <stdexcept>
#include <vector>

namespace magpie {
// Caller serializes access. Pending producer pixels never mutate a delivered
// C ABI buffer; metadata changes only when its matching frame is delivered.
class CaptureMailbox {
    uint64_t generation_{},deliveredGeneration_{};
    CaptureEpoch epoch_;
    mp_capture_source_info selected_=emptySource(),pendingSource_=emptySource(),deliveredSource_=emptySource();
    std::vector<uint8_t> pending_,delivered_;
    uint32_t pendingWidth_{},pendingHeight_{};
    bool fresh_{};
public:
    uint64_t begin(const mp_capture_source_info& source) {
        if(generation_==std::numeric_limits<uint64_t>::max())throw std::runtime_error("Capture generation exhausted");
        ++generation_;selected_=source;fresh_=false;epoch_.pause();return generation_;
    }
    uint64_t generation() const {return generation_;}
    bool accepts(uint64_t generation) const {return generation!=0 && generation==generation_;}
    bool deliveredIsCurrent() const {return deliveredGeneration_!=0 && deliveredGeneration_==generation_;}
    uint64_t sequence() const {return epoch_.sequence();}
    bool inputPaused() const {return epoch_.paused();}
    bool pauseInput() {fresh_=false;return epoch_.pause();}
    bool matchesPresentedSource(uint64_t window,uint32_t process,uint64_t sequence) const {
        // Ordinary frame latency must not release held keys/drag gestures. A
        // previously submitted frame stays valid within the same geometry epoch;
        // source/resize/suspension starts a new floor on the next delivery.
        return epoch_.accepts(sequence) && deliveredIsCurrent() && deliveredSource_.window_id==window && deliveredSource_.process_id==process;
    }
    bool permitsInput(uint64_t window,uint32_t process,uint64_t sequence) const {
        return matchesPresentedSource(window,process,sequence) && deliveredSource_.kind==1 && window!=0 && process!=0 &&
            (deliveredSource_.flags&(MP_SOURCE_WINDOW|MP_SOURCE_PID))==(MP_SOURCE_WINDOW|MP_SOURCE_PID);
    }
    const mp_capture_source_info& source() const {return deliveredSource_;}
    bool publishRegion(uint64_t generation,const uint8_t* bgra,uint32_t bufferWidth,uint32_t bufferHeight,size_t stride,uint32_t x,uint32_t y,uint32_t width,uint32_t height) {
        if(!accepts(generation))return false;
        if(!bgra||!bufferWidth||!bufferHeight||uint64_t(bufferWidth)*bufferHeight>16777216||stride<uint64_t(bufferWidth)*4||stride>std::numeric_limits<size_t>::max()/bufferHeight||
            !width||!height||width>bufferWidth||height>bufferHeight||x>bufferWidth-width||y>bufferHeight-height)throw std::runtime_error("Invalid capture crop region");
        return publish(generation,bgra+size_t(y)*stride+size_t(x)*4,width,height,stride);
    }
    bool publish(uint64_t generation,const uint8_t* bgra,uint32_t width,uint32_t height,size_t stride) {
        if(!accepts(generation))return false;
        if(!bgra || !width || !height || uint64_t(width)*height>16777216 || stride<uint64_t(width)*4 ||
            stride>std::numeric_limits<size_t>::max()/height)throw std::runtime_error("Invalid captured pixel buffer");
        pending_.resize(size_t(width)*height*4);
        for(size_t y=0;y<height;++y)for(size_t x=0;x<width;++x) {
            const auto* src=bgra+y*stride+x*4;auto* dst=pending_.data()+(y*width+x)*4;
            dst[0]=src[2];dst[1]=src[1];dst[2]=src[0];dst[3]=255;
        }
        pendingWidth_=width;pendingHeight_=height;pendingSource_=selected_;fresh_=true;return true;
    }
    bool deliver(const uint8_t** pixels,uint32_t* width,uint32_t* height) {
        if(!fresh_)return false;
        epoch_.deliver(pendingWidth_,pendingHeight_);
        delivered_.swap(pending_);deliveredSource_=pendingSource_;deliveredGeneration_=generation_;
        *pixels=delivered_.data();*width=pendingWidth_;*height=pendingHeight_;fresh_=false;return true;
    }
};
}
