#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <dwmapi.h>
#include "magpie.h"
#include <cstdlib>
#include <cstring>
#include <cstdio>
#include <atomic>
#include <array>
#include <iostream>
#include <iterator>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>
#include <filesystem>
#include <fstream>

// Opt-in only: creates a disposable source process/window on the hosted runner.
// Never registered with the default CTest suite or run on the user's desktop.
namespace {
constexpr UINT Rename=WM_APP+1,NotifyDestroy=WM_APP+2;
LRESULT CALLBACK sourceProc(HWND window,UINT message,WPARAM w,LPARAM l) {
    if(message==WM_SETCURSOR) {
        auto count=reinterpret_cast<uintptr_t>(GetPropW(window,L"MagpieFixtureCursorUpdates"));
        SetPropW(window,L"MagpieFixtureCursorUpdates",reinterpret_cast<HANDLE>(count+1));
    }
    // Native mouse acceptance for the disposable production-output test. These
    // properties observe real dispatched input, never posted mouse messages.
    if(message==WM_LBUTTONDOWN||message==WM_LBUTTONUP||message==WM_MOUSEWHEEL||
        (message==WM_MOUSEMOVE&&(w&MK_LBUTTON))) {
        const wchar_t* property=message==WM_LBUTTONDOWN?L"MagpieFixtureMouseDown":
            message==WM_LBUTTONUP?L"MagpieFixtureMouseUp":message==WM_MOUSEWHEEL?L"MagpieFixtureWheel":L"MagpieFixtureDrag";
        auto count=reinterpret_cast<uintptr_t>(GetPropW(window,property));
        SetPropW(window,property,reinterpret_cast<HANDLE>(count+1));
        SetPropW(window,L"MagpieFixtureMousePoint",reinterpret_cast<HANDLE>(l));
        if(message==WM_LBUTTONDOWN)SetCapture(window);
        if(message==WM_LBUTTONUP)ReleaseCapture();
        if(message==WM_MOUSEWHEEL)SetPropW(window,L"MagpieFixtureWheelDelta",reinterpret_cast<HANDLE>(intptr_t(SHORT(HIWORD(w)))));
        return 0;
    }
    // Observe dispatched keyboard events; the parent reads these properties
    // after SendInput, without posting keyboard messages to this window.
    if(message==WM_KEYDOWN||message==WM_KEYUP||message==WM_CHAR) {
        const wchar_t* property=message==WM_KEYDOWN?L"MagpieFixtureKeyDown":message==WM_KEYUP?L"MagpieFixtureKeyUp":L"MagpieFixtureChar";
        auto count=reinterpret_cast<uintptr_t>(GetPropW(window,property));
        SetPropW(window,property,reinterpret_cast<HANDLE>(count+1));
        SetPropW(window,message==WM_CHAR?L"MagpieFixtureLastChar":L"MagpieFixtureLastKey",reinterpret_cast<HANDLE>(w));
        return 0;
    }
    if(message==WM_PAINT) {
        PAINTSTRUCT paint{};HDC dc=BeginPaint(window,&paint);RECT rect{};GetClientRect(window,&rect);
        HBRUSH brush=CreateSolidBrush(RGB(23,67,109));FillRect(dc,&rect,brush);DeleteObject(brush);EndPaint(window,&paint);return 0;
    }
    if(message==NotifyDestroy) { NotifyWinEvent(EVENT_OBJECT_DESTROY,window,OBJID_WINDOW,CHILDID_SELF);return 0; }
    if(message==Rename) { SetWindowTextW(window,L"Renamed Magpie fixture");return 0; }
    if(message==WM_DESTROY) { PostQuitMessage(0);return 0; }
    return DefWindowProcW(window,message,w,l);
}
void require(bool ok,const char* text) {
    if(!ok) {std::cerr<<"CAPTURE FIXTURE ASSERT: "<<text<<std::endl;throw std::runtime_error(text);}
}
void native(bool ok,const char* operation) {
    // Read the thread-local error AFTER the call. Argument evaluation order
    // must not retain a c_str invalidated by that call updating the error.
    if(!ok) {
        auto message=std::string(operation)+": "+mp_last_error();
        std::cerr<<"CAPTURE FIXTURE CALL FAIL: "<<message<<std::endl;
        throw std::runtime_error(message);
    }
}
LONG WINAPI crash(EXCEPTION_POINTERS* exception) {
    auto address=exception->ExceptionRecord->ExceptionAddress;HMODULE module{};wchar_t path[32768]{};
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(address),&module);
    if(module)GetModuleFileNameW(module,path,DWORD(std::size(path)));
    std::wcerr<<L"CAPTURE FIXTURE CRASH: code=0x"<<std::hex<<exception->ExceptionRecord->ExceptionCode<<L" module="<<path
        <<L" offset=0x"<<(reinterpret_cast<uintptr_t>(address)-reinterpret_cast<uintptr_t>(module))<<std::endl;
    return EXCEPTION_EXECUTE_HANDLER;
}
LONG WINAPI firstAccessViolation(EXCEPTION_POINTERS* exception) {
    static std::atomic<bool> recorded{false};
    if(exception->ExceptionRecord->ExceptionCode!=EXCEPTION_ACCESS_VIOLATION||recorded.exchange(true))return EXCEPTION_CONTINUE_SEARCH;
    // First-chance diagnostics survive failures occurring inside a system
    // component's own exception boundary. Do not consume or translate the AV.
    auto address=exception->ExceptionRecord->ExceptionAddress;HMODULE module{};char path[32768]{};
    GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCSTR>(address),&module);
    if(module)GetModuleFileNameA(module,path,DWORD(std::size(path)));
    std::fprintf(stderr,"CAPTURE FIRST AV: module=%s offset=0x%llx address=%p thread=%lu\n",path,
        static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(address)-reinterpret_cast<uintptr_t>(module)),address,GetCurrentThreadId());
    void* stack[24]{};USHORT count=CaptureStackBackTrace(0,USHORT(std::size(stack)),stack,nullptr);
    for(USHORT i=0;i<count;++i) {
        HMODULE owner{};char name[MAX_PATH]{};
        GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,reinterpret_cast<LPCSTR>(stack[i]),&owner);
        if(owner)GetModuleFileNameA(owner,name,DWORD(std::size(name)));
        std::fprintf(stderr,"CAPTURE AV STACK: %s +0x%llx\n",name,
            static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(stack[i])-reinterpret_cast<uintptr_t>(owner)));
    }
    std::fflush(stderr);return EXCEPTION_CONTINUE_SEARCH;
}
std::wstring title(DWORD parent) { return L"Magpie disposable GDI fixture "+std::to_wstring(parent); }
int child(DWORD parent) {
    std::unique_ptr<void,decltype(&CloseHandle)> owner(OpenProcess(SYNCHRONIZE,FALSE,parent),CloseHandle);
    require(bool(owner),"Fixture parent is not alive");
    HINSTANCE instance=GetModuleHandleW(nullptr);
    // Genuine native AND/XOR cursor: transparent except an inversion cross at
    // its hotspot. Desktop GetPixel excludes the OS pointer, so the production
    // test can independently prove its software cursor is actually composed.
    std::array<BYTE,128> andMask,xorMask{};andMask.fill(255);
    for(int i=0;i<10;++i) {xorMask[size_t(i)*4]|=BYTE(0x80>>3);xorMask[3*4+size_t(i/8)]|=BYTE(0x80>>(i%8));}
    HCURSOR cursor=CreateCursor(instance,3,3,32,32,andMask.data(),xorMask.data());
    require(cursor!=nullptr,"Cannot create native inversion cursor fixture");
    WNDCLASSW cls{};cls.lpfnWndProc=sourceProc;cls.hInstance=instance;cls.lpszClassName=L"MagpieGdiDisposableSource";
    cls.hCursor=cursor;
    require(RegisterClassW(&cls)!=0,"Cannot register source fixture class");
    HWND window=CreateWindowW(cls.lpszClassName,title(parent).c_str(),WS_OVERLAPPEDWINDOW,100,100,300,240,nullptr,nullptr,instance,nullptr);
    require(window!=nullptr,"Cannot create source fixture window");
    // Explicit visible-cursor contract for this source application, including
    // desktops whose initial display count is -1 because no mouse is installed.
    // Balance only our own increment when the source exits.
    struct CursorDisplay {~CursorDisplay(){ShowCursor(FALSE);}} cursorDisplay;
    int cursorCount=ShowCursor(TRUE);
    SetPropW(window,L"MagpieFixtureCursorDisplayCount",reinterpret_cast<HANDLE>(intptr_t(cursorCount)+1000));
    ShowWindow(window,SW_SHOW);UpdateWindow(window);AllowSetForegroundWindow(parent);SetForegroundWindow(window);
    HANDLE parentWait{};
    require(RegisterWaitForSingleObject(&parentWait,owner.get(),[](void* context,BOOLEAN) {
        PostThreadMessageW(DWORD(reinterpret_cast<uintptr_t>(context)),WM_QUIT,0,0);
    },reinterpret_cast<void*>(uintptr_t(GetCurrentThreadId())),INFINITE,WT_EXECUTEONLYONCE)!=0,"Cannot monitor fixture parent lifetime");
    MSG message{};while(GetMessageW(&message,nullptr,0,0)>0) { TranslateMessage(&message);DispatchMessageW(&message); }
    UnregisterWaitEx(parentWait,INVALID_HANDLE_VALUE);
    if(IsWindow(window))DestroyWindow(window);
    UnregisterClassW(cls.lpszClassName,instance);DestroyCursor(cursor);
    return 0;
}
struct Process {
    PROCESS_INFORMATION info{};
    HANDLE job{};
    HWND window{};
    ~Process() {
        if(window&&IsWindow(window))PostMessageW(window,WM_CLOSE,0,0);
        if(info.hProcess) {
            if(WaitForSingleObject(info.hProcess,3000)==WAIT_TIMEOUT) { TerminateProcess(info.hProcess,1);WaitForSingleObject(info.hProcess,3000); }
            CloseHandle(info.hProcess);
        }
        if(info.hThread)CloseHandle(info.hThread);
        if(job)CloseHandle(job);
    }
};
}
int main(int argc,char** argv) {
    SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX);SetUnhandledExceptionFilter(crash);
    try {
        if(!std::getenv("MAGPIE_HOSTED_WINDOWS_CAPTURE_FIXTURE"))throw std::runtime_error("This visible-window fixture requires an explicit disposable hosted-runner opt-in");
        struct Trace {
            PVOID handle=AddVectoredExceptionHandler(1,firstAccessViolation);
            ~Trace(){if(handle)RemoveVectoredExceptionHandler(handle);}
        } trace;
        if(argc==3&&std::string(argv[1])=="--source")return child(DWORD(std::stoul(argv[2])));
        bool titleBar=argc==2&&std::string(argv[1])=="--wgc-title-bar";
        bool wgc=titleBar||(argc==2&&std::string(argv[1])=="--wgc");
        require(argc==1||wgc,"Unknown capture fixture arguments");
        const auto mask=mp_capture_backend_mask();
        require(mp_capture_backend()==((mask&(1u<<4))?4u:3u),"Default Windows capture backend does not prefer available WGC");
        uint32_t backend=wgc?4u:3u;
        std::cerr<<"CAPTURE FIXTURE: capability probe "<<backend<<std::endl;
        if(!(mp_capture_backend_mask()&(1u<<backend))) {std::cout<<"WINDOWS WGC UNAVAILABLE: runtime capability probe rejected this hosted desktop; no WGC capture evidence.\n";return 77;}
        std::cerr<<"CAPTURE FIXTURE: create independent source"<<std::endl;
        wchar_t path[32768]{};require(GetModuleFileNameW(nullptr,path,DWORD(std::size(path)))!=0,"Cannot find fixture executable");
        std::wstring command=L"\""+std::wstring(path)+L"\" --source "+std::to_wstring(GetCurrentProcessId());
        STARTUPINFOW startup{};startup.cb=sizeof(startup);Process process;
        process.job=CreateJobObjectW(nullptr,nullptr);require(process.job!=nullptr,"Cannot create fixture cleanup job");
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};limits.BasicLimitInformation.LimitFlags=JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        require(SetInformationJobObject(process.job,JobObjectExtendedLimitInformation,&limits,sizeof(limits))!=0,"Cannot configure child cleanup");
        require(CreateProcessW(path,command.data(),nullptr,nullptr,FALSE,0,nullptr,nullptr,&startup,&process.info)!=0,"Cannot create independent source process");
        require(AssignProcessToJobObject(process.job,process.info.hProcess)!=0,"Cannot bind source lifetime to the fixture");
        for(int i=0;i<200&&!process.window;i++) { process.window=FindWindowW(L"MagpieGdiDisposableSource",title(GetCurrentProcessId()).c_str());Sleep(25); }
        require(process.window!=nullptr,"Source window did not appear on the runner desktop");
        ShowWindow(process.window,SW_RESTORE);SetForegroundWindow(process.window);
        for(int i=0;i<100&&GetForegroundWindow()!=process.window;i++)Sleep(20);
        require(GetForegroundWindow()==process.window,"Runner cannot activate disposable source; capture evidence is unavailable");
        require(RedrawWindow(process.window,nullptr,nullptr,RDW_INVALIDATE|RDW_UPDATENOW)!=0,"Cannot paint disposable source");Sleep(100);
        std::cerr<<"CAPTURE FIXTURE: create selected backend"<<std::endl;
        std::unique_ptr<void,decltype(&mp_capture_destroy)> capture(mp_capture_create_backend(backend),mp_capture_destroy);
        require(mp_capture_instance_backend(capture.get())==backend,"Requested backend was silently replaced");
        native(mp_capture_set_title_bar(capture.get(),titleBar?1:0)==1,"Configure title bar capture");
        native(bool(capture),"Create capture");native(mp_capture_start(capture.get())==1,"Start capture");
        require(mp_capture_set_title_bar(capture.get(),titleBar?0:1)==-1,"Active capture accepted a title bar geometry change");
        std::cerr<<"CAPTURE FIXTURE: first frame"<<std::endl;
        const uint8_t* retained{};std::vector<uint8_t> retainedCopy;
        auto frame=[&](const char* phase) {
            std::cerr<<"CAPTURE FIXTURE: "<<phase<<std::endl;
            const uint8_t* pixels=nullptr;uint32_t width=0,height=0;
            int status=0;
            for(int i=0;i<500&&status==0;++i) {
                status=mp_capture_frame(capture.get(),&pixels,&width,&height);
                if(status==0) {
                    require(!retained||memcmp(retained,retainedCopy.data(),retainedCopy.size())==0,"Waiting changed the previously delivered C ABI pixel buffer");
                    Sleep(20);
                }
            }
            if(status==0) {
                std::cerr<<"CAPTURE FIXTURE TIMEOUT: "<<phase<<": "<<mp_last_error()<<std::endl;
                throw std::runtime_error(std::string("Timed out waiting for ")+phase+": "+mp_last_error());
            }
            native(status==1,phase);
            require(width>40&&height>40&&pixels,"Fixture produced no actual capture frame");
            RECT actual{};require(GetClientRect(process.window,&actual)!=0,"Cannot query actual source client");
            uint32_t expectedHeight=uint32_t(actual.bottom-actual.top);
            if(titleBar) {
                RECT bounds{};require(SUCCEEDED(DwmGetWindowAttribute(process.window,DWMWA_EXTENDED_FRAME_BOUNDS,&bounds,sizeof(bounds))),"Cannot read actual title bar frame bounds");
                POINT origin{};require(ClientToScreen(process.window,&origin)!=0,"Cannot query actual client screen position");
                LONG border=origin.x-bounds.left;
                require(border>0&&origin.y>bounds.top+border,"Disposable source does not expose a native caption");
                expectedHeight+=uint32_t(origin.y-bounds.top-border);
            }
            require(width==uint32_t(actual.right-actual.left)&&height==expectedHeight,"Capture dimensions do not match independently queried source/caption bounds");
            const auto* sample=pixels+(size_t(height/2)*width+width/2)*4;
            if(sample[0]!=23||sample[1]!=67||sample[2]!=109||sample[3]!=255) {
                const auto sequence=mp_capture_frame_sequence(capture.get());
                std::cerr<<"CAPTURE PIXEL MISMATCH: backend="<<backend<<" title="<<titleBar<<" phase="<<phase<<" sequence="<<sequence
                    <<" size="<<width<<'x'<<height<<" centerRGBA="<<unsigned(sample[0])<<','<<unsigned(sample[1])<<','<<unsigned(sample[2])<<','<<unsigned(sample[3])<<std::endl;
                HDC dc=GetDC(process.window);COLORREF live=dc?GetPixel(dc,actual.right/2,actual.bottom/2):CLR_INVALID;
                if(dc)ReleaseDC(process.window,dc);
                std::cerr<<"CAPTURE PIXEL LIVE CLIENT: colorref="<<std::hex<<live<<std::dec<<std::endl;
                // Preserve the failing first delivery before any further frame
                // call or teardown. No retry or repaint to hide the mismatch.
                auto path=std::filesystem::path("artifacts/platform-build-checks/win-x64")/
                    (std::string("windows-capture-mismatch-")+std::to_string(backend)+(titleBar?"-title":"")+"-"+std::to_string(sequence)+".rgba");
                std::filesystem::create_directories(path.parent_path());
                std::ofstream saved(path,std::ios::binary);saved.write(reinterpret_cast<const char*>(pixels),std::streamsize(size_t(width)*height*4));
                require(bool(saved),"Cannot preserve first mismatching capture pixels");saved.close();
                require(false,"Captured RGBA pixels differ from independently painted source");
            }
            if(titleBar) {
                bool different=false;
                for(uint32_t y=0;y<height-uint32_t(actual.bottom-actual.top);++y) {
                    const auto* top=pixels+(size_t(y)*width+width/2)*4;
                    different|=top[0]!=23||top[1]!=67||top[2]!=109;
                }
                require(different,"Title bar capture fabricated repeated client pixels in the native caption");
            }
            retained=pixels;retainedCopy.assign(pixels,pixels+size_t(width)*height*4);
            const auto sequence=mp_capture_frame_sequence(capture.get());
            std::cerr<<"CAPTURE FIXTURE DELIVERED: "<<phase<<" sequence="<<sequence<<" size="<<width<<'x'<<height<<std::endl;
            return sequence;
        };
        auto first=frame("initial frame");mp_capture_source_info info{};
        native(mp_capture_source(capture.get(),&info,sizeof(info))==1,"Read source metadata");
        const auto window=uint64_t(reinterpret_cast<uintptr_t>(process.window));
        require(info.window_id==window&&info.process_id==process.info.dwProcessId,"Captured window/PID differs from independent source");
        require(mp_capture_snapshot_title(capture.get(),window,info.process_id,first,&info,sizeof(info))==1,"Presented source identity not accepted");
        DWORD_PTR result{};require(SendMessageTimeoutW(process.window,Rename,0,0,SMTO_ABORTIFHUNG,2000,&result)!=0,"Source rename request failed");
        require(mp_capture_snapshot_title(capture.get(),window,info.process_id,first,&info,sizeof(info))==1&&std::string(info.title)=="Renamed Magpie fixture","Actual source rename was not reflected");
        require(MoveWindow(process.window,100,100,420,310,TRUE)!=0,"Cannot resize source");Sleep(100);
        require(mp_capture_snapshot_title(capture.get(),window,info.process_id,first,&info,sizeof(info))==0,"Old submitted geometry accepted after source resize");
        auto resized=frame("resized frame");require(resized>first,"Source resize did not advance delivered sequence");
        require(mp_capture_snapshot_title(capture.get(),window,info.process_id,first,&info,sizeof(info))==0,"Old sequence became valid again after resize");
        require(mp_capture_snapshot_title(capture.get(),window,process.info.dwProcessId,resized,&info,sizeof(info))==1,"Rejecting an old sequence invalidated the current matching source");
        for(const SIZE dimensions: {SIZE{260,210},SIZE{540,370},SIZE{300,240},SIZE{420,310}}) {
            const auto beforeResize=resized;
            require(MoveWindow(process.window,100,100,dimensions.cx,dimensions.cy,TRUE)!=0,"Cannot continuously resize source");
            require(mp_capture_snapshot_title(capture.get(),window,process.info.dwProcessId,beforeResize,&info,sizeof(info))==0,"Previous geometry remained valid during continuous resize");
            resized=frame("continuous grow/shrink/return frame");
            require(resized>beforeResize,"Continuous resize did not advance the delivered sequence");
            require(mp_capture_snapshot_title(capture.get(),window,process.info.dwProcessId,beforeResize,&info,sizeof(info))==0,"Old sequence revived during return to an earlier source size");
            require(mp_capture_snapshot_title(capture.get(),window,process.info.dwProcessId,resized,&info,sizeof(info))==1,"Fresh continuous-resize geometry was rejected");
        }
        require(mp_capture_source_state(capture.get())==1,"Live source availability not reported");
        ShowWindow(process.window,SW_MINIMIZE);Sleep(100);
        const uint8_t* pixels=nullptr;uint32_t width=0,height=0;
        require(mp_capture_source_state(capture.get())==2,"Minimized source was not suspended");
        require(mp_capture_frame(capture.get(),&pixels,&width,&height)==0,"Minimized source must wait without ending the session");
        require(mp_capture_frame_sequence(capture.get())==resized&&memcmp(retained,retainedCopy.data(),retainedCopy.size())==0,"Minimize changed delivered frame/sequence");
        ShowWindow(process.window,SW_RESTORE);UpdateWindow(process.window);Sleep(100);
        require(mp_capture_snapshot_title(capture.get(),window,process.info.dwProcessId,resized,&info,sizeof(info))==0,"Pre-minimize frame accepted before a restored source frame");
        require(mp_capture_source_state(capture.get())==1,"Restored source remained unavailable");
        auto restored=frame("restored frame");require(restored>resized,"Restored source did not publish a fresh frame");
        require(mp_capture_snapshot_title(capture.get(),window,process.info.dwProcessId,restored,&info,sizeof(info))==1,"Restored source metadata did not recover");
        ShowWindow(process.window,SW_HIDE);Sleep(100);
        require(mp_capture_source_state(capture.get())==2,"Hidden source was not suspended");
        require(mp_capture_frame(capture.get(),&pixels,&width,&height)==0,"Hidden source must wait without publishing");
        require(mp_capture_frame_sequence(capture.get())==restored&&memcmp(retained,retainedCopy.data(),retainedCopy.size())==0,"Hide changed delivered frame/sequence");
        ShowWindow(process.window,SW_SHOWNOACTIVATE);UpdateWindow(process.window);Sleep(100);
        require(mp_capture_source_state(capture.get())==1,"Shown source remained unavailable");
        require(mp_capture_snapshot_title(capture.get(),window,process.info.dwProcessId,restored,&info,sizeof(info))==0,"Pre-hide frame became valid before a fresh shown frame");
        const auto hiddenSequence=restored;restored=frame("shown after hidden frame");
        require(restored>hiddenSequence&&mp_capture_snapshot_title(capture.get(),window,process.info.dwProcessId,hiddenSequence,&info,sizeof(info))==0,"Show did not require a fresh source epoch");
        for(int cycle=0;cycle<4;++cycle) {
            const auto previous=restored;
            ShowWindow(process.window,SW_HIDE);Sleep(50);
            require(mp_capture_source_state(capture.get())==2&&mp_capture_frame(capture.get(),&pixels,&width,&height)==0,
                "Repeated hide did not suspend frame delivery");
            require(mp_capture_frame_sequence(capture.get())==previous&&memcmp(retained,retainedCopy.data(),retainedCopy.size())==0,
                "Repeated hide changed the delivered buffer/sequence");
            ShowWindow(process.window,SW_SHOWNOACTIVATE);UpdateWindow(process.window);Sleep(50);
            require(mp_capture_snapshot_title(capture.get(),window,process.info.dwProcessId,previous,&info,sizeof(info))==0,
                "Repeated show revived the pre-hide frame epoch");
            restored=frame("repeated hide/show fresh frame");
            require(restored>previous,"Repeated show did not produce a new delivered epoch");
        }
        DWORD liveProcess{};DWORD liveThread=GetWindowThreadProcessId(process.window,&liveProcess);
        require(liveThread&&liveProcess==process.info.dwProcessId,"Disposable source identity changed unexpectedly");
        require(SendMessageTimeoutW(process.window,NotifyDestroy,0,0,SMTO_ABORTIFHUNG,2000,&result)!=0,"Cannot emit fixture lifetime event");
        // This fixture emits a real WinEvent without destroying the live HWND,
        // proving rejection comes from the watcher, not just IsWindow/PID tests.
        bool rejected=false;
        for(int i=0;i<200&&!rejected;i++) { rejected=mp_capture_frame(capture.get(),&pixels,&width,&height)==-1;if(!rejected)Sleep(10); }
        DWORD stillProcess{};
        require(rejected&&IsWindow(process.window)&&GetWindowThreadProcessId(process.window,&stillProcess)==liveThread&&stillProcess==liveProcess,"Source lifetime event did not invalidate an otherwise matching live tuple");
        require(mp_capture_snapshot_title(capture.get(),window,process.info.dwProcessId,restored,&info,sizeof(info))==0,"Destroyed lifetime retained title/input identity");
        require(mp_capture_source_state(capture.get())==3,"Destroyed source lifetime still reported available");
        auto oldCapture=std::move(capture);
        capture.reset(mp_capture_create_backend(backend));native(bool(capture),"Create fresh capture");SetForegroundWindow(process.window);
        native(mp_capture_set_title_bar(capture.get(),titleBar?1:0)==1,"Configure fresh capture caption");
        native(mp_capture_start(capture.get())==1,"Start fresh capture");
        auto fresh=frame("independent recapture frame");require(fresh!=0,"Fresh capture did not recover independently of the old lifetime");
        require(mp_capture_frame(oldCapture.get(),&pixels,&width,&height)==-1,"Old lifetime revived after another capture selected the same source tuple");
        oldCapture.reset();
        PostMessageW(process.window,WM_CLOSE,0,0);
        require(WaitForSingleObject(process.info.hProcess,5000)==WAIT_OBJECT_0,"Source process did not close");
        require(mp_capture_frame(capture.get(),&pixels,&width,&height)==-1,"Closed source was captured as a valid frame");
        require(mp_capture_snapshot_title(capture.get(),window,process.info.dwProcessId,fresh,&info,sizeof(info))==0,"Closed source retained input/title identity");
        require(mp_capture_source_state(capture.get())==3,"Closed source state not reported");
        mp_capture_cancel(capture.get());
        if(titleBar)std::cout<<"WINDOWS WGC TITLE BAR PASS: actual native caption pixels and client pixels, independent bounds, immutable active geometry, resize and independent recapture preserve selection.\n";
        std::cout<<(wgc?"WINDOWS WGC FIXTURE PASS: ":"WINDOWS GDI FIXTURE PASS: ")<<"independent source process, actual painted RGBA, HWND/PID identity, rename, resize sequence rejection, continuous grow/shrink/return, waiting-buffer preservation, minimize/restore fresh-frame guard, hide/show suspension without session failure, repeated hide/show fresh-frame guard, live source-state ABI, actual out-of-context lifetime WinEvent, matching-tuple permanent rejection, independent recapture, destruction and cleanup; lifetime event is injected, no Vulkan or input injection.\n";return 0;
    }catch(const std::exception& error) { std::cerr<<error.what()<<'\n';return 1; }
}
