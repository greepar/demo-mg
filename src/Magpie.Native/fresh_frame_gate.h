#pragma once
#include <chrono>
#include <stdexcept>

namespace magpie {
// Caller serializes with the mailbox. Coalesce initial frames of a restarted
// capture pool for a bounded interval; a single static/transparent candidate
// still becomes deliverable without requiring another source repaint.
class FreshFrameGate {
public:
    using Clock=std::chrono::steady_clock;
private:
    bool active_{},candidate_{};
    Clock::duration interval_{};
    Clock::time_point release_{};
public:
    void begin(Clock::duration interval) {
        if(interval<Clock::duration::zero()||interval>std::chrono::milliseconds(100))throw std::invalid_argument("Invalid fresh-frame publication interval");
        interval_=interval;active_=true;candidate_=false;
    }
    void observe(Clock::time_point now) {
        if(active_&&!candidate_) {candidate_=true;release_=now+interval_;}
    }
    bool ready(Clock::time_point now) const {return !active_||(candidate_&&now>=release_);}
    void finish() {active_=false;candidate_=false;}
};
}
