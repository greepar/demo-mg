#include <limits>
#include <cstring>
#include <set>
#include <cctype>
#include "magpie.h"
#include "kernel.h"
#include <iomanip>
#include <vulkan/vulkan.h>
#include <cstring>
#include "gpu_profile.h"
#include "fsr_reference.h"
#include "precision.h"
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <iostream>
#include <fstream>
#include <memory>
#include <stdexcept>
#include <vector>
namespace {
void require(bool condition, const char* message) { if (!condition) throw std::runtime_error(message); }
double weight(double x) {
    x = std::abs(x);
    if (x < 1e-8) return 1;
    if (x >= 3) return 0;
    const double pi = std::acos(-1.0);
    return 3 * std::sin(pi * x) * std::sin(pi * x / 3) / (pi * pi * x * x);
}
std::vector<uint8_t> reference(const std::vector<uint8_t>& input, int w, int h, int ow, int oh, double strength) {
    std::vector<uint8_t> out(size_t(ow) * oh * 4);
    auto sample = [&](int x, int y, int c) { return input[(std::clamp(y, 0, h-1) * w + std::clamp(x, 0, w-1)) * 4 + c]; };
    for (int y = 0; y < oh; ++y) for (int x = 0; x < ow; ++x) {
        const double sx = (x + .5) * w / ow - .5, sy = (y + .5) * h / oh - .5;
        const int ix = int(std::floor(sx)), iy = int(std::floor(sy));
        double wx[6], wy[6], sumx = 0, sumy = 0;
        for (int i = 0; i < 6; ++i) { wx[i] = weight(sx - (ix-2+i)); wy[i] = weight(sy - (iy-2+i)); sumx += wx[i]; sumy += wy[i]; }
        for (int c = 0; c < 3; ++c) {
            double value = 0;
            for (int j = 0; j < 6; ++j) for (int i = 0; i < 6; ++i) value += sample(ix-2+i, iy-2+j, c) * wx[i] * wy[j] / (sumx * sumy);
            double lo = std::min({sample(ix,iy,c), sample(ix+1,iy,c), sample(ix,iy+1,c), sample(ix+1,iy+1,c)});
            double hi = std::max({sample(ix,iy,c), sample(ix+1,iy,c), sample(ix,iy+1,c), sample(ix+1,iy+1,c)});
            value += (std::clamp(value, lo, hi) - value) * strength;
            out[(y*ow+x)*4+c] = uint8_t(std::round(std::clamp(value, 0.0, 255.0)));
        }
        out[(y*ow+x)*4+3] = 255;
    }
    return out;
}
}
int main(int argc,char** argv) {
    try {
        if(argc==2 && std::string(argv[1])=="--effect-metadata") {
            std::cout<<std::setprecision(9)<<'[';
            for(size_t index=0;index<std::size(multiEffects);index++) {
                const auto& effect=multiEffects[index];if(index)std::cout<<',';
                std::cout<<"{\"id\":"<<effect.id<<",\"fp16\":"<<(multiPasses[effect.first].halfData?"true":"false")<<",\"scale\":"<<effect.scaleFactor<<",\"passes\":"<<effect.count<<",\"textures\":[";
                for(uint32_t i=0;i<effect.textures;i++) {
                    const auto& t=effect.specs[i];if(i)std::cout<<',';
                    std::cout<<'['<<t.format<<','<<t.widthAxis<<','<<t.widthFactor<<','<<t.heightAxis<<','<<t.heightFactor<<','<<t.bytes<<']';
                }
                std::cout<<"],\"parameters\":[";
                for(uint32_t i=0;i<effect.parameterCount;i++) {
                    const auto& p=effect.parameters[i];if(i)std::cout<<',';
                    std::cout<<'['<<p.minimum<<','<<p.maximum<<','<<p.defaultValue<<','<<(p.integral?"true":"false")<<']';
                }
                std::cout<<"]}";
            }
            std::cout<<"]\n";return 0;
        }
        const std::string command=argc>1?argv[1]:"";
        if(command=="--precision-policy") {
            require(!nativeHalfEnabled(0,false)&&!nativeHalfEnabled(0,true)&&!nativeHalfEnabled(1,false)&&nativeHalfEnabled(1,true)&&nativeHalfEnabled(2,true),"Precision fallback policy changed");
            for(auto test:{std::pair<uint32_t,bool>{2,false},{3,false},{3,true},{UINT32_MAX,true}}) {
                bool rejected=false;try { (void)nativeHalfEnabled(test.first,test.second); }catch(const std::runtime_error&) { rejected=true; }
                require(rejected,"Unsupported/invalid forced precision accepted");
            }
            require(!mp_create_with_precision(nullptr,3)&&!mp_fp16_supported(nullptr)&&!mp_effect_uses_fp16(nullptr,4),"Invalid precision ABI accepted");
            std::cout<<"PRECISION POLICY PASS: FP32, auto fallback, forced FP16 rejection and invalid/null ABI\n";return 0;
        }
        uint32_t precision=command=="--all-effects-smoke-auto"?1:command=="--all-effects-smoke-fp16" || command=="--original-effect-fixture-fp16"?2:0;
        std::unique_ptr<void, decltype(&mp_destroy)> engine(mp_create_with_precision(nullptr,precision), mp_destroy);
        if(command=="--precision-info") {
            require(bool(engine),mp_last_error());bool supported=mp_fp16_supported(engine.get());
            std::unique_ptr<void,decltype(&mp_destroy)> automatic(mp_create_with_precision(mp_device_identifier(engine.get()),1),mp_destroy);
            require(bool(automatic)&&bool(mp_effect_uses_fp16(automatic.get(),4))==supported,"Automatic precision did not follow device support");
            std::unique_ptr<void,decltype(&mp_destroy)> forced(mp_create_with_precision(mp_device_identifier(engine.get()),2),mp_destroy);
            require(bool(forced)==supported,"Forced precision did not follow device support");
            require(!mp_effect_uses_fp16(engine.get(),4)&&!mp_effect_uses_fp16(automatic.get(),0)&&!mp_effect_uses_fp16(automatic.get(),UINT32_MAX),"Legacy/unqualified effect used FP16");
            std::cout<<"{\"supported\":"<<(supported?"true":"false")<<",\"device\":\""<<mp_device_name(engine.get())<<"\",\"legacy_fp32\":true,\"automatic_verified\":true,\"forced_verified\":true}\n";return 0;
        }
        if(argc==3 && (command=="--all-effects-smoke" || command=="--all-effects-smoke-auto" || command=="--all-effects-smoke-fp16")) {
            require(bool(engine),mp_last_error());require(mp_set_gpu_profiling(engine.get(),1),mp_last_error());
            std::ofstream report(argv[2]);require(bool(report),"Cannot create all-effects report");report<<'[';bool first=true;
            std::vector<uint32_t> ids={0,1,2,3,4,5};for(const auto& effect:multiEffects)ids.push_back(effect.id);
            for(uint32_t id:ids) {
                const auto* graph=multiEffect(id);uint32_t passes=graph?graph->count:1;
                bool capable=graph?multiPasses[graph->first].halfData!=nullptr:kernels[id].halfData!=nullptr;
                bool useHalf=precision!=0 && mp_fp16_supported(engine.get()) && capable;
                require(bool(mp_effect_uses_fp16(engine.get(),id))==useHalf,"Selected shader precision differs from device/source capability");
                uint64_t previousHash=0;
                std::cout<<"BEGIN EFFECT "<<id<<" device="<<mp_device_name(engine.get())<<'\n'<<std::flush;
                for(auto size:{std::pair<uint32_t,uint32_t>{1,1},{7,5},{17,13}})for(uint32_t pattern=0;pattern<2;pattern++) {
                    auto [w,h]=size;uint32_t factor=graph?graph->scaleFactor:id==5?1:0;
                    uint32_t ow=factor?w*factor:2*w+1,oh=factor?h*factor:2*h+1;
                    if(id==14) { ow=std::max(1u,w/2);oh=std::max(1u,h/2); }
                    mp_effect_config stage{};stage.step={id,ow,oh,0,0};
                    if(graph)for(uint32_t p=0;p<graph->parameterCount;p++) {
                        float& value=graph->legacyExtra?stage.extra[p]:p==0?stage.step.parameter1:p==1?stage.step.parameter2:stage.extra[p-2];value=graph->parameters[p].defaultValue;
                    }
                    else if(id==1)stage.step.parameter1=.5f;
                    else if(id==3) { stage.step.parameter1=.33f;stage.step.parameter2=.33f; }
                    else if(id==5)stage.step.parameter1=.87f;
                    std::vector<uint8_t> input(w*h*4),output(ow*oh*4),repeat(output.size());
                    for(size_t p=0;p<input.size();p++)input[p]=p%4==3?255:pattern?uint8_t((p*131+id*17)%251):73;
                    require(mp_scale_chain_configured_v2(engine.get(),input.data(),w,h,output.data(),output.size(),&stage,1),mp_last_error());
                    require(mp_scale_chain_configured_v2(engine.get(),input.data(),w,h,repeat.data(),repeat.size(),&stage,1),mp_last_error());
                    require(output==repeat,"Original effect repeat/resize changed output");
                    mp_gpu_profile_v2 profile{};require(mp_get_gpu_profile_v2(engine.get(),&profile),mp_last_error());
                    if(profile.supported)require(profile.status==1 && profile.stage_count==1 && profile.pass_count==passes && profile.effect_ids[0]==id,"Original effect internal pass timing incomplete");
                    uint64_t hash=14695981039346656037ull;uint32_t opaque=0;
                    for(size_t p=0;p<output.size();p++) { hash=(hash^output[p])*1099511628211ull;if(p%4==3&&output[p]==255)opaque++; }
                    require(opaque==ow*oh,"Opaque original input produced incomplete/nonopaque output");
                    if(pattern)require(hash!=previousHash,"Original effect ignored changed input pixels");else previousHash=hash;
                    if(!first)report<<',';
                    first=false;
                    report<<"{\"effect\":"<<id<<",\"input\":["<<w<<','<<h<<"],\"output\":["<<ow<<','<<oh<<"],\"pattern\":"<<pattern<<",\"passes\":"<<passes<<",\"fp16\":"<<(useHalf?"true":"false")<<",\"hash\":"<<hash<<",\"opaque_pixels\":"<<opaque<<",\"pixels\":"<<ow*oh<<",\"passed\":true}"<<std::flush;
                }
                std::cout<<"PASS EFFECT "<<id<<" six actual GPU cases\n"<<std::flush;
            }
            std::vector<mp_effect_config> chain;
            for(const auto& effect:multiEffects)if(effect.scaleFactor<=1 && chain.size()<MP_MAX_CHAIN_STAGES) {
                mp_effect_config stage{};stage.step={effect.id,7,5,0,0};
                for(uint32_t p=0;p<effect.parameterCount;p++) {
                    float& value=effect.legacyExtra?stage.extra[p]:p==0?stage.step.parameter1:p==1?stage.step.parameter2:stage.extra[p-2];value=effect.parameters[p].defaultValue;
                }
                chain.push_back(stage);
            }
            require(chain.size()==MP_MAX_CHAIN_STAGES,"Insufficient mixed graph stages for full-chain validation");
            std::vector<uint8_t> source(7*5*4),expected(source.size()),temporary(source.size()),actual(source.size());
            for(size_t p=0;p<source.size();p++)source[p]=p%4==3?255:uint8_t(p*71%251);
            expected=source;
            for(const auto& stage:chain) {
                require(mp_scale_chain_configured_v2(engine.get(),expected.data(),7,5,temporary.data(),temporary.size(),&stage,1),mp_last_error());expected.swap(temporary);
            }
            require(mp_scale_chain_configured_v2(engine.get(),source.data(),7,5,actual.data(),actual.size(),chain.data(),uint32_t(chain.size())),mp_last_error());
            require(actual==expected,"Seventeen mixed graph stages differ from separately executed effects");
            mp_gpu_profile_v2 combined{};require(mp_get_gpu_profile_v2(engine.get(),&combined),mp_last_error());
            uint32_t expectedPasses=0;for(const auto& stage:chain)expectedPasses+=multiEffect(stage.step.effect)->count;
            if(combined.supported)require(combined.status==1&&combined.stage_count==chain.size()&&combined.pass_count==expectedPasses,"Mixed graph internal pass timing is incomplete");
            std::cout<<"ALL EFFECTS MIXED CHAIN PASS: 17 stages, "<<expectedPasses<<" real passes, exact separate/combined output\n";
            report<<"]\n";require(bool(report),"All-effects report write failed");
            std::cout<<"ALL EFFECTS GPU PASS: "<<ids.size()<<" registered original effects, repeat/resize and real pass timing. Numerical references remain a separate gate.\n";return 0;
        }
        if(argc==10 && (command=="--graph-filter-fixture" || command=="--original-effect-fixture" || command=="--original-effect-fixture-fp16")) {
            require(bool(engine),mp_last_error());
            uint32_t id=uint32_t(std::stoul(argv[2])),w=uint32_t(std::stoul(argv[3])),h=uint32_t(std::stoul(argv[4]));
            uint32_t ow=uint32_t(std::stoul(argv[5])),oh=uint32_t(std::stoul(argv[6]));
            const auto* graph=multiEffect(id);
            require((id<=5 || graph) && w && h && w<=768 && h<=768 && uint64_t(w)*h<=16384 && ow && oh && ow<=768 && oh<=768,"Invalid original effect fixture arguments");
            uint32_t parameterCount=graph?graph->parameterCount:id==3?2u:(id==1 || id==5)?1u:0u;
            auto parameter=[&](mp_effect_config& value,uint32_t slot)->float& { return graph&&graph->legacyExtra?value.extra[slot]:slot==0?value.step.parameter1:slot==1?value.step.parameter2:value.extra[slot-2]; };
            std::vector<uint8_t> input(w*h*4),output(ow*oh*4),repeat(output.size());
            std::ifstream source(argv[7],std::ios::binary);source.read(reinterpret_cast<char*>(input.data()),std::streamsize(input.size()));require(source.gcount()==std::streamsize(input.size()),"Incomplete filter input");
            mp_effect_config stage{};stage.step={id,ow,oh,0,0};std::ifstream parameters(argv[9]);
            for(uint32_t i=0;i<parameterCount;i++)require(bool(parameters>>parameter(stage,i)),"Missing original effect parameter");
            require(mp_set_gpu_profiling(engine.get(),1),mp_last_error());
            require(mp_scale_chain_configured_v2(engine.get(),input.data(),w,h,output.data(),output.size(),&stage,1),mp_last_error());
            require(mp_scale_chain_configured_v2(engine.get(),input.data(),w,h,repeat.data(),repeat.size(),&stage,1),mp_last_error());require(output==repeat,"Filter reuse changed pixels");
            mp_gpu_profile_v2 profile{};require(mp_get_gpu_profile_v2(engine.get(),&profile),mp_last_error());
            if(profile.supported)require(profile.status==1 && profile.stage_count==1 && profile.pass_count==(graph?graph->count:1) && profile.effect_ids[0]==id,"Effect GPU timing is incomplete");
            for(uint32_t slot=0;slot<parameterCount;slot++) {
                auto bad=stage;float& value=parameter(bad,slot);
                value=std::numeric_limits<float>::quiet_NaN();std::fill(repeat.begin(),repeat.end(),73);
                require(!mp_scale_chain_configured_v2(engine.get(),input.data(),w,h,repeat.data(),repeat.size(),&bad,1),"Invalid filter parameter accepted");
                require(std::all_of(repeat.begin(),repeat.end(),[](uint8_t v){return v==73;}),"Invalid filter parameter modified output");
            }
            require(mp_scale_chain_configured_v2(engine.get(),input.data(),w,h,repeat.data(),repeat.size(),&stage,1),mp_last_error());require(output==repeat,"Filter recovery changed pixels");
            std::ofstream target(argv[8],std::ios::binary);target.write(reinterpret_cast<const char*>(output.data()),std::streamsize(output.size()));require(bool(target),"Filter output write failed");
            std::cout<<"GRAPH PASS: device="<<mp_device_name(engine.get())<<" passes="<<(graph?graph->count:1)<<" parameters="<<parameterCount<<" registered effect repeat/recovery/timing/atomicity\n";return 0;
        }
        if(argc==2 && std::string(argv[1])=="--sampler-precision") {
            require(bool(engine),mp_last_error());
            VkApplicationInfo application{VK_STRUCTURE_TYPE_APPLICATION_INFO};application.apiVersion=VK_API_VERSION_1_1;
            VkInstanceCreateInfo info{VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO};info.pApplicationInfo=&application;
#ifdef __APPLE__
            const char* portability=VK_KHR_PORTABILITY_ENUMERATION_EXTENSION_NAME;info.enabledExtensionCount=1;info.ppEnabledExtensionNames=&portability;info.flags=VK_INSTANCE_CREATE_ENUMERATE_PORTABILITY_BIT_KHR;
#endif
            VkInstance instance{};require(vkCreateInstance(&info,nullptr,&instance)==VK_SUCCESS,"Sampler fixture instance failed");
            uint32_t count=0;vkEnumeratePhysicalDevices(instance,&count,nullptr);std::vector<VkPhysicalDevice> devices(count);vkEnumeratePhysicalDevices(instance,&count,devices.data());
            bool found=false;
            for(auto device:devices) { VkPhysicalDeviceProperties properties{};vkGetPhysicalDeviceProperties(device,&properties);if(std::strcmp(properties.deviceName,mp_device_name(engine.get()))==0) { std::cout<<properties.deviceName<<'|'<<properties.limits.subTexelPrecisionBits<<std::endl;found=true;break; } }
            vkDestroyInstance(instance,nullptr);require(found,"Selected device sampler limits missing");return 0;
        }
        if(argc==7 && std::string(argv[1])=="--smaa-fixture") {
            require(bool(engine),mp_last_error());
            uint32_t effect=uint32_t(std::stoul(argv[2])),w=uint32_t(std::stoul(argv[3])),h=uint32_t(std::stoul(argv[4]));
            require(effect>=20 && effect<=23 && w>0 && h>0 && w<=128 && h<=128,"Invalid SMAA fixture dimensions");
            std::vector<uint8_t> input(w*h*4),output(w*h*4);
            std::ifstream file(argv[5],std::ios::binary);file.read(reinterpret_cast<char*>(input.data()),std::streamsize(input.size()));require(file.gcount()==std::streamsize(input.size()),"SMAA fixture input incomplete");
            require(mp_scale_ex(engine.get(),input.data(),w,h,output.data(),w,h,effect,0,0),mp_last_error());
            std::ofstream target(argv[6],std::ios::binary);target.write(reinterpret_cast<const char*>(output.data()),std::streamsize(output.size()));require(bool(target),"SMAA fixture output failed");return 0;
        }
        if(argc==7 && (std::string(argv[1])=="--anime-fixture" || std::string(argv[1])=="--effect-fixture")) {
            require(bool(engine),mp_last_error());
            const auto mode=std::stoul(argv[2]),width=std::stoul(argv[3]),height=std::stoul(argv[4]);
            require(width>0 && width<=64 && height>0 && height<=64 && ((mode>=7 && mode<=11) || mode==13 || (mode>=15 && mode<=19)),"Invalid CNN fixture dimensions");
            std::vector<uint8_t> source(width*height*4),result(width*height*16);
            std::ifstream file(argv[5],std::ios::binary);file.read(reinterpret_cast<char*>(source.data()),std::streamsize(source.size()));
            require(file.gcount()==std::streamsize(source.size()),"CNN fixture input is incomplete");
            require(mp_scale_ex(engine.get(),source.data(),uint32_t(width),uint32_t(height),result.data(),uint32_t(width*2),uint32_t(height*2),uint32_t(mode),0,0),mp_last_error());
            std::ofstream output(argv[6],std::ios::binary);output.write(reinterpret_cast<const char*>(result.data()),std::streamsize(result.size()));require(bool(output),"CNN fixture output failed");
            return 0;
        }
        if(argc==9 && std::string(argv[1])=="--ssim-fixture") {
            require(bool(engine),mp_last_error());
            uint32_t w=std::stoul(argv[2]),h=std::stoul(argv[3]),ow=std::stoul(argv[4]),oh=std::stoul(argv[5]);float sharpness=std::stof(argv[6]);
            require(w>0 && h>0 && w<=64 && h<=64 && ow>0 && oh>0 && ow<=128 && oh<=128,"Invalid SSim fixture dimensions");
            std::vector<uint8_t> input(w*h*4),output(ow*oh*4);
            std::ifstream file(argv[7],std::ios::binary);file.read(reinterpret_cast<char*>(input.data()),input.size());require(file.gcount()==std::streamsize(input.size()),"SSim fixture input incomplete");
            require(mp_scale_ex(engine.get(),input.data(),w,h,output.data(),ow,oh,14,sharpness,0),mp_last_error());
            std::ofstream target(argv[8],std::ios::binary);target.write(reinterpret_cast<const char*>(output.data()),output.size());require(bool(target),"SSim fixture output failed");return 0;
        }
        if(argc==10 && std::string(argv[1])=="--crt-fixture") {
            require(bool(engine),mp_last_error());
            uint32_t width=uint32_t(std::stoul(argv[2])),height=uint32_t(std::stoul(argv[3]));
            uint32_t ow=uint32_t(std::stoul(argv[4])),oh=uint32_t(std::stoul(argv[5]));
            require(width>0 && width<=64 && height>0 && height<=64 && ow>0 && ow<=128 && oh>0 && oh<=128,"Invalid CRT fixture dimensions");
            std::vector<uint8_t> source(width*height*4),result(ow*oh*4);
            std::ifstream input(argv[6],std::ios::binary);input.read(reinterpret_cast<char*>(source.data()),std::streamsize(source.size()));
            require(input.gcount()==std::streamsize(source.size()),"CRT fixture input incomplete");
            mp_effect_config config{};config.step={12,ow,oh,0,0};
            std::ifstream parameters(argv[8]);for(int i=0;i<15;i++)require(bool(parameters>>config.extra[i]),"CRT fixture parameters incomplete");
            require(std::string(argv[9])=="configured","Unknown CRT fixture ABI");
            require(mp_scale_chain_configured(engine.get(),source.data(),width,height,result.data(),result.size(),&config,1),mp_last_error());
            std::ofstream output(argv[7],std::ios::binary);output.write(reinterpret_cast<const char*>(result.data()),std::streamsize(result.size()));require(bool(output),"CRT fixture output failed");return 0;
        }
        require(argc==1,"Unknown native test arguments");
        require(engine != nullptr, mp_last_error());
        std::cout << "Vulkan device: " << mp_device_name(engine.get()) << '\n';
        {
            std::unique_ptr<void,decltype(&mp_devices_destroy)> devices(mp_devices_create(),mp_devices_destroy);
            require(bool(devices),mp_last_error());uint32_t count=mp_devices_count(devices.get());
            require(count>0,"Engine created without any enumerated devices");
            std::set<std::string> identifiers;int best=-1;std::string expectedAutomatic;
            const uint8_t source[]={13,29,91,255,201,7,19,255};
            for(uint32_t i=0;i<count;i++) {
                mp_device_info info{};require(mp_devices_get(devices.get(),i,&info),mp_last_error());
                require(std::strlen(info.identifier)==32 && std::strlen(info.name)>0,"Incomplete device metadata");
                require(identifiers.insert(info.identifier).second,"Duplicate device UUID");
                int score=info.type==2?5:info.type==1?4:1;
                if(score>best) { best=score;expectedAutomatic=info.identifier; }
                std::unique_ptr<void,decltype(&mp_destroy)> selected(mp_create_for_device(info.identifier),mp_destroy);
                require(bool(selected),mp_last_error());
                require(std::strcmp(mp_device_identifier(selected.get()),info.identifier)==0,"Selected engine device UUID differs");
                require(std::strcmp(mp_device_name(selected.get()),info.name)==0,"Selected engine device name differs");
                std::vector<uint8_t> scaled(4*2*4);
                require(mp_scale(selected.get(),source,2,1,scaled.data(),4,2,0,0),mp_last_error());
                for(uint32_t y=0;y<2;y++)for(uint32_t x=0;x<4;x++)for(uint32_t c=0;c<4;c++)require(scaled[(y*4+x)*4+c]==source[(x/2)*4+c],"Selected device did not execute exact nearest dispatch");
                std::cout<<"DEVICE PASS: "<<info.name<<" UUID="<<info.identifier<<" type="<<info.type<<'\n';
            }
            require(expectedAutomatic==mp_device_identifier(engine.get()),"Automatic GPU scoring changed or disagrees with enumeration");
            mp_device_info untouched{};std::strcpy(untouched.name,"keep");
            require(!mp_devices_get(devices.get(),count,&untouched),"Out-of-range device snapshot index accepted");
            require(std::strcmp(untouched.name,"keep")==0,"Failed device query modified output");
            require(!mp_devices_get(devices.get(),0,nullptr),"Null device metadata output accepted");
            require(!mp_devices_get(nullptr,0,&untouched),"Null device snapshot accepted");
            std::string absent(32,'f');while(identifiers.count(absent))absent[0]=absent[0]=='f'?'e':char(absent[0]-1);
            require(!mp_create_for_device(absent.c_str()),"Missing explicit GPU silently fell back");
            require(std::string(mp_last_error()).find("unavailable")!=std::string::npos,"Missing GPU diagnostic is not actionable");
            for(const char* invalid : {"x","0123","0000000000000000000000000000000z","000000000000000000000000000000000"})require(!mp_create_for_device(invalid),"Invalid device UUID accepted");
            std::string upper=*identifiers.begin();for(char& c:upper)c=char(std::toupper(static_cast<unsigned char>(c)));
            std::unique_ptr<void,decltype(&mp_destroy)> upperEngine(mp_create_for_device(upper.c_str()),mp_destroy);
            require(bool(upperEngine),mp_last_error());
            require(*identifiers.begin()==mp_device_identifier(upperEngine.get()),"Uppercase UUID did not normalize");
            std::unique_ptr<void,decltype(&mp_destroy)> automatic(mp_create_for_device(""),mp_destroy);
            require(bool(automatic) && expectedAutomatic==mp_device_identifier(automatic.get()),"Explicit Automatic differs from legacy creation");
            std::cout<<"PASS: device snapshots, UUID selection, every enumerated adapter dispatch, automatic scoring, missing-device rejection and ABI guards\n";
        }
        const int w = 7, h = 5, ow = 18, oh = 14;
        std::vector<uint8_t> input(w*h*4), output(ow*oh*4);
        for (int y=0; y<h; ++y) for (int x=0; x<w; ++x) {
            auto i=(y*w+x)*4; input[i]=uint8_t(x*37); input[i+1]=uint8_t(y*51); input[i+2]=uint8_t((x+y)*23); input[i+3]=255;
        }
        require(mp_scale(engine.get(), input.data(), w,h,output.data(),ow,oh,0,.5f), mp_last_error());
        for (int y=0; y<oh; ++y) for (int x=0; x<ow; ++x) for (int c=0; c<4; ++c)
            require(output[(y*ow+x)*4+c] == input[((int((y+.5)*h/oh)*w)+int((x+.5)*w/ow))*4+c], "Nearest scaling pixels differ");
        for (float strength : {0.f, .5f, 1.f}) {
            require(mp_scale(engine.get(), input.data(),w,h,output.data(),ow,oh,1,strength), mp_last_error());
            auto expected = reference(input,w,h,ow,oh,strength);
            int maxError=0;
            for (size_t i=0; i<output.size(); ++i) maxError=std::max(maxError,std::abs(int(output[i])-int(expected[i])));
            std::cout << "Lanczos AR=" << strength << ", max channel error=" << maxError << '\n';
            if (maxError > 1) for (size_t i=0;i<output.size();++i) if (std::abs(int(output[i])-int(expected[i]))>1) { std::cout << "pixel " << (i/4)%ow << "," << (i/4)/ow << " channel " << i%4 << " actual " << int(output[i]) << " expected " << int(expected[i]) << '\n'; break; }
            require(maxError<=1, "Lanczos differs from independent CPU reference");
        }
        // Original bilinear shader against an independent four-tap CPU sampler.
        require(mp_scale(engine.get(),input.data(),w,h,output.data(),ow,oh,2,.5f),mp_last_error());
        for(int y=0;y<oh;++y) for(int x=0;x<ow;++x) for(int c=0;c<3;++c) {
            double sx=(x+.5)*w/ow-.5, sy=(y+.5)*h/oh-.5;
            int ix=int(std::floor(sx)), iy=int(std::floor(sy));
            double fx=sx-ix, fy=sy-iy;
            auto sample=[&](int xx,int yy) {return input[(std::clamp(yy,0,h-1)*w+std::clamp(xx,0,w-1))*4+c];};
            double expected=(sample(ix,iy)*(1-fx)+sample(ix+1,iy)*fx)*(1-fy)+(sample(ix,iy+1)*(1-fx)+sample(ix+1,iy+1)*fx)*fy;
            require(std::abs(int(output[(y*ow+x)*4+c])-int(std::round(expected)))<=1,"Bilinear differs from CPU reference");
        }
        // Bicubic B/C polynomial reference, independent of the optimized nine-fetch HLSL.
        for(float b : {0.f,.33f,1.f}) for(float cParam : {0.f,.33f,1.f}) {
            require(mp_scale_ex(engine.get(),input.data(),w,h,output.data(),ow,oh,3,b,cParam),mp_last_error());
            auto cubic=[&](double v) { double t=std::abs(v), c=cParam;
                if(t<1) return ((12-9*b-6*c)*t*t*t+(-18+12*b+6*c)*t*t+6-2*b)/6;
                if(t<2) return ((-b-6*c)*t*t*t+(6*b+30*c)*t*t+(-12*b-48*c)*t+8*b+24*c)/6;
                return 0.0; };
            int maxError=0;
            for(int y=0;y<oh;++y) for(int x=0;x<ow;++x) for(int c=0;c<3;++c) {
                double sx=(x+.5)*w/ow-.5,sy=(y+.5)*h/oh-.5,expected=0;
                int ix=int(std::floor(sx)),iy=int(std::floor(sy));
                for(int yy=iy-1;yy<=iy+2;++yy) for(int xx=ix-1;xx<=ix+2;++xx)
                    expected+=input[(std::clamp(yy,0,h-1)*w+std::clamp(xx,0,w-1))*4+c]*cubic(sx-xx)*cubic(sy-yy);
                maxError=std::max(maxError,std::abs(int(output[(y*ow+x)*4+c])-int(std::round(std::clamp(expected,0.0,255.0)))));
            }
            std::cout << "Bicubic B=" << b << " C=" << cParam << ", max channel error=" << maxError << '\n';
            require(maxError<=1,"Bicubic differs from CPU reference");
        }
        // Switching kernels without resizing must update both pipeline and sampler.
        for(uint32_t mode : {3u,2u,0u,1u,3u}) {
            uint8_t flat[]{23,45,67,255};
            require(mp_scale(engine.get(),flat,1,1,output.data(),ow,oh,mode,.33f),mp_last_error());
            for(size_t i=0;i<output.size();++i) require(std::abs(int(output[i])-int(flat[i%4]))<=1,"Effect changed constant color");
        }
        require(!mp_scale_ex(engine.get(),input.data(),w,h,output.data(),ow,oh,3,.33f,-.1f),"Negative Bicubic C accepted");
        require(!mp_scale_ex(engine.get(),input.data(),w,h,output.data(),ow,oh,3,.33f,NAN),"NaN Bicubic C accepted");
        // FSR EASU: original 16x16 compute dispatch must cover partial blocks safely.
        for(uint32_t color : {0u,23u,255u}) {
            std::vector<uint8_t> flat(7*5*4);
            for(size_t i=0;i<flat.size();++i)flat[i]=i%4==3?255:uint8_t(color);
            std::vector<uint8_t> upscaled(19*13*4);
            require(mp_scale_ex(engine.get(),flat.data(),7,5,upscaled.data(),19,13,4,0,0),mp_last_error());
            for(size_t i=0;i<upscaled.size();++i)require(std::abs(int(upscaled[i])-int(i%4==3?255:color))<=1,"FSR EASU partial block or constant color failed");
        }
        for(int pattern=0;pattern<3;++pattern) {
            auto source=input;
            if(pattern>0)for(size_t i=0;i<source.size();++i)if(i%4!=3)source[i]=uint8_t((i*73+pattern*41)%251);
            std::vector<uint8_t> actual(18*14*4);
            require(mp_scale_ex(engine.get(),source.data(),w,h,actual.data(),18,14,4,0,0),mp_last_error());
            auto expected=easuReference(source,w,h,18,14);int maxError=0;
            for(size_t i=0;i<actual.size();++i)maxError=std::max(maxError,std::abs(int(actual[i])-int(expected[i])));
            std::cout << "EASU CPU pattern=" << pattern << ", max channel error=" << maxError << '\n';
            if(maxError>1)for(size_t i=0;i<actual.size();++i)if(std::abs(int(actual[i])-int(expected[i]))>1) {std::cout<<"EASU mismatch "<<(i/4)%18<<","<<(i/4)/18<<" c="<<i%4<<" actual="<<int(actual[i])<<" expected="<<int(expected[i])<<'\n';break;}
            require(maxError<=1,"FSR EASU differs from scalar 12-tap reference");
        }
        for(int pattern=0;pattern<3;++pattern) {
            auto source=input;
            if(pattern>0)for(size_t i=0;i<source.size();++i)if(i%4!=3)source[i]=uint8_t((i*73+pattern*41)%251);
            std::vector<uint8_t> actual(19*13*4);
            require(mp_scale_ex(engine.get(),source.data(),w,h,actual.data(),19,13,4,0,0),mp_last_error());
            auto expected=easuReference(source,w,h,19,13);int maxError=0;
            for(size_t i=0;i<actual.size();++i)maxError=std::max(maxError,std::abs(int(actual[i])-int(expected[i])));
            std::cout << "EASU exact coordinate boundary max error=" << maxError << '\n';
            require(maxError<=1,"EASU integer coordinate boundary reference differs");
        }
        {
            std::vector<uint8_t> down(5*3*4),repeat(down.size()),composed(19*13*4),standalone(composed.size());
            require(mp_scale_ex(engine.get(),input.data(),w,h,down.data(),5,3,14,1,0),mp_last_error());
            require(mp_scale_ex(engine.get(),input.data(),w,h,repeat.data(),5,3,14,1,0),mp_last_error());
            require(down==repeat,"SSim resource reuse changed output");
            require(mp_scale_ex(engine.get(),down.data(),5,3,standalone.data(),19,13,3,0,.5f),mp_last_error());
            const mp_effect_step steps[]={{14,5,3,1,0},{3,19,13,0,.5f}};
            require(mp_set_gpu_profiling(engine.get(),1),mp_last_error());
            require(mp_scale_chain(engine.get(),input.data(),w,h,composed.data(),composed.size(),steps,2),mp_last_error());
            require(composed==standalone,"SSim mixed PS/CS chain differs from standalone calls");
            mp_gpu_profile_v2 sample{};require(mp_get_gpu_profile_v2(engine.get(),&sample),mp_last_error());
            if(sample.supported)require(sample.status==1 && sample.stage_count==2 && sample.pass_count==6 && sample.pass_counts[0]==5 && sample.effect_ids[0]==14,"SSim five-pass timing is incomplete");
            require(mp_set_gpu_profiling(engine.get(),0),mp_last_error());
            auto retained=down;
            for(float invalid:{.99f,3.01f,std::numeric_limits<float>::quiet_NaN()}) {
                require(!mp_scale_ex(engine.get(),input.data(),w,h,down.data(),5,3,14,invalid,0),"SSim accepted invalid oversharp");
                require(down==retained,"Invalid SSim parameter changed caller pixels");
            }
            require(mp_scale_ex(engine.get(),input.data(),w,h,down.data(),5,3,14,3,0),mp_last_error());
            std::vector<uint8_t> resized(3*5*4);
            require(mp_scale_ex(engine.get(),input.data(),w,h,resized.data(),3,5,14,1.5f,0),mp_last_error());
            for(size_t i=3;i<resized.size();i+=4)require(resized[i]==255,"SSim odd resize output is incomplete");
            require(mp_scale_ex(engine.get(),input.data(),w,h,down.data(),5,3,14,1,0),mp_last_error());
            require(down==retained,"SSim intermediate resize failed to restore original output");
            std::cout<<"SSIM PASS: five mixed PS/CS passes, mixed RGBA16F/UNORM and input/output dimensions, parameters, reuse/resize/restore, GPU chain/timestamps and failure atomicity\n";
        }
        // Multi-pass signed RGBA16F CNNs: switch models without resizing, then mix with legacy stages.
        std::vector<uint8_t> anime(w*h*16),again(anime.size());
        for(uint32_t effect : {7u,8u,9u,10u,11u,13u,15u,16u,17u,18u,19u,7u}) {
            require(mp_scale_ex(engine.get(),input.data(),w,h,anime.data(),w*2,h*2,effect,0,0),mp_last_error());
            require(mp_scale_ex(engine.get(),input.data(),w,h,again.data(),w*2,h*2,effect,0,0),mp_last_error());
            require(anime==again,"CNN resource reuse changed pixels");
            for(size_t i=3;i<anime.size();i+=4)require(anime[i]==255,"CNN partial-block alpha is incomplete");
        }
        std::vector<uint8_t> expanded(w*h*64),mixed(19*13*4),expectedMixed(mixed.size());
        require(mp_scale_ex(engine.get(),anime.data(),w*2,h*2,expanded.data(),w*4,h*4,8,0,0),mp_last_error());
        require(mp_scale_ex(engine.get(),expanded.data(),w*4,h*4,expectedMixed.data(),19,13,1,.5f,0),mp_last_error());
        const mp_effect_step mixedSteps[]={{7,w*2,h*2,0,0},{8,w*4,h*4,0,0},{1,19,13,.5f,0}};
        require(mp_scale_chain(engine.get(),input.data(),w,h,mixed.data(),mixed.size(),mixedSteps,3),mp_last_error());
        require(mixed==expectedMixed,"Mixed CNN/legacy GPU-only chain differs from standalone passes");
        require(mp_scale_ex(engine.get(),input.data(),w,h,anime.data(),w*2,h*2,9,0,0),mp_last_error());
        require(mp_scale_ex(engine.get(),anime.data(),w*2,h*2,expanded.data(),w*4,h*4,10,0,0),mp_last_error());
        require(mp_scale_ex(engine.get(),expanded.data(),w*4,h*4,expectedMixed.data(),19,13,1,.5f,0),mp_last_error());
        const mp_effect_step fsrcnnxSteps[]={{9,w*2,h*2,0,0},{10,w*4,h*4,0,0},{1,19,13,.5f,0}};
        require(mp_scale_chain(engine.get(),input.data(),w,h,mixed.data(),mixed.size(),fsrcnnxSteps,3),mp_last_error());
        require(mixed==expectedMixed,"FSRCNNX/LineArt/Lanczos chain differs from standalone execution");
        // Switch RGBA8 and RGBA16F models at different chain positions and retain exact results.
        require(mp_scale_ex(engine.get(),input.data(),w,h,anime.data(),w*2,h*2,11,0,0),mp_last_error());
        require(mp_scale_ex(engine.get(),anime.data(),w*2,h*2,expanded.data(),w*4,h*4,9,0,0),mp_last_error());
        require(mp_scale_ex(engine.get(),expanded.data(),w*4,h*4,expectedMixed.data(),19,13,1,.5f,0),mp_last_error());
        const mp_effect_step cunnySteps[]={{11,w*2,h*2,0,0},{9,w*4,h*4,0,0},{1,19,13,.5f,0}};
        require(mp_scale_chain(engine.get(),input.data(),w,h,mixed.data(),mixed.size(),cunnySteps,3),mp_last_error());
        require(mixed==expectedMixed,"CuNNy UNORM / FSRCNNX half / Lanczos chain differs from standalone execution");
        require(mp_scale_ex(engine.get(),input.data(),w,h,anime.data(),w*2,h*2,13,0,0),mp_last_error());
        require(mp_scale_ex(engine.get(),anime.data(),w*2,h*2,expanded.data(),w*4,h*4,11,0,0),mp_last_error());
        require(mp_scale_ex(engine.get(),expanded.data(),w*4,h*4,expectedMixed.data(),19,13,1,.5f,0),mp_last_error());
        const mp_effect_step nnSteps[]={{13,w*2,h*2,0,0},{11,w*4,h*4,0,0},{1,19,13,.5f,0}};
        require(mp_set_gpu_profiling(engine.get(),1),mp_last_error());
        require(mp_scale_chain(engine.get(),input.data(),w,h,mixed.data(),mixed.size(),nnSteps,3),mp_last_error());
        require(mixed==expectedMixed,"NNEDI3 R16F / CuNNy UNORM / Lanczos chain differs from standalone execution");
        mp_gpu_profile_v2 nnTiming{};require(mp_get_gpu_profile_v2(engine.get(),&nnTiming),mp_last_error());
        if(nnTiming.supported)require(nnTiming.status==1 && nnTiming.stage_count==3 && nnTiming.pass_count==9 && nnTiming.pass_counts[0]==2 && nnTiming.effect_ids[0]==13,"NNEDI3 two-pass GPU timing is incorrect");
        require(mp_set_gpu_profiling(engine.get(),0),mp_last_error());
        auto nnBefore=anime;
        require(!mp_scale_ex(engine.get(),input.data(),w,h,anime.data(),w*2-1,h*2,13,0,0),"NNEDI3 accepted non-2x output");
        require(anime==nnBefore,"Invalid NNEDI3 dimensions changed caller output");
        std::cout<<"NNEDI3 PASS: scalar R16F / doubled-height intermediate, 32x8 threads, reuse, mixed-format chains, two-pass timing and failure atomicity\n";
        {
            require(mp_scale_ex(engine.get(),input.data(),w,h,anime.data(),w*2,h*2,15,0,0),mp_last_error());
            require(mp_scale_ex(engine.get(),anime.data(),w*2,h*2,expanded.data(),w*4,h*4,16,0,0),mp_last_error());
            require(mp_scale_ex(engine.get(),expanded.data(),w*4,h*4,expectedMixed.data(),19,13,14,1.7f,0),mp_last_error());
            const mp_effect_step stages[]={{15,w*2,h*2,0,0},{16,w*4,h*4,0,0},{14,19,13,1.7f,0}};
            require(mp_set_gpu_profiling(engine.get(),1),mp_last_error());
            require(mp_scale_chain(engine.get(),input.data(),w,h,mixed.data(),mixed.size(),stages,3),mp_last_error());
            require(mixed==expectedMixed,"Expanded Anime4K / SSim GPU chain differs from separate execution");
            mp_gpu_profile_v2 sample{};require(mp_get_gpu_profile_v2(engine.get(),&sample),mp_last_error());
            if(sample.supported)require(sample.status==1 && sample.stage_count==3 && sample.pass_count==17 && sample.pass_counts[0]==4 && sample.pass_counts[1]==8 && sample.pass_counts[2]==5,"Expanded Anime4K four/eight pass timings are wrong");
            require(mp_set_gpu_profiling(engine.get(),0),mp_last_error());
            for(uint32_t mode:{15u,16u,17u,18u}) {
                auto retained=anime;
                require(!mp_scale_ex(engine.get(),input.data(),w,h,anime.data(),w*2-1,h*2,mode,0,0),"Expanded Anime4K accepted non-2x output");
                require(anime==retained,"Invalid expanded Anime4K changed caller output");
            }
            std::cout<<"ANIME EXPANDED NATIVE PASS: four models, 4/8-pass mixed chain with SSim, GPU-only transfer, full timing and failure atomicity\n";
        }
        {
            require(mp_scale_ex(engine.get(),input.data(),w,h,anime.data(),w*2,h*2,19,0,0),mp_last_error());
            auto retained=anime;
            require(!mp_scale_ex(engine.get(),input.data(),w,h,anime.data(),w*2-1,h*2,19,0,0) && anime==retained,"Invalid RAVU dimensions changed output");
            require(mp_scale_ex(engine.get(),anime.data(),w*2,h*2,expectedMixed.data(),19,13,1,.5f,0),mp_last_error());
            const mp_effect_step sourceChain[]={{19,w*2,h*2,0,0},{1,19,13,.5f,0}};
            require(mp_set_gpu_profiling(engine.get(),1),mp_last_error());
            require(mp_scale_chain(engine.get(),input.data(),w,h,mixed.data(),mixed.size(),sourceChain,2),mp_last_error());
            require(mixed==expectedMixed,"RAVU SOURCE / Lanczos chain differs from separate execution");
            mp_gpu_profile_v2 sample{};require(mp_get_gpu_profile_v2(engine.get(),&sample),mp_last_error());
            if(sample.supported)require(sample.status==1 && sample.stage_count==2 && sample.pass_count==2 && sample.effect_ids[0]==19,"RAVU SOURCE GPU timing failed");
            require(mp_set_gpu_profiling(engine.get(),0),mp_last_error());
            require(mp_scale_ex(engine.get(),input.data(),w,h,anime.data(),w*2,h*2,19,0,0),mp_last_error());
            require(anime==retained,"RAVU SOURCE was discarded or rewritten during frame reuse");
            std::cout<<"SOURCE PASS: embedded original DDS LUT upload, immutable repeated frames, model/chain switch and reuse, GPU timing and invalid-dimension atomicity\n";
        }
        require(!mp_scale_ex(engine.get(),input.data(),w,h,anime.data(),w*2-1,h*2,11,0,0),"CuNNy accepted non-2x output");
        require(!mp_scale_ex(engine.get(),input.data(),w,h,anime.data(),w*2-1,h*2,7,0,0),"CNN accepted non-2x output");
        require(!mp_scale_ex(engine.get(),input.data(),w,h,anime.data(),w*2,h*2,6,0,0),"Reserved virtual effect accepted by native core");
        std::cout << "PASS: Anime4K / FSRCNNX / LineArt / CuNNy multi-pass, original half and UNORM images, reuse/switch, mixed CNN chains, exact 2x constraint\n";
        // Both C ABIs agree on default CRT parameters; configured arrays validate before output writes.
        {
            std::vector<uint8_t> untouched(w*h*4,73);
            require(!mp_scale_ex(engine.get(),input.data(),w,h,untouched.data(),w,h,14,1,2),"Unused single-parameter graph slot lost its legacy range guard");
            require(std::all_of(untouched.begin(),untouched.end(),[](uint8_t v){return v==73;}),"Invalid unused graph parameter modified output");
        }
        mp_effect_config crt{};crt.step={12,ow,oh,0,0};
        const float crtValues[16]={2.4f,2.2f,1.5f,1,2,.03f,1000,0,0,100,100,.3f,1,.3f,0,0};
        std::copy_n(crtValues,16,crt.extra);
        std::vector<uint8_t> crtDefault(output.size()),crtConfigured(output.size());
        require(mp_scale_ex(engine.get(),input.data(),w,h,crtDefault.data(),ow,oh,12,0,0),mp_last_error());
        require(mp_scale_chain_configured(engine.get(),input.data(),w,h,crtConfigured.data(),crtConfigured.size(),&crt,1),mp_last_error());
        require(crtDefault==crtConfigured,"Legacy and configured CRT defaults differ");
        for(float invalid : {0.f,6.f,std::numeric_limits<float>::quiet_NaN()}) {
            crt.extra[0]=invalid;std::fill(crtConfigured.begin(),crtConfigured.end(),73);
            require(!mp_scale_chain_configured(engine.get(),input.data(),w,h,crtConfigured.data(),crtConfigured.size(),&crt,1),"CRT invalid gamma accepted");
            require(std::all_of(crtConfigured.begin(),crtConfigured.end(),[](uint8_t v){return v==73;}),"Invalid CRT request modified output");
        }
        std::copy_n(crtValues,16,crt.extra);crt.extra[12]=1.5f;
        require(!mp_scale_chain_configured(engine.get(),input.data(),w,h,crtConfigured.data(),crtConfigured.size(),&crt,1),"Fractional CRT integer accepted");
        const float crtMinimum[15]={.1f,.1f,.1f,0,.1f,0,80,-.5f,-.5f,-125,-125,0,1,.1f,0};
        const float crtMaximum[15]={5,5,3,1,10,1,2000,.5f,.5f,125,125,.3f,3,.5f,1};
        for(uint32_t slot=0;slot<15;slot++) {
            for(float invalid:{crtMinimum[slot]-1,crtMaximum[slot]+1,std::numeric_limits<float>::quiet_NaN(),std::numeric_limits<float>::infinity()}) {
                std::copy_n(crtValues,16,crt.extra);crt.extra[slot]=invalid;
                std::fill(crtConfigured.begin(),crtConfigured.end(),73);
                require(!mp_scale_chain_configured(engine.get(),input.data(),w,h,crtConfigured.data(),crtConfigured.size(),&crt,1),"Graph parameter slot accepted invalid range or nonfinite value");
                require(std::all_of(crtConfigured.begin(),crtConfigured.end(),[](uint8_t v){return v==73;}),"Invalid graph parameter slot modified output");
            }
        }
        for(uint32_t slot:{3u,6u,9u,10u,12u}) {
            std::copy_n(crtValues,16,crt.extra);crt.extra[slot]+=.5f;
            require(!mp_scale_chain_configured(engine.get(),input.data(),w,h,crtConfigured.data(),crtConfigured.size(),&crt,1),"Graph integer parameter slot accepted a fraction");
        }
        std::copy_n(crtValues,16,crt.extra);crt.extra[11]=0;
        require(mp_scale_chain_configured(engine.get(),input.data(),w,h,crtConfigured.data(),crtConfigured.size(),&crt,1),mp_last_error());
        require(crtConfigured!=crtDefault,"CRT dot mask parameter did not change output");
        std::vector<uint8_t> crtFinal(19*13*4),crtChain(crtFinal.size());
        require(mp_scale_ex(engine.get(),crtConfigured.data(),ow,oh,crtFinal.data(),19,13,1,.5f,0),mp_last_error());
        mp_effect_config crtStages[2]={crt,{}};crtStages[1].step={1,19,13,.5f,0};
        require(mp_scale_chain_configured(engine.get(),input.data(),w,h,crtChain.data(),crtChain.size(),crtStages,2),mp_last_error());
        require(crtFinal==crtChain,"Configured CRT to Lanczos GPU chain differs from separate execution");
        require(mp_scale_ex(engine.get(),input.data(),w,h,anime.data(),w*2,h*2,11,0,0),mp_last_error());
        require(mp_scale_ex(engine.get(),input.data(),w,h,crtConfigured.data(),ow,oh,12,0,0),mp_last_error());
        require(crtConfigured==crtDefault,"CNN to CRT resource switch changed output");
        std::cout << "PASS: CRT defaults, configured ABI, parameters, failure atomicity, chains and CNN resource switching\n";
        // Timestamp clock arithmetic and the no-support path do not depend on this GPU's capabilities.
        require(GpuProfiler::delta((uint64_t(1)<<36)-5,3,36)==8,"GPU timestamp wrap handling is incorrect");
        require(GpuProfiler::delta(UINT64_MAX-4,3,64)==8,"64-bit GPU timestamp wrap handling is incorrect");
        require(!GpuProfiler::supported(0,1) && !GpuProfiler::supported(65,1) && !GpuProfiler::supported(64,0),"GPU timestamp support checks are incorrect");
        {
            GpuProfiler unsupported;unsupported.initialize(VK_NULL_HANDLE,0,1);unsupported.setEnabled(true);unsupported.complete();
            require(!unsupported.latest.supported && unsupported.latest.enabled && unsupported.latest.status==2 && unsupported.latest.stage_count==0,"Unsupported timestamp path fabricated data");
        }
        mp_gpu_profile timing{};require(mp_get_gpu_profile(engine.get(),&timing),mp_last_error());
        require(!timing.enabled && timing.status==0 && timing.stage_count==0,"GPU profiling was not disabled by default");
        require(!mp_set_gpu_profiling(engine.get(),2) && !mp_set_gpu_profiling(nullptr,1),"Invalid GPU profiling API request accepted");
        require(!mp_get_gpu_profile(engine.get(),nullptr),"Null GPU profiling output accepted");
        require(mp_set_gpu_profiling(engine.get(),1),mp_last_error());
        std::vector<uint8_t> profiled(output.size()),unprofiled(output.size());
        require(mp_scale_ex(engine.get(),input.data(),w,h,profiled.data(),ow,oh,1,.5f,0),mp_last_error());
        require(mp_get_gpu_profile(engine.get(),&timing),mp_last_error());
        auto verifyTiming=[&](const mp_gpu_profile& value,uint32_t stages,uint32_t passes) {
            if(!value.supported) { require(value.status==2 && value.stage_count==0,"Unsupported device exposed GPU measurements");return; }
            require(value.status==1 && value.enabled && value.timestamp_bits>0 && value.timestamp_period_ns>0,"GPU timestamp results unavailable or invalid");
            require(value.stage_count==stages && value.pass_count==passes,"GPU timestamp stage/pass counts differ from dispatch graph");
            require(std::isfinite(value.total_ms) && value.total_ms>=0,"Invalid GPU total time");
            double sum=0;
            for(uint32_t s=0;s<stages;s++) {
                double passSum=0;require(value.first_pass[s]+value.pass_counts[s]<=passes,"GPU timing pass range is invalid");
                for(uint32_t j=0;j<value.pass_counts[s];j++) { double time=value.pass_ms[value.first_pass[s]+j];require(std::isfinite(time) && time>=0,"Invalid GPU pass interval");passSum+=time; }
                require(std::abs(passSum-value.stage_ms[s])<1e-7,"GPU effect interval does not equal its pass intervals");sum+=value.stage_ms[s];
            }
            require(sum<=value.total_ms+1e-7,"GPU intervals exceed the whole command interval");
        };
        verifyTiming(timing,1,1);uint64_t sequence=timing.frame_sequence;
        const mp_effect_step gpuStages[]={{11,w*2,h*2,0,0},{9,w*4,h*4,0,0},{1,19,13,.5f,0}};
        std::vector<uint8_t> gpuMixed(19*13*4),gpuPlain(gpuMixed.size());
        require(mp_scale_chain(engine.get(),input.data(),w,h,gpuMixed.data(),gpuMixed.size(),gpuStages,3),mp_last_error());
        require(mp_get_gpu_profile(engine.get(),&timing),mp_last_error());verifyTiming(timing,3,13);
        require(timing.frame_sequence==sequence+1,"GPU profiling results are stale");
        sequence=timing.frame_sequence;
        require(!mp_scale_chain(engine.get(),input.data(),w,h,gpuMixed.data(),1,gpuStages,3),"Short profiled output accepted");
        mp_gpu_profile afterFailure{};require(mp_get_gpu_profile(engine.get(),&afterFailure),mp_last_error());
        require(afterFailure.frame_sequence==sequence && afterFailure.total_ms==timing.total_ms,"Failed dispatch replaced the completed GPU sample");
        std::cout<<"GPU PROFILE PASS: supported="<<timing.supported<<" valid_bits="<<timing.timestamp_bits<<" period_ns="<<timing.timestamp_period_ns<<" stages="<<timing.stage_count<<" passes="<<timing.pass_count<<" total_ms="<<timing.total_ms<<'\n';
        require(mp_set_gpu_profiling(engine.get(),0),mp_last_error());
        require(mp_scale_chain(engine.get(),input.data(),w,h,gpuPlain.data(),gpuPlain.size(),gpuStages,3),mp_last_error());
        require(gpuPlain==gpuMixed,"Timestamp queries changed mixed CNN output pixels");
        require(mp_scale_ex(engine.get(),input.data(),w,h,unprofiled.data(),ow,oh,1,.5f,0),mp_last_error());
        require(unprofiled==profiled,"Timestamp queries changed Lanczos output pixels");
        require(mp_get_gpu_profile(engine.get(),&timing),mp_last_error());
        require(!timing.enabled && timing.status==0 && timing.stage_count==0 && timing.pass_count==0 && timing.total_ms==0,"Disabled profiler retained stale data");
        require(mp_set_gpu_profiling(engine.get(),1),mp_last_error());
        mp_effect_step maximumStages[16]{};
        for(uint32_t i=0;i<16;i++)maximumStages[i]={i%2==0?11u:0u,uint32_t(i%2==0?w*2:w),uint32_t(i%2==0?h*2:h),0,0};
        std::vector<uint8_t> maximumOutput(input.size());
        require(mp_scale_chain(engine.get(),input.data(),w,h,maximumOutput.data(),maximumOutput.size(),maximumStages,16),mp_last_error());
        require(mp_get_gpu_profile(engine.get(),&timing),mp_last_error());verifyTiming(timing,16,56);
        require(mp_set_gpu_profiling(engine.get(),0),mp_last_error());
        std::cout<<"GPU PROFILE BOUNDARY PASS: re-enabled query pool, 16 stages / 56 passes\n";
        static_assert(sizeof(mp_gpu_profile)==2416 && sizeof(mp_gpu_profile_v2)==2440);
        mp_effect_config extended[17]{};
        for(uint32_t i=0;i<16;i++)extended[i].step={0,uint32_t(w),uint32_t(h),0,0};
        extended[16].step={3,uint32_t(ow),uint32_t(oh),0,.5f};
        std::vector<uint8_t> presented(output.size()),presentationReference(output.size());
        require(mp_set_gpu_profiling(engine.get(),1),mp_last_error());
        require(mp_scale_chain_configured_v2(engine.get(),input.data(),w,h,presented.data(),presented.size(),extended,17),mp_last_error());
        mp_gpu_profile_v2 extendedTiming{};
        require(mp_get_gpu_profile_v2(engine.get(),&extendedTiming),mp_last_error());
        if(extendedTiming.supported)require(extendedTiming.status==1 && extendedTiming.stage_count==17 && extendedTiming.pass_count==17 && extendedTiming.effect_ids[16]==3,"Presentation stage was omitted from extended profiling");
        struct { mp_gpu_profile sample;uint64_t canary[2]; } legacyTiming{{},{0x123456789abcdef0ull,0x9876543210abcdefull}};
        require(mp_get_gpu_profile(engine.get(),&legacyTiming.sample),mp_last_error());
        require(legacyTiming.canary[0]==0x123456789abcdef0ull && legacyTiming.canary[1]==0x9876543210abcdefull,"Legacy GPU timing ABI overwrote caller memory");
        require(legacyTiming.sample.stage_count==0 && legacyTiming.sample.pass_count==0 && legacyTiming.sample.status==(extendedTiming.supported?3u:2u),"Legacy profiling exposed a truncated presentation graph");
        auto retained=presented;
        require(!mp_scale_chain_configured(engine.get(),input.data(),w,h,presented.data(),presented.size(),extended,17),"Legacy configured API accepted seventeen stages");
        require(!mp_scale_chain_configured_v2(engine.get(),input.data(),w,h,presented.data(),presented.size(),extended,18) && presented==retained,"Extended chain failure changed output");
        require(!mp_get_gpu_profile_v2(nullptr,&extendedTiming) && !mp_get_gpu_profile_v2(engine.get(),nullptr),"Invalid extended timing request accepted");
        require(mp_set_gpu_profiling(engine.get(),0),mp_last_error());
        require(mp_scale_ex(engine.get(),input.data(),w,h,presentationReference.data(),ow,oh,3,0,.5f),mp_last_error());
        require(presented==presentationReference,"Seventeenth presentation stage differs from separate Bicubic execution");
        std::cout<<"PRESENTATION V2 PASS: 17 stages, full timestamps, legacy 2416-byte canary and limits, exact B=0/C=0.5 output\n";
        // Exercise the last user slot as CNN, followed by the reserved presentation slot.
        for(uint32_t i=0;i<16;i++)extended[i].step={i%2==0?0u:11u,uint32_t(i%2==0?w:w*2),uint32_t(i%2==0?h:h*2),0,0};
        require(mp_set_gpu_profiling(engine.get(),1),mp_last_error());
        require(mp_scale_chain_configured_v2(engine.get(),input.data(),w,h,presented.data(),presented.size(),extended,17),mp_last_error());
        require(mp_get_gpu_profile_v2(engine.get(),&extendedTiming),mp_last_error());
        if(extendedTiming.supported)require(extendedTiming.status==1 && extendedTiming.stage_count==17 && extendedTiming.pass_count==57 && extendedTiming.pass_counts[15]==6 && extendedTiming.effect_ids[16]==3,"Last-user-slot CNN timing or presentation dispatch is invalid");
        require(mp_set_gpu_profiling(engine.get(),0),mp_last_error());
        std::vector<uint8_t> firstSixteen(w*2*h*2*4);
        require(mp_scale_chain_configured(engine.get(),input.data(),w,h,firstSixteen.data(),firstSixteen.size(),extended,16),mp_last_error());
        require(mp_scale_ex(engine.get(),firstSixteen.data(),w*2,h*2,presentationReference.data(),ow,oh,3,0,.5f),mp_last_error());
        require(presented==presentationReference,"Sixteen mixed user effects plus presentation differ from separate GPU execution");
        std::cout<<"PRESENTATION CNN BOUNDARY PASS: CNN at user slot 16, post at slot 17, 57 passes, exact pixels\n";
        // RCAS uses the original zero-outside Texture.Load semantics at image boundaries.
        std::vector<uint8_t> sharpened(input.size());
        for(float sharpness : {0.f,.5f,1.f}) {
            require(mp_scale_ex(engine.get(),input.data(),w,h,sharpened.data(),w,h,5,sharpness,0),mp_last_error());
            auto sample=[&](int x,int y,int c)->double { if(x<0||x>=w||y<0||y>=h)return 0;return input[(y*w+x)*4+c]/255.0; };
            int maxError=0;
            for(int y=0;y<h;++y)for(int x=0;x<w;++x) {
                double ring[5][3];
                const int dx[]={0,-1,0,1,0},dy[]={-1,0,0,0,1};
                double luma[5];
                for(int p=0;p<5;++p) { for(int c=0;c<3;++c)ring[p][c]=sample(x+dx[p],y+dy[p],c);luma[p]=ring[p][2]*.5+ring[p][0]*.5+ring[p][1]; }
                double noise=(luma[0]+luma[1]+luma[3]+luma[4])*.25-luma[2];
                double spread=*std::max_element(luma,luma+5)-*std::min_element(luma,luma+5);
                double nz=spread>0?std::clamp(std::abs(noise)/spread,0.0,1.0):0;
                double lobe=-1;
                for(int c=0;c<3;++c) {
                    double lo=std::min({ring[0][c],ring[1][c],ring[3][c],ring[4][c]});
                    double hi=std::max({ring[0][c],ring[1][c],ring[3][c],ring[4][c]});
                    double hitMin=hi>0?std::min(lo,ring[2][c])/(4*hi):0;
                    double hitMax=lo<1?(1-std::max(hi,ring[2][c]))/(4*lo-4):0;
                    lobe=std::max(lobe,std::max(-hitMin,hitMax));
                }
                lobe=std::max(-.1875,std::min(lobe,0.0))*sharpness*(1-.5*nz);
                for(int c=0;c<3;++c) {
                    double value=(lobe*(ring[0][c]+ring[1][c]+ring[3][c]+ring[4][c])+ring[2][c])/(4*lobe+1);
                    int expected=int(std::round(std::clamp(value,0.0,1.0)*255));
                    maxError=std::max(maxError,std::abs(int(sharpened[(y*w+x)*4+c])-expected));
                }
                require(sharpened[(y*w+x)*4+3]==255,"RCAS alpha is invalid");
            }
            std::cout << "RCAS sharpness=" << sharpness << ", max channel error=" << maxError << '\n';
            require(maxError<=1,"FSR RCAS differs from independent CPU reference");
        }
        std::vector<uint8_t> fsrStage(19*13*4),fsrExpected(fsrStage.size()),fsrActual(fsrStage.size());
        require(mp_scale_ex(engine.get(),input.data(),w,h,fsrStage.data(),19,13,4,0,0),mp_last_error());
        require(mp_scale_ex(engine.get(),fsrStage.data(),19,13,fsrExpected.data(),19,13,5,.87f,0),mp_last_error());
        mp_effect_step fsrSteps[]={{4,19,13,0,0},{5,19,13,.87f,0}};
        require(mp_scale_chain(engine.get(),input.data(),w,h,fsrActual.data(),fsrActual.size(),fsrSteps,2),mp_last_error());
        require(fsrActual==fsrExpected,"FSR chain differs from original ordered EASU/RCAS passes");
        fsrSteps[1].output_width=18;
        require(!mp_scale_chain(engine.get(),input.data(),w,h,fsrActual.data(),fsrActual.size(),fsrSteps,2),"RCAS accepted changed output dimensions");
        std::cout << "PASS: FSR EASU odd dispatch, RCAS CPU reference/edges, FSR chain, same-size constraint\n";
        // Chain must match explicit independent dispatches, including intermediate quantization.
        mp_effect_step chain[] = {{1,18,14,.5f,0}, {3,11,9,.2f,.7f}, {2,19,13,0,0}};
        std::vector<uint8_t> intermediate1(18*14*4), intermediate2(11*9*4), expectedChain(19*13*4), actualChain(expectedChain.size());
        require(mp_scale_ex(engine.get(),input.data(),w,h,intermediate1.data(),18,14,1,.5f,0),mp_last_error());
        require(mp_scale_ex(engine.get(),intermediate1.data(),18,14,intermediate2.data(),11,9,3,.2f,.7f),mp_last_error());
        require(mp_scale_ex(engine.get(),intermediate2.data(),11,9,expectedChain.data(),19,13,2,0,0),mp_last_error());
        for(int repeat=0; repeat<4; ++repeat) {
            require(mp_scale_chain(engine.get(),input.data(),w,h,actualChain.data(),actualChain.size(),chain,3),mp_last_error());
            require(actualChain==expectedChain,"GPU chain differs from ordered standalone effects");
        }
        std::fill(actualChain.begin(),actualChain.end(),123);
        require(!mp_scale_chain(engine.get(),input.data(),w,h,actualChain.data(),1,chain,3),"Undersized chain buffer accepted");
        require(std::all_of(actualChain.begin(),actualChain.end(),[](uint8_t x){return x==123;}),"Rejected chain changed caller output");
        chain[1].effect=std::numeric_limits<uint32_t>::max();
        require(!mp_scale_chain(engine.get(),input.data(),w,h,actualChain.data(),actualChain.size(),chain,3),"Invalid middle effect accepted");
        chain[1].effect=3;
        require(!mp_scale_chain(engine.get(),input.data(),w,h,actualChain.data(),actualChain.size(),chain,0),"Empty chain accepted");
        require(!mp_scale_chain(engine.get(),input.data(),w,h,actualChain.data(),actualChain.size(),chain,17),"Oversized chain accepted");
        require(mp_scale_chain(engine.get(),input.data(),w,h,actualChain.data(),actualChain.size(),chain,3),mp_last_error());
        require(actualChain==expectedChain,"Chain failed to recover after invalid request");
        mp_effect_step longChain[16];
        std::fill_n(longChain,16,mp_effect_step{0,1,1,0,0});
        uint8_t solid[]{10,20,30,255}, finalPixel[4]{};
        require(mp_scale_chain(engine.get(),solid,1,1,finalPixel,sizeof(finalPixel),longChain,16),mp_last_error());
        require(std::equal(solid,solid+4,finalPixel),"Maximum-length chain changed nearest pixel");
        std::cout << "PASS: ordered three-stage GPU-only chain, reuse, 16 stages, short output, invalid stage, recovery\n";
        {
            std::vector<uint8_t> smaaInput(7*5*4),smaaOutput(smaaInput.size()),sequential(smaaInput.size());
            for(size_t i=0;i<smaaInput.size();i++)smaaInput[i]=i%4==3?255:uint8_t(i*31%251);
            for(uint32_t effect=20;effect<=23;effect++) {
                require(mp_scale_ex(engine.get(),smaaInput.data(),7,5,smaaOutput.data(),7,5,effect,0,0),mp_last_error());
                std::vector<uint8_t> invalid(8*5*4,119);
                require(!mp_scale_ex(engine.get(),smaaInput.data(),7,5,invalid.data(),8,5,effect,0,0),"SMAA accepted non-matching dimensions");
                require(std::all_of(invalid.begin(),invalid.end(),[](uint8_t b){return b==119;}),"Rejected SMAA changed caller bytes");
                uint8_t one[]{23,45,67,119},result[4]{};
                require(mp_scale_ex(engine.get(),one,1,1,result,1,1,effect,0,0),mp_last_error());
                require(std::equal(one,one+4,result),"SMAA changed a single constant RGBA pixel");
            }
            mp_effect_step stages[4]={{20,7,5,0,0},{21,7,5,0,0},{22,7,5,0,0},{23,7,5,0,0}};
            require(mp_set_gpu_profiling(engine.get(),1),mp_last_error());
            require(mp_scale_chain(engine.get(),smaaInput.data(),7,5,smaaOutput.data(),smaaOutput.size(),stages,4),mp_last_error());
            mp_gpu_profile_v2 timing{};require(mp_get_gpu_profile_v2(engine.get(),&timing),mp_last_error());
            if(timing.supported)require(timing.stage_count==4&&timing.pass_count==12&&timing.pass_counts[0]==3&&timing.pass_counts[3]==3,"SMAA pass graph timing invalid");
            auto previous=smaaInput;
            for(uint32_t effect=20;effect<=23;effect++) { require(mp_scale_ex(engine.get(),previous.data(),7,5,sequential.data(),7,5,effect,0,0),mp_last_error());previous=sequential; }
            require(smaaOutput==sequential,"SMAA four-stage graph differs from ordered standalone effects");
            std::cout<<"PASS: four SMAA presets, embedded SOURCE tables, RG8/RG16F/RGBA8/RGBA16F, same-size rejection and unchanged caller bytes, 1x1 RGBA, twelve-pass chain/timing/reuse\n";
        }
        // Resource reallocation, identity, thin images and invalid input recovery.
        std::vector<uint8_t> identity(input.size());
        require(mp_scale(engine.get(),input.data(),w,h,identity.data(),w,h,1,.5f),mp_last_error());
        require(input==identity,"Identity scaling changed pixels");
        uint8_t pixel[]{23,45,67,255};
        require(mp_scale(engine.get(),pixel,1,1,output.data(),ow,oh,1,.5f),mp_last_error());
        for(size_t i=0;i<output.size();++i) require(output[i]==pixel[i%4],"Constant image changed at edges");
        require(!mp_scale(engine.get(),pixel,0,1,output.data(),ow,oh,1,.5f),"Zero width was accepted");
        require(!mp_scale(engine.get(),pixel,1,1,output.data(),UINT32_MAX,oh,1,.5f),"Overflow dimensions were accepted");
        require(mp_scale(engine.get(),pixel,1,1,output.data(),ow,oh,0,.5f),mp_last_error());
        std::cout << "PASS: real Vulkan dispatch, nearest, original Lanczos/anti-ringing, edges, resize, invalid input recovery\n";
        return 0;
    } catch (const std::exception& ex) { std::cerr << ex.what() << '\n'; return 1; }
}
