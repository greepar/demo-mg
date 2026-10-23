#pragma once
// GPU intermediate images retain each model's original half or UNORM storage.
struct MultiImage {
    bool source{},uploaded{};std::unique_ptr<Buffer> upload;
    uint32_t width{},height{};VkDevice device{};VkImage image{};VkDeviceMemory memory{};VkImageView view{};
    ~MultiImage() { if(view)vkDestroyImageView(device,view,nullptr);if(image)vkDestroyImage(device,image,nullptr);if(memory)vkFreeMemory(device,memory,nullptr); }
};
struct MultiFrame { uint32_t effect{};std::vector<std::unique_ptr<MultiImage>> textures; };
struct MultiSettings {
    uint32_t width,height,outWidth,outHeight;
    float p1;uint32_t mode;float p2,reserved;
    float extra[16];
};
static_assert(sizeof(MultiSettings)==96 && offsetof(MultiSettings,extra)==32);
struct MultiRenderer {
    bool fp16{};
    const MultiEffectKernel* definition{};
    uint32_t sampledCount{},storageCount{};
    VkDevice device{};VkPhysicalDevice physical{};VkPhysicalDeviceProperties properties{};VkPhysicalDeviceMemoryProperties memories{};
    VkDescriptorSetLayout descriptors{};VkPipelineLayout layout{};VkDescriptorPool pool{};
    VkShaderModule shaders[std::size(multiPasses)]{};VkPipeline pipelines[std::size(multiPasses)]{};
    VkSampler samplers[2]{};std::vector<VkDescriptorSet> sets;
    ~MultiRenderer() {
        if(pool)vkDestroyDescriptorPool(device,pool,nullptr);
        for(auto pipeline:pipelines)if(pipeline)vkDestroyPipeline(device,pipeline,nullptr);
        for(auto shader:shaders)if(shader)vkDestroyShaderModule(device,shader,nullptr);
        for(auto sampler:samplers)if(sampler)vkDestroySampler(device,sampler,nullptr);
        if(layout)vkDestroyPipelineLayout(device,layout,nullptr);
        if(descriptors)vkDestroyDescriptorSetLayout(device,descriptors,nullptr);
    }
    static VkDescriptorType descriptorType(uint32_t i) {
        return i<multiSampledCount ? VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE : i<multiStorageBinding ? VK_DESCRIPTOR_TYPE_SAMPLER : i<multiOutputBinding ? VK_DESCRIPTOR_TYPE_STORAGE_IMAGE : VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
    }
    bool activeBinding(uint32_t i) const {
        return i<sampledCount || (i>=multiSamplerBinding && i<multiSamplerBinding+2) ||
            (i>=multiStorageBinding && i<multiStorageBinding+storageCount) || i==multiOutputBinding;
    }
    void initialize(VkDevice target,VkPhysicalDevice physicalDevice,const MultiEffectKernel& effect,bool useHalf=false) {
        fp16=useHalf;
        definition=&effect;sampledCount=effect.textures+1;storageCount=1;
        for(uint32_t p=effect.first;p<effect.first+effect.count;p++) {
            uint32_t outputs=0;for(int output:multiPasses[p].outputs)if(output>=0)outputs++;
            storageCount=std::max(storageCount,outputs);
        }
        device=target;physical=physicalDevice;vkGetPhysicalDeviceProperties(physicalDevice,&properties);vkGetPhysicalDeviceMemoryProperties(physicalDevice,&memories);
        const auto& limits=properties.limits;
        if(sampledCount>limits.maxPerStageDescriptorSampledImages || sampledCount>limits.maxDescriptorSetSampledImages ||
           storageCount>limits.maxPerStageDescriptorStorageImages || storageCount>limits.maxDescriptorSetStorageImages ||
           limits.maxPerStageDescriptorSamplers<2 || limits.maxDescriptorSetSamplers<2 ||
           limits.maxPerStageDescriptorStorageBuffers<1 || limits.maxDescriptorSetStorageBuffers<1 ||
           sampledCount+storageCount+3>limits.maxPerStageResources || sizeof(MultiSettings)>limits.maxPushConstantsSize)
            throw std::runtime_error("Effect graph descriptor or push constant requirements exceed GPU limits");
        std::vector<VkDescriptorSetLayoutBinding> bindings;
        for(uint32_t i=0;i<multiBindingCount;i++) {
            if(!activeBinding(i))continue;
            auto type=descriptorType(i);
            bindings.push_back({i,type,1,VK_SHADER_STAGE_COMPUTE_BIT,nullptr});
        }
        VkDescriptorSetLayoutCreateInfo descriptorInfo{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};descriptorInfo.bindingCount=uint32_t(bindings.size());descriptorInfo.pBindings=bindings.data();
        check(vkCreateDescriptorSetLayout(device,&descriptorInfo,nullptr,&descriptors),"multi descriptor layout");
        VkPushConstantRange range{VK_SHADER_STAGE_COMPUTE_BIT,0,sizeof(MultiSettings)};
        VkPipelineLayoutCreateInfo layoutInfo{VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO};layoutInfo.setLayoutCount=1;layoutInfo.pSetLayouts=&descriptors;layoutInfo.pushConstantRangeCount=1;layoutInfo.pPushConstantRanges=&range;
        check(vkCreatePipelineLayout(device,&layoutInfo,nullptr,&layout),"multi pipeline layout");
        for(uint32_t i=0;i<2;i++) {
            VkSamplerCreateInfo info{VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO};info.magFilter=info.minFilter=i ? VK_FILTER_LINEAR : VK_FILTER_NEAREST;
            info.addressModeU=info.addressModeV=info.addressModeW=VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
            check(vkCreateSampler(device,&info,nullptr,&samplers[i]),"multi sampler");
        }
        const uint32_t setCount=MP_MAX_CHAIN_STAGES*effect.count;sets.resize(setCount);
        VkDescriptorPoolSize sizes[]={{VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,setCount*sampledCount},{VK_DESCRIPTOR_TYPE_SAMPLER,setCount*2},{VK_DESCRIPTOR_TYPE_STORAGE_IMAGE,setCount*storageCount},{VK_DESCRIPTOR_TYPE_STORAGE_BUFFER,setCount}};
        VkDescriptorPoolCreateInfo poolInfo{VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO};poolInfo.maxSets=setCount;poolInfo.poolSizeCount=4;poolInfo.pPoolSizes=sizes;
        check(vkCreateDescriptorPool(device,&poolInfo,nullptr,&pool),"multi descriptor pool");
        std::vector<VkDescriptorSetLayout> layouts(setCount,descriptors);
        VkDescriptorSetAllocateInfo info{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO};info.descriptorPool=pool;info.descriptorSetCount=setCount;info.pSetLayouts=layouts.data();
        check(vkAllocateDescriptorSets(device,&info,sets.data()),"multi descriptor sets");
    }
    static VkFormat storageFormat(uint32_t code) {
        constexpr VkFormat formats[]={VK_FORMAT_R16G16B16A16_SFLOAT,VK_FORMAT_R8G8B8A8_UNORM,VK_FORMAT_R16_SFLOAT,VK_FORMAT_R16G16_SFLOAT,VK_FORMAT_R8_UNORM,VK_FORMAT_R8G8_UNORM};
        if(code>=std::size(formats))throw std::runtime_error("Unknown multi-pass texture format");
        return formats[code];
    }
    static MultiSettings settings(const MultiEffectKernel& effect,uint32_t width,uint32_t height,uint32_t outWidth,uint32_t outHeight,float p1,float p2,const float* extra) {
        MultiSettings values{width,height,outWidth,outHeight,p1,effect.id,p2,0,{}};
        for(uint32_t i=0;i<effect.parameterCount;i++) {
            const auto& parameter=effect.parameters[i];
            uint32_t slot=effect.legacyExtra?i:i>=2?i-2:0;
            float value=effect.legacyExtra || i>=2 ? (extra?extra[slot]:parameter.defaultValue) : i==0?p1:p2;
            if(!std::isfinite(value) || value<parameter.minimum || value>parameter.maximum || (parameter.integral && std::floor(value)!=value))
                throw std::runtime_error("Invalid multi-pass effect parameter range or integer value");
            if(effect.legacyExtra || i>=2)values.extra[slot]=value;
        }
        return values;
    }
    static uint64_t resourceUnits(uint32_t id,uint32_t width,uint32_t height,uint32_t outWidth,uint32_t outHeight,uint32_t limit) {
        const auto* effect=multiEffect(id);if(!effect)throw std::runtime_error("Unknown multi-pass effect");
        uint64_t bytes=effect->textures?0:4;const uint32_t dimensions[]={width,height,outWidth,outHeight,1};
        for(uint32_t i=0;i<effect->textures;i++) {
            const auto& spec=effect->specs[i];uint64_t w=uint64_t(dimensions[spec.widthAxis])*spec.widthFactor,h=uint64_t(dimensions[spec.heightAxis])*spec.heightFactor;
            if(!w || !h || w>limit || h>limit || w*h>16777216)throw std::runtime_error("Multi-pass intermediate exceeds device or 16-megapixel limit");
            bytes+=w*h*spec.bytes;
        }
        return (bytes+3)/4;
    }
    void prepare(const MultiEffectKernel& effect) {
        if(!definition || definition->id!=effect.id)throw std::runtime_error("Effect renderer/layout mismatch");
        for(uint32_t i=0;i<effect.textures;i++) {
            VkFormatProperties format{};vkGetPhysicalDeviceFormatProperties(physical,storageFormat(effect.specs[i].format),&format);
            constexpr VkFormatFeatureFlags needed=VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT|VK_FORMAT_FEATURE_STORAGE_IMAGE_BIT;
            if((format.optimalTilingFeatures&needed)!=needed)throw std::runtime_error("GPU does not support this multi-pass sampled/storage texture format");
        }
        for(uint32_t i=effect.first;i<effect.first+effect.count;i++) {
            if(pipelines[i])continue;
            const auto& kernel=multiPasses[i];uint64_t invocations=1;
            for(uint32_t axis=0;axis<3;axis++) {
                if(kernel.threads[axis]>properties.limits.maxComputeWorkGroupSize[axis])throw std::runtime_error("Effect thread dimensions exceed GPU limits");
                invocations*=kernel.threads[axis];
            }
            if(invocations>properties.limits.maxComputeWorkGroupInvocations)throw std::runtime_error("Effect thread count exceeds GPU limit");
            if(!shaders[i]) {
                bool useHalf=fp16 && kernel.halfData;
                VkShaderModuleCreateInfo shaderInfo{VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO};shaderInfo.codeSize=useHalf?kernel.halfSize:kernel.size;shaderInfo.pCode=useHalf?kernel.halfData:kernel.data;
                check(vkCreateShaderModule(device,&shaderInfo,nullptr,&shaders[i]),"multi shader module");
            }
            VkComputePipelineCreateInfo pipelineInfo{VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO};
            pipelineInfo.stage={VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,nullptr,0,VK_SHADER_STAGE_COMPUTE_BIT,shaders[i],"main",nullptr};pipelineInfo.layout=layout;
            check(vkCreateComputePipelines(device,VK_NULL_HANDLE,1,&pipelineInfo,nullptr,&pipelines[i]),"multi compute pipeline");
        }
    }
    std::unique_ptr<MultiFrame> create(uint32_t effect,uint32_t width,uint32_t height,uint32_t outWidth,uint32_t outHeight) {
        const auto& graph=*multiEffect(effect);resourceUnits(effect,width,height,outWidth,outHeight,properties.limits.maxImageDimension2D);prepare(graph);
        const uint32_t dimensions[]={width,height,outWidth,outHeight,1};
        auto frame=std::make_unique<MultiFrame>();frame->effect=effect;
        // Keep every descriptor binding valid after switching from a graph
        // with intermediates. This 1x1 image is descriptor padding, never read
        // or written by a graph that declares no intermediate textures.
        const MultiTextureKernel padding{1,4,1,4,1,4,nullptr,0};
        for(uint32_t i=0;i<std::max(1u,graph.textures);i++) {
            const auto& spec=graph.textures?graph.specs[i]:padding;
            auto texture=std::make_unique<MultiImage>();texture->device=device;texture->width=dimensions[spec.widthAxis]*spec.widthFactor;texture->height=dimensions[spec.heightAxis]*spec.heightFactor;
            VkImageCreateInfo info{VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO};info.imageType=VK_IMAGE_TYPE_2D;info.format=storageFormat(spec.format);
            info.extent={texture->width,texture->height,1};info.mipLevels=info.arrayLayers=1;info.samples=VK_SAMPLE_COUNT_1_BIT;info.tiling=VK_IMAGE_TILING_OPTIMAL;
            info.usage=VK_IMAGE_USAGE_STORAGE_BIT|VK_IMAGE_USAGE_SAMPLED_BIT|(spec.source?VK_IMAGE_USAGE_TRANSFER_DST_BIT:0);
            check(vkCreateImage(device,&info,nullptr,&texture->image),"multi image");
            VkMemoryRequirements requirements{};vkGetImageMemoryRequirements(device,texture->image,&requirements);
            uint32_t memoryType=UINT32_MAX;
            for(uint32_t j=0;j<memories.memoryTypeCount;j++)if((requirements.memoryTypeBits&(1u<<j)) && (memories.memoryTypes[j].propertyFlags&VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT)) { memoryType=j;break; }
            if(memoryType==UINT32_MAX)throw std::runtime_error("No device-local CNN texture memory");
            VkMemoryAllocateInfo allocation{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO};allocation.allocationSize=requirements.size;allocation.memoryTypeIndex=memoryType;
            check(vkAllocateMemory(device,&allocation,nullptr,&texture->memory),"multi image allocation");check(vkBindImageMemory(device,texture->image,texture->memory,0),"multi image binding");
            VkImageViewCreateInfo view{VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO};view.image=texture->image;view.viewType=VK_IMAGE_VIEW_TYPE_2D;view.format=info.format;view.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1};
            check(vkCreateImageView(device,&view,nullptr,&texture->view),"multi image view");
            if(spec.source) {
                if(spec.sourceBytes!=uint64_t(texture->width)*texture->height*spec.bytes)throw std::runtime_error("Invalid embedded SOURCE payload");
                texture->source=true;texture->upload=std::make_unique<Buffer>();auto& upload=*texture->upload;upload.device=device;
                VkBufferCreateInfo bufferInfo{VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO};bufferInfo.size=spec.sourceBytes;bufferInfo.usage=VK_BUFFER_USAGE_TRANSFER_SRC_BIT;bufferInfo.sharingMode=VK_SHARING_MODE_EXCLUSIVE;
                check(vkCreateBuffer(device,&bufferInfo,nullptr,&upload.handle),"SOURCE upload buffer");
                VkMemoryRequirements required{};vkGetBufferMemoryRequirements(device,upload.handle,&required);
                uint32_t hostType=UINT32_MAX;
                for(uint32_t j=0;j<memories.memoryTypeCount;j++)if((required.memoryTypeBits&(1u<<j)) && (memories.memoryTypes[j].propertyFlags&(VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT|VK_MEMORY_PROPERTY_HOST_COHERENT_BIT))==(VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT|VK_MEMORY_PROPERTY_HOST_COHERENT_BIT)) { hostType=j;break; }
                if(hostType==UINT32_MAX)throw std::runtime_error("No coherent SOURCE staging memory");
                VkMemoryAllocateInfo host{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO};host.allocationSize=required.size;host.memoryTypeIndex=hostType;
                check(vkAllocateMemory(device,&host,nullptr,&upload.memory),"SOURCE staging allocation");
                check(vkBindBufferMemory(device,upload.handle,upload.memory,0),"SOURCE staging binding");
                check(vkMapMemory(device,upload.memory,0,VK_WHOLE_SIZE,0,&upload.mapped),"SOURCE staging map");
                std::memcpy(upload.mapped,spec.source,spec.sourceBytes);
            }
            frame->textures.push_back(std::move(texture));
        }
        return frame;
    }
    void configure(uint32_t index,const MultiFrame& frame,VkImageView input,VkBuffer output) {
        if(frame.textures.empty())throw std::runtime_error("Missing graph descriptor padding or intermediate images");
        const auto& effect=*multiEffect(frame.effect);
        for(uint32_t pass=0;pass<effect.count;pass++) {
            const auto& kernel=multiPasses[effect.first+pass];
            VkDescriptorImageInfo images[multiOutputBinding]{};
            images[0]={VK_NULL_HANDLE,input,VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL};
            for(uint32_t i=1;i<sampledCount;i++)images[i]={VK_NULL_HANDLE,frame.textures[std::min(size_t(i-1),frame.textures.size()-1)]->view,VK_IMAGE_LAYOUT_GENERAL};
            images[multiSamplerBinding]={samplers[0],VK_NULL_HANDLE,VK_IMAGE_LAYOUT_UNDEFINED};images[multiSamplerBinding+1]={samplers[1],VK_NULL_HANDLE,VK_IMAGE_LAYOUT_UNDEFINED};
            for(uint32_t slot=0;slot<storageCount;slot++)images[multiStorageBinding+slot]={VK_NULL_HANDLE,frame.textures[kernel.outputs[slot]<0?0:kernel.outputs[slot]]->view,VK_IMAGE_LAYOUT_GENERAL};
            VkDescriptorBufferInfo buffer{output,0,VK_WHOLE_SIZE};VkWriteDescriptorSet writes[multiBindingCount]{};
            uint32_t writeCount=0;
            for(uint32_t i=0;i<multiBindingCount;i++) {
                if(!activeBinding(i))continue;
                auto type=descriptorType(i);
                writes[writeCount++]={VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET,nullptr,sets[index*effect.count+pass],i,0,1,type,i<multiOutputBinding?&images[i]:nullptr,i==multiOutputBinding?&buffer:nullptr,nullptr};
            }
            vkUpdateDescriptorSets(device,writeCount,writes,0,nullptr);
        }
    }
    void record(VkCommandBuffer command,uint32_t index,const MultiFrame& frame,uint32_t width,uint32_t height,uint32_t outWidth,uint32_t outHeight,float parameter1,float parameter2,const float* extra,GpuProfiler* profiler=nullptr) {
        for(const auto& texture:frame.textures) {
            if(texture->source) {
                if(texture->uploaded)continue;
                VkImageMemoryBarrier copy{VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER};copy.oldLayout=VK_IMAGE_LAYOUT_UNDEFINED;copy.newLayout=VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
                copy.srcQueueFamilyIndex=copy.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED;copy.dstAccessMask=VK_ACCESS_TRANSFER_WRITE_BIT;
                copy.image=texture->image;copy.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1};
                vkCmdPipelineBarrier(command,VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,VK_PIPELINE_STAGE_TRANSFER_BIT,0,0,nullptr,0,nullptr,1,&copy);
                VkBufferImageCopy region{};region.imageSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,0,1};region.imageExtent={texture->width,texture->height,1};
                vkCmdCopyBufferToImage(command,texture->upload->handle,texture->image,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,1,&region);
                copy.oldLayout=VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;copy.newLayout=VK_IMAGE_LAYOUT_GENERAL;copy.srcAccessMask=VK_ACCESS_TRANSFER_WRITE_BIT;copy.dstAccessMask=VK_ACCESS_SHADER_READ_BIT;
                vkCmdPipelineBarrier(command,VK_PIPELINE_STAGE_TRANSFER_BIT,VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,0,0,nullptr,0,nullptr,1,&copy);
                continue;
            }
            VkImageMemoryBarrier barrier{VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER};barrier.oldLayout=VK_IMAGE_LAYOUT_UNDEFINED;barrier.newLayout=VK_IMAGE_LAYOUT_GENERAL;
            barrier.srcQueueFamilyIndex=barrier.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED;barrier.dstAccessMask=VK_ACCESS_SHADER_READ_BIT|VK_ACCESS_SHADER_WRITE_BIT;
            barrier.image=texture->image;barrier.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1};
            vkCmdPipelineBarrier(command,VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,0,0,nullptr,0,nullptr,1,&barrier);
        }
        const auto& effect=*multiEffect(frame.effect);
        auto push=settings(effect,width,height,outWidth,outHeight,parameter1,parameter2,extra);
        for(uint32_t pass=0;pass<effect.count;pass++) {
            const auto& kernel=multiPasses[effect.first+pass];
            vkCmdBindPipeline(command,VK_PIPELINE_BIND_POINT_COMPUTE,pipelines[effect.first+pass]);
            vkCmdBindDescriptorSets(command,VK_PIPELINE_BIND_POINT_COMPUTE,layout,0,1,&sets[index*effect.count+pass],0,nullptr);
            vkCmdPushConstants(command,layout,VK_SHADER_STAGE_COMPUTE_BIT,0,sizeof(push),&push);
            uint32_t dispatchWidth=kernel.final?outWidth:frame.textures[kernel.outputs[0]]->width;
            uint32_t dispatchHeight=kernel.final?outHeight:frame.textures[kernel.outputs[0]]->height;
            vkCmdDispatch(command,(dispatchWidth+kernel.blockX-1)/kernel.blockX,(dispatchHeight+kernel.blockY-1)/kernel.blockY,1);
            if(pass+1<effect.count) {
                VkMemoryBarrier barrier{VK_STRUCTURE_TYPE_MEMORY_BARRIER};barrier.srcAccessMask=VK_ACCESS_SHADER_WRITE_BIT;barrier.dstAccessMask=VK_ACCESS_SHADER_READ_BIT|VK_ACCESS_SHADER_WRITE_BIT;
                vkCmdPipelineBarrier(command,VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,0,1,&barrier,0,nullptr,0,nullptr);
            }
            if(profiler)profiler->pass(command);
        }
    }
};
