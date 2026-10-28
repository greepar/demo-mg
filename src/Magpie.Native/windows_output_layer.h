#pragma once
#include <windows.h>
#include <dwmapi.h>
#include <shobjidl.h>
#include <memory>
#include <vector>

namespace magpie {
template<class Valid> inline int markOutputFullscreen(HWND output,bool fullscreen,Valid&& valid) {
    HRESULT initialized=CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);
    if(FAILED(initialized)&&initialized!=RPC_E_CHANGED_MODE)return 0;
    struct Apartment {bool balance;~Apartment(){if(balance)CoUninitialize();}} apartment{SUCCEEDED(initialized)};
    struct Taskbar {ITaskbarList2* value{};~Taskbar(){if(value)value->Release();}} taskbar;
    HRESULT created=CoCreateInstance(__uuidof(TaskbarList),nullptr,CLSCTX_INPROC_SERVER,
        __uuidof(ITaskbarList2),reinterpret_cast<void**>(&taskbar.value));
    if(FAILED(created)||!taskbar.value||FAILED(taskbar.value->HrInit()))return 0;
    // COM initialization/activation can pump messages on the UI apartment.
    if(!valid())return 0;
    return SUCCEEDED(taskbar.value->MarkFullscreenWindow(output,fullscreen?TRUE:FALSE))?1:0;
}
inline bool outputProcessIntegrity(HANDLE process,DWORD& level) {
    HANDLE raw{};
    if(!OpenProcessToken(process,TOKEN_QUERY,&raw))return false;
    std::unique_ptr<void,decltype(&CloseHandle)> token(raw,CloseHandle);
    DWORD length{};
    if(GetTokenInformation(token.get(),TokenIntegrityLevel,nullptr,0,&length)||
        GetLastError()!=ERROR_INSUFFICIENT_BUFFER||length<sizeof(TOKEN_MANDATORY_LABEL)||length>65536)return false;
    std::vector<unsigned char> bytes(length);
    if(!GetTokenInformation(token.get(),TokenIntegrityLevel,bytes.data(),length,&length))return false;
    const auto* label=reinterpret_cast<const TOKEN_MANDATORY_LABEL*>(bytes.data());
    if(!IsValidSid(label->Label.Sid))return false;
    const auto count=*GetSidSubAuthorityCount(label->Label.Sid);
    if(!count)return false;
    level=*GetSidSubAuthority(label->Label.Sid,DWORD(count-1));
    return true;
}
inline bool outputWindowMovable(HWND window) {
    DWORD process{};
    if(!window||!GetWindowThreadProcessId(window,&process))return false;
    std::unique_ptr<void,decltype(&CloseHandle)> handle(OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,process),CloseHandle);
    DWORD foregroundLevel{},ownLevel{};
    return handle&&outputProcessIntegrity(handle.get(),foregroundLevel)&&outputProcessIntegrity(GetCurrentProcess(),ownLevel)&&foregroundLevel<=ownLevel;
}

// Original ScalingWindow::_UpdateFocusStateAsync's demotion order. Merely
// clearing TOPMOST can lift the owner above the newly foreground application.
inline bool restoreOutputLayer(HWND output,HWND source) {
    HWND foreground=GetForegroundWindow();
    DWORD foregroundProcess{};
    const DWORD foregroundThread=foreground?GetWindowThreadProcessId(foreground,&foregroundProcess):0;
    const bool movable=outputWindowMovable(foreground);
    constexpr UINT flags=SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOSIZE;
    // For a higher-integrity foreground window, never attempt to reposition
    // that window. Synchronize with composition before the three-window path.
    if(!movable)DwmFlush();
    HDWP positions=BeginDeferWindowPos(movable?2:3);
    if(!positions)return false;
    positions=DeferWindowPos(positions,output,HWND_NOTOPMOST,0,0,0,0,flags);
    if(!positions)return false; // DeferWindowPos frees a failed batch.
    positions=DeferWindowPos(positions,output,source,0,0,0,0,flags|(movable?0:SWP_NOOWNERZORDER));
    if(!positions)return false;
    if(!movable) {
        positions=DeferWindowPos(positions,source,output,0,0,0,0,flags|SWP_NOOWNERZORDER);
        if(!positions)return false;
    }
    if(!EndDeferWindowPos(positions))return false;
    // Exactly the same still-foreground HWND/identity must be restored. A
    // window switch during batching must not raise an obsolete application.
    DWORD liveProcess{};
    if(movable&&foreground&&GetForegroundWindow()==foreground&&foregroundThread&&
        GetWindowThreadProcessId(foreground,&liveProcess)==foregroundThread&&liveProcess==foregroundProcess)
        return SetWindowPos(foreground,HWND_TOP,0,0,0,0,flags)!=0;
    return true;
}
}
