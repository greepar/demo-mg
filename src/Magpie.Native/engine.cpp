#include "magpie.h"
#include "error.h"
#include "kernel.h"
#include <vulkan/vulkan.h>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <vector>
#include <unordered_map>
#include "gpu_profile.h"
#include "precision.h"

namespace magpie { thread_local std::string error; }
#ifndef _WIN32
extern "C" int mp_capture_windows_pointer_rect(void*,uint64_t,uint32_t,uint64_t,mp_pointer_rect*,uint32_t) {return 0;}
extern "C" int mp_windows_cursor_copy(uint64_t,mp_cursor_shape*,uint32_t) {return 0;}
extern "C" void mp_windows_cursor_destroy(uint64_t) {}
extern "C" int mp_windows_cursor_draw(uint64_t,uint8_t*,uint64_t,uint32_t,uint32_t,int32_t,int32_t,uint32_t,uint32_t) {return 0;}
extern "C" int mp_windows_system_cursor(int) {return 0;}
extern "C" uint64_t mp_windows_pointer_window(void*,int32_t,int32_t,int) {return 0;}
extern "C" uint32_t mp_capture_source_state(void*) { return 0; }
extern "C" int mp_capture_restore_output_layer(void*,void*,uint64_t,uint32_t) { return 0; }
extern "C" int mp_capture_mark_output_fullscreen(void*,void*,uint64_t,uint32_t,int) { return 0; }
#endif
#ifndef __APPLE__
extern "C" int mp_capture_overlay_region(void*,double,double,double,double) { return 0; }
extern "C" int mp_capture_configure_input(void*,void*,double,double,double,double,double,double,double,double,double,const char*,int,uint64_t,uint64_t,uint32_t,uint64_t,const char*,int,uint64_t) { return 0; }
#endif
namespace {
void check(VkResult result, const char* operation) {
    if (result != VK_SUCCESS) throw std::runtime_error(std::string(operation) + " failed (Vulkan " + std::to_string(result) + ")");
}
struct Buffer {
    VkDevice device{};
    VkBuffer handle{};
    VkDeviceMemory memory{};
    void* mapped{};
    ~Buffer() {
        if (mapped) vkUnmapMemory(device, memory);
        if (handle) vkDestroyBuffer(device, handle, nullptr);
        if (memory) vkFreeMemory(device, memory, nullptr);
    }
};
#include "multipass.h"
struct Frame {
    VkDevice device{};
    uint32_t width{}, height{}, outWidth{}, outHeight{};
    std::unique_ptr<Buffer> upload, output;
    std::unique_ptr<MultiFrame> multi;
    VkImage image{};
    VkDeviceMemory memory{};
    VkImageView view{};
    ~Frame() {
        if (view) vkDestroyImageView(device, view, nullptr);
        if (image) vkDestroyImage(device, image, nullptr);
        if (memory) vkFreeMemory(device, memory, nullptr);
    }
};
static bool isGraphEffect(uint32_t effect) { return multiEffect(effect)!=nullptr; }
static uint32_t kernelIndex(uint32_t effect) { return effect; }
uint32_t instanceApiVersion() {
    uint32_t version=VK_API_VERSION_1_1;
    if(auto query=reinterpret_cast<PFN_vkEnumerateInstanceVersion>(vkGetInstanceProcAddr(nullptr,"vkEnumerateInstanceVersion")))check(query(&version),"vkEnumerateInstanceVersion");
    return std::min(version,uint32_t(VK_API_VERSION_1_2));
}
VkInstance createInstance() {
    VkInstance instance{};
        VkApplicationInfo app{VK_STRUCTURE_TYPE_APPLICATION_INFO};
        app.pApplicationName = "Magpie";
        app.apiVersion = instanceApiVersion();
        VkInstanceCreateInfo info{VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO};
        info.pApplicationInfo = &app;
        uint32_t extensionCount{};
        check(vkEnumerateInstanceExtensionProperties(nullptr,&extensionCount,nullptr),"vkEnumerateInstanceExtensionProperties");
        std::vector<VkExtensionProperties> extensions(extensionCount);
        check(vkEnumerateInstanceExtensionProperties(nullptr,&extensionCount,extensions.data()),"vkEnumerateInstanceExtensionProperties");
        const char* portability="VK_KHR_portability_enumeration";
        if(std::any_of(extensions.begin(),extensions.end(),[&](const auto& e){return std::strcmp(e.extensionName,portability)==0;})) {
            info.flags |= VK_INSTANCE_CREATE_ENUMERATE_PORTABILITY_BIT_KHR;
            info.enabledExtensionCount=1;info.ppEnabledExtensionNames=&portability;
        }
        check(vkCreateInstance(&info, nullptr, &instance), "vkCreateInstance");
    return instance;
}
struct InstanceScope {
    VkInstance handle=createInstance();
    ~InstanceScope() { if(handle)vkDestroyInstance(handle,nullptr); }
};
struct DeviceCandidate { VkPhysicalDevice handle;VkPhysicalDeviceProperties properties;uint32_t family,timestampBits;mp_device_info info; };
static_assert(sizeof(mp_device_info)==304,"Device information ABI changed");
std::vector<DeviceCandidate> enumerateDevices(VkInstance instance) {
    uint32_t count{};
    check(vkEnumeratePhysicalDevices(instance,&count,nullptr),"vkEnumeratePhysicalDevices");
    std::vector<VkPhysicalDevice> handles(count);
    check(vkEnumeratePhysicalDevices(instance,&count,handles.data()),"vkEnumeratePhysicalDevices");
    std::vector<DeviceCandidate> devices;
    constexpr char digits[]="0123456789abcdef";
    for(auto candidate:handles) {
        VkPhysicalDeviceIDProperties ids{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_ID_PROPERTIES};
        VkPhysicalDeviceProperties2 properties{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROPERTIES_2};properties.pNext=&ids;
        vkGetPhysicalDeviceProperties2(candidate,&properties);
        const auto& props=properties.properties;
        if(props.apiVersion<VK_API_VERSION_1_1)continue;
        VkFormatProperties format{};vkGetPhysicalDeviceFormatProperties(candidate,VK_FORMAT_R8G8B8A8_UNORM,&format);
        constexpr auto features=VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT|VK_FORMAT_FEATURE_SAMPLED_IMAGE_FILTER_LINEAR_BIT|VK_FORMAT_FEATURE_TRANSFER_DST_BIT;
        if((format.optimalTilingFeatures&features)!=features)continue;
        uint32_t countFamilies{};vkGetPhysicalDeviceQueueFamilyProperties(candidate,&countFamilies,nullptr);
        std::vector<VkQueueFamilyProperties> queues(countFamilies);vkGetPhysicalDeviceQueueFamilyProperties(candidate,&countFamilies,queues.data());
        for(uint32_t family=0;family<countFamilies;family++) {
            if(!queues[family].queueCount || !(queues[family].queueFlags&VK_QUEUE_COMPUTE_BIT))continue;
            mp_device_info info{};
            for(uint32_t i=0;i<VK_UUID_SIZE;i++) { info.identifier[i*2]=digits[ids.deviceUUID[i]>>4];info.identifier[i*2+1]=digits[ids.deviceUUID[i]&15]; }
            std::memcpy(info.name,props.deviceName,sizeof(info.name));info.name[sizeof(info.name)-1]=0;
            info.type=props.deviceType;info.vendor_id=props.vendorID;info.device_id=props.deviceID;
            if(std::any_of(devices.begin(),devices.end(),[&](const auto& d){return std::strcmp(info.identifier,d.info.identifier)==0;}))throw std::runtime_error("Vulkan devices have duplicate UUIDs");
            devices.push_back({candidate,props,family,queues[family].timestampValidBits,info});break;
        }
    }
    return devices;
}
std::string normalizeDeviceId(const char* id) {
    if(!id || !*id)return {};
    std::string result;
    for(uint32_t i=0;i<32;i++) {
        char value=id[i];
        if(value>='A' && value<='F')value=char(value-'A'+'a');
        if(!((value>='0' && value<='9') || (value>='a' && value<='f')))throw std::runtime_error("Vulkan device UUID must contain 32 hexadecimal digits");
        result+=value;
    }
    if(id[32])throw std::runtime_error("Vulkan device UUID must contain 32 hexadecimal digits");
    return result;
}
int deviceScore(uint32_t type) { return type==VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU?5:type==VK_PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU?4:1; }
using DeviceSnapshot=std::vector<mp_device_info>;
struct Engine {
    VkInstance instance{};
    VkPhysicalDevice physical{};
    VkPhysicalDeviceProperties properties{};
    VkPhysicalDeviceMemoryProperties memories{};
    VkDevice device{};
    VkQueue queue{};
    VkDescriptorSetLayout descriptors{};
    VkPipelineLayout layout{};
    VkShaderModule shaders[std::size(kernels)]{};
    VkPipeline pipelines[std::size(kernels)]{};
    VkSampler samplers[std::size(kernels)]{};
    VkDescriptorPool descriptorPool{};
    VkDescriptorSet descriptorSets[MP_MAX_CHAIN_STAGES]{};
    VkCommandPool commandPool{};
    VkCommandBuffer command{};
    VkFence fence{};
    GpuProfiler profiler;
    static_assert(sizeof(mp_gpu_profile)==2416,"GPU profile ABI changed");
    static_assert(sizeof(mp_gpu_profile_v2)==2440,"GPU profile v2 ABI changed");
    uint64_t snapshotSerial{};
    uint32_t snapshotCount{},snapshotEffects[MP_MAX_CHAIN_STAGES]{};
    mp_effect_step snapshotSteps[MP_MAX_CHAIN_STAGES]{};
    float snapshotExtra[MP_MAX_CHAIN_STAGES*16]{};
    static_assert(sizeof(mp_pass_snapshot_info)==56);
    static_assert(sizeof(mp_stage_snapshot_info)==32);
    std::unique_ptr<Frame> frames[MP_MAX_CHAIN_STAGES];
    std::unordered_map<uint32_t,std::unique_ptr<MultiRenderer>> multi;
    std::mutex mutex;
    ~Engine() {
        if (device) vkDeviceWaitIdle(device);
        for (auto& frame : frames) frame.reset();
        multi.clear();profiler.release();
        if (fence) vkDestroyFence(device, fence, nullptr);
        if (commandPool) vkDestroyCommandPool(device, commandPool, nullptr);
        if (descriptorPool) vkDestroyDescriptorPool(device, descriptorPool, nullptr);
        for (auto sampler : samplers) if (sampler) vkDestroySampler(device, sampler, nullptr);
        for (auto pipeline : pipelines) if (pipeline) vkDestroyPipeline(device, pipeline, nullptr);
        for (auto shader : shaders) if (shader) vkDestroyShaderModule(device, shader, nullptr);
        if (layout) vkDestroyPipelineLayout(device, layout, nullptr);
        if (descriptors) vkDestroyDescriptorSetLayout(device, descriptors, nullptr);
        if (device) vkDestroyDevice(device, nullptr);
        if (instance) vkDestroyInstance(instance, nullptr);
    }
    uint32_t memoryType(uint32_t bits, VkMemoryPropertyFlags flags) {
        for (uint32_t i = 0; i < memories.memoryTypeCount; ++i)
            if ((bits & (1u << i)) && (memories.memoryTypes[i].propertyFlags & flags) == flags) return i;
        throw std::runtime_error("No compatible Vulkan memory type");
    }
    std::string identifier;
    bool fp16Supported{},fp16Enabled{};
    MultiRenderer& graphRenderer(uint32_t id) {
        auto found=multi.find(id);
        if(found!=multi.end())return *found->second;
        const auto* definition=multiEffect(id);if(!definition)throw std::runtime_error("Unknown effect renderer");
        auto renderer=std::make_unique<MultiRenderer>();renderer->initialize(device,physical,*definition,fp16Enabled);
        return *multi.emplace(id,std::move(renderer)).first->second;
    }
    void initialize(const char* requested=nullptr,uint32_t precision=0) {
        (void)nativeHalfEnabled(precision,true);
        const auto selectedId=normalizeDeviceId(requested);
        instance=createInstance();
        const auto devices=enumerateDevices(instance);
        int best=-1;uint32_t family{},timestampBits{};
        for(const auto& candidate:devices) {
            if(!selectedId.empty() && selectedId!=candidate.info.identifier)continue;
            int score=deviceScore(candidate.info.type);
            if(score>best) { best=score;physical=candidate.handle;properties=candidate.properties;family=candidate.family;timestampBits=candidate.timestampBits;identifier=candidate.info.identifier; }
        }
        if(!physical)throw std::runtime_error(selectedId.empty()?"No usable Vulkan 1.1 compute device supports RGBA8 sampling":"The selected Vulkan device is unavailable. Choose another device or Automatic in the profile.");
        uint32_t extensionCount{};std::vector<VkExtensionProperties> extensions;
        vkGetPhysicalDeviceMemoryProperties(physical, &memories);
        float priority = 1;
        VkDeviceQueueCreateInfo queueInfo{VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO};
        queueInfo.queueFamilyIndex = family; queueInfo.queueCount = 1; queueInfo.pQueuePriorities = &priority;
        VkDeviceCreateInfo deviceInfo{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO};
        deviceInfo.queueCreateInfoCount = 1; deviceInfo.pQueueCreateInfos = &queueInfo;
        extensionCount=0;
        check(vkEnumerateDeviceExtensionProperties(physical,nullptr,&extensionCount,nullptr),"vkEnumerateDeviceExtensionProperties");
        extensions.resize(extensionCount);
        check(vkEnumerateDeviceExtensionProperties(physical,nullptr,&extensionCount,extensions.data()),"vkEnumerateDeviceExtensionProperties");
        auto hasExtension=[&](const char* name) { return std::any_of(extensions.begin(),extensions.end(),[&](const auto& e){return std::strcmp(e.extensionName,name)==0;}); };
        std::vector<const char*> enabledExtensions;
        if(hasExtension("VK_KHR_portability_subset"))enabledExtensions.push_back("VK_KHR_portability_subset");
        VkPhysicalDeviceShaderFloat16Int8Features halfFeatures{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SHADER_FLOAT16_INT8_FEATURES};
        bool coreHalf=instanceApiVersion()>=VK_API_VERSION_1_2 && properties.apiVersion>=VK_API_VERSION_1_2;
        bool rte16=false;
        if(coreHalf || hasExtension(VK_KHR_SHADER_FLOAT_CONTROLS_EXTENSION_NAME)) {
            VkPhysicalDeviceFloatControlsProperties controls{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FLOAT_CONTROLS_PROPERTIES};
            VkPhysicalDeviceProperties2 properties2{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROPERTIES_2};properties2.pNext=&controls;
            vkGetPhysicalDeviceProperties2(physical,&properties2);rte16=controls.shaderRoundingModeRTEFloat16;
            if(!coreHalf)enabledExtensions.push_back(VK_KHR_SHADER_FLOAT_CONTROLS_EXTENSION_NAME);
        }
        if(coreHalf || hasExtension(VK_KHR_SHADER_FLOAT16_INT8_EXTENSION_NAME)) {
            VkPhysicalDeviceFeatures2 features{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2};features.pNext=&halfFeatures;
            vkGetPhysicalDeviceFeatures2(physical,&features);fp16Supported=halfFeatures.shaderFloat16 && rte16;
            if(!coreHalf)enabledExtensions.push_back(VK_KHR_SHADER_FLOAT16_INT8_EXTENSION_NAME);
            halfFeatures.shaderInt8=VK_FALSE;deviceInfo.pNext=&halfFeatures;
        }
        fp16Enabled=nativeHalfEnabled(precision,fp16Supported);
        deviceInfo.enabledExtensionCount=uint32_t(enabledExtensions.size());deviceInfo.ppEnabledExtensionNames=enabledExtensions.data();
        VkPhysicalDeviceFeatures available{},enabled{};vkGetPhysicalDeviceFeatures(physical,&available);
        enabled.shaderStorageImageExtendedFormats=available.shaderStorageImageExtendedFormats;deviceInfo.pEnabledFeatures=&enabled;
        check(vkCreateDevice(physical, &deviceInfo, nullptr, &device), "vkCreateDevice");
        vkGetDeviceQueue(device, family, 0, &queue);
        profiler.initialize(device,timestampBits,properties.limits.timestampPeriod);
        VkDescriptorSetLayoutBinding bindings[] = {
            {0, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr},
            {1, VK_DESCRIPTOR_TYPE_SAMPLER, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr},
            {2, VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, 1, VK_SHADER_STAGE_COMPUTE_BIT, nullptr}
        };
        VkDescriptorSetLayoutCreateInfo descriptorInfo{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};
        descriptorInfo.bindingCount = 3; descriptorInfo.pBindings = bindings;
        check(vkCreateDescriptorSetLayout(device, &descriptorInfo, nullptr, &descriptors), "vkCreateDescriptorSetLayout");
        VkPushConstantRange range{VK_SHADER_STAGE_COMPUTE_BIT, 0, 96};
        VkPipelineLayoutCreateInfo layoutInfo{VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO};
        layoutInfo.setLayoutCount = 1; layoutInfo.pSetLayouts = &descriptors;
        layoutInfo.pushConstantRangeCount = 1; layoutInfo.pPushConstantRanges = &range;
        check(vkCreatePipelineLayout(device, &layoutInfo, nullptr, &layout), "vkCreatePipelineLayout");
        for (size_t i = 0; i < std::size(kernels); ++i) {
        VkShaderModuleCreateInfo shaderInfo{VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO};
        bool useHalf=fp16Enabled && kernels[i].halfData;
        shaderInfo.codeSize = useHalf?kernels[i].halfSize:kernels[i].size; shaderInfo.pCode = useHalf?kernels[i].halfData:kernels[i].data;
        check(vkCreateShaderModule(device, &shaderInfo, nullptr, &shaders[i]), "vkCreateShaderModule");
        VkComputePipelineCreateInfo pipelineInfo{VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO};
        pipelineInfo.stage = {VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO, nullptr, 0, VK_SHADER_STAGE_COMPUTE_BIT, shaders[i], "main", nullptr};
        pipelineInfo.layout = layout;
        check(vkCreateComputePipelines(device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &pipelines[i]), "vkCreateComputePipelines");
        VkSamplerCreateInfo samplerInfo{VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO};
        samplerInfo.magFilter = samplerInfo.minFilter = kernels[i].linear ? VK_FILTER_LINEAR : VK_FILTER_NEAREST;
        samplerInfo.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
        samplerInfo.addressModeU = samplerInfo.addressModeV = samplerInfo.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        samplerInfo.maxLod = 0;
        check(vkCreateSampler(device, &samplerInfo, nullptr, &samplers[i]), "vkCreateSampler");
        }
        VkDescriptorPoolSize sizes[] = {{VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, MP_MAX_CHAIN_STAGES}, {VK_DESCRIPTOR_TYPE_SAMPLER, MP_MAX_CHAIN_STAGES}, {VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, MP_MAX_CHAIN_STAGES}};
        VkDescriptorPoolCreateInfo poolInfo{VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO};
        poolInfo.maxSets = MP_MAX_CHAIN_STAGES; poolInfo.poolSizeCount = 3; poolInfo.pPoolSizes = sizes;
        check(vkCreateDescriptorPool(device, &poolInfo, nullptr, &descriptorPool), "vkCreateDescriptorPool");
        VkDescriptorSetAllocateInfo allocation{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO};
        VkDescriptorSetLayout layouts[MP_MAX_CHAIN_STAGES];
        std::fill_n(layouts, size_t(MP_MAX_CHAIN_STAGES), descriptors);
        allocation.descriptorPool = descriptorPool; allocation.descriptorSetCount = MP_MAX_CHAIN_STAGES; allocation.pSetLayouts = layouts;
        check(vkAllocateDescriptorSets(device, &allocation, descriptorSets), "vkAllocateDescriptorSets");
        VkCommandPoolCreateInfo commands{VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO};
        commands.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT; commands.queueFamilyIndex = family;
        check(vkCreateCommandPool(device, &commands, nullptr, &commandPool), "vkCreateCommandPool");
        VkCommandBufferAllocateInfo commandInfo{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO};
        commandInfo.commandPool = commandPool; commandInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY; commandInfo.commandBufferCount = 1;
        check(vkAllocateCommandBuffers(device, &commandInfo, &command), "vkAllocateCommandBuffers");
        VkFenceCreateInfo fenceInfo{VK_STRUCTURE_TYPE_FENCE_CREATE_INFO};
        check(vkCreateFence(device, &fenceInfo, nullptr, &fence), "vkCreateFence");
    }
    std::unique_ptr<Buffer> buffer(VkDeviceSize size, VkBufferUsageFlags usage) {
        auto result = std::make_unique<Buffer>(); result->device = device;
        VkBufferCreateInfo info{VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO};
        info.size = size; info.usage = usage;
        check(vkCreateBuffer(device, &info, nullptr, &result->handle), "vkCreateBuffer");
        VkMemoryRequirements requirements;
        vkGetBufferMemoryRequirements(device, result->handle, &requirements);
        VkMemoryAllocateInfo allocation{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO};
        allocation.allocationSize = requirements.size;
        allocation.memoryTypeIndex = memoryType(requirements.memoryTypeBits, VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);
        check(vkAllocateMemory(device, &allocation, nullptr, &result->memory), "vkAllocateMemory(buffer)");
        check(vkBindBufferMemory(device, result->handle, result->memory, 0), "vkBindBufferMemory");
        check(vkMapMemory(device, result->memory, 0, VK_WHOLE_SIZE, 0, &result->mapped), "vkMapMemory");
        return result;
    }
    void resize(uint32_t index, uint32_t width, uint32_t height, uint32_t outWidth, uint32_t outHeight) {
        auto& frame = frames[index];
        const auto descriptorSet = descriptorSets[index];
        if (frame && frame->width == width && frame->height == height && frame->outWidth == outWidth && frame->outHeight == outHeight) return;
        auto next = std::make_unique<Frame>(); next->device = device;
        next->width = width; next->height = height; next->outWidth = outWidth; next->outHeight = outHeight;
        if (index == 0) next->upload = buffer(uint64_t(width) * height * 4, VK_BUFFER_USAGE_TRANSFER_SRC_BIT);
        next->output = buffer(uint64_t(outWidth) * outHeight * 4, VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_SRC_BIT);
        VkImageCreateInfo image{VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO};
        image.imageType = VK_IMAGE_TYPE_2D; image.format = VK_FORMAT_R8G8B8A8_UNORM;
        image.extent = {width, height, 1}; image.mipLevels = image.arrayLayers = 1;
        image.samples = VK_SAMPLE_COUNT_1_BIT; image.tiling = VK_IMAGE_TILING_OPTIMAL;
        image.usage = VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_SAMPLED_BIT;
        check(vkCreateImage(device, &image, nullptr, &next->image), "vkCreateImage");
        VkMemoryRequirements requirements;
        vkGetImageMemoryRequirements(device, next->image, &requirements);
        VkMemoryAllocateInfo allocation{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO};
        allocation.allocationSize = requirements.size; allocation.memoryTypeIndex = memoryType(requirements.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
        check(vkAllocateMemory(device, &allocation, nullptr, &next->memory), "vkAllocateMemory(image)");
        check(vkBindImageMemory(device, next->image, next->memory, 0), "vkBindImageMemory");
        VkImageViewCreateInfo viewInfo{VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO};
        viewInfo.image = next->image; viewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D; viewInfo.format = image.format;
        viewInfo.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
        check(vkCreateImageView(device, &viewInfo, nullptr, &next->view), "vkCreateImageView");
        VkDescriptorImageInfo imageInfo{VK_NULL_HANDLE, next->view, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL};
        VkDescriptorImageInfo samplerInfo{samplers[0], VK_NULL_HANDLE, VK_IMAGE_LAYOUT_UNDEFINED};
        VkDescriptorBufferInfo outputInfo{next->output->handle, 0, VK_WHOLE_SIZE};
        VkWriteDescriptorSet writes[] = {
            {VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET, nullptr, descriptorSet, 0, 0, 1, VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, &imageInfo, nullptr, nullptr},
            {VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET, nullptr, descriptorSet, 1, 0, 1, VK_DESCRIPTOR_TYPE_SAMPLER, &samplerInfo, nullptr, nullptr},
            {VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET, nullptr, descriptorSet, 2, 0, 1, VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, nullptr, &outputInfo, nullptr}
        };
        vkUpdateDescriptorSets(device, 3, writes, 0, nullptr);
        frame = std::move(next);
    }
    void validate(uint32_t width, uint32_t height, const mp_effect_step& step) const {
        const auto outWidth = step.output_width, outHeight = step.output_height;
        if (!width || !height || !outWidth || !outHeight || (step.effect>5 && !isGraphEffect(step.effect)) ||
            !std::isfinite(step.parameter1) || (!(isGraphEffect(step.effect) && multiEffect(step.effect)->parameterCount && !multiEffect(step.effect)->legacyExtra) && (step.parameter1<0 || step.parameter1>1)) ||
            !std::isfinite(step.parameter2) || (!(isGraphEffect(step.effect) && multiEffect(step.effect)->parameterCount>1 && !multiEffect(step.effect)->legacyExtra) && (step.parameter2 < 0 || step.parameter2 > 1)))
            throw std::runtime_error("Invalid effect chain dimensions or parameters");
        // EASU computes the source-coordinate floor with signed 32-bit integers.
        if (step.effect == 4 && (width > 32768 || height > 32768 || outWidth > 32768 || outHeight > 32768))
            throw std::runtime_error("EASU dimensions exceed the exact coordinate limit of 32768");
        if (isGraphEffect(step.effect) && multiEffect(step.effect)->scaleFactor && (uint64_t(width)*multiEffect(step.effect)->scaleFactor!=outWidth || uint64_t(height)*multiEffect(step.effect)->scaleFactor!=outHeight))
            throw std::runtime_error("This effect requires its declared fixed input-relative output dimensions");
        if (!isGraphEffect(step.effect) && kernels[kernelIndex(step.effect)].sameSize && (width != outWidth || height != outHeight))
            throw std::runtime_error("This effect must retain its input dimensions");
        if (width > properties.limits.maxImageDimension2D || height > properties.limits.maxImageDimension2D ||
            outWidth > properties.limits.maxImageDimension2D || outHeight > properties.limits.maxImageDimension2D ||
            uint64_t(width) * height > 16777216 || uint64_t(outWidth) * outHeight > 16777216 ||
            uint64_t(outWidth) * outHeight * 4 > properties.limits.maxStorageBufferRange ||
            (uint64_t(outWidth) + 7) / 8 > properties.limits.maxComputeWorkGroupCount[0] ||
            (uint64_t(outHeight) + 7) / 8 > properties.limits.maxComputeWorkGroupCount[1])
            throw std::runtime_error("Effect chain dimensions exceed Vulkan device or 16-megapixel limit");
    }
    void chain(const uint8_t* input, uint32_t width, uint32_t height, uint8_t* output,
               uint64_t outputBytes, const mp_effect_step* steps, uint32_t count, const float* extra=nullptr) {
        std::lock_guard lock(mutex);
        if (!input || !output || !steps || count == 0 || count > MP_MAX_CHAIN_STAGES)
            throw std::runtime_error("An effect chain must contain 1 to 17 stages and valid buffers");
        // Validate the whole request before allocating or writing output.
        auto iw = width, ih = height;
        uint64_t pixels = 0;
        for (uint32_t i = 0; i < count; ++i) {
            validate(iw, ih, steps[i]);
            if(isGraphEffect(steps[i].effect))MultiRenderer::settings(*multiEffect(steps[i].effect),iw,ih,steps[i].output_width,steps[i].output_height,steps[i].parameter1,steps[i].parameter2,extra?extra+i*16:nullptr);
            pixels += uint64_t(iw) * ih + uint64_t(steps[i].output_width) * steps[i].output_height;
            if(isGraphEffect(steps[i].effect))pixels+=MultiRenderer::resourceUnits(steps[i].effect,iw,ih,steps[i].output_width,steps[i].output_height,properties.limits.maxImageDimension2D);
            iw = steps[i].output_width; ih = steps[i].output_height;
        }
        if (pixels > 67108864) throw std::runtime_error("Effect chain exceeds aggregate 64-megapixel resource limit");
        const uint64_t required = uint64_t(iw) * ih * 4;
        if (outputBytes < required) throw std::runtime_error("Effect chain output buffer is too small");
        if(snapshotSerial==UINT64_MAX)throw std::runtime_error("Stage snapshot serial exhausted");
        snapshotCount=0;
        iw = width; ih = height;
        for (uint32_t i = 0; i < count; ++i) {
            resize(i, iw, ih, steps[i].output_width, steps[i].output_height);
            if(isGraphEffect(steps[i].effect)) {
                auto& renderer=graphRenderer(steps[i].effect);
                auto& frame=frames[i];
                if(!frame->multi || frame->multi->effect!=steps[i].effect) {
                    frame->multi=renderer.create(steps[i].effect,iw,ih,steps[i].output_width,steps[i].output_height);
                    renderer.configure(i,*frame->multi,frame->view,frame->output->handle);
                }
            } else {
                frames[i]->multi.reset();
                VkDescriptorImageInfo sampler{samplers[kernelIndex(steps[i].effect)], VK_NULL_HANDLE, VK_IMAGE_LAYOUT_UNDEFINED};
                VkWriteDescriptorSet write{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET, nullptr, descriptorSets[i], 1, 0, 1, VK_DESCRIPTOR_TYPE_SAMPLER, &sampler, nullptr, nullptr};
                vkUpdateDescriptorSets(device, 1, &write, 0, nullptr);
            }
            iw = steps[i].output_width; ih = steps[i].output_height;
        }
        for (uint32_t i = count; i < MP_MAX_CHAIN_STAGES; ++i) frames[i].reset();
        // Keep live-chain renderers and a small recent cache, rather than
        // retaining every large model ever selected in a long-running session.
        if(multi.size()>MP_MAX_CHAIN_STAGES) {
            for(auto it=multi.begin();it!=multi.end();) {
                bool active=false;
                for(const auto& frame:frames)if(frame && frame->multi && frame->multi->effect==it->first) { active=true;break; }
                if(active)++it;else it=multi.erase(it);
            }
        }
        std::memcpy(frames[0]->upload->mapped, input, size_t(width) * height * 4);
        check(vkResetCommandBuffer(command, 0), "vkResetCommandBuffer");
        VkCommandBufferBeginInfo begin{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};
        begin.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
        check(vkBeginCommandBuffer(command, &begin), "vkBeginCommandBuffer");
        uint32_t passCounts[MP_MAX_CHAIN_STAGES]{};for(uint32_t i=0;i<count;i++)passCounts[i]=isGraphEffect(steps[i].effect)?multiEffect(steps[i].effect)->count:1;
        profiler.begin(command,count,steps,passCounts);
        for (uint32_t i = 0; i < count; ++i) {
            const auto& frame = frames[i];
            const auto& step = steps[i];
            VkImageMemoryBarrier imageBarrier{VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER};
            imageBarrier.oldLayout = VK_IMAGE_LAYOUT_UNDEFINED; imageBarrier.newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
            imageBarrier.srcQueueFamilyIndex = imageBarrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            imageBarrier.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
            imageBarrier.image = frame->image; imageBarrier.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
            vkCmdPipelineBarrier(command, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0, nullptr, 0, nullptr, 1, &imageBarrier);
            VkBufferImageCopy copy{};
            copy.imageSubresource = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1}; copy.imageExtent = {frame->width, frame->height, 1};
            const VkBuffer source = i == 0 ? frame->upload->handle : frames[i-1]->output->handle;
            vkCmdCopyBufferToImage(command, source, frame->image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &copy);
            imageBarrier.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL; imageBarrier.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
            imageBarrier.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT; imageBarrier.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
            vkCmdPipelineBarrier(command, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, 0, nullptr, 0, nullptr, 1, &imageBarrier);
            if(isGraphEffect(step.effect))graphRenderer(step.effect).record(command,i,*frame->multi,frame->width,frame->height,step.output_width,step.output_height,step.parameter1,step.parameter2,extra?extra+i*16:nullptr,&profiler);
            else {
            struct { uint32_t width, height, outWidth, outHeight; float parameter1; uint32_t mode; float parameter2; float reserved; float extra[16]; }
                settings{frame->width, frame->height, step.output_width, step.output_height, step.parameter1, step.effect, step.parameter2,0,{}};
            vkCmdBindPipeline(command, VK_PIPELINE_BIND_POINT_COMPUTE, pipelines[kernelIndex(step.effect)]);
            vkCmdBindDescriptorSets(command, VK_PIPELINE_BIND_POINT_COMPUTE, layout, 0, 1, &descriptorSets[i], 0, nullptr);
            vkCmdPushConstants(command, layout, VK_SHADER_STAGE_COMPUTE_BIT, 0, sizeof(settings), &settings);
            const auto block = kernels[kernelIndex(step.effect)].block;
            vkCmdDispatch(command, (step.output_width + block - 1) / block, (step.output_height + block - 1) / block, 1);
            profiler.pass(command);
            }
            VkMemoryBarrier barrier{VK_STRUCTURE_TYPE_MEMORY_BARRIER};
            barrier.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT;
            barrier.dstAccessMask = VK_ACCESS_HOST_READ_BIT | (i + 1 < count ? VK_ACCESS_TRANSFER_READ_BIT : 0);
            vkCmdPipelineBarrier(command, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                VK_PIPELINE_STAGE_HOST_BIT | (i + 1 < count ? VK_PIPELINE_STAGE_TRANSFER_BIT : 0), 0, 1, &barrier, 0, nullptr, 0, nullptr);
        }
        profiler.finishRecording(command);
        check(vkEndCommandBuffer(command), "vkEndCommandBuffer");
        check(vkResetFences(device, 1, &fence), "vkResetFences");
        VkSubmitInfo submit{VK_STRUCTURE_TYPE_SUBMIT_INFO};
        submit.commandBufferCount = 1; submit.pCommandBuffers = &command;
        check(vkQueueSubmit(queue, 1, &submit, fence), "vkQueueSubmit");
        check(vkWaitForFences(device, 1, &fence, VK_TRUE, UINT64_MAX), "vkWaitForFences");
        for(uint32_t i=0;i<count;i++)if(frames[i]->multi)for(auto& texture:frames[i]->multi->textures)if(texture->source) { texture->uploaded=true;texture->upload.reset(); }
        std::memcpy(output, frames[count-1]->output->mapped, size_t(required));
        profiler.complete();
        ++snapshotSerial;snapshotCount=count;
        for(uint32_t i=0;i<count;i++)snapshotEffects[i]=steps[i].effect;
        std::copy_n(steps,count,snapshotSteps);
        std::fill_n(snapshotExtra,MP_MAX_CHAIN_STAGES*16,0.f);
        if(extra)std::copy_n(extra,count*16,snapshotExtra);
    }

