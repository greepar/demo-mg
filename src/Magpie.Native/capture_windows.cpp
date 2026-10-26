#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "magpie.h"
#include "error.h"
#include "source_info.h"
#include "capture_epoch.h"
#include "input_mapping.h"
#include <atomic>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <vector>

namespace {
struct Capture {
    std::atomic<bool> cancelled{false};
    HWND window{};
    DWORD sourceProcess{};
    magpie::WindowIdentity identity;
    HDC memory{};
    HBITMAP bitmap{};
    HGDIOBJ previous{};
    void* bits{};
    uint32_t width{}, height{};
    std::vector<uint8_t> pixels;
    std::mutex epochMutex;
    magpie::CaptureEpoch epoch;
    bool sameSource() const {
        DWORD process{};DWORD thread=window?GetWindowThreadProcessId(window,&process):0;
        return identity.matches(uint64_t(reinterpret_cast<uintptr_t>(window)),process,thread);
    }
    void requireSource() {
        if(sameSource())return;
        { std::lock_guard lock(epochMutex);epoch.pause(); }
        throw std::runtime_error("The selected source window was closed or its process/thread identity changed");
    }
    bool snapshotMatches(uint64_t sequence) {
        std::lock_guard lock(epochMutex);RECT rect{};
        if(cancelled||!sameSource()||IsIconic(window)||!GetClientRect(window,&rect)) { epoch.pause();return false; }
        const auto w=uint32_t(rect.right-rect.left),h=uint32_t(rect.bottom-rect.top);
        if(!epoch.accepts(epoch.sequence(),w,h)) { epoch.pause();return false; }
        return epoch.accepts(sequence,w,h)&&sameSource();
    }
    ~Capture() { reset(); if (memory) DeleteDC(memory); }
    void reset() {
        if (previous) SelectObject(memory, previous);
        if (bitmap) DeleteObject(bitmap);
        previous=nullptr; bitmap=nullptr; bits=nullptr; width=height=0;
    }
    void start() {
        if (cancelled) throw std::runtime_error("Capture was cancelled");
        window=GetForegroundWindow();
        DWORD process{};
        DWORD thread=window?GetWindowThreadProcessId(window,&process):0;
        if (!window || !thread) throw std::runtime_error("No foreground window is available");
        if (process==GetCurrentProcessId()) throw std::runtime_error("Select the source window during the start delay. Windows currently uses the GDI capture backend.");
        sourceProcess=process;identity={uint64_t(reinterpret_cast<uintptr_t>(window)),process,thread};
        requireSource();
        memory=CreateCompatibleDC(nullptr);
        if (!memory) throw std::runtime_error("Cannot create a Windows capture DC");
    }
    void frame(const uint8_t** rgba,uint32_t* w,uint32_t* h) {
        if (cancelled) throw std::runtime_error("Capture was stopped");
        requireSource();
        if (IsIconic(window)) { std::lock_guard lock(epochMutex);epoch.pause();throw std::runtime_error("The source window is minimized"); }
        RECT rect{};
        if (!GetClientRect(window,&rect)) throw std::runtime_error("Cannot read source window dimensions");
        const auto nw=uint32_t(rect.right-rect.left), nh=uint32_t(rect.bottom-rect.top);
        if (!nw || !nh || uint64_t(nw)*nh>16777216) throw std::runtime_error("Unsupported source window dimensions");
        HDC source=GetDC(window);
        if (!source) throw std::runtime_error("Cannot access source window pixels");
        struct Release { HWND window; HDC dc; ~Release(){ReleaseDC(window,dc);} } release{window,source};
        if (width!=nw || height!=nh) {
            reset();
            BITMAPINFO info{};info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);
            info.bmiHeader.biWidth=LONG(nw);info.bmiHeader.biHeight=-LONG(nh);
            info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;info.bmiHeader.biCompression=BI_RGB;
            bitmap=CreateDIBSection(source,&info,DIB_RGB_COLORS,&bits,nullptr,0);
            if (!bitmap) throw std::runtime_error("Cannot allocate source pixel buffer");
            previous=SelectObject(memory,bitmap);
            if (!previous || previous==HGDI_ERROR) { previous=nullptr; throw std::runtime_error("Cannot bind source pixel buffer"); }
            width=nw;height=nh;pixels.resize(size_t(width)*height*4);
        }
        if (!BitBlt(memory,0,0,int(width),int(height),source,0,0,SRCCOPY|CAPTUREBLT)) throw std::runtime_error("GDI could not capture this window; GPU/protected content may require Graphics Capture");
        GdiFlush();requireSource();
        const auto* bgra=static_cast<const uint8_t*>(bits);
        for (size_t i=0;i<pixels.size();i+=4) { pixels[i]=bgra[i+2];pixels[i+1]=bgra[i+1];pixels[i+2]=bgra[i];pixels[i+3]=255; }
        requireSource();
        {std::lock_guard lock(epochMutex);epoch.deliver(width,height);}
        *rgba=pixels.data();*w=width;*h=height;
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
extern "C" void* mp_capture_create() {
    try { return new Capture(); } catch(const std::exception& ex){magpie::error=ex.what();return nullptr;}
}
extern "C" void mp_capture_destroy(void* c){delete static_cast<Capture*>(c);}
extern "C" uint32_t mp_capture_backend(void){return 3;}
extern "C" uint64_t mp_capture_frame_sequence(void* capture) {
    if(!capture)return 0;
    auto& c=*static_cast<Capture*>(capture);std::lock_guard lock(c.epochMutex);return c.epoch.sequence();
}
extern "C" void mp_capture_cancel(void* c){if(c)static_cast<Capture*>(c)->cancelled=true;}
extern "C" int mp_capture_start(void* c){try {if(!c)throw std::runtime_error("Capture is not initialized");static_cast<Capture*>(c)->start();return 1;}catch(const std::exception& ex){magpie::error=ex.what();return 0;}}
extern "C" int mp_capture_frame(void* c,const uint8_t** rgba,uint32_t* w,uint32_t* h){try{if(!c||!rgba||!w||!h)throw std::runtime_error("Invalid capture arguments");static_cast<Capture*>(c)->frame(rgba,w,h);return 1;}catch(const std::exception& ex){magpie::error=ex.what();return -1;}}
