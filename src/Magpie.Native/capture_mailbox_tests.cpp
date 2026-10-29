#include "capture_mailbox.h"
#include "fresh_frame_gate.h"
#include <array>
#include <iostream>
#include <string>

static void require(bool value,const char* message) {if(!value)throw std::runtime_error(message);}
int main() {
    try {
        using namespace std::chrono_literals;
        magpie::FreshFrameGate gate;auto now=magpie::FreshFrameGate::Clock::time_point{};
        require(gate.ready(now),"Normal frame delivery unexpectedly delayed");
        gate.begin(32ms);require(!gate.ready(now+1h),"No candidate fabricated a restored frame");
        gate.observe(now);require(!gate.ready(now+31ms)&&gate.ready(now+32ms),"Fresh publication boundary differs");
        gate.observe(now+20ms);require(gate.ready(now+32ms),"Continued source updates postponed restoration indefinitely");
        gate.finish();require(gate.ready(now),"Steady-state frames retained startup latency");
        gate.begin(32ms);require(!gate.ready(now+1h),"Another restart retained the previous candidate");
        magpie::CaptureMailbox primed;auto source=magpie::emptySource();source.kind=1;source.window_id=7;source.process_id=8;source.flags=MP_SOURCE_WINDOW|MP_SOURCE_PID;
        auto epoch=primed.begin(source);std::array<uint8_t,4> transparent{},opaque{109,67,23,255};
        const uint8_t* held=nullptr;uint32_t heldWidth{},heldHeight{};
        primed.publish(epoch,transparent.data(),1,1,4);gate.observe(now);
        require(!gate.ready(now+15ms)&&primed.sequence()==0&&!primed.permitsInput(7,8,1),"Held first candidate advanced pixels/input epoch");
        primed.publish(epoch,opaque.data(),1,1,4);gate.observe(now+16ms);
        require(gate.ready(now+32ms)&&primed.deliver(&held,&heldWidth,&heldHeight)&&held[0]==23&&held[1]==67&&held[2]==109&&held[3]==255,"Latest real candidate did not replace transient restore content");
        gate.finish();gate.begin(32ms);primed.pauseInput();auto previousSequence=primed.sequence();
        primed.publish(epoch,transparent.data(),1,1,4);gate.observe(now+1s);
        require(!gate.ready(now+1s+31ms)&&held[0]==23&&primed.sequence()==previousSequence,"Pending transparent frame changed the returned buffer");
        require(gate.ready(now+1s+32ms)&&primed.deliver(&held,&heldWidth,&heldHeight)&&held[0]==0&&held[1]==0&&held[2]==0,"Legitimate static transparent source was rejected or required another frame");
        bool invalidGate=false;try {gate.begin(101ms);}catch(const std::invalid_argument&){invalidGate=true;}
        require(invalidGate,"Unbounded compositor priming interval accepted");
        invalidGate=false;try {gate.begin(-1ms);}catch(const std::invalid_argument&){invalidGate=true;}
        require(invalidGate,"Negative compositor priming interval accepted");
        gate.begin(0ms);require(!gate.ready(now),"Zero interval fabricated a candidate");
        gate.observe(now);require(gate.ready(now),"Zero interval delayed a real candidate");
        magpie::CaptureMailbox box;const uint8_t* pixels=nullptr;uint32_t width=0,height=0;
        require(!box.deliver(&pixels,&width,&height) && box.source().flags==0,"Unselected mailbox fabricated a frame/source");
        auto first=magpie::emptySource();first.kind=1;first.window_id=1;first.process_id=11;first.flags=MP_SOURCE_WINDOW|MP_SOURCE_PID|MP_SOURCE_TITLE;magpie::sourceText(first.title,"first");
        auto second=first;second.window_id=2;second.process_id=22;magpie::sourceText(second.title,"second");
        auto a=box.begin(first);
        std::array<uint8_t,24> padded={3,2,1,0,6,5,4,0,99,99,99,99,9,8,7,0,12,11,10,0,99,99,99,99};
        require(box.publish(a,padded.data(),2,2,12),"First source frame rejected");
        require(box.source().flags==0,"Producer metadata became visible before delivery");
        require(box.deliver(&pixels,&width,&height) && width==2 && height==2,"First frame dimensions incorrect");
        const std::array<uint8_t,16> expected={1,2,3,255,4,5,6,255,7,8,9,255,10,11,12,255};
        require(std::equal(expected.begin(),expected.end(),pixels),"BGRA conversion or row padding is incorrect");
        require(box.source().window_id==1 && box.deliveredIsCurrent(),"First frame metadata mismatch");
        require(box.permitsInput(1,11,1) && !box.permitsInput(1,22,1) && !box.permitsInput(2,11,1) && !box.permitsInput(0,0,1),"Input source identity guard accepted an unrelated/unknown target");
        require(box.matchesPresentedSource(1,11,1) && !box.matchesPresentedSource(2,22,2),"Presented-frame identity mismatch was not detected");
        require(box.sequence()==1 && !box.permitsInput(1,11,0) && !box.permitsInput(1,11,2),"Unsubmitted frame sequence accepted input");
        auto b=box.begin(second);
        require(!box.deliveredIsCurrent() && !box.deliver(&pixels,&width,&height),"Selection replacement redelivered stale pixels");
        require(!box.permitsInput(1,11,1) && !box.permitsInput(2,22,2),"Input remained enabled during selection replacement");
        require(!box.matchesPresentedSource(1,11,1) && !box.matchesPresentedSource(2,22,2),"Pending selection did not require a new submitted frame");
        require(box.source().window_id==1 && std::equal(expected.begin(),expected.end(),pixels),"Selection mutated delivered pixels or metadata");
        require(!box.publish(a,nullptr,0,0,0),"Stale callback was not ignored before inspecting its buffer");
        const std::array<uint8_t,4> blue={200,0,0,0};
        box.publish(b,blue.data(),1,1,4);
        require(box.source().window_id==1 && std::equal(expected.begin(),expected.end(),pixels),"New producer changed a still-delivered source frame");
        require(box.deliver(&pixels,&width,&height) && width==1 && height==1 && pixels[2]==200 && box.source().window_id==2,"Replacement frame/source were not delivered together");
        require(!box.permitsInput(1,11,1) && box.permitsInput(2,22,2),"Input was not restricted to the newly delivered identity");
        box.publish(b,padded.data(),2,2,12);box.begin(first);
        require(!box.deliver(&pixels,&width,&height) && box.source().window_id==2,"Queued frame survived reselection");
        auto c=box.generation();
        auto invalid=[&](const uint8_t* data,uint32_t w,uint32_t h,size_t stride) {try {box.publish(c,data,w,h,stride);throw std::logic_error("Invalid frame accepted");}catch(const std::runtime_error&) {}};
        invalid(nullptr,1,1,4);invalid(blue.data(),0,1,4);invalid(blue.data(),1,0,4);invalid(blue.data(),4097,4096,4097*4);invalid(blue.data(),2,1,4);invalid(blue.data(),1,2,std::numeric_limits<size_t>::max());
        require(!box.deliver(&pixels,&width,&height),"Rejected frame became deliverable");
        box.publish(c,blue.data(),1,1,4);box.publish(c,padded.data(),2,2,12);
        require(box.deliver(&pixels,&width,&height) && width==2 && height==2 && std::equal(expected.begin(),expected.end(),pixels) && std::string(box.source().title)=="first","Latest pending frame/source did not win");
        const uint8_t* stable=pixels;box.publish(c,blue.data(),1,1,4);
        require(std::equal(expected.begin(),expected.end(),stable),"Async producer overwrote the ABI's returned buffer");
        auto display=first;display.kind=2;auto d=box.begin(display);box.publish(d,blue.data(),1,1,4);box.deliver(&pixels,&width,&height);
        require(!box.permitsInput(1,11,box.sequence()),"Non-window source was allowed to forward input");
        std::cout<<"CAPTURE MAILBOX PASS: generation rejection, atomic frame/source delivery, retained ABI buffers, padded BGRA conversion, replacement sizes, latest frame and invalid/overflow budgets.\n";return 0;
    }catch(const std::exception& error){std::cerr<<error.what()<<'\n';return 1;}
}
