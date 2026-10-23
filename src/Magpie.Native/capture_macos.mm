#import <AppKit/AppKit.h>
#import <ScreenCaptureKit/ScreenCaptureKit.h>
#import <CoreVideo/CoreVideo.h>
#import <CoreMedia/CoreMedia.h>
#include "magpie.h"
#include "error.h"
#include "capture_mailbox.h"
#include "capture_geometry.h"
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
    magpie::CaptureMailbox frames;
    magpie::CaptureResize resize;
    void* activeStream{};
    std::mutex mutex;
    std::condition_variable changed;
    std::atomic<bool> cancelled{false};
    bool ready{};
    std::string failure;
    void fail(std::string message) { std::lock_guard lock(mutex);if(cancelled)return;failure=std::move(message);changed.notify_all(); }
    void fail(uint64_t generation,std::string message) { std::lock_guard lock(mutex);if(cancelled || !frames.accepts(generation))return;failure=std::move(message);changed.notify_all(); }
    void failStream(void* stream,std::string message) { std::lock_guard lock(mutex);if(cancelled || activeStream!=stream)return;failure=std::move(message);changed.notify_all(); }
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
- (void)requestResize:(magpie::CaptureResizeRequest)request stream:(SCStream*)stream;
- (void)pauseInputForStream:(SCStream*)stream generation:(uint64_t)generation;
- (SCStreamConfiguration*)configurationForSize:(magpie::CaptureSize)size;
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
    if(![NSThread isMainThread]) { dispatch_async(dispatch_get_main_queue(),^{[self contentSharingPicker:picker didCancelForStream:stream];});return; }
    // Cancelling a repicker leaves the currently running capture intact.
    if(self.stream || state->cancelled)return;
    state->fail("Screen capture selection was cancelled");
}
- (void)contentSharingPickerStartDidFailWithError:(NSError*)error {state->fail(error.localizedDescription.UTF8String ?: "System capture picker failed");}
- (void)contentSharingPicker:(SCContentSharingPicker*)picker didUpdateWithFilter:(SCContentFilter*)filter forStream:(SCStream*)stream {
    if(![NSThread isMainThread]) { dispatch_async(dispatch_get_main_queue(),^{[self contentSharingPicker:picker didUpdateWithFilter:filter forStream:stream];});return; }
    if(state->cancelled || (stream && stream!=self.stream))return;
    if(!self.input)self.input=[MagpieInputBridge new];
    // Picker filters expose the exact selected window identity starting in 15.2.
    // Never guess a process/window from a title or an overlapping screen rectangle.
    [self.input setSourceWindow:0 process:0];
    auto source=magpie::emptySource();
    if(@available(macOS 15.2,*)) {
        if(filter.style==SCShareableContentStyleWindow && filter.includedWindows.count==1) {
            SCWindow* window=filter.includedWindows.firstObject;
            source.kind=1;source.window_id=window.windowID;source.flags|=MP_SOURCE_WINDOW;
            const char* title=window.title.UTF8String;
            if(title && *title){magpie::sourceText(source.title,title);source.flags|=MP_SOURCE_TITLE;}
            if(window.owningApplication) {
                source.process_id=uint32_t(window.owningApplication.processID);source.flags|=MP_SOURCE_PID;
                NSRunningApplication* application=[NSRunningApplication runningApplicationWithProcessIdentifier:window.owningApplication.processID];
                NSString* path=application.executableURL.path;
                NSString* name=path.lastPathComponent.stringByDeletingPathExtension;
                if(!name.length)name=window.owningApplication.applicationName;
                const char* executable=path.UTF8String;const char* processName=name.UTF8String;
                if(executable && *executable && strlen(executable)<sizeof(source.executable_path)){magpie::sourceText(source.executable_path,executable);source.flags|=MP_SOURCE_EXECUTABLE;}
                if(processName && *processName){magpie::sourceText(source.process_name,processName);source.flags|=MP_SOURCE_PROCESS_NAME;}
            }
        }
        else if(filter.style==SCShareableContentStyleDisplay)source.kind=2;
    }
    magpie::CaptureSize size;
    try {size=magpie::captureSize(filter.contentRect.size.width*filter.pointPixelScale,filter.contentRect.size.height*filter.pointPixelScale);}
    catch(const std::exception& error) {state->fail(error.what());return;}
    SCStreamConfiguration* config=[self configurationForSize:size];
    if(!self.sampleQueue)self.sampleQueue=dispatch_queue_create("org.magpie.capture",DISPATCH_QUEUE_SERIAL);
    SCStream* previous=self.stream;
    SCStream* current=[[SCStream alloc] initWithFilter:filter configuration:config delegate:self];
    if(!current) {state->fail("Cannot create screen stream");return;}
    uint64_t generation;
    {
        std::lock_guard lock(state->mutex);
        if(state->cancelled)return;
        generation=state->frames.begin(source);state->resize.reset(generation,size);state->activeStream=(__bridge void*)current;
    }
    self.stream=current;
    // A fresh stream separates old/new frame callbacks unambiguously and uses
    // the replacement filter's own dimensions. Old completion/error callbacks
    // are ignored by generation/stream identity rather than terminating it.
    [previous stopCaptureWithCompletionHandler:^(NSError* error){(void)error;}];
    NSError* error=nil;
    if(![current addStreamOutput:self type:SCStreamOutputTypeScreen sampleHandlerQueue:self.sampleQueue error:&error]) {
        state->fail(generation,error.localizedDescription.UTF8String ?: "Cannot configure screen stream");return;
    }
    [current startCaptureWithCompletionHandler:^(NSError* error){
        if(error){state->fail(generation,error.localizedDescription.UTF8String ?: "Cannot start screen stream");return;}
        std::lock_guard lock(state->mutex);
        if(state->cancelled || !state->frames.accepts(generation))return;
        state->ready=true;state->changed.notify_all();
    }];
}
- (SCStreamConfiguration*)configurationForSize:(magpie::CaptureSize)size {
    SCStreamConfiguration* config=[SCStreamConfiguration new];
    config.width=size.width;config.height=size.height;config.pixelFormat=kCVPixelFormatType_32BGRA;
    config.showsCursor=NO;config.ignoreShadowsSingleWindow=YES;config.queueDepth=3;config.minimumFrameInterval=CMTimeMake(1,60);
    return config;
}
- (void)pauseInputForStream:(SCStream*)stream generation:(uint64_t)generation {
    dispatch_async(dispatch_get_main_queue(),^{
        {
            std::lock_guard lock(state->mutex);
            if(state->cancelled || state->activeStream!=(__bridge void*)stream || !state->frames.accepts(generation) || !state->frames.inputPaused())return;
        }
        [self.input setSourceWindow:0 process:0];
    });
}
- (void)requestResize:(magpie::CaptureResizeRequest)request stream:(SCStream*)stream {
    dispatch_async(dispatch_get_main_queue(),^{
        {
            std::lock_guard lock(state->mutex);
            if(state->cancelled || state->activeStream!=(__bridge void*)stream || !state->frames.accepts(request.generation))return;
        }
        [self.input setSourceWindow:0 process:0];
        [stream updateConfiguration:[self configurationForSize:request.size] completionHandler:^(NSError* error){
            if(error) {state->fail(request.generation,error.localizedDescription.UTF8String ?: "Cannot resize capture stream");return;}
            std::optional<magpie::CaptureResizeRequest> next;
            {
                std::lock_guard lock(state->mutex);
                if(state->cancelled || !state->frames.accepts(request.generation) || !state->resize.complete(request))return;
                next=state->resize.next();
            }
            if(next)[self requestResize:*next stream:stream];
        }];
    });
}
- (void)stream:(SCStream*)stream didStopWithError:(NSError*)error {
    state->failStream((__bridge void*)stream,error.localizedDescription.UTF8String ?: "Screen sharing stopped");
}
- (void)stream:(SCStream*)stream didOutputSampleBuffer:(CMSampleBufferRef)sample ofType:(SCStreamOutputType)type {
    if(type!=SCStreamOutputTypeScreen || state->cancelled || !CMSampleBufferIsValid(sample))return;
    CFArrayRef attachments=CMSampleBufferGetSampleAttachmentsArray(sample,false);
    if(!attachments || CFArrayGetCount(attachments)==0)return;
    NSDictionary* frameInfo=(__bridge NSDictionary*)CFArrayGetValueAtIndex(attachments,0);
    NSNumber* status=frameInfo[SCStreamFrameInfoStatus];
    if(status && status.integerValue==SCFrameStatusStopped) {state->failStream((__bridge void*)stream,"The capture source stopped");return;}
    if(status && (status.integerValue==SCFrameStatusBlank || status.integerValue==SCFrameStatusSuspended)) {
        std::lock_guard lock(state->mutex);
        if(!state->cancelled && state->activeStream==(__bridge void*)stream && state->frames.pauseInput())[self pauseInputForStream:stream generation:state->frames.generation()];
        return;
    }
    if(!status || status.integerValue!=SCFrameStatusComplete)return;
    CVPixelBufferRef image=CMSampleBufferGetImageBuffer(sample);
    if(!image || CVPixelBufferGetPixelFormatType(image)!=kCVPixelFormatType_32BGRA)return;
    if(CVPixelBufferLockBaseAddress(image,kCVPixelBufferLock_ReadOnly)!=kCVReturnSuccess)return;
    try {
        const auto width=CVPixelBufferGetWidth(image),height=CVPixelBufferGetHeight(image),stride=CVPixelBufferGetBytesPerRow(image);
        const auto* base=static_cast<const uint8_t*>(CVPixelBufferGetBaseAddress(image));
        std::lock_guard lock(state->mutex);
        if(!state->cancelled && state->activeStream==(__bridge void*)stream) {
            try {
                if(width>16777216 || height>16777216)throw std::runtime_error("Invalid captured pixel buffer");
                std::optional<magpie::CaptureContent> content;
                NSDictionary* rectangle=frameInfo[SCStreamFrameInfoContentRect];
                NSNumber* pixelScale=frameInfo[SCStreamFrameInfoScaleFactor];
                NSNumber* contentScale=frameInfo[SCStreamFrameInfoContentScale];
                if(rectangle && pixelScale && contentScale) {
                    CGRect rect;
                    if(!CGRectMakeWithDictionaryRepresentation((__bridge CFDictionaryRef)rectangle,&rect))throw std::runtime_error("Invalid capture content rectangle");
                    content=magpie::CaptureContent{rect.origin.x,rect.origin.y,rect.size.width,rect.size.height,pixelScale.doubleValue,contentScale.doubleValue};
                }
                auto geometry=magpie::captureGeometry({uint32_t(width),uint32_t(height)},content);
                const auto generation=state->frames.generation();
                if(geometry.metadata && geometry.natural.width && geometry.natural.height)state->resize.observe(generation,geometry.natural);
                auto request=state->resize.next();
                if(!state->resize.ready(geometry)) {
                    if(state->frames.pauseInput())[self pauseInputForStream:stream generation:generation];
                } else state->frames.publishRegion(generation,base,uint32_t(width),uint32_t(height),stride,geometry.x,geometry.y,geometry.crop.width,geometry.crop.height);
                if(request)[self requestResize:*request stream:stream];
            }catch(const std::exception& error) {state->failure=error.what();state->changed.notify_all();}
        }
    } catch(const std::exception& error){state->failStream((__bridge void*)stream,error.what());}
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
        return state->frames.deliver(rgba,width,height)?1:0;
    }
};
}
extern "C" void* mp_capture_create(){@autoreleasepool{try{if(@available(macOS 14.0,*))return new Capture();throw std::runtime_error("Screen capture requires macOS 14 or newer");}catch(const std::exception& ex){magpie::error=ex.what();return nullptr;}}}
extern "C" void mp_capture_destroy(void* c){@autoreleasepool{delete static_cast<Capture*>(c);}}
extern "C" uint32_t mp_capture_backend(void){return 2;}
extern "C" uint64_t mp_capture_frame_sequence(void* capture) {
    if(!capture)return 0;
    auto state=static_cast<Capture*>(capture)->state;std::lock_guard lock(state->mutex);return state->frames.sequence();
}
extern "C" int mp_capture_source(void* capture,mp_capture_source_info* info,uint32_t size){@autoreleasepool{
    if(!capture||!info||size!=sizeof(*info)){magpie::error="Invalid source metadata arguments";return 0;}
    auto state=static_cast<Capture*>(capture)->state;std::lock_guard lock(state->mutex);*info=state->frames.source();return 1;
}}
extern "C" int mp_capture_snapshot_title(void* capture,uint64_t expectedWindow,uint32_t expectedProcess,uint64_t expectedSequence,mp_capture_source_info* info,uint32_t size) {@autoreleasepool{
    if(!capture||!info||size!=sizeof(*info)||!expectedWindow||!expectedProcess||expectedWindow>UINT32_MAX)return 0;
    auto state=static_cast<Capture*>(capture)->state;
    {
        std::lock_guard lock(state->mutex);
        if(state->cancelled||!state->frames.permitsInput(expectedWindow,expectedProcess,expectedSequence))return 0;
    }
    NSArray* windows=CFBridgingRelease(CGWindowListCopyWindowInfo(kCGWindowListOptionIncludingWindow,CGWindowID(expectedWindow)));
    for(NSDictionary* window in windows) {
        if([window[(__bridge NSString*)kCGWindowNumber] unsignedIntValue]!=expectedWindow||[window[(__bridge NSString*)kCGWindowOwnerPID] unsignedIntValue]!=expectedProcess)continue;
        id name=window[(__bridge NSString*)kCGWindowName];
        if(![name isKindOfClass:NSString.class])return 0;
        const char* text=[(NSString*)name UTF8String];if(!text)return 0;
        auto result=magpie::emptySource();result.kind=1;result.window_id=expectedWindow;result.process_id=expectedProcess;
        result.flags=MP_SOURCE_WINDOW|MP_SOURCE_PID|MP_SOURCE_TITLE;magpie::sourceText(result.title,text);
        // The query runs without the capture lock. Recheck after the window
        // server reply in case resize/reselection invalidated the UI snapshot.
        std::lock_guard lock(state->mutex);
        if(state->cancelled||!state->frames.permitsInput(expectedWindow,expectedProcess,expectedSequence))return 0;
        *info=result;return 1;
    }
    return 0;
}}
extern "C" void mp_capture_cancel(void* c){@autoreleasepool{if(c)static_cast<Capture*>(c)->cancel();}}
extern "C" int mp_capture_start(void* c){@autoreleasepool{try{if(!c)throw std::runtime_error("Capture is not initialized");static_cast<Capture*>(c)->start();return 1;}catch(const std::exception& ex){magpie::error=ex.what();return 0;}}}
extern "C" int mp_capture_frame(void* c,const uint8_t** rgba,uint32_t* w,uint32_t* h){@autoreleasepool{try{if(!c||!rgba||!w||!h)throw std::runtime_error("Invalid capture arguments");return static_cast<Capture*>(c)->frame(rgba,w,h);}catch(const std::exception& ex){magpie::error=ex.what();return -1;}}}

