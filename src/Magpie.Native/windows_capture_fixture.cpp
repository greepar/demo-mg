#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "magpie.h"
#include <cstdlib>
#include <cstring>
#include <cstdio>
#include <atomic>
#include <iostream>
#include <iterator>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

// Opt-in only: creates a disposable source process/window on the hosted runner.
// Never registered with the default CTest suite or run on the user's desktop.
namespace {
constexpr UINT Rename=WM_APP+1,NotifyDestroy=WM_APP+2;
LRESULT CALLBACK sourceProc(HWND window,UINT message,WPARAM w,LPARAM l) {
    if(message==WM_PAINT) {
        PAINTSTRUCT paint{};HDC dc=BeginPaint(window,&paint);RECT rect{};GetClientRect(window,&rect);
        HBRUSH brush=CreateSolidBrush(RGB(23,67,109));FillRect(dc,&rect,brush);DeleteObject(brush);EndPaint(window,&paint);return 0;
    }
    if(message==NotifyDestroy) { NotifyWinEvent(EVENT_OBJECT_DESTROY,window,OBJID_WINDOW,CHILDID_SELF);return 0; }
    if(message==Rename) { SetWindowTextW(window,L"Renamed Magpie fixture");return 0; }
    if(message==WM_DESTROY) { PostQuitMessage(0);return 0; }
    return DefWindowProcW(window,message,w,l);
}
void require(bool ok,const char* text) { if(!ok)throw std::runtime_error(text); }
void native(bool ok,const char* operation) {
    // Read the thread-local error AFTER the call. Argument evaluation order
    // must not retain a c_str invalidated by that call updating the error.
    if(!ok)throw std::runtime_error(std::string(operation)+": "+mp_last_error());
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
    HINSTANCE instance=GetModuleHandleW(nullptr);
    WNDCLASSW cls{};cls.lpfnWndProc=sourceProc;cls.hInstance=instance;cls.lpszClassName=L"MagpieGdiDisposableSource";
    require(RegisterClassW(&cls)!=0,"Cannot register source fixture class");
    HWND window=CreateWindowW(cls.lpszClassName,title(parent).c_str(),WS_OVERLAPPEDWINDOW,100,100,300,240,nullptr,nullptr,instance,nullptr);
    require(window!=nullptr,"Cannot create source fixture window");
    ShowWindow(window,SW_SHOW);UpdateWindow(window);AllowSetForegroundWindow(parent);SetForegroundWindow(window);
    MSG message{};while(GetMessageW(&message,nullptr,0,0)>0) { TranslateMessage(&message);DispatchMessageW(&message); }
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
        bool wgc=argc==2&&std::string(argv[1])=="--wgc";
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
        native(bool(capture),"Create capture");native(mp_capture_start(capture.get())==1,"Start capture");
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
            if(status==0)throw std::runtime_error(std::string("Timed out waiting for ")+phase+": "+mp_last_error());
            native(status==1,phase);
            require(width>40&&height>40&&pixels,"Fixture produced no actual capture frame");
            RECT actual{};require(GetClientRect(process.window,&actual)!=0&&width==uint32_t(actual.right-actual.left)&&height==uint32_t(actual.bottom-actual.top),"GDI dimensions do not match the live source client");
            const auto* sample=pixels+(size_t(height/2)*width+width/2)*4;
            require(sample[0]==23&&sample[1]==67&&sample[2]==109&&sample[3]==255,"GDI RGBA pixels differ from independently painted source");
            retained=pixels;retainedCopy.assign(pixels,pixels+size_t(width)*height*4);
            return mp_capture_frame_sequence(capture.get());
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
        ShowWindow(process.window,SW_MINIMIZE);Sleep(100);
        const uint8_t* pixels=nullptr;uint32_t width=0,height=0;
        require(mp_capture_frame(capture.get(),&pixels,&width,&height)==-1,"Minimized source was captured as a valid frame");
        ShowWindow(process.window,SW_RESTORE);UpdateWindow(process.window);Sleep(100);
        require(mp_capture_snapshot_title(capture.get(),window,process.info.dwProcessId,resized,&info,sizeof(info))==0,"Pre-minimize frame accepted before a restored source frame");
        auto restored=frame("restored frame");require(restored>resized,"Restored source did not publish a fresh frame");
        require(mp_capture_snapshot_title(capture.get(),window,process.info.dwProcessId,restored,&info,sizeof(info))==1,"Restored source metadata did not recover");
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
        auto oldCapture=std::move(capture);
        capture.reset(mp_capture_create_backend(backend));native(bool(capture),"Create fresh capture");SetForegroundWindow(process.window);
        native(mp_capture_start(capture.get())==1,"Start fresh capture");
        auto fresh=frame("independent recapture frame");require(fresh!=0,"Fresh capture did not recover independently of the old lifetime");
        require(mp_capture_frame(oldCapture.get(),&pixels,&width,&height)==-1,"Old lifetime revived after another capture selected the same source tuple");
        oldCapture.reset();
        PostMessageW(process.window,WM_CLOSE,0,0);
        require(WaitForSingleObject(process.info.hProcess,5000)==WAIT_OBJECT_0,"Source process did not close");
        require(mp_capture_frame(capture.get(),&pixels,&width,&height)==-1,"Closed source was captured as a valid frame");
        require(mp_capture_snapshot_title(capture.get(),window,process.info.dwProcessId,fresh,&info,sizeof(info))==0,"Closed source retained input/title identity");
        mp_capture_cancel(capture.get());
        std::cout<<(wgc?"WINDOWS WGC FIXTURE PASS: ":"WINDOWS GDI FIXTURE PASS: ")<<"independent source process, actual painted RGBA, HWND/PID identity, rename, resize sequence rejection, continuous grow/shrink/return, waiting-buffer preservation, minimize/restore fresh-frame guard, actual out-of-context lifetime WinEvent, matching-tuple permanent rejection, independent recapture, destruction and cleanup; lifetime event is injected, no Vulkan or input injection.\n";return 0;
    }catch(const std::exception& error) { std::cerr<<error.what()<<'\n';return 1; }
}