    mp_pass_snapshot_info passSnapshot(uint32_t stage,uint32_t pass,uint32_t output) const {
        if(stage>=snapshotCount)throw std::runtime_error("No successful output for this effect");
        const auto& frame=*frames[stage];const auto* graph=multiEffect(snapshotEffects[stage]);
        uint32_t count=graph?graph->count:1;
        if(pass>=count)throw std::runtime_error("Invalid export pass");
        mp_pass_snapshot_info info{snapshotSerial,0,stage,pass,output,0,count,UINT32_MAX,1,frame.outWidth,frame.outHeight,0};
        if(!graph||pass+1==count) {
            if(output)throw std::runtime_error("Final pass has only one output");
            info.output_count=1;info.bytes=uint64_t(info.width)*info.height*4;return info;
        }
        const auto& kernel=multiPasses[graph->first+pass];
        for(int texture:kernel.outputs)if(texture>=0)++info.output_count;
        if(output>=info.output_count)throw std::runtime_error("Invalid pass output");
        info.texture=uint32_t(kernel.outputs[output]);
        const auto& image=*frame.multi->textures[info.texture];const auto& spec=graph->specs[info.texture];
        if(image.source)throw std::runtime_error("A SOURCE texture is not a pass output");
        info.format=spec.format;info.width=image.width;info.height=image.height;info.bytes=uint64_t(info.width)*info.height*spec.bytes;
        if(!info.bytes||info.bytes>128ull*1024*1024)throw std::runtime_error("Pass export exceeds the 128 MiB readback limit");
        return info;
    }
    void copyPass(const mp_pass_snapshot_info& info,uint8_t* pixels) {
        const auto& frame=*frames[info.stage];
        if(info.texture==UINT32_MAX) { std::memcpy(pixels,frame.output->mapped,size_t(info.bytes));return; }
        auto staging=buffer(info.bytes,VK_BUFFER_USAGE_TRANSFER_DST_BIT);
        check(vkResetCommandBuffer(command,0),"export command reset");
        VkCommandBufferBeginInfo begin{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};begin.flags=VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
        check(vkBeginCommandBuffer(command,&begin),"export command begin");
        const auto& step=snapshotSteps[info.stage];
        graphRenderer(step.effect).record(command,info.stage,*frame.multi,frame.width,frame.height,frame.outWidth,frame.outHeight,
            step.parameter1,step.parameter2,snapshotExtra+info.stage*16,nullptr,info.pass+1);
        const auto& image=*frame.multi->textures[info.texture];
        VkImageMemoryBarrier before{VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER};before.oldLayout=VK_IMAGE_LAYOUT_GENERAL;before.newLayout=VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
        before.srcQueueFamilyIndex=before.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED;before.srcAccessMask=VK_ACCESS_SHADER_WRITE_BIT;before.dstAccessMask=VK_ACCESS_TRANSFER_READ_BIT;
        before.image=image.image;before.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1};
        vkCmdPipelineBarrier(command,VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,VK_PIPELINE_STAGE_TRANSFER_BIT,0,0,nullptr,0,nullptr,1,&before);
        VkBufferImageCopy region{};region.imageSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,0,1};region.imageExtent={info.width,info.height,1};
        vkCmdCopyImageToBuffer(command,image.image,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,staging->handle,1,&region);
        VkMemoryBarrier host{VK_STRUCTURE_TYPE_MEMORY_BARRIER};host.srcAccessMask=VK_ACCESS_TRANSFER_WRITE_BIT;host.dstAccessMask=VK_ACCESS_HOST_READ_BIT;
        before.oldLayout=VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;before.newLayout=VK_IMAGE_LAYOUT_GENERAL;before.srcAccessMask=VK_ACCESS_TRANSFER_READ_BIT;before.dstAccessMask=VK_ACCESS_SHADER_READ_BIT|VK_ACCESS_SHADER_WRITE_BIT;
        vkCmdPipelineBarrier(command,VK_PIPELINE_STAGE_TRANSFER_BIT,VK_PIPELINE_STAGE_HOST_BIT|VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,0,1,&host,0,nullptr,1,&before);
        check(vkEndCommandBuffer(command),"export command end");check(vkResetFences(device,1,&fence),"export fence reset");
        VkSubmitInfo submit{VK_STRUCTURE_TYPE_SUBMIT_INFO};submit.commandBufferCount=1;submit.pCommandBuffers=&command;
        check(vkQueueSubmit(queue,1,&submit,fence),"export submit");check(vkWaitForFences(device,1,&fence,VK_TRUE,UINT64_MAX),"export wait");
        std::memcpy(pixels,staging->mapped,size_t(info.bytes));
    }

};
}
extern "C" const char* mp_last_error() { return magpie::error.c_str(); }
extern "C" void* mp_create() {
    try { auto engine = std::make_unique<Engine>(); engine->initialize(); return engine.release(); }
    catch (const std::exception& ex) { magpie::error = ex.what(); return nullptr; }
}
extern "C" void* mp_devices_create() {
    try {
        InstanceScope instance;auto result=std::make_unique<DeviceSnapshot>();
        for(const auto& device:enumerateDevices(instance.handle))result->push_back(device.info);
        return result.release();
    } catch(const std::exception& ex) { magpie::error=ex.what();return nullptr; }
}
extern "C" void mp_devices_destroy(void* devices) { delete static_cast<DeviceSnapshot*>(devices); }
extern "C" uint32_t mp_devices_count(void* devices) { return devices?uint32_t(static_cast<DeviceSnapshot*>(devices)->size()):0; }
extern "C" int mp_devices_get(void* devices,uint32_t index,mp_device_info* info) {
    try {
        if(!devices || !info || index>=static_cast<DeviceSnapshot*>(devices)->size())throw std::runtime_error("Invalid Vulkan device snapshot index or output");
        *info=(*static_cast<DeviceSnapshot*>(devices))[index];return 1;
    } catch(const std::exception& ex) { magpie::error=ex.what();return 0; }
}
extern "C" void* mp_create_for_device(const char* identifier) {
    try { auto engine=std::make_unique<Engine>();engine->initialize(identifier);return engine.release(); }
    catch(const std::exception& ex) { magpie::error=ex.what();return nullptr; }
}
extern "C" const char* mp_device_identifier(void* engine) { return engine?static_cast<Engine*>(engine)->identifier.c_str():""; }
extern "C" void* mp_create_with_precision(const char* identifier,uint32_t precision) {
    try { auto engine=std::make_unique<Engine>();engine->initialize(identifier,precision);return engine.release(); }
    catch(const std::exception& ex) { magpie::error=ex.what();return nullptr; }
}
extern "C" int mp_fp16_supported(void* engine) { return engine && static_cast<Engine*>(engine)->fp16Supported; }
extern "C" int mp_effect_uses_fp16(void* engine,uint32_t effect) {
    if(!engine || !static_cast<Engine*>(engine)->fp16Enabled)return 0;
    if(const auto* graph=multiEffect(effect))return multiPasses[graph->first].halfData!=nullptr;
    return effect<std::size(kernels) && kernels[effect].halfData;
}
extern "C" void mp_destroy(void* engine) { delete static_cast<Engine*>(engine); }
extern "C" const char* mp_device_name(void* engine) { return engine ? static_cast<Engine*>(engine)->properties.deviceName : ""; }
extern "C" int mp_scale_ex(void* engine, const uint8_t* input, uint32_t width, uint32_t height, uint8_t* output, uint32_t outWidth, uint32_t outHeight, uint32_t mode, float strength, float parameter2) {
    try {
        if (!engine) throw std::runtime_error("Engine is not initialized");
        const mp_effect_step step{mode, outWidth, outHeight, strength, parameter2};
        static_cast<Engine*>(engine)->chain(input, width, height, output, uint64_t(outWidth) * outHeight * 4, &step, 1);
        return 1;
    } catch (const std::exception& ex) { magpie::error = ex.what(); return 0; }
}

