#import <AppKit/AppKit.h>
#import <ScreenCaptureKit/ScreenCaptureKit.h>
#import <CoreVideo/CoreVideo.h>
#import <CoreMedia/CoreMedia.h>
#include "magpie.h"
#include "error.h"
#import "input_macos.h"
#include <atomic>
#include <cmath>
#include <chrono>
#include <condition_variable>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <vector>

namespace {
struct State {
    std::mutex mutex;
    std::condition_variable changed;
    std::atomic<bool> cancelled{false};
    bool ready{}, fresh{};
    std::string failure;
    std::vector<uint8_t> pixels,delivered;
    uint32_t width{},height{};
    void fail(std::string message) { std::lock_guard lock(mutex);failure=std::move(message);changed.notify_all(); }
};
}
API_AVAILABLE(macos(14.0))
@interface MagpieCaptureObserver : NSObject <SCContentSharingPickerObserver, SCStreamOutput, SCStreamDelegate> {
@public
    std::shared_ptr<State> state;
}
@property(nonatomic,strong) SCStream* stream;
@property(nonatomic,strong) dispatch_queue_t sampleQueue;
@property(nonatomic,strong) MagpieInputBridge* input;
- (void)present;
- (void)stop;
@end
@implementation MagpieCaptureObserver
- (void)present {
    if(state->cancelled)return;
    SCContentSharingPicker* picker=SCContentSharingPicker.sharedPicker;
    SCContentSharingPickerConfiguration* configuration=[SCContentSharingPickerConfiguration new];
    configuration.allowedPickerModes=SCContentSharingPickerModeSingleWindow | SCContentSharingPickerModeSingleDisplay;
    picker.defaultConfiguration=configuration;
    [picker addObserver:self];picker.active=YES;[picker present];
}
- (void)stop {
    [self.input stop];
    [SCContentSharingPicker.sharedPicker removeObserver:self];
    [self.stream stopCaptureWithCompletionHandler:^(NSError* error) { (void)error; }];
    self.stream=nil;
}
- (void)contentSharingPicker:(SCContentSharingPicker*)picker didCancelForStream:(SCStream*)stream {
    (void)picker;(void)stream;state->fail("Screen capture selection was cancelled");
}
- (void)contentSharingPickerStartDidFailWithError:(NSError*)error {state->fail(error.localizedDescription.UTF8String ?: "System capture picker failed");}
- (void)contentSharingPicker:(SCContentSharingPicker*)picker didUpdateWithFilter:(SCContentFilter*)filter forStream:(SCStream*)stream {
    (void)picker;(void)stream;
    if(state->cancelled)return;
    if(!self.input)self.input=[MagpieInputBridge new];
    // Picker filters expose the exact selected window identity starting in 15.2.
    // Never guess a process/window from a title or an overlapping screen rectangle.
    [self.input setSourceWindow:0 process:0];
    if(@available(macOS 15.2,*)) {
        if(filter.style==SCShareableContentStyleWindow && filter.includedWindows.count==1) {
            SCWindow* window=filter.includedWindows.firstObject;
            [self.input setSourceWindow:window.windowID process:window.owningApplication.processID];
        }
    }
    if(self.stream) {
        [self.stream updateContentFilter:filter completionHandler:^(NSError* error){if(error)state->fail(error.localizedDescription.UTF8String);}];
        return;
    }
    const double w=std::ceil(filter.contentRect.size.width*filter.pointPixelScale);
    const double h=std::ceil(filter.contentRect.size.height*filter.pointPixelScale);
    if(!std::isfinite(w)||!std::isfinite(h)||w<1||h<1||w*h>16777216) {state->fail("Unsupported source dimensions");return;}
    SCStreamConfiguration* config=[SCStreamConfiguration new];
    config.width=NSUInteger(w);config.height=NSUInteger(h);config.pixelFormat=kCVPixelFormatType_32BGRA;
    // The output uses the local system pointer. Baking the source pointer into
    // captured pixels would display a second cursor and scale it with the image.
    config.showsCursor=NO;config.queueDepth=3;config.minimumFrameInterval=CMTimeMake(1,60);
    self.sampleQueue=dispatch_queue_create("org.magpie.capture",DISPATCH_QUEUE_SERIAL);
    self.stream=[[SCStream alloc] initWithFilter:filter configuration:config delegate:self];
    NSError* error=nil;
    if(![self.stream addStreamOutput:self type:SCStreamOutputTypeScreen sampleHandlerQueue:self.sampleQueue error:&error]) {
        state->fail(error.localizedDescription.UTF8String ?: "Cannot configure screen stream");return;
    }
    [self.stream startCaptureWithCompletionHandler:^(NSError* error){
        if(error){state->fail(error.localizedDescription.UTF8String);return;}
        std::lock_guard lock(state->mutex);state->ready=true;state->changed.notify_all();
    }];
}
- (void)stream:(SCStream*)stream didStopWithError:(NSError*)error {
    (void)stream;state->fail(error.localizedDescription.UTF8String ?: "Screen sharing stopped");
}
- (void)stream:(SCStream*)stream didOutputSampleBuffer:(CMSampleBufferRef)sample ofType:(SCStreamOutputType)type {
    (void)stream;
    if(type!=SCStreamOutputTypeScreen || state->cancelled || !CMSampleBufferIsValid(sample))return;
    CVPixelBufferRef image=CMSampleBufferGetImageBuffer(sample);
    if(!image || CVPixelBufferGetPixelFormatType(image)!=kCVPixelFormatType_32BGRA)return;
    if(CVPixelBufferLockBaseAddress(image,kCVPixelBufferLock_ReadOnly)!=kCVReturnSuccess)return;
    try {
        const auto width=CVPixelBufferGetWidth(image),height=CVPixelBufferGetHeight(image),stride=CVPixelBufferGetBytesPerRow(image);
        const auto* base=static_cast<const uint8_t*>(CVPixelBufferGetBaseAddress(image));
        if(!base || !width || !height || uint64_t(width)*height>16777216 || stride<width*4)throw std::runtime_error("Invalid captured pixel buffer");
        std::lock_guard lock(state->mutex);
        state->pixels.resize(width*height*4);
        for(size_t y=0;y<height;++y)for(size_t x=0;x<width;++x) {
            const auto* src=base+y*stride+x*4;auto* dst=state->pixels.data()+(y*width+x)*4;
            dst[0]=src[2];dst[1]=src[1];dst[2]=src[0];dst[3]=255;
        }
        state->width=uint32_t(width);state->height=uint32_t(height);state->fresh=true;
    } catch(const std::exception& error){state->fail(error.what());}
    CVPixelBufferUnlockBaseAddress(image,kCVPixelBufferLock_ReadOnly);
}
@end

