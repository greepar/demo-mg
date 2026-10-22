#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "magpie.h"
#include "error.h"
#include <atomic>
#include <memory>
#include <stdexcept>
#include <vector>

namespace {
struct Capture {
    std::atomic<bool> cancelled{false};
    HWND window{};
    HDC memory{};
    HBITMAP bitmap{};
    HGDIOBJ previous{};
    void* bits{};
    uint32_t width{}, height{};
    std::vector<uint8_t> pixels;
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
        if (!window || !GetWindowThreadProcessId(window,&process)) throw std::runtime_error("No foreground window is available");
        if (process==GetCurrentProcessId()) throw std::runtime_error("Select the source window during the start delay. Windows currently uses the GDI capture backend.");
        memory=CreateCompatibleDC(nullptr);
        if (!memory) throw std::runtime_error("Cannot create a Windows capture DC");
    }
    void frame(const uint8_t** rgba,uint32_t* w,uint32_t* h) {
        if (cancelled) throw std::runtime_error("Capture was stopped");
        if (!IsWindow(window)) throw std::runtime_error("The source window was closed");
        if (IsIconic(window)) throw std::runtime_error("The source window is minimized");
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
        GdiFlush();
        const auto* bgra=static_cast<const uint8_t*>(bits);
        for (size_t i=0;i<pixels.size();i+=4) { pixels[i]=bgra[i+2];pixels[i+1]=bgra[i+1];pixels[i+2]=bgra[i];pixels[i+3]=255; }
        *rgba=pixels.data();*w=width;*h=height;
    }
};
}
extern "C" void* mp_capture_create() {
    try { return new Capture(); } catch(const std::exception& ex){magpie::error=ex.what();return nullptr;}
}
extern "C" void mp_capture_destroy(void* c){delete static_cast<Capture*>(c);}
extern "C" void mp_capture_cancel(void* c){if(c)static_cast<Capture*>(c)->cancelled=true;}
extern "C" int mp_capture_start(void* c){try {if(!c)throw std::runtime_error("Capture is not initialized");static_cast<Capture*>(c)->start();return 1;}catch(const std::exception& ex){magpie::error=ex.what();return 0;}}
extern "C" int mp_capture_frame(void* c,const uint8_t** rgba,uint32_t* w,uint32_t* h){try{if(!c||!rgba||!w||!h)throw std::runtime_error("Invalid capture arguments");static_cast<Capture*>(c)->frame(rgba,w,h);return 1;}catch(const std::exception& ex){magpie::error=ex.what();return -1;}}