extern "C" int mp_scale(void* engine, const uint8_t* input, uint32_t width, uint32_t height, uint8_t* output, uint32_t outWidth, uint32_t outHeight, uint32_t mode, float strength) {
    return mp_scale_ex(engine, input, width, height, output, outWidth, outHeight, mode, strength, 1.f/3.f);
}

extern "C" int mp_scale_chain(void* engine, const uint8_t* input, uint32_t width, uint32_t height,
                                uint8_t* output, uint64_t outputBytes, const mp_effect_step* steps, uint32_t count) {
    try {
        if (!engine || count>16) throw std::runtime_error("Invalid legacy chain engine or stage count");
        static_cast<Engine*>(engine)->chain(input, width, height, output, outputBytes, steps, count);
        return 1;
    } catch (const std::exception& ex) { magpie::error = ex.what(); return 0; }
}

extern "C" int mp_scale_chain_configured_v2(void* engine,const uint8_t* input,uint32_t width,uint32_t height,
                                        uint8_t* output,uint64_t outputBytes,const mp_effect_config* stages,uint32_t count) {
    try {
        if(!engine || !stages || count==0 || count>MP_MAX_CHAIN_STAGES)throw std::runtime_error("Invalid configured effect chain");
        mp_effect_step steps[MP_MAX_CHAIN_STAGES]{};float extra[MP_MAX_CHAIN_STAGES*16]{};
        for(uint32_t i=0;i<count;i++) { steps[i]=stages[i].step;std::copy_n(stages[i].extra,16,extra+i*16); }
        static_cast<Engine*>(engine)->chain(input,width,height,output,outputBytes,steps,count,extra);
        return 1;
    } catch(const std::exception& ex) { magpie::error=ex.what();return 0; }
}