extern "C" int mp_capture_configure_input(void* c,void* window,double x,double y,double width,double height,
    double cropX,double cropY,double cropWidth,double cropHeight,double toolbarHeight,
    const char* reservedCharacter,int reservedCode,uint64_t reservedModifiers,uint64_t expectedWindow,uint32_t expectedProcess,uint64_t expectedSequence,const char* screenshotCharacter,int screenshotCode,uint64_t screenshotModifiers) {
    @autoreleasepool {
        if(!c || !window || ![NSThread isMainThread])return -1;
        auto capture=static_cast<Capture*>(c);
        if(capture->state->cancelled)return -1;
        auto input=capture->observer.input;
        if(!input)return 0;
        auto source=magpie::emptySource();
        bool pending;
        {
            std::lock_guard lock(capture->state->mutex);
            const auto& frames=capture->state->frames;
            pending=!frames.matchesPresentedSource(expectedWindow,expectedProcess,expectedSequence);
            if(frames.permitsInput(expectedWindow,expectedProcess,expectedSequence))source=frames.source();
        }
        [input setSourceWindow:CGWindowID(source.window_id) process:pid_t(source.process_id)];
        [input setReservedCharacter:reservedCharacter?[NSString stringWithUTF8String:reservedCharacter]:@""
            keyCode:reservedCode modifiers:reservedModifiers];
        [input setScreenshotCharacter:screenshotCharacter?[NSString stringWithUTF8String:screenshotCharacter]:@"" keyCode:screenshotCode modifiers:screenshotModifiers];
        int result=[input configureWindow:(__bridge NSWindow*)window destination:CGRectMake(x,y,width,height)
            crop:CGRectMake(cropX,cropY,cropWidth,cropHeight) toolbarHeight:toolbarHeight];
        return pending?3:result;
    }
}
