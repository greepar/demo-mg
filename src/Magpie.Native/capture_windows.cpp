#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <dwmapi.h>
#include "magpie.h"
#include "error.h"
#include "source_info.h"
#include "capture_epoch.h"
#include "maximized_policy.h"
#include "windows_source_watch.h"
#include "windows_graphics_capture.h"
#include "windows_output_layer.h"
#include <atomic>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <vector>

namespace {
struct Capture {
    uint32_t backend=3;
    bool titleBar{};
    bool maximizedPolicyConfigured{},windowed{},allowMaximized{};
    std::atomic<HWND> policyRenderer{nullptr};
    std::atomic<bool> cancelled{false};
    HWND window{};
    DWORD sourceProcess{};
    magpie::WindowIdentity identity;
    std::unique_ptr<magpie::WindowsSourceWatch> lifetimeWatch;
    std::unique_ptr<magpie::WindowsGraphicsCapture> graphics;
    uint64_t graphicsRevision{};
    HDC memory{};
    HBITMAP bitmap{};
    HGDIOBJ previous{};
    void* bits{};
    uint32_t width{}, height{},bitmapWidth{},bitmapHeight{};
    std::vector<uint8_t> pixels,pending;
    std::mutex epochMutex;
    magpie::CaptureEpoch epoch;
    bool sameSource() const {
        DWORD process{};DWORD thread=window?GetWindowThreadProcessId(window,&process):0;
        const auto id=uint64_t(reinterpret_cast<uintptr_t>(window));
        return !cancelled&&(lifetimeWatch?lifetimeWatch->matches(id,process,thread):identity.matches(id,process,thread));
    }
    void requireSource() {
        if(sameSource())return;
        { std::lock_guard lock(epochMutex);epoch.pause(); }
        throw std::runtime_error("The selected source window was closed or its process/thread identity changed");
    }
    bool suspended() const { return window && (IsIconic(window)||!IsWindowVisible(window)); }
    magpie::MaximizedPolicy sourcePolicy() const {
        if(!maximizedPolicyConfigured)return magpie::MaximizedPolicy::Allowed;
        auto policy=magpie::maximizedPolicy(IsZoomed(window)!=FALSE,windowed,allowMaximized);
        if(policy!=magpie::MaximizedPolicy::Allowed||allowMaximized)return policy;
        HWND renderer=policyRenderer.load();if(!renderer)return policy;
        BOOL native{};bool nativeFrame=SUCCEEDED(DwmGetWindowAttribute(window,DWMWA_NCRENDERING_ENABLED,&native,sizeof(native)))&&native;
        if(nativeFrame)return policy;
        DWORD process{};
        if(!GetWindowThreadProcessId(renderer,&process)||process!=GetCurrentProcessId()||GetAncestor(renderer,GA_ROOT)!=renderer)
            throw std::runtime_error("The owned scaling renderer is no longer available");
        RECT sourceRect{},rendererRect{};POINT origin{};
        if(!GetWindowRect(window,&sourceRect)||!GetClientRect(renderer,&rendererRect)||!ClientToScreen(renderer,&origin))
            throw std::runtime_error("Cannot compare source and renderer bounds");
        OffsetRect(&rendererRect,origin.x,origin.y);
        return magpie::fullscreenPolicy(nativeFrame,EqualRect(&sourceRect,&rendererRect)!=FALSE,allowMaximized);
    }
    void requireSourcePolicy() {
        if(suspended())return;
        try {
            auto policy=sourcePolicy();
            if(policy==magpie::MaximizedPolicy::Allowed)return;
            throw std::runtime_error(magpie::maximizedPolicyMessage(policy));
        }catch(...) {
            // This is a fatal presentation-policy error, not a hidden-source
            // wait. Invalidate input without restarting the still-visible WGC
            // session just before its consumer tears capture down.
            {std::lock_guard lock(epochMutex);epoch.pause();}
            throw;
        }
    }
    void pause() {
        if(graphics)graphics->suspend();
        std::lock_guard lock(epochMutex);epoch.pause();
    }
    uint32_t sourceState() {
        if(cancelled) {pause();return 3;}
        if(!window)return 0;
        if(!sameSource()) {pause();return 3;}
        if(suspended()) {pause();return 2;}
        return 1;
    }
    bool snapshotMatches(uint64_t sequence) {
        std::lock_guard lock(epochMutex);RECT rect{};
        try {
        if(graphics&&suspended())graphics->suspend();
        if(cancelled||!sameSource()||suspended()||sourcePolicy()!=magpie::MaximizedPolicy::Allowed||(graphics&&(graphics->paused()||graphics->revision()!=graphicsRevision))||!GetClientRect(window,&rect)) { epoch.pause();return false; }
        if(graphics) {
            if(!graphics->sizeMatches(width,height)) {epoch.pause();return false;}
            return epoch.accepts(sequence)&&sameSource();
        }
        const auto w=uint32_t(rect.right-rect.left),h=uint32_t(rect.bottom-rect.top);
        if(!epoch.accepts(epoch.sequence(),w,h)) { epoch.pause();return false; }
        return epoch.accepts(sequence,w,h)&&sameSource();
        }catch(...) {epoch.pause();throw;}
    }
    ~Capture() { cancelled=true;graphics.reset();lifetimeWatch.reset();reset(); if (memory) DeleteDC(memory); }
    void reset() {
        if (previous) SelectObject(memory, previous);
        if (bitmap) DeleteObject(bitmap);
        previous=nullptr; bitmap=nullptr; bits=nullptr; bitmapWidth=bitmapHeight=0;
    }
    void start() {
        if (cancelled) throw std::runtime_error("Capture was cancelled");
        if(window||memory||lifetimeWatch)throw std::runtime_error("Capture was already started; create a fresh capture for a new source");
        window=GetForegroundWindow();
        DWORD process{};
        DWORD thread=window?GetWindowThreadProcessId(window,&process):0;
        if (!window || !thread) throw std::runtime_error("No foreground window is available");
        if (process==GetCurrentProcessId()) throw std::runtime_error("Select another application's source window during the start delay.");
        sourceProcess=process;identity={uint64_t(reinterpret_cast<uintptr_t>(window)),process,thread};
        requireSource();
        requireSourcePolicy();
        lifetimeWatch=std::make_unique<magpie::WindowsSourceWatch>(identity,cancelled);lifetimeWatch->start();
        requireSource();
        if(backend==4) {graphics=std::make_unique<magpie::WindowsGraphicsCapture>(window,titleBar,cancelled,[this]{return sameSource();});return;}
        memory=CreateCompatibleDC(nullptr);
        if (!memory) throw std::runtime_error("Cannot create a Windows capture DC");
    }
    int frame(const uint8_t** rgba,uint32_t* w,uint32_t* h) {
        if (cancelled) throw std::runtime_error("Capture was stopped");
        requireSource();
        if(suspended()) {pause();return 0;}
        requireSourcePolicy();
        if(graphics) {
            const uint8_t* captured{};uint32_t capturedWidth{},capturedHeight{};
            uint64_t revision{};int result=graphics->frame(&captured,&capturedWidth,&capturedHeight,&revision);requireSource();
            if(suspended()) {pause();return 0;}
            requireSourcePolicy();
            std::lock_guard lock(epochMutex);
            if(revision!=graphics->revision()) {epoch.pause();return 0;}
            if(graphics->paused()||revision!=graphicsRevision)epoch.pause();
            if(result==1) {
                if(!graphics->sizeMatches(capturedWidth,capturedHeight)) {epoch.pause();return 0;}
                // A rejected mailbox delivery must not recycle the buffer still
                // held by the UI after we return Waiting. Own the outer C ABI
                // buffer separately and replace it only for accepted frames.
                pixels.assign(captured,captured+size_t(capturedWidth)*capturedHeight*4);
                width=capturedWidth;height=capturedHeight;graphicsRevision=revision;epoch.deliver(width,height);
                *rgba=pixels.data();*w=width;*h=height;
            }
            return result;
        }
        RECT rect{};
        if (!GetClientRect(window,&rect)) throw std::runtime_error("Cannot read source window dimensions");
        const auto nw=uint32_t(rect.right-rect.left), nh=uint32_t(rect.bottom-rect.top);
        if(!nw||!nh) {pause();return 0;}
        if(uint64_t(nw)*nh>16777216)throw std::runtime_error("Unsupported source window dimensions");
        HDC source=GetDC(window);
        if(!source) {requireSource();if(suspended()) {pause();return 0;}throw std::runtime_error("Cannot access source window pixels");}
        struct Release { HWND window; HDC dc; ~Release(){ReleaseDC(window,dc);} } release{window,source};
        if (bitmapWidth!=nw || bitmapHeight!=nh) {
            {std::lock_guard lock(epochMutex);epoch.pause();}
            reset();
            BITMAPINFO info{};info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);
            info.bmiHeader.biWidth=LONG(nw);info.bmiHeader.biHeight=-LONG(nh);
            info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;info.bmiHeader.biCompression=BI_RGB;
            bitmap=CreateDIBSection(source,&info,DIB_RGB_COLORS,&bits,nullptr,0);
            if (!bitmap) throw std::runtime_error("Cannot allocate source pixel buffer");
            previous=SelectObject(memory,bitmap);
            if (!previous || previous==HGDI_ERROR) { previous=nullptr; throw std::runtime_error("Cannot bind source pixel buffer"); }
            bitmapWidth=nw;bitmapHeight=nh;
        }
        if(!BitBlt(memory,0,0,int(nw),int(nh),source,0,0,SRCCOPY|CAPTUREBLT)) {requireSource();if(suspended()) {pause();return 0;}throw std::runtime_error("GDI could not capture this window; GPU/protected content may require Graphics Capture");}
        GdiFlush();requireSource();
        pending.resize(size_t(nw)*nh*4);
        const auto* bgra=static_cast<const uint8_t*>(bits);
        for(size_t i=0;i<pending.size();i+=4) {pending[i]=bgra[i+2];pending[i+1]=bgra[i+1];pending[i+2]=bgra[i];pending[i+3]=255;}
        requireSource();
        RECT current{};
        requireSourcePolicy();
        if(suspended()||!GetClientRect(window,&current)||current.right-current.left!=LONG(nw)||current.bottom-current.top!=LONG(nh)) {pause();return 0;}
        {std::lock_guard lock(epochMutex);epoch.deliver(nw,nh);pixels.swap(pending);width=nw;height=nh;}
        *rgba=pixels.data();*w=width;*h=height;
        return 1;
    }
};
std::string utf8(const wchar_t* value,size_t length) {
    if(!length)return {};
    int bytes=WideCharToMultiByte(CP_UTF8,0,value,int(length),nullptr,0,nullptr,nullptr);
    if(bytes<=0)return {};
    std::string result(size_t(bytes),'\0');WideCharToMultiByte(CP_UTF8,0,value,int(length),result.data(),bytes,nullptr,nullptr);return result;
}
}
extern "C" int mp_capture_source(void* capture,mp_capture_source_info* info,uint32_t size) {
    try {
        if(!capture||!info||size!=sizeof(*info))throw std::runtime_error("Invalid source metadata arguments");
        *info=magpie::emptySource();auto& c=*static_cast<Capture*>(capture);
        DWORD process{};
        if(!c.window)return 1;
        if(!c.sameSource()||!GetWindowThreadProcessId(c.window,&process)||process!=c.sourceProcess)throw std::runtime_error("The selected source window is no longer available");
        info->kind=1;info->process_id=process;info->window_id=uint64_t(reinterpret_cast<uintptr_t>(c.window));info->flags=MP_SOURCE_PID|MP_SOURCE_WINDOW;
        wchar_t title[4096]{};int length=GetWindowTextW(c.window,title,int(std::size(title)));
        if(length>0){magpie::sourceText(info->title,utf8(title,size_t(length)));info->flags|=MP_SOURCE_TITLE;}
        HANDLE handle=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,process);
        if(handle) {
            wchar_t path[32768]{};DWORD capacity=DWORD(std::size(path));BOOL success=QueryFullProcessImageNameW(handle,0,path,&capacity);CloseHandle(handle);
            if(success) {
                auto text=utf8(path,capacity);
                if(!text.empty()&&text.size()<sizeof(info->executable_path)){magpie::sourceText(info->executable_path,text);info->flags|=MP_SOURCE_EXECUTABLE;}
                auto name=text.substr(text.find_last_of("/\\")+1);auto dot=name.find_last_of('.');if(dot!=std::string::npos)name.resize(dot);
                magpie::sourceText(info->process_name,name);if(!name.empty())info->flags|=MP_SOURCE_PROCESS_NAME;
            }
        }
        if(!c.sameSource())throw std::runtime_error("The selected source identity changed during metadata query");
        return 1;
    }catch(const std::exception& ex){magpie::error=ex.what();return 0;}
}
extern "C" int mp_capture_snapshot_title(void* capture,uint64_t expectedWindow,uint32_t expectedProcess,uint64_t expectedSequence,mp_capture_source_info* info,uint32_t size) {
    try {
        if(!capture||!info||size!=sizeof(*info)||!expectedWindow||!expectedProcess)return 0;
        auto& c=*static_cast<Capture*>(capture);DWORD process{};
        if(c.cancelled||uint64_t(reinterpret_cast<uintptr_t>(c.window))!=expectedWindow||c.sourceProcess!=expectedProcess||
            !IsWindow(c.window)||!GetWindowThreadProcessId(c.window,&process)||process!=expectedProcess||!c.snapshotMatches(expectedSequence))return 0;
        wchar_t title[4096]{};SetLastError(ERROR_SUCCESS);
        int length=GetWindowTextW(c.window,title,int(std::size(title)));DWORD error=GetLastError();
        if(length==0&&error!=ERROR_SUCCESS)return 0;
        if(c.cancelled||!IsWindow(c.window)||!GetWindowThreadProcessId(c.window,&process)||process!=expectedProcess||!c.snapshotMatches(expectedSequence))return 0;
        auto result=magpie::emptySource();result.kind=1;result.window_id=expectedWindow;result.process_id=expectedProcess;
        result.flags=MP_SOURCE_WINDOW|MP_SOURCE_PID|MP_SOURCE_TITLE;magpie::sourceText(result.title,utf8(title,size_t(length)));*info=result;return 1;
    }catch(const std::exception& ex){magpie::error=ex.what();return 0;}
}
extern "C" int mp_capture_restore_output_layer(void* capture,void* output,uint64_t expectedWindow,uint32_t expectedProcess) {
    try {
        if(!capture||!output||!expectedWindow||!expectedProcess)return 0;
        auto& c=*static_cast<Capture*>(capture);
        HWND window=static_cast<HWND>(output);DWORD process{};
        if(c.cancelled||uint64_t(reinterpret_cast<uintptr_t>(c.window))!=expectedWindow||c.sourceProcess!=expectedProcess||!c.sameSource())return 0;
        if(window==c.window||!GetWindowThreadProcessId(window,&process)||process!=GetCurrentProcessId()||
            GetAncestor(window,GA_ROOT)!=window||GetWindow(window,GW_OWNER)!=c.window)
            throw std::runtime_error("Output layer restoration requires this process's top-level window owned by the selected live source");
        if(c.suspended())return 0;
        if(!magpie::restoreOutputLayer(window,c.window))throw std::runtime_error("Cannot restore source/output/foreground window order");
        return 1;
    }catch(const std::exception& ex){magpie::error=ex.what();return -1;}
}
extern "C" int mp_capture_mark_output_fullscreen(void* capture,void* output,uint64_t expectedWindow,uint32_t expectedProcess,int fullscreen) {
    try {
        if(!capture||!output||!expectedWindow||!expectedProcess)return 0;
        auto& c=*static_cast<Capture*>(capture);HWND window=static_cast<HWND>(output);DWORD process{};
        if(c.cancelled||uint64_t(reinterpret_cast<uintptr_t>(c.window))!=expectedWindow||c.sourceProcess!=expectedProcess||!c.sameSource())return 0;
        if((fullscreen!=0&&fullscreen!=1)||window==c.window||!GetWindowThreadProcessId(window,&process)||process!=GetCurrentProcessId()||
            GetAncestor(window,GA_ROOT)!=window||GetWindow(window,GW_OWNER)!=c.window)
            throw std::runtime_error("Fullscreen shell update requires this process's top-level output owned by the selected live source");
        if(c.suspended())return 0;
        return magpie::markOutputFullscreen(window,fullscreen!=0,[&] {
            DWORD current{};
            return !c.cancelled&&c.sameSource()&&!c.suspended()&&GetWindowThreadProcessId(window,&current)&&
                current==GetCurrentProcessId()&&GetAncestor(window,GA_ROOT)==window&&GetWindow(window,GW_OWNER)==c.window;
        });
    }catch(const std::exception& ex){magpie::error=ex.what();return -1;}
}
extern "C" void* mp_capture_create() {
    return mp_capture_create_backend(0);
}
extern "C" int mp_capture_windows_pointer_rect(void* capture,uint64_t window,uint32_t process,uint64_t sequence,mp_pointer_rect* rect,uint32_t size) {
    try {
        if(!capture||!rect||size!=sizeof(*rect))throw std::runtime_error("Invalid pointer geometry arguments");
        auto& c=*static_cast<Capture*>(capture);
        if(!window||!process||window!=uint64_t(reinterpret_cast<uintptr_t>(c.window))||process!=c.sourceProcess||!c.snapshotMatches(sequence))return 0;
        RECT region{};
        if(c.graphics) {if(!c.graphics->pointerRect(region))return 0;}
        else {
            POINT origin{};if(!GetClientRect(c.window,&region)||!ClientToScreen(c.window,&origin))return 0;
            OffsetRect(&region,origin.x,origin.y);
        }
        if(!c.snapshotMatches(sequence))return 0;
        *rect={region.left,region.top,region.right,region.bottom};return 1;
    }catch(const std::exception& ex){magpie::error=ex.what();return -1;}
}
extern "C" void mp_capture_destroy(void* c){delete static_cast<Capture*>(c);}
extern "C" uint32_t mp_capture_backend(void){return magpie::WindowsGraphicsCapture::supported()?4u:3u;}
extern "C" uint32_t mp_capture_backend_mask(void){return (1u<<3)|(magpie::WindowsGraphicsCapture::supported()?(1u<<4):0u);}
extern "C" uint32_t mp_capture_instance_backend(void* capture){return capture?static_cast<Capture*>(capture)->backend:0;}
extern "C" int mp_capture_set_title_bar(void* capture,int enabled) {
    try {
        if(!capture||(enabled!=0&&enabled!=1))throw std::runtime_error("Invalid title bar capture arguments");
        auto& c=*static_cast<Capture*>(capture);
        if(c.window)throw std::runtime_error("Title bar capture must be configured before starting a fresh session");
        if(enabled&&c.backend!=4) {magpie::error="This backend cannot toggle title bar capture";return 0;}
        c.titleBar=enabled!=0;return 1;
    }catch(const std::exception& ex){magpie::error=ex.what();return -1;}
}
extern "C" int mp_capture_set_maximized_policy(void* capture,int windowed,int allow) {
    try {
        if(!capture||(windowed!=0&&windowed!=1)||(allow!=0&&allow!=1))throw std::runtime_error("Invalid maximized source policy arguments");
        auto& c=*static_cast<Capture*>(capture);
        if(c.window)throw std::runtime_error("Maximized source policy must be configured before starting a fresh session");
        c.windowed=windowed!=0;c.allowMaximized=allow!=0;c.maximizedPolicyConfigured=true;return 1;
    }catch(const std::exception& ex){magpie::error=ex.what();return -1;}
}
extern "C" int mp_capture_attach_renderer_policy(void* capture,void* output) {
    try {
        if(!capture||!output)throw std::runtime_error("Invalid scaling renderer policy arguments");
        auto& c=*static_cast<Capture*>(capture);HWND renderer=static_cast<HWND>(output);DWORD process{};
        if(!c.maximizedPolicyConfigured)throw std::runtime_error("Scaling source policy is not configured");
        if(!c.window||!c.sameSource())return 0;
        HWND owner=GetWindow(renderer,GW_OWNER);
        // Owner reconstruction can still be pending after Show/restore. The
        // read-only geometry guard must already protect this process's output.
        if(!GetWindowThreadProcessId(renderer,&process)||process!=GetCurrentProcessId()||GetAncestor(renderer,GA_ROOT)!=renderer||(owner&&owner!=c.window))
            throw std::runtime_error("Scaling policy requires this process's renderer owned by the selected source");
        c.policyRenderer=renderer;if(c.suspended())return 0;
        c.requireSourcePolicy();return 1;
    }catch(const std::exception& ex){magpie::error=ex.what();return -1;}
}
extern "C" void* mp_capture_create_backend(uint32_t backend) {
    try {
        const auto mask=mp_capture_backend_mask();
        if(backend==0)backend=(mask&(1u<<4))?4u:3u;
        if(backend>=32||!(mask&(1u<<backend)))throw std::runtime_error("The requested Windows capture backend is not supported by this desktop");
        auto capture=std::make_unique<Capture>();capture->backend=backend;return capture.release();
    }catch(const std::exception& ex){magpie::error=ex.what();return nullptr;}
}
extern "C" uint32_t mp_capture_source_state(void* capture) {return capture?static_cast<Capture*>(capture)->sourceState():0;}
extern "C" uint64_t mp_capture_frame_sequence(void* capture) {
    if(!capture)return 0;
    auto& c=*static_cast<Capture*>(capture);std::lock_guard lock(c.epochMutex);return c.epoch.sequence();
}
extern "C" void mp_capture_cancel(void* c){if(c)static_cast<Capture*>(c)->cancelled=true;}
extern "C" int mp_capture_start(void* c){try {if(!c)throw std::runtime_error("Capture is not initialized");static_cast<Capture*>(c)->start();return 1;}catch(const std::exception& ex){magpie::error=ex.what();return 0;}}
extern "C" int mp_capture_frame(void* c,const uint8_t** rgba,uint32_t* w,uint32_t* h){try{if(!c||!rgba||!w||!h)throw std::runtime_error("Invalid capture arguments");return static_cast<Capture*>(c)->frame(rgba,w,h);}catch(const std::exception& ex){magpie::error=ex.what();return -1;}}