namespace {
struct Capture {
    std::shared_ptr<State> state=std::make_shared<State>();
    MagpieCaptureObserver* observer;
    Capture(){observer=[MagpieCaptureObserver new];observer->state=state;}
    void cancel(){state->cancelled=true;state->changed.notify_all();auto bridge=observer;dispatch_async(dispatch_get_main_queue(),^{[bridge stop];});}
    ~Capture(){cancel();}
    void start(){
        if([NSThread isMainThread])throw std::runtime_error("Capture selection must run on a worker thread");
        auto bridge=observer;dispatch_async(dispatch_get_main_queue(),^{[bridge present];});
        std::unique_lock lock(state->mutex);
        if(!state->changed.wait_for(lock,std::chrono::minutes(5),[&]{return state->cancelled||state->ready||!state->failure.empty();}))throw std::runtime_error("Capture selection timed out");
        if(state->cancelled)throw std::runtime_error("Capture selection cancelled");
        if(!state->failure.empty())throw std::runtime_error(state->failure);
    }
    int frame(const uint8_t** rgba,uint32_t* width,uint32_t* height){
        std::lock_guard lock(state->mutex);
        if(state->cancelled)throw std::runtime_error("Capture was stopped");
        if(!state->failure.empty())throw std::runtime_error(state->failure);
        if(!state->fresh)return 0;
        state->delivered=state->pixels;state->fresh=false;*rgba=state->delivered.data();*width=state->width;*height=state->height;return 1;
    }
};
}
extern "C" void* mp_capture_create(){@autoreleasepool{try{if(@available(macOS 14.0,*))return new Capture();throw std::runtime_error("Screen capture requires macOS 14 or newer");}catch(const std::exception& ex){magpie::error=ex.what();return nullptr;}}}
extern "C" void mp_capture_destroy(void* c){@autoreleasepool{delete static_cast<Capture*>(c);}}
extern "C" void mp_capture_cancel(void* c){@autoreleasepool{if(c)static_cast<Capture*>(c)->cancel();}}
extern "C" int mp_capture_start(void* c){@autoreleasepool{try{if(!c)throw std::runtime_error("Capture is not initialized");static_cast<Capture*>(c)->start();return 1;}catch(const std::exception& ex){magpie::error=ex.what();return 0;}}}
extern "C" int mp_capture_frame(void* c,const uint8_t** rgba,uint32_t* w,uint32_t* h){@autoreleasepool{try{if(!c||!rgba||!w||!h)throw std::runtime_error("Invalid capture arguments");return static_cast<Capture*>(c)->frame(rgba,w,h);}catch(const std::exception& ex){magpie::error=ex.what();return -1;}}}

extern "C" int mp_capture_configure_input(void* c,void* window,double x,double y,double width,double height,
    double cropX,double cropY,double cropWidth,double cropHeight,double toolbarHeight,
    const char* reservedCharacter,int reservedCode,uint64_t reservedModifiers) {
    @autoreleasepool {
        if(!c || !window || ![NSThread isMainThread])return -1;
        auto capture=static_cast<Capture*>(c);
        if(capture->state->cancelled)return -1;
        auto input=capture->observer.input;
        if(!input)return 0;
        [input setReservedCharacter:reservedCharacter?[NSString stringWithUTF8String:reservedCharacter]:@""
            keyCode:reservedCode modifiers:reservedModifiers];
        return [input configureWindow:(__bridge NSWindow*)window destination:CGRectMake(x,y,width,height)
            crop:CGRectMake(cropX,cropY,cropWidth,cropHeight) toolbarHeight:toolbarHeight];
    }
}
