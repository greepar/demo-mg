#pragma once
#include <windows.h>
#include <wct.h>
#include <algorithm>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdio>
#include <cstdint>
#include <cstring>
#include <mutex>
#include <thread>

namespace magpie {
// Hosted-fixture diagnostics only. No capture locks, process memory dumps,
// retries, timeout changes, or extra references to WinRT capture resources.
class WindowsCaptureDiagnostics {
    bool enabled_{};
    uint64_t sampleRevision_=~uint64_t(0);
    uint32_t samples_{},lastCenter_{};
    std::atomic<const char*> producerStep_{"initialize apartment"},consumerStep_{"capture construction"};
    std::atomic<DWORD> producerThread_{0},consumerThread_{GetCurrentThreadId()};
    std::jthread observer_;
    static bool environment(const char* name,const char* value) {
        char found[64]{};DWORD length=GetEnvironmentVariableA(name,found,DWORD(sizeof(found)));
        return length>0&&length<sizeof(found)&&std::strcmp(found,value)==0;
    }
    static void chain(const char* role,DWORD thread) {
        if(!thread)return;
        HWCT session=OpenThreadWaitChainSession(0,nullptr);
        if(!session) {std::fprintf(stderr,"WGC WAIT CHAIN: %s thread=%lu open-error=%lu\n",role,thread,GetLastError());return;}
        WAITCHAIN_NODE_INFO nodes[16]{};DWORD count=16;BOOL cycle=FALSE;
        // WCT_OUT_OF_PROC_FLAG | WCT_OUT_OF_PROC_CS_FLAG. COM callbacks are
        // not installed; unsupported waits may report only one thread node.
        if(GetThreadWaitChain(session,0,0x01|0x04,thread,&count,nodes,&cycle)) {
            std::fprintf(stderr,"WGC WAIT CHAIN: %s thread=%lu cycle=%d nodes=%lu\n",role,thread,int(cycle),count);
            for(DWORD i=0;i<std::min(count,DWORD(16));++i) {
                std::fprintf(stderr,"WGC WAIT NODE: role=%s index=%lu type=%u status=%u",role,i,
                    unsigned(nodes[i].ObjectType),unsigned(nodes[i].ObjectStatus));
                if(nodes[i].ObjectType==WctThreadType)
                    std::fprintf(stderr," pid=%lu tid=%lu",nodes[i].ThreadObject.ProcessId,nodes[i].ThreadObject.ThreadId);
                std::fputc('\n',stderr);
            }
        } else std::fprintf(stderr,"WGC WAIT CHAIN: %s thread=%lu query-error=%lu\n",role,thread,GetLastError());
        CloseThreadWaitChainSession(session);
    }
public:
    WindowsCaptureDiagnostics() {
        if(!environment("GITHUB_ACTIONS","true")||!environment("RUNNER_ENVIRONMENT","github-hosted")||
            !environment("MAGPIE_HOSTED_WINDOWS_CAPTURE_FIXTURE","1"))return;
        enabled_=true;
        observer_=std::jthread([this](std::stop_token stop) {
            std::mutex mutex;std::condition_variable_any changed;std::unique_lock lock(mutex);
            while(!stop.stop_requested()) {
                changed.wait_for(lock,stop,std::chrono::seconds(5),[]{return false;});
                if(stop.stop_requested())break;
                DWORD producer=producerThread_.load(),consumer=consumerThread_.load();
                std::fprintf(stderr,"WGC PROGRESS: pid=%lu producer=%lu stage=\"%s\" consumer=%lu stage=\"%s\"\n",
                    GetCurrentProcessId(),producer,producerStep_.load(),consumer,consumerStep_.load());
                chain("producer",producer);chain("consumer",consumer);std::fflush(stderr);
            }
        });
    }
    void producer(const char* step) {producerThread_=GetCurrentThreadId();producerStep_=step;}
    static int64_t compositorClock() {
        LARGE_INTEGER ticks{},frequency{};
        if(!QueryPerformanceCounter(&ticks)||!QueryPerformanceFrequency(&frequency)||frequency.QuadPart<=0)return 0;
        return int64_t(static_cast<long double>(ticks.QuadPart)*10000000.L/frequency.QuadPart);
    }
    void pixels(const uint8_t* bgra,uint32_t width,uint32_t height,size_t stride,uint64_t revision,int64_t timestamp,bool titleBar,int64_t startBefore,int64_t startAfter) {
        if(!enabled_)return;
        const uint8_t* center=bgra+size_t(height/2)*stride+size_t(width/2)*4;
        uint32_t packed{};std::memcpy(&packed,center,4);
        if(sampleRevision_!=revision) {sampleRevision_=revision;samples_=0;}
        if(samples_++<2||packed!=lastCenter_) {
            std::fprintf(stderr,"WGC RAW PIXELS: pid=%lu revision=%llu timestamp=%lld title=%d size=%ux%u stride=%llu centerBGRA=%u,%u,%u,%u sessionStart=%lld..%lld\n",
                GetCurrentProcessId(),static_cast<unsigned long long>(revision),static_cast<long long>(timestamp),int(titleBar),width,height,
                static_cast<unsigned long long>(stride),unsigned(center[0]),unsigned(center[1]),unsigned(center[2]),unsigned(center[3]),
                static_cast<long long>(startBefore),static_cast<long long>(startAfter));
            std::fflush(stderr);
        }
        lastCenter_=packed;
    }
    void consumer(const char* step) {consumerThread_=GetCurrentThreadId();consumerStep_=step;}
    void publication(const char* phase,std::chrono::steady_clock::duration interval,uint32_t candidates) {
        if(!enabled_)return;
        std::fprintf(stderr,"WGC RESTART PUBLICATION: phase=%s intervalUs=%lld candidates=%u\n",phase,
            static_cast<long long>(std::chrono::duration_cast<std::chrono::microseconds>(interval).count()),candidates);
        std::fflush(stderr);
    }
    struct ConsumerScope {
        WindowsCaptureDiagnostics& owner;
        ConsumerScope(WindowsCaptureDiagnostics& value,const char* step):owner(value){owner.consumer(step);}
        ~ConsumerScope(){owner.consumer("idle");}
        ConsumerScope(const ConsumerScope&)=delete;
    };
};
}
