#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <dwmapi.h>
#include "magpie.h"
#include "error.h"
#include <cstring>
#include <stdexcept>

namespace {
template<class T> T procedure(HMODULE module,const char* name) {
    auto address=module?GetProcAddress(module,name):nullptr;T result{};
    static_assert(sizeof(result)==sizeof(address));std::memcpy(&result,&address,sizeof(result));return result;
}
struct Bitmap {
    HBITMAP handle{};
    ~Bitmap(){if(handle)DeleteObject(handle);}
};
bool pointInWindow(HWND window,POINT point) {
    if(!IsWindowVisible(window)||(GetWindowLongPtrW(window,GWL_EXSTYLE)&WS_EX_TRANSPARENT))return false;
    RECT outer{};if(!GetWindowRect(window,&outer)||!PtInRect(&outer,point))return false;
    DWORD cloaked{};
    if(SUCCEEDED(DwmGetWindowAttribute(window,DWMWA_CLOAKED,&cloaked,sizeof(cloaked)))&&cloaked)return false;
    RECT client{};POINT origin{};
    if(GetClientRect(window,&client)&&ClientToScreen(window,&origin)) {
        OffsetRect(&client,origin.x,origin.y);
        if(PtInRect(&client,point)) {
            SetLastError(0);
            if(ChildWindowFromPointEx(window,{point.x-client.left,point.y-client.top},
                CWP_SKIPINVISIBLE|CWP_SKIPDISABLED|CWP_SKIPTRANSPARENT))return true;
            if(!GetLastError())return false;
        }
    }
    HRGN region=CreateRectRgn(0,0,0,0);
    if(!region)return true;
    int kind=GetWindowRgn(window,region);
    bool visible=(kind!=SIMPLEREGION&&kind!=COMPLEXREGION)||PtInRegion(region,point.x-outer.left,point.y-outer.top);
    DeleteObject(region);return visible;
}
}

extern "C" uint64_t mp_windows_pointer_window(void* output,int32_t x,int32_t y,int skipOutput) {
    HWND target=static_cast<HWND>(output);DWORD process{};
    if(!target||!GetWindowThreadProcessId(target,&process)||process!=GetCurrentProcessId())return 0;
    struct Query {HWND output,hit{};POINT point;bool skip;RECT client{};} query{target,nullptr,{x,y},skipOutput!=0,{}};
    POINT origin{};
    if(!GetClientRect(target,&query.client)||!ClientToScreen(target,&origin))return 0;
    OffsetRect(&query.client,origin.x,origin.y);
    // Port of CursorManager's z-ordered window/region hit test. Do not toggle
    // transparent style on each tick: even a short opaque interval steals real
    // cross-process clicks while the physical pointer is on the source.
    EnumWindows([](HWND window,LPARAM data)->BOOL {
        auto& q=*reinterpret_cast<Query*>(data);
        if(window==q.output) {
            if(q.skip||!IsWindowVisible(window)||!PtInRect(&q.client,q.point))return TRUE;
        }else if(!pointInWindow(window,q.point))return TRUE;
        q.hit=window;return FALSE;
    },reinterpret_cast<LPARAM>(&query));
    return uint64_t(reinterpret_cast<uintptr_t>(query.hit));
}

extern "C" int mp_windows_cursor_copy(uint64_t cursor,mp_cursor_shape* shape,uint32_t size) {
    try {
        if(!cursor||!shape||size!=sizeof(*shape))throw std::runtime_error("Invalid native cursor snapshot arguments");
        HICON copy=CopyIcon(reinterpret_cast<HICON>(uintptr_t(cursor)));
        if(!copy)throw std::runtime_error("Cannot copy actual system cursor");
        struct Icon {HICON handle;~Icon(){if(handle)DestroyIcon(handle);}} owned{copy};
        ICONINFO info{};if(!GetIconInfo(copy,&info))throw std::runtime_error("Cannot query actual cursor bitmaps");
        Bitmap color{info.hbmColor},mask{info.hbmMask};BITMAP bitmap{};
        if(!GetObjectW(info.hbmColor?info.hbmColor:info.hbmMask,sizeof(bitmap),&bitmap))throw std::runtime_error("Cannot query actual cursor dimensions");
        LONG height=info.hbmColor?bitmap.bmHeight:bitmap.bmHeight/2;
        if(bitmap.bmWidth<=0||height<=0||bitmap.bmWidth>4096||height>4096||info.xHotspot>=DWORD(bitmap.bmWidth)||info.yHotspot>=DWORD(height))
            throw std::runtime_error("Unsupported actual cursor bitmap geometry");
        *shape={uint64_t(reinterpret_cast<uintptr_t>(copy)),uint32_t(bitmap.bmWidth),uint32_t(height),info.xHotspot,info.yHotspot};
        owned.handle=nullptr;return 1;
    }catch(const std::exception& ex){magpie::error=ex.what();return -1;}
}
extern "C" void mp_windows_cursor_destroy(uint64_t cursor) {if(cursor)DestroyIcon(reinterpret_cast<HICON>(uintptr_t(cursor)));}

