#pragma once
#include "magpie.h"
#include <vulkan/vulkan.h>
#include <chrono>
#include <cmath>
#include <limits>
#include <stdexcept>
#include <string>

// Completion-boundary intervals on one queue. Includes transfers/barriers and query overhead;
// not isolated shader latency or calibrated CPU/presentation timing.
struct GpuProfiler {
    VkDevice device{};VkQueryPool pool{};
    uint32_t bits{},written{};double period{};
    bool enabled{},recording{};uint64_t sequence{};
    mp_gpu_profile_v2 latest{},pending{};
    std::chrono::steady_clock::time_point began;
    ~GpuProfiler() { release(); }
    static bool supported(uint32_t validBits,double ns) { return validBits>0 && validBits<=64 && std::isfinite(ns) && ns>0; }
    static uint64_t mask(uint32_t validBits) {
        if(validBits==0 || validBits>64)throw std::invalid_argument("Invalid GPU timestamp width");
        return validBits==64?std::numeric_limits<uint64_t>::max():(uint64_t(1)<<validBits)-1;
    }
    static uint64_t delta(uint64_t start,uint64_t end,uint32_t validBits) { return (end-start)&mask(validBits); }
    mp_gpu_profile_v2 empty() const {
        mp_gpu_profile_v2 info{};info.supported=supported(bits,period);info.enabled=enabled;info.timestamp_bits=bits;info.timestamp_period_ns=period;info.frame_sequence=sequence;
        info.status=enabled && !info.supported?2u:0u;return info;
    }
    void initialize(VkDevice target,uint32_t validBits,double ns) { device=target;bits=validBits;period=ns;latest=empty(); }
    void release() { if(pool)vkDestroyQueryPool(device,pool,nullptr);pool=VK_NULL_HANDLE;device=VK_NULL_HANDLE; }
    void setEnabled(bool value) {
        if(value && supported(bits,period) && !pool) {
            VkQueryPoolCreateInfo info{VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO};info.queryType=VK_QUERY_TYPE_TIMESTAMP;info.queryCount=MP_GPU_MAX_PASSES+2;
            VkResult result=vkCreateQueryPool(device,&info,nullptr,&pool);
            if(result!=VK_SUCCESS)throw std::runtime_error("GPU timestamp query pool allocation failed (Vulkan "+std::to_string(result)+")");
        }
        enabled=value;latest=empty();
    }
    void begin(VkCommandBuffer command,uint32_t count,const mp_effect_step* stages,const uint32_t* passCounts) {
        pending=empty();recording=false;written=0;
        if(!enabled || !pending.supported)return;
        pending.stage_count=count;
        for(uint32_t i=0;i<count;i++) {
            pending.effect_ids[i]=stages[i].effect;pending.first_pass[i]=pending.pass_count;pending.pass_counts[i]=passCounts[i];pending.pass_count+=passCounts[i];
        }
        if(pending.pass_count>MP_GPU_MAX_PASSES) { pending.status=3;pending.stage_count=pending.pass_count=0;return; }
        began=std::chrono::steady_clock::now();recording=true;
        vkCmdResetQueryPool(command,pool,0,pending.pass_count+2);
        vkCmdWriteTimestamp(command,VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,pool,0);
    }
    void pass(VkCommandBuffer command) {
        if(recording)vkCmdWriteTimestamp(command,VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,pool,++written);
    }
    void finishRecording(VkCommandBuffer command) {
        if(recording)vkCmdWriteTimestamp(command,VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,pool,pending.pass_count+1);
    }
    void complete() {
        ++sequence;
        if(!recording) { latest=empty();if(enabled && latest.supported)latest.status=3;return; }
        auto fail=[&] { latest=empty();latest.status=3; };
        if(written!=pending.pass_count) { fail();return; }
        struct Query { uint64_t ticks,available; } queries[MP_GPU_MAX_PASSES+2]{};
        uint32_t count=pending.pass_count+2;
        VkResult result=vkGetQueryPoolResults(device,pool,0,count,sizeof(Query)*count,queries,sizeof(Query),VK_QUERY_RESULT_64_BIT|VK_QUERY_RESULT_WITH_AVAILABILITY_BIT);
        if(result!=VK_SUCCESS) { fail();return; }
        for(uint32_t i=0;i<count;i++)if(!queries[i].available) { fail();return; }
        double cpuNs=std::chrono::duration<double,std::nano>(std::chrono::steady_clock::now()-began).count();
        if(cpuNs>=std::ldexp(1.0,int(bits))*period) { fail();return; }
        uint64_t summed=0;const auto max=mask(bits);
        for(uint32_t i=0;i<count-1;i++) {
            uint64_t interval=delta(queries[i].ticks,queries[i+1].ticks,bits);
            if(interval>max-summed) { fail();return; }summed+=interval;
            if(i<pending.pass_count)pending.pass_ms[i]=double(interval)*period/1000000.0;
        }
        if(summed!=delta(queries[0].ticks,queries[count-1].ticks,bits)) { fail();return; }
        pending.total_ms=double(summed)*period/1000000.0;
        for(uint32_t i=0;i<pending.stage_count;i++)for(uint32_t p=0;p<pending.pass_counts[i];p++)pending.stage_ms[i]+=pending.pass_ms[pending.first_pass[i]+p];
        pending.status=1;pending.frame_sequence=sequence;latest=pending;
    }
};