extern "C" int mp_scale_chain_configured(void* engine,const uint8_t* input,uint32_t width,uint32_t height,
    uint8_t* output,uint64_t outputBytes,const mp_effect_config* stages,uint32_t count) {
    if(count>16) { magpie::error="Legacy configured chain exceeds sixteen stages";return 0; }
    return mp_scale_chain_configured_v2(engine,input,width,height,output,outputBytes,stages,count);
}

extern "C" int mp_stage_snapshot(void* engine,uint32_t index,mp_stage_snapshot_info* output,uint32_t size) {
    try {
        if(!engine||!output||size!=sizeof(*output))throw std::runtime_error("Invalid stage snapshot arguments");
        auto& target=*static_cast<Engine*>(engine);std::lock_guard lock(target.mutex);
        if(index>=target.snapshotCount)throw std::runtime_error("No successful output for this stage");
        const auto& frame=*target.frames[index];
        *output={target.snapshotSerial,index,target.snapshotCount,target.snapshotEffects[index],frame.outWidth,frame.outHeight,0};return 1;
    }catch(const std::exception& error){magpie::error=error.what();return 0;}
}
extern "C" int mp_copy_stage_snapshot(void* engine,uint64_t serial,uint32_t index,uint8_t* rgba,uint64_t bytes) {
    try {
        if(!engine||!rgba)throw std::runtime_error("Invalid stage snapshot buffers");
        auto& target=*static_cast<Engine*>(engine);std::lock_guard lock(target.mutex);
        if(!serial||serial!=target.snapshotSerial||index>=target.snapshotCount)throw std::runtime_error("Stage snapshot is unavailable or belongs to a different execution");
        const auto& frame=*target.frames[index];
        if(bytes!=uint64_t(frame.outWidth)*frame.outHeight*4)throw std::runtime_error("Stage snapshot output size differs");
        std::memcpy(rgba,frame.output->mapped,size_t(bytes));return 1;
    }catch(const std::exception& error){magpie::error=error.what();return 0;}
}