extern "C" int mp_windows_cursor_draw(uint64_t cursor,uint8_t* rgba,uint64_t bytes,uint32_t width,uint32_t height,int32_t x,int32_t y,uint32_t drawWidth,uint32_t drawHeight) {
    try {
        if(!cursor||!rgba||!width||!height||uint64_t(width)*height>16777216||bytes!=uint64_t(width)*height*4||
            !drawWidth||!drawHeight||drawWidth>4096||drawHeight>4096)throw std::runtime_error("Invalid software cursor composite arguments");
        HDC dc=CreateCompatibleDC(nullptr);if(!dc)throw std::runtime_error("Cannot create cursor composition DC");
        struct Dc {HDC handle;~Dc(){DeleteDC(handle);}} ownedDc{dc};
        BITMAPINFO info{};info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);info.bmiHeader.biWidth=LONG(width);info.bmiHeader.biHeight=-LONG(height);
        info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;info.bmiHeader.biCompression=BI_RGB;
        void* bits{};Bitmap bitmap{CreateDIBSection(dc,&info,DIB_RGB_COLORS,&bits,nullptr,0)};
        if(!bitmap.handle||!bits)throw std::runtime_error("Cannot allocate cursor composition surface");
        HGDIOBJ previous=SelectObject(dc,bitmap.handle);
        if(!previous||previous==HGDI_ERROR)throw std::runtime_error("Cannot bind cursor composition surface");
        struct Select {HDC dc;HGDIOBJ previous;~Select(){SelectObject(dc,previous);}} selected{dc,previous};
        auto* bgra=static_cast<uint8_t*>(bits);
        for(size_t i=0;i<size_t(bytes);i+=4) {bgra[i]=rgba[i+2];bgra[i+1]=rgba[i+1];bgra[i+2]=rgba[i];bgra[i+3]=255;}
        // DrawIconEx implements native premultiplied color, masked-color and
        // monochrome AND/XOR against the actual underlying scaled pixels. An
        // alpha-only arrow overlay would corrupt invert/monochrome cursors.
        if(!DrawIconEx(dc,x,y,reinterpret_cast<HICON>(uintptr_t(cursor)),int(drawWidth),int(drawHeight),0,nullptr,DI_NORMAL))
            throw std::runtime_error("Cannot composite actual native cursor");
        GdiFlush();
        for(size_t i=0;i<size_t(bytes);i+=4) {rgba[i]=bgra[i+2];rgba[i+1]=bgra[i+1];rgba[i+2]=bgra[i];rgba[i+3]=255;}
        return 1;
    }catch(const std::exception& ex){magpie::error=ex.what();return -1;}
}

extern "C" int mp_windows_system_cursor(int show) {
    if(show!=0&&show!=1)return -1;
    // Same system-wide visibility primitive and Magnification fallback as the
    // original. ShowCursor's per-input-thread counter is not equivalent.
    using Show=void(WINAPI*)(BOOL);
    static auto native=procedure<Show>(GetModuleHandleW(L"user32.dll"),"ShowSystemCursor");
    if(native) {native(show!=0);return 1;}
    struct Magnification {
        HMODULE library=LoadLibraryW(L"Magnification.dll");
        using Initialize=BOOL(WINAPI*)();using Visibility=BOOL(WINAPI*)(BOOL);
        Initialize initialize=procedure<Initialize>(library,"MagInitialize"),uninitialize=procedure<Initialize>(library,"MagUninitialize");
        Visibility visibility=procedure<Visibility>(library,"MagShowSystemCursor");
        bool ready=initialize&&uninitialize&&visibility&&initialize();
        ~Magnification(){if(ready) {visibility(TRUE);uninitialize();}if(library)FreeLibrary(library);}
    };
    static Magnification fallback;
    return fallback.ready&&fallback.visibility(show!=0)?1:0;
}
