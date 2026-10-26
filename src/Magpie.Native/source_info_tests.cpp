#include "source_info.h"
#include "input_mapping.h"
#include <limits>
#include <iostream>
#include <stdexcept>
#include <memory>
#include <string>

void require(bool value,const char* message) { if(!value)throw std::runtime_error(message); }
int main() {
    try {
        magpie::InputMapping mapping{{100,50,800,400},{-200,30,400,200},{.25,.1,.5,.8}};
        magpie::InputPoint mapped{999,999};
        require(mapping.map(500,250,false,mapped)&&mapped.x==0&&mapped.y==130,"Cropped/negative-origin source coordinate mapping differs");
        require(mapping.map(100,50,false,mapped)&&mapped.x==-100&&mapped.y==50,"Source crop near edge differs");
        require(mapping.map(9999,9999,true,mapped)&&mapped.x<100&&mapped.x>99.999&&mapped.y<210&&mapped.y>209.999,"Captured drag leaked past source crop far edge");
        magpie::InputMapping fractional{{12.5,27.25,600,337.5},{2048,-1536,400,225},{0,0,1,1}};
        magpie::InputPoint center;
        require(fractional.map(312.5,196,false,center)&&center.x==2248&&center.y==-1423.5,"Fractional output scale and negative monitor origin changed source center");
        const auto kept=mapped;
        require(!mapping.map(900,450,false,mapped)&&mapped.x==kept.x&&mapped.y==kept.y,"Outside non-drag mapped or changed caller result");
        require(!mapping.map(std::numeric_limits<double>::quiet_NaN(),30,true,mapped),"Nonfinite pointer mapped");
        auto invalid=mapping;invalid.crop.width=2;require(!invalid.map(500,250,true,mapped),"Crop outside source accepted");
        invalid=mapping;invalid.destination.width=0;require(!invalid.map(500,250,true,mapped),"Empty displayed image accepted");
        magpie::WindowIdentity identity{42,10,11};
        require(identity.matches(42,10,11)&&!identity.matches(42,12,11)&&!identity.matches(42,10,12)&&!identity.matches(43,10,11)&&!magpie::WindowIdentity{}.matches(0,0,0),"Selected window/process/thread guard differs");
        magpie::WindowLifetime lifetime(identity);
        lifetime.observeDestroy(43,10,11,true,true);lifetime.observeDestroy(42,10,12,true,true);
        lifetime.observeDestroy(42,10,11,false,true);lifetime.observeDestroy(42,10,11,true,false);
        require(lifetime.matches(42,10,11),"Unrelated/child destruction invalidated selected top-level");
        lifetime.observeDestroy(42,10,11,true,true);
        require(lifetime.destroyed()&&!lifetime.matches(42,10,11),"Destroyed source regained validity when the numeric identity was reused");
        magpie::WindowLifetime nextLifetime(identity);
        lifetime.observeDestroy(42,10,11,true,true);
        require(nextLifetime.matches(42,10,11),"Old lifetime callback invalidated a fresh capture");
        const magpie::LocalInputRegion overlay{10,20,310,250};
        require(overlay.contains(10,20)&&overlay.contains(319.9,269.9),"Profiler local region lost interior/near edge");
        require(!overlay.contains(320,20)&&!overlay.contains(10,270)&&!overlay.contains(9,30),"Profiler local region excluded unrelated source pixels");
        require(!magpie::LocalInputRegion{}.contains(0,0),"Cleared profiler region intercepted input");
        require(!magpie::LocalInputRegion{0,0,-1,2}.valid()&&!magpie::LocalInputRegion{std::numeric_limits<double>::infinity(),0,2,2}.valid(),"Malformed profiler region accepted");
        require(!overlay.contains(std::numeric_limits<double>::quiet_NaN(),30),"Nonfinite pointer entered profiler region");
#ifdef __APPLE__
        require(mp_capture_overlay_region(nullptr,0,0,0,0)==-1,"Null Cocoa capture accepted a local input region");
#else
        require(mp_capture_overlay_region(nullptr,0,0,0,0)==0,"Unsupported input backend fabricated local overlay support");
#endif
        std::cout<<"INPUT MAPPING MODEL PASS: crop, negative source origin, far-edge drag clamp, non-drag exclusion, malformed input and window/process/thread identity with permanent per-capture lifetime tombstones.\n";
        char shortText[5];magpie::sourceText(shortText,"AB\xf0\x9f\x98\x80Z");require(std::string(shortText)=="AB","Truncation split a UTF-8 scalar");
        char exact[7];magpie::sourceText(exact,"AB\xf0\x9f\x98\x80Z");require(std::string(exact)=="AB\xf0\x9f\x98\x80","Exact scalar boundary was truncated incorrectly");
        char one[1];magpie::sourceText(one,"text");require(one[0]==0,"Single-byte field is not terminated");
        char title[1024];magpie::sourceText(title,std::string(1022,'a')+"\xf0\x9f\x98\x80");require(std::strlen(title)==1022,"Long metadata split a UTF-8 scalar");
        std::unique_ptr<void,decltype(&mp_capture_destroy)> capture(mp_capture_create(),mp_capture_destroy);require(bool(capture),mp_last_error());
        require(mp_capture_frame_sequence(capture.get())==0,"Unselected capture fabricated a delivered-frame sequence");
        mp_capture_source_info titleSnapshot=magpie::emptySource();
        require(mp_capture_snapshot_title(capture.get(),1,2,3,&titleSnapshot,sizeof(titleSnapshot))==0,"Unselected capture returned a foreign live title");
        require(mp_capture_snapshot_title(capture.get(),1,2,3,&titleSnapshot,sizeof(titleSnapshot)-1)==0,"Live title accepted an invalid ABI size");
        require(mp_capture_snapshot_title(nullptr,1,2,3,&titleSnapshot,sizeof(titleSnapshot))==0,"Null capture returned a live title");
        require(mp_capture_snapshot_title(capture.get(),1,2,3,nullptr,sizeof(titleSnapshot))==0,"Null live-title output accepted");
        struct Guarded { mp_capture_source_info source;uint64_t guard; } value{magpie::emptySource(),0x123456789abcdef0ull};
        require(mp_capture_source(capture.get(),&value.source,sizeof(value.source))==1,mp_last_error());
        require(value.guard==0x123456789abcdef0ull,"Source snapshot wrote beyond the ABI buffer");
        require(value.source.version==1 && value.source.flags==0 && value.source.kind==0 && value.source.process_id==0 && value.source.window_id==0,"Unselected capture fabricated metadata");
        require(value.source.title[0]==0 && value.source.process_name[0]==0 && value.source.executable_path[0]==0,"Unknown metadata strings are not empty");
        require(mp_capture_source(capture.get(),&value.source,sizeof(value.source)-1)==0,"Invalid ABI size accepted");
        require(mp_capture_source(nullptr,&value.source,sizeof(value.source))==0,"Null capture accepted");
        require(mp_capture_source(capture.get(),nullptr,sizeof(value.source))==0,"Null output accepted");
        std::cout<<"SOURCE INFO PASS: 5400-byte ABI, UTF-8 truncation, unselected real capture, buffer guard and invalid arguments; no source picker.\n";return 0;
    }catch(const std::exception& error){std::cerr<<error.what()<<'\n';return 1;}
}