extern "C" int mp_probe_stage_readback(void* engine,uint64_t serial,uint32_t index,uint8_t* rgba,uint64_t bytes) {
    try {
        if(!engine||!rgba)throw std::runtime_error("Invalid readback probe arguments");
        auto& target=*static_cast<Engine*>(engine);std::lock_guard lock(target.mutex);
        if(!serial||serial!=target.snapshotSerial||index>=target.snapshotCount)throw std::runtime_error("Readback probe requires the current completed frame");
        const auto& frame=*target.frames[index];
        if(bytes!=uint64_t(frame.outWidth)*frame.outHeight*4)throw std::runtime_error("Readback probe output size differs");
        auto readback=target.buffer(bytes,VK_BUFFER_USAGE_TRANSFER_DST_BIT);
        check(vkResetCommandBuffer(target.command,0),"probe reset command buffer");
        VkCommandBufferBeginInfo begin{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};begin.flags=VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
        check(vkBeginCommandBuffer(target.command,&begin),"probe begin command buffer");
        VkMemoryBarrier source{VK_STRUCTURE_TYPE_MEMORY_BARRIER};source.srcAccessMask=VK_ACCESS_SHADER_WRITE_BIT;source.dstAccessMask=VK_ACCESS_TRANSFER_READ_BIT;
        vkCmdPipelineBarrier(target.command,VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,VK_PIPELINE_STAGE_TRANSFER_BIT,0,1,&source,0,nullptr,0,nullptr);
        VkBufferCopy region{0,0,bytes};vkCmdCopyBuffer(target.command,frame.output->handle,readback->handle,1,&region);
        VkMemoryBarrier host{VK_STRUCTURE_TYPE_MEMORY_BARRIER};host.srcAccessMask=VK_ACCESS_TRANSFER_WRITE_BIT;host.dstAccessMask=VK_ACCESS_HOST_READ_BIT;
        vkCmdPipelineBarrier(target.command,VK_PIPELINE_STAGE_TRANSFER_BIT,VK_PIPELINE_STAGE_HOST_BIT,0,1,&host,0,nullptr,0,nullptr);
        check(vkEndCommandBuffer(target.command),"probe end command buffer");check(vkResetFences(target.device,1,&target.fence),"probe reset fence");
        VkSubmitInfo submit{VK_STRUCTURE_TYPE_SUBMIT_INFO};submit.commandBufferCount=1;submit.pCommandBuffers=&target.command;
        check(vkQueueSubmit(target.queue,1,&submit,target.fence),"probe submit copy");
        check(vkWaitForFences(target.device,1,&target.fence,VK_TRUE,UINT64_MAX),"probe wait copy");
        std::memcpy(rgba,readback->mapped,size_t(bytes));return 1;
    }catch(const std::exception& error){magpie::error=error.what();return 0;}
}

