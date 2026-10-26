#include "capture_geometry.h"
#include "capture_mailbox.h"
#include "windows_capture_region.h"
#include <array>
#include <iostream>
#include <memory>
#include <string_view>

static void require(bool value,const char* message) {if(!value)throw std::runtime_error(message);}
int main(int argc,char** argv) {
    (void)argc;(void)argv;
    try {
        using namespace magpie;
        WindowRegionGeometry framed;
        framed.window={100,100,400,340};framed.frame={107,100,393,333};framed.client={108,132,392,332};
        framed.nativeFrame=true;framed.caption=true;framed.thickFrame=true;framed.monitor={0,0,1920,1080};
        require(windowFrameKind(framed)==WindowFrameKind::Native,"Native window classification differs from original");
        require(windowCaptureRegion(framed,false)==WindowRect{108,132,392,332},"Default native client/caption crop differs");
        require(windowCaptureRegion(framed,true)==WindowRect{108,101,392,332},"Native title bar was removed or border retained");
        auto scrolling=framed;scrolling.client.right-=17;scrolling.client.bottom-=17;
        require(windowCaptureRegion(scrolling,false)==WindowRect{108,132,392,332},"Native scrollbar pixels were cropped as client margins");
        auto thick=framed;thick.caption=false;
        require(windowFrameKind(thick)==WindowFrameKind::OnlyThickFrame&&windowCaptureRegion(thick,true)==windowCaptureRegion(thick,false),"Thick top frame mistaken for a caption");
        auto custom=framed;custom.nativeFrame=false;
        require(windowCaptureRegion(custom,true)==custom.window&&windowCaptureRegion(custom,false)==custom.client,"Custom nonclient title bar rules differ");
        custom.verticalScroll=true;custom.horizontalScroll=true;custom.client.right-=17;custom.client.bottom-=17;
        require(windowCaptureRegion(custom,false)==WindowRect{108,132,392,332},"Custom scrollbar border inference differs");
        custom.caption=custom.thickFrame=false;
        require(windowCaptureRegion(custom,false)==custom.window,"Frameless content was reduced to a synthetic client area");
        auto uwp=framed;uwp.client.top=uwp.window.top;uwp.hasUwpClient=true;uwp.uwpClient={108,140,392,332};
        require(windowFrameKind(uwp)==WindowFrameKind::NoTitleBar&&windowCaptureRegion(uwp,false)==uwp.uwpClient,"UWP input-sink client not used");
        auto borderless=uwp;borderless.client.left=borderless.window.left;borderless.hasUwpClient=false;
        require(windowFrameKind(borderless)==WindowFrameKind::NoBorder&&windowCaptureRegion(borderless,true)==borderless.frame,"Windows 10 borderless crop differs");
        borderless.win11=true;borderless.monitorDpi=144;
        require(windowCaptureRegion(borderless,true)==WindowRect{109,102,391,331},"Windows 11 fractional monitor DPI border rounding differs");
        auto maximized=framed;maximized.maximized=true;maximized.frame={-8,-8,1928,1088};maximized.client={0,23,1920,1080};
        require(windowCaptureRegion(maximized,true)==WindowRect{0,0,1920,1080}&&windowCaptureRegion(maximized,false)==WindowRect{0,23,1920,1080},"Maximized source escaped physical monitor bounds");
        std::cout<<"WINDOW CAPTURE REGION PASS: original caption/native/custom/thick/borderless/UWP/scrollbar/maximized and fractional monitor border rules.\n";
        auto initial=captureGeometry({200,120},CaptureContent{0,0,100,60,2,1});
        require(initial.complete && initial.crop==CaptureSize{200,120} && initial.natural==CaptureSize{200,120},"Retina content size was not converted to pixels");
        auto grow=captureGeometry({200,120},CaptureContent{0,0,100,50,2,.5});
        require(grow.crop==CaptureSize{200,100} && grow.natural==CaptureSize{400,200},"Scaled old buffer was mistaken for natural source size");
        auto dpi=captureGeometry({20,10},CaptureContent{2.4,1.6,8,4,1.25,1});
        require(dpi.x==3&&dpi.y==2&&dpi.crop==CaptureSize{10,5}&&dpi.natural==CaptureSize{10,5},"Fractional scale or content offset conversion failed");
        auto outside=captureGeometry({800,600},CaptureContent{0,0,1000,800,1,1});
        require(!outside.complete && outside.natural==CaptureSize{1000,800},"Oversized old buffer did not retain resize target while refusing partial pixels");
        auto empty=captureGeometry({200,120},CaptureContent{0,0,0,0,1,1});
        require(!empty.complete && empty.metadata && empty.natural==CaptureSize{},"Transient empty content became a drawable source");
        auto precision=captureGeometry({4,2},CaptureContent{0,0,4.00000000001,2,1,1});
        require(precision.complete&&precision.natural==CaptureSize{4,2},"Floating-point noise caused a one-pixel reconfiguration loop");
        auto jitter=captureGeometry({20,10},CaptureContent{1.999999999,1.000000001,3,2,1,1});
        require(jitter.x==2&&jitter.y==1&&jitter.crop==CaptureSize{3,2},"Near-integer content origin jitter changed the pixel/geometry epoch");
        auto fallback=captureGeometry({200,120},{});require(!fallback.metadata&&fallback.complete&&fallback.crop==CaptureSize{200,120},"Missing metadata fabricated a content rectangle");
        auto invalid=[&](CaptureContent c) {try{captureGeometry({200,120},c);throw std::logic_error("Invalid geometry accepted");}catch(const std::runtime_error&) {}};
        invalid({0,0,-1,10,1,1});invalid({0,0,1,10,0,1});invalid({0,0,1,10,1,0});invalid({NAN,0,1,10,1,1});invalid({0,0,INFINITY,10,1,1});invalid({0,0,4097,4096,1,1});invalid({0,0,10,10,1,NAN});
        CaptureResize resize;resize.reset(1,{200,120});require(resize.ready(initial)&&!resize.next(),"Initial stream queued a redundant configuration");
        resize.observe(1,grow.natural);auto first=resize.next();require(first&&first->size==CaptureSize{400,200}&&!resize.ready(grow),"Resize did not request recovered natural dimensions");
        resize.observe(1,{160,80});resize.observe(1,{300,180});require(!resize.next(),"Rapid resize started overlapping native configuration calls");
        require(resize.complete(*first),"Current resize completion was ignored");auto latest=resize.next();
        require(latest&&latest->size==CaptureSize{300,180}&&!resize.complete(*first),"Resize did not coalesce latest target or accepted duplicate completion");
        require(resize.complete(*latest)&&resize.ready(captureGeometry({300,180},CaptureContent{0,0,150,90,2,1})),"Applied resize did not accept its matching new frame");
        resize.observe(1,{600,400});auto stale=resize.next();resize.reset(2,{100,60});
        require(stale&&!resize.complete(*stale),"Old source's completion changed the new stream configuration");
        resize.observe(1,{640,480});require(!resize.next()&&resize.ready(captureGeometry({100,60},{})),"Old source's sample changed the new resize target");
        resize.observe(2,{800,600});auto expand=resize.next();resize.observe(2,{100,60});require(expand&&resize.complete(*expand),"In-flight expansion failed");auto shrink=resize.next();require(shrink&&shrink->size==CaptureSize{100,60}&&resize.complete(*shrink),"Resize back to original dimensions lost the newest request");

        CaptureMailbox box;auto source=emptySource();source.kind=1;source.window_id=10;source.process_id=20;source.flags=MP_SOURCE_WINDOW|MP_SOURCE_PID;auto generation=box.begin(source);
        std::array<uint8_t,96> padded{};
        for(size_t y=0;y<4;y++)for(size_t x=0;x<5;x++){auto p=padded.data()+y*24+x*4;p[0]=uint8_t(100+y*10+x);p[1]=uint8_t(50+y*10+x);p[2]=uint8_t(y*10+x);p[3]=1;}
        auto crop=captureGeometry({5,4},CaptureContent{1,1,3,2,1,1});
        box.publishRegion(generation,padded.data(),5,4,24,crop.x,crop.y,crop.crop.width,crop.crop.height);
        const uint8_t* pixels=nullptr;uint32_t width=0,height=0;require(box.deliver(&pixels,&width,&height)&&width==3&&height==2,"Cropped frame dimensions incorrect");
        const std::array<uint8_t,24> expected={11,61,111,255,12,62,112,255,13,63,113,255,21,71,121,255,22,72,122,255,23,73,123,255};
        require(std::equal(expected.begin(),expected.end(),pixels)&&box.permitsInput(10,20,1),"Crop selected padding/wrong channels or lost frame identity");
        box.pauseInput();require(!box.permitsInput(10,20,1)&&!box.deliver(&pixels,&width,&height),"Resize pause allowed input or stale redelivery");
        box.publishRegion(generation,padded.data(),5,4,24,0,0,2,1);require(!box.permitsInput(10,20,1),"New producer frame resumed input before delivery");
        require(box.deliver(&pixels,&width,&height)&&box.sequence()==2&&!box.permitsInput(10,20,1)&&box.permitsInput(10,20,2),"Same-window resize accepted the old UI frame sequence");
        box.publishRegion(generation,padded.data(),5,4,24,0,0,2,1);box.deliver(&pixels,&width,&height);
        require(box.sequence()==3&&box.permitsInput(10,20,2)&&box.permitsInput(10,20,3)&&!box.permitsInput(10,20,1)&&!box.permitsInput(10,20,4),"Ordinary frame latency interrupted input or escaped the geometry epoch");
        auto badCrop=[&](uint32_t x,uint32_t y,uint32_t w,uint32_t h,size_t stride) {try{box.publishRegion(generation,padded.data(),5,4,stride,x,y,w,h);throw std::logic_error("Invalid crop accepted");}catch(const std::runtime_error&) {}};
        badCrop(5,0,1,1,24);badCrop(0,4,1,1,24);badCrop(UINT32_MAX,0,1,1,24);badCrop(0,0,6,1,24);badCrop(0,0,1,1,16);badCrop(0,0,1,1,std::numeric_limits<size_t>::max());
        require(!box.publishRegion(generation-1,nullptr,0,0,0,0,0,0,0),"Stale cropped frame was inspected or accepted");
        CaptureEpoch epoch;require(!epoch.accepts(0)&&epoch.sequence()==0,"Unselected epoch accepted an unknown frame");
        epoch.deliver(640,480);epoch.deliver(640,480);
        require(epoch.accepts(1,640,480)&&epoch.accepts(2,640,480)&&!epoch.accepts(1,800,600)&&!epoch.accepts(3,640,480),"Geometry check accepted a resized/future frame or rejected ordinary frame latency");
        epoch.deliver(800,600);require(!epoch.accepts(2)&&epoch.accepts(3,800,600),"Resize without an explicit pause kept the previous frame epoch valid");
        epoch.pause();require(!epoch.accepts(3),"Suspension kept live metadata/input usable");
        epoch.deliver(800,600);require(!epoch.accepts(3)&&epoch.accepts(4,800,600),"Resume did not require a new matching frame");
        std::cout<<"CAPTURE GEOMETRY PASS: Retina/fractional scales, natural-size recovery, bounded padded BGRA crop, async resize coalescing, stale/duplicate completions, same-window frame sequence/input pause, missing/empty metadata and invalid/overflow budgets.\n";
#ifdef MAGPIE_GEOMETRY_GPU
        if(argc>1&&std::string_view(argv[1])=="--gpu") {
            box.pauseInput();box.publishRegion(generation,padded.data(),5,4,24,crop.x,crop.y,crop.crop.width,crop.crop.height);box.deliver(&pixels,&width,&height);
            std::unique_ptr<void,decltype(&mp_destroy)> engine(mp_create(),mp_destroy);require(bool(engine),mp_last_error());
            std::array<uint8_t,96> output{};require(mp_scale(engine.get(),pixels,width,height,output.data(),6,4,0,0)!=0,mp_last_error());
            for(size_t y=0;y<4;y++)for(size_t x=0;x<6;x++)for(size_t c=0;c<4;c++)require(output[(y*6+x)*4+c]==expected[((y/2)*3+x/2)*4+c],"Cropped native source did not reach Vulkan with the correct stride/coordinates");
            std::cout<<"CAPTURE GEOMETRY VULKAN PASS: padded BGRA -> bounded native crop -> packed RGBA -> exact nearest 2x, device="<<mp_device_name(engine.get())<<"\n";
        }
#endif
        return 0;
    }catch(const std::exception& error){std::cerr<<error.what()<<'\n';return 1;}
}
