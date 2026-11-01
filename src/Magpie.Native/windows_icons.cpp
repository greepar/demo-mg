#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <shlobj.h>
#include "magpie.h"
#include "error.h"
#include "icon_pixels.h"
#include <memory>
#include <string>
#include <mutex>

namespace {
struct Icon {HICON value{};~Icon(){if(value)DestroyIcon(value);}};
struct Bitmap {HBITMAP value{};~Bitmap(){if(value)DeleteObject(value);}};
struct Dc {HDC value=CreateCompatibleDC(nullptr);~Dc(){if(value)DeleteDC(value);}};
struct Pixels {uint32_t width{},height{};std::vector<uint8_t> rgba;};
bool matches(HWND window,uint32_t process,uint32_t thread) {
    DWORD current{};return window&&GetWindowThreadProcessId(window,&current)==thread&&current==process;
}
SIZE dimensions(HICON icon) {
    ICONINFO info{};if(!icon||!GetIconInfo(icon,&info))return {};
    Bitmap color{info.hbmColor},mask{info.hbmMask};BITMAP bitmap{};
    if(!info.fIcon||!color.value||!GetObjectW(color.value,sizeof(bitmap),&bitmap))return {};
    return {bitmap.bmWidth,bitmap.bmHeight};
}
HICON windowIcon(HWND window,uint32_t preferred) {
    // Keep the original small/big/class/owner order, but bound queries to an
    // unresponsive application. These requests run off the UI thread.
    for(int depth=0;window&&depth<256;window=GetWindow(window,GW_OWNER),++depth) {
        DWORD_PTR reply{};HICON small{};
        if(SendMessageTimeoutW(window,WM_GETICON,ICON_SMALL,0,SMTO_ERRORONEXIT,500,&reply)&&reply) {
            small=reinterpret_cast<HICON>(reply);if(dimensions(small).cx>=LONG(preferred))return CopyIcon(small);
        }
        reply=0;if(SendMessageTimeoutW(window,WM_GETICON,ICON_BIG,0,SMTO_ERRORONEXIT,500,&reply)&&reply)return CopyIcon(reinterpret_cast<HICON>(reply));
        if(!small) {small=reinterpret_cast<HICON>(GetClassLongPtrW(window,GCLP_HICONSM));if(small&&dimensions(small).cx>=LONG(preferred))return CopyIcon(small);}
        if(HICON big=reinterpret_cast<HICON>(GetClassLongPtrW(window,GCLP_HICON)))return CopyIcon(big);
        if(small)return CopyIcon(small);
    }
    return nullptr;
}
std::unique_ptr<Pixels> readIcon(HICON icon) {
    ICONINFO info{};if(!GetIconInfo(icon,&info))return {};
    Bitmap color{info.hbmColor},mask{info.hbmMask};BITMAP bitmap{};
    if(!info.fIcon||!color.value||!GetObjectW(color.value,sizeof(bitmap),&bitmap)||bitmap.bmWidth<=0||bitmap.bmHeight<=0||
        bitmap.bmWidth>4096||bitmap.bmHeight>4096||uint64_t(bitmap.bmWidth)*bitmap.bmHeight>1048576)return {};
    Dc dc;if(!dc.value)return {};
    auto read=[&](HBITMAP source)->std::vector<uint8_t> {
        BITMAP actual{};if(!GetObjectW(source,sizeof(actual),&actual)||actual.bmWidth!=bitmap.bmWidth||actual.bmHeight!=bitmap.bmHeight)return {};
        BITMAPINFO format{};format.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);format.bmiHeader.biWidth=bitmap.bmWidth;format.bmiHeader.biHeight=-bitmap.bmHeight;
        format.bmiHeader.biPlanes=1;format.bmiHeader.biBitCount=32;format.bmiHeader.biCompression=BI_RGB;
        std::vector<uint8_t> data(size_t(bitmap.bmWidth)*size_t(bitmap.bmHeight)*4);
        if(GetDIBits(dc.value,source,0,UINT(bitmap.bmHeight),data.data(),&format,DIB_RGB_COLORS)!=bitmap.bmHeight)return {};
        return data;
    };
    auto colors=read(color.value);if(colors.empty())return {};
    bool alpha=false;for(size_t i=3;i<colors.size();i+=4)if(colors[i]){alpha=true;break;}
    std::vector<uint8_t> masks;if(!alpha&&mask.value){masks=read(mask.value);if(masks.empty())return {};}
    auto output=std::make_unique<Pixels>();output->width=uint32_t(bitmap.bmWidth);output->height=uint32_t(bitmap.bmHeight);
    output->rgba=magpie::iconPixels(colors,masks);return output;
}
std::unique_ptr<Pixels> fileIcon(const wchar_t* path,uint32_t preferred) {
    Icon icon;SHDefExtractIconW(path,0,0,&icon.value,nullptr,preferred);if(icon.value)return readIcon(icon.value);
    // Original serializes this Shell fallback because its image factory has
    // produced inconsistent results under concurrent extraction.
    static std::mutex shellMutex;std::lock_guard lock(shellMutex);
    struct Apartment {HRESULT result=CoInitializeEx(nullptr,COINIT_MULTITHREADED);~Apartment(){if(SUCCEEDED(result))CoUninitialize();}} apartment;
    if(FAILED(apartment.result)&&apartment.result!=RPC_E_CHANGED_MODE)return {};
    struct Factory {IShellItemImageFactory* value{};~Factory(){if(value)value->Release();}} factory;
    if(FAILED(SHCreateItemFromParsingName(path,nullptr,IID_PPV_ARGS(&factory.value))))return {};
    Bitmap image;
    for(;;) {
        HRESULT result=factory.value->GetImage({LONG(preferred),LONG(preferred)},SIIGBF(SIIGBF_BIGGERSIZEOK|SIIGBF_ICONONLY),&image.value);
        if(result==E_PENDING){if(image.value){DeleteObject(image.value);image.value=nullptr;}Sleep(0);continue;}
        if(FAILED(result))return {};
        break;
    }
    BITMAP geometry{};
    if(!image.value||!GetObjectW(image.value,sizeof(geometry),&geometry)||geometry.bmWidth<=0||geometry.bmHeight<=0||
        geometry.bmWidth>4096||geometry.bmHeight>4096||uint64_t(geometry.bmWidth)*geometry.bmHeight>1048576)return {};
    Dc dc;if(!dc.value)return {};
    BITMAPINFO format{};format.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);format.bmiHeader.biWidth=geometry.bmWidth;format.bmiHeader.biHeight=-geometry.bmHeight;
    format.bmiHeader.biPlanes=1;format.bmiHeader.biBitCount=32;format.bmiHeader.biCompression=BI_RGB;
    std::vector<uint8_t> bgra(size_t(geometry.bmWidth)*size_t(geometry.bmHeight)*4);
    if(GetDIBits(dc.value,image.value,0,UINT(geometry.bmHeight),bgra.data(),&format,DIB_RGB_COLORS)!=geometry.bmHeight)return {};
    auto output=std::make_unique<Pixels>();output->width=uint32_t(geometry.bmWidth);output->height=uint32_t(geometry.bmHeight);output->rgba.resize(bgra.size());
    // The original Shell path always premultiplies the returned bitmap, even
    // though some Shell handlers may already have premultiplied their data.
    for(size_t i=0;i<bgra.size();i+=4) {
        uint32_t alpha=bgra[i+3];output->rgba[i]=uint8_t((uint32_t(bgra[i+2])*alpha+127)/255);
        output->rgba[i+1]=uint8_t((uint32_t(bgra[i+1])*alpha+127)/255);output->rgba[i+2]=uint8_t((uint32_t(bgra[i])*alpha+127)/255);output->rgba[i+3]=uint8_t(alpha);
    }
    return output;
}
}
extern "C" void* mp_windows_window_icon(uint64_t window,uint32_t process,uint32_t thread,uint32_t preferred) {
    try {
        if(!window||!process||!thread||preferred<1||preferred>512)return nullptr;
        HWND source=reinterpret_cast<HWND>(uintptr_t(window));if(!matches(source,process,thread))return nullptr;
        Icon icon{windowIcon(source,preferred)};std::unique_ptr<Pixels> result;
        if(icon.value)result=readIcon(icon.value);
        else {
            HANDLE handle=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,process);
            if(handle) {
                wchar_t path[32768]{};DWORD count=DWORD(std::size(path));BOOL success=QueryFullProcessImageNameW(handle,0,path,&count);CloseHandle(handle);
                if(success)result=fileIcon(path,preferred);
            }
        }
        if(!matches(source,process,thread))return nullptr;
        return result.release();
    }catch(const std::exception& ex){magpie::error=ex.what();return nullptr;}
}
extern "C" void* mp_windows_executable_icon(const char* path,uint32_t preferred) {
    try {
        if(!path||!*path||preferred<1||preferred>512)return nullptr;
        int size=MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,path,-1,nullptr,0);if(size<2||size>32768)return nullptr;
        std::wstring wide(size_t(size),L'\0');if(!MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,path,-1,wide.data(),size))return nullptr;
        return fileIcon(wide.c_str(),preferred).release();
    }catch(const std::exception& ex){magpie::error=ex.what();return nullptr;}
}
extern "C" int mp_windows_icon_pixels(void* icon,const uint8_t** rgba,uint32_t* width,uint32_t* height) {
    if(!icon||!rgba||!width||!height)return -1;
    const auto& value=*static_cast<Pixels*>(icon);*rgba=value.rgba.data();*width=value.width;*height=value.height;return 1;
}
extern "C" void mp_windows_icon_destroy(void* icon) {delete static_cast<Pixels*>(icon);}