extern "C" int mp_pass_snapshot(void* engine,uint32_t stage,uint32_t pass,uint32_t output,mp_pass_snapshot_info* info,uint32_t size) {
    try {
        if(!engine||!info||size!=sizeof(*info))throw std::runtime_error("Invalid pass snapshot arguments");
        auto& target=*static_cast<Engine*>(engine);std::lock_guard lock(target.mutex);*info=target.passSnapshot(stage,pass,output);return 1;
    }catch(const std::exception& error){magpie::error=error.what();return 0;}
}
extern "C" int mp_copy_pass_snapshot(void* engine,uint64_t serial,uint32_t stage,uint32_t pass,uint32_t output,uint8_t* pixels,uint64_t bytes) {
    try {
        if(!engine||!pixels)throw std::runtime_error("Invalid pass snapshot buffer");
        auto& target=*static_cast<Engine*>(engine);std::lock_guard lock(target.mutex);const auto info=target.passSnapshot(stage,pass,output);
        if(!serial||info.serial!=serial||bytes!=info.bytes)throw std::runtime_error("Pass snapshot serial or output length differs");
        target.copyPass(info,pixels);return 1;
    }catch(const std::exception& error){magpie::error=error.what();return 0;}
}

extern "C" int mp_set_gpu_profiling(void* engine,int enabled) {
    try {
        if(!engine || (enabled!=0 && enabled!=1))throw std::runtime_error("Invalid engine or GPU profiler flag");
        auto& target=*static_cast<Engine*>(engine);std::lock_guard lock(target.mutex);target.profiler.setEnabled(enabled!=0);return 1;
    } catch(const std::exception& ex) { magpie::error=ex.what();return 0; }
}
extern "C" int mp_get_gpu_profile_v2(void* engine,mp_gpu_profile_v2* output) {
    try {
        if(!engine || !output)throw std::runtime_error("Invalid engine or GPU profile output");
        auto& target=*static_cast<Engine*>(engine);std::lock_guard lock(target.mutex);*output=target.profiler.latest;return 1;
    } catch(const std::exception& ex) { magpie::error=ex.what();return 0; }
}

