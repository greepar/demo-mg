#pragma once
#include <windows.h>
#include "input_mapping.h"
#include <chrono>
#include <condition_variable>
#include <mutex>
#include <stdexcept>
#include <string>
#include <thread>

namespace magpie {
// Out-of-context WinEvents arrive on the registering thread. Keep a private
// message pump rather than depending on a Task.Run worker pumping USER messages.
// No callback DLL is injected into the source process.
class WindowsSourceWatch {
    const WindowIdentity selected_;
    WindowLifetime lifetime_;
    const std::atomic<bool>& cancelled_;
    std::atomic<bool> stop_{false},failed_{false};
    HANDLE stopEvent_{};
    std::thread thread_;
    std::mutex mutex_;
    std::condition_variable readyCondition_;
    bool ready_=false;
    DWORD registrationError_=0;
    inline static thread_local WindowsSourceWatch* current_=nullptr;

    static void CALLBACK event(HWINEVENTHOOK,DWORD kind,HWND window,LONG object,LONG child,DWORD thread,DWORD) {
        auto* watch=current_;
        if(!watch||kind!=EVENT_OBJECT_DESTROY)return;
        watch->lifetime_.observeDestroy(uint64_t(reinterpret_cast<uintptr_t>(window)),watch->selected_.process,thread,object==OBJID_WINDOW,child==CHILDID_SELF);
    }
    void run() {
        MSG message{};PeekMessageW(&message,nullptr,0,0,PM_NOREMOVE);
        current_=this;
        HWINEVENTHOOK hook=SetWinEventHook(EVENT_OBJECT_DESTROY,EVENT_OBJECT_DESTROY,nullptr,event,selected_.process,selected_.thread,WINEVENT_OUTOFCONTEXT);
        {
            std::lock_guard lock(mutex_);
            if(!hook) { registrationError_=GetLastError();failed_.store(true); }
            ready_=true;
        }
        readyCondition_.notify_all();
        while(hook&&!stop_.load()&&!cancelled_.load()) {
            DWORD result=MsgWaitForMultipleObjectsEx(1,&stopEvent_,50,QS_ALLINPUT,MWMO_INPUTAVAILABLE);
            if(result==WAIT_OBJECT_0)break;
            if(result==WAIT_FAILED) { failed_.store(true);break; }
            while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)) {
                if(message.message==WM_QUIT) { failed_.store(true);stop_.store(true);break; }
                TranslateMessage(&message);DispatchMessageW(&message);
                if(stop_.load()||cancelled_.load())break;
            }
        }
        if(hook)UnhookWinEvent(hook);
        current_=nullptr;
    }
public:
    WindowsSourceWatch(WindowIdentity selected,const std::atomic<bool>& cancelled):selected_(selected),lifetime_(selected),cancelled_(cancelled) {
        stopEvent_=CreateEventW(nullptr,TRUE,FALSE,nullptr);
        if(!stopEvent_)throw std::runtime_error("Cannot allocate source lifetime stop event");
    }
    WindowsSourceWatch(const WindowsSourceWatch&)=delete;
    WindowsSourceWatch& operator=(const WindowsSourceWatch&)=delete;
    ~WindowsSourceWatch() {
        stop_.store(true);
        SetEvent(stopEvent_);
        if(thread_.joinable())thread_.join();
        CloseHandle(stopEvent_);
    }
    void start() {
        if(thread_.joinable())throw std::runtime_error("Source lifetime watch was already started");
        thread_=std::thread([this] { run(); });
        std::unique_lock lock(mutex_);
        if(!readyCondition_.wait_for(lock,std::chrono::seconds(5),[this] { return ready_; }))throw std::runtime_error("Source lifetime watch did not initialize");
        if(failed_.load())throw std::runtime_error("Cannot watch source window lifetime (Win32 "+std::to_string(registrationError_)+")");
    }
    bool matches(uint64_t window,uint32_t process,uint32_t thread) const { return !failed_.load()&&lifetime_.matches(window,process,thread); }
};
}
