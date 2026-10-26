#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "magpie.h"
#include <cstdlib>
#include <iostream>
#include <iterator>
#include <memory>
#include <stdexcept>
#include <string>

// Opt-in only: creates a disposable source process/window on the hosted runner.
// Never registered with the default CTest suite or run on the user's desktop.
namespace {
constexpr UINT Rename=WM_APP+1;
LRESULT CALLBACK sourceProc(HWND window,UINT message,WPARAM w,LPARAM l) {
    if(message==WM_PAINT) {
        PAINTSTRUCT paint{};HDC dc=BeginPaint(window,&paint);RECT rect{};GetClientRect(window,&rect);
        HBRUSH brush=CreateSolidBrush(RGB(23,67,109));FillRect(dc,&rect,brush);DeleteObject(brush);EndPaint(window,&paint);return 0;
    }
    if(message==Rename) { SetWindowTextW(window,L"Renamed Magpie fixture");return 0; }
    if(message==WM_DESTROY) { PostQuitMessage(0);return 0; }
    return DefWindowProcW(window,message,w,l);
}
void require(bool ok,const char* text) { if(!ok)throw std::runtime_error(text); }
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
    try {
        if(!std::getenv("MAGPIE_HOSTED_WINDOWS_CAPTURE_FIXTURE"))throw std::runtime_error("This visible-window fixture requires an explicit disposable hosted-runner opt-in");
        if(argc==3&&std::string(argv[1])=="--source")return child(DWORD(std::stoul(argv[2])));
        require(argc==1,"Unknown capture fixture arguments");
        require(mp_capture_backend()==3,"Fixture is not linked to the actual Windows GDI backend");
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
        std::unique_ptr<void,decltype(&mp_capture_destroy)> capture(mp_capture_create(),mp_capture_destroy);
        require(bool(capture),mp_last_error());require(mp_capture_start(capture.get())==1,mp_last_error());
        auto frame=[&] {
            const uint8_t* pixels=nullptr;uint32_t width=0,height=0;
            require(mp_capture_frame(capture.get(),&pixels,&width,&height)==1,mp_last_error());
            require(width>40&&height>40&&pixels,"Fixture produced no actual GDI frame");
            RECT actual{};require(GetClientRect(process.window,&actual)!=0&&width==uint32_t(actual.right-actual.left)&&height==uint32_t(actual.bottom-actual.top),"GDI dimensions do not match the live source client");
            const auto* sample=pixels+(size_t(height/2)*width+width/2)*4;
            require(sample[0]==23&&sample[1]==67&&sample[2]==109&&sample[3]==255,"GDI RGBA pixels differ from independently painted source");
            return mp_capture_frame_sequence(capture.get());
        };
        auto first=frame();mp_capture_source_info info{};
        require(mp_capture_source(capture.get(),&info,sizeof(info))==1,mp_last_error());
        const auto window=uint64_t(reinterpret_cast<uintptr_t>(process.window));
        require(info.window_id==window&&info.process_id==process.info.dwProcessId,"Captured window/PID differs from independent source");
        require(mp_capture_snapshot_title(capture.get(),window,info.process_id,first,&info,sizeof(info))==1,"Presented source identity not accepted");
        DWORD_PTR result{};require(SendMessageTimeoutW(process.window,Rename,0,0,SMTO_ABORTIFHUNG,2000,&result)!=0,"Source rename request failed");
        require(mp_capture_snapshot_title(capture.get(),window,info.process_id,first,&info,sizeof(info))==1&&std::string(info.title)=="Renamed Magpie fixture","Actual source rename was not reflected");
        require(MoveWindow(process.window,100,100,420,310,TRUE)!=0,"Cannot resize source");Sleep(100);
        require(mp_capture_snapshot_title(capture.get(),window,info.process_id,first,&info,sizeof(info))==0,"Old submitted geometry accepted after source resize");
        auto resized=frame();require(resized>first,"Source resize did not advance delivered sequence");
        require(mp_capture_snapshot_title(capture.get(),window,info.process_id,first,&info,sizeof(info))==0,"Old sequence became valid again after resize");
        require(mp_capture_snapshot_title(capture.get(),window,process.info.dwProcessId,resized,&info,sizeof(info))==1,"Rejecting an old sequence invalidated the current matching source");
        ShowWindow(process.window,SW_MINIMIZE);Sleep(100);
        const uint8_t* pixels=nullptr;uint32_t width=0,height=0;
        require(mp_capture_frame(capture.get(),&pixels,&width,&height)==-1,"Minimized source was captured as a valid frame");
        ShowWindow(process.window,SW_RESTORE);UpdateWindow(process.window);Sleep(100);
        require(mp_capture_snapshot_title(capture.get(),window,process.info.dwProcessId,resized,&info,sizeof(info))==0,"Pre-minimize frame accepted before a restored source frame");
        auto restored=frame();require(restored>resized,"Restored source did not publish a fresh frame");
        require(mp_capture_snapshot_title(capture.get(),window,process.info.dwProcessId,restored,&info,sizeof(info))==1,"Restored source metadata did not recover");
        PostMessageW(process.window,WM_CLOSE,0,0);
        require(WaitForSingleObject(process.info.hProcess,5000)==WAIT_OBJECT_0,"Source process did not close");
        require(mp_capture_frame(capture.get(),&pixels,&width,&height)==-1,"Closed source was captured as a valid frame");
        require(mp_capture_snapshot_title(capture.get(),window,process.info.dwProcessId,restored,&info,sizeof(info))==0,"Closed source retained input/title identity");
        mp_capture_cancel(capture.get());
        std::cout<<"WINDOWS GDI FIXTURE PASS: independent source process, actual painted RGBA, HWND/PID identity, rename, resize sequence rejection, minimize/restore fresh-frame guard, destruction and cleanup; no Vulkan or input injection.\n";return 0;
    }catch(const std::exception& error) { std::cerr<<error.what()<<'\n';return 1; }
}