extern "C" int mp_get_gpu_profile(void* engine,mp_gpu_profile* output) {
    if(!output) { magpie::error="Invalid GPU profile output";return 0; }
    mp_gpu_profile_v2 sample{};if(!mp_get_gpu_profile_v2(engine,&sample))return 0;
    *output={};output->supported=sample.supported;output->enabled=sample.enabled;output->status=sample.status;
    output->timestamp_bits=sample.timestamp_bits;output->timestamp_period_ns=sample.timestamp_period_ns;output->frame_sequence=sample.frame_sequence;
    if(sample.stage_count>MP_GPU_MAX_STAGES) { output->status=3;return 1; }
    output->stage_count=sample.stage_count;output->pass_count=sample.pass_count;output->total_ms=sample.total_ms;
    std::copy_n(sample.effect_ids,size_t(MP_GPU_MAX_STAGES),output->effect_ids);std::copy_n(sample.first_pass,size_t(MP_GPU_MAX_STAGES),output->first_pass);
    std::copy_n(sample.pass_counts,size_t(MP_GPU_MAX_STAGES),output->pass_counts);std::copy_n(sample.stage_ms,size_t(MP_GPU_MAX_STAGES),output->stage_ms);
    std::copy_n(sample.pass_ms,size_t(MP_GPU_MAX_PASSES),output->pass_ms);return 1;
}
