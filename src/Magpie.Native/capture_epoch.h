#pragma once
#include <cstdint>
#include <limits>
#include <stdexcept>

namespace magpie {
// Caller serializes access. Old submitted frames remain usable during ordinary
// rendering latency, but not across source/geometry changes or suspension.
class CaptureEpoch {
    uint64_t sequence_{},floor_{};
    uint32_t width_{},height_{};
    bool paused_{true};
public:
    uint64_t sequence() const {return sequence_;}
    bool paused() const {return paused_;}
    bool pause() {bool changed=!paused_;paused_=true;return changed;}
    void deliver(uint32_t width,uint32_t height) {
        if(!width||!height)throw std::runtime_error("Invalid delivered frame dimensions");
        if(sequence_==std::numeric_limits<uint64_t>::max())throw std::runtime_error("Capture frame sequence exhausted");
        ++sequence_;
        if(paused_||width!=width_||height!=height_)floor_=sequence_;
        width_=width;height_=height;paused_=false;
    }
    bool accepts(uint64_t sequence) const {return !paused_&&sequence!=0&&sequence>=floor_&&sequence<=sequence_;}
    bool accepts(uint64_t sequence,uint32_t width,uint32_t height) const {return accepts(sequence)&&width==width_&&height==height_;}
};
}
