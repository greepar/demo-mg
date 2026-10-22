// Standalone original graph execution against the production Vulkan engine.
// kernel.h contains the selected graph plus the normal scalar kernels; this
// fixture never registers the graph in the desktop application's effect menu.
#include "magpie.h"
#include "kernel.h"
#include <algorithm>
#include <cmath>
#include <fstream>
#include <iostream>
#include <limits>
#include <memory>
#include <stdexcept>
#include <vector>

static void require(bool value,const char* message) { if(!value)throw std::runtime_error(message); }
int main(int argc,char** argv) {
    try {
        require(argc==8,"Expected width height outWidth outHeight input output parameters");
        auto w=uint32_t(std::stoul(argv[1])),h=uint32_t(std::stoul(argv[2]));
        auto ow=uint32_t(std::stoul(argv[3])),oh=uint32_t(std::stoul(argv[4]));
        require(w && h && ow && oh && w<=128 && h<=128 && ow<=768 && oh<=768,"Fixture image exceeds bounds");
        static_assert(std::size(multiEffects)==1);
        const auto& effect=multiEffects[0];
        std::vector<uint8_t> input(w*h*4),output(ow*oh*4),repeat(output.size());
        std::ifstream source(argv[5],std::ios::binary);source.read(reinterpret_cast<char*>(input.data()),std::streamsize(input.size()));
        require(source.gcount()==std::streamsize(input.size()),"Incomplete graph fixture input");
        std::ifstream parameters(argv[7]);mp_effect_config config{};config.step={effect.id,ow,oh,0,0};
        auto slot=[&](mp_effect_config& stage,uint32_t i)->float& { return effect.legacyExtra?stage.extra[i]:i==0?stage.step.parameter1:i==1?stage.step.parameter2:stage.extra[i-2]; };
        for(uint32_t i=0;i<effect.parameterCount;i++)require(bool(parameters>>slot(config,i)),"Incomplete graph parameters");
        std::unique_ptr<void,decltype(&mp_destroy)> engine(mp_create(),mp_destroy);require(bool(engine),mp_last_error());
        require(mp_set_gpu_profiling(engine.get(),1),mp_last_error());
        require(mp_scale_chain_configured_v2(engine.get(),input.data(),w,h,output.data(),output.size(),&config,1),mp_last_error());
        require(mp_scale_chain_configured_v2(engine.get(),input.data(),w,h,repeat.data(),repeat.size(),&config,1),mp_last_error());
        require(output==repeat,"Repeated original graph output changed");
        mp_gpu_profile_v2 profile{};require(mp_get_gpu_profile_v2(engine.get(),&profile),mp_last_error());
        if(profile.supported)require(profile.status==1 && profile.stage_count==1 && profile.pass_count==effect.count,"Incomplete graph GPU timing");
        uint32_t rejected=0;
        for(uint32_t i=0;i<effect.parameterCount;i++) {
            const auto& parameter=effect.parameters[i];
            for(float invalid:{parameter.minimum-1,parameter.maximum+1,std::numeric_limits<float>::quiet_NaN(),std::numeric_limits<float>::infinity()}) {
                auto bad=config;slot(bad,i)=invalid;std::fill(repeat.begin(),repeat.end(),73);
                require(!mp_scale_chain_configured_v2(engine.get(),input.data(),w,h,repeat.data(),repeat.size(),&bad,1),"Invalid original graph parameter accepted");
                require(std::all_of(repeat.begin(),repeat.end(),[](uint8_t v){return v==73;}),"Invalid graph parameter modified caller bytes");++rejected;
            }
        }
        // A fixed output graph must reject an incompatible size atomically.
        if(effect.scaleFactor) {
            auto bad=config;bad.step.output_width=ow+1;std::fill(repeat.begin(),repeat.end(),73);
            require(!mp_scale_chain_configured_v2(engine.get(),input.data(),w,h,repeat.data(),repeat.size(),&bad,1),"Fixed graph accepted invalid dimensions");
            require(std::all_of(repeat.begin(),repeat.end(),[](uint8_t v){return v==73;}),"Invalid graph dimensions modified caller bytes");
        }
        require(mp_scale_chain_configured_v2(engine.get(),input.data(),w,h,repeat.data(),repeat.size(),&config,1),mp_last_error());
        require(output==repeat,"Graph failed to recover after parameter rejection");
        std::ofstream target(argv[6],std::ios::binary);target.write(reinterpret_cast<const char*>(output.data()),std::streamsize(output.size()));require(bool(target),"Graph output write failed");
        std::cout<<"GRAPH PASS: device="<<mp_device_name(engine.get())<<" passes="<<effect.count<<" parameters="<<effect.parameterCount<<" rejected="<<rejected<<" repeat/recovery/timing/atomicity\n";
        return 0;
    } catch(const std::exception& error) { std::cerr<<error.what()<<'\n';return 1; }
}
