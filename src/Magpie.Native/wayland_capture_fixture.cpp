#include "magpie.h"
#include <gio/gio.h>
#include <pipewire/pipewire.h>
#include <spa/param/video/format-utils.h>
#include <spa/buffer/meta.h>
#include <atomic>
#include <chrono>
#include <cstring>
#include <filesystem>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>
#include <syncstream>
#include <thread>
#include <unistd.h>
#include <vector>

namespace {
void require(bool value,const char* message) { if(!value)throw std::runtime_error(message); }
void emit(const std::string& text) { std::osyncstream(std::cout)<<text<<std::endl; }
void guard() {
    const char* runtime=getenv("XDG_RUNTIME_DIR");
    require(getenv("MAGPIE_PIPEWIRE_FIXTURE_PRIVATE") && std::string(getenv("MAGPIE_PIPEWIRE_FIXTURE_PRIVATE"))=="1","Requires isolated fixture wrapper");
    require(runtime && std::filesystem::is_regular_file(std::filesystem::path(runtime)/"magpie-fixture-private"),"Missing private runtime marker");
    require(getenv("PIPEWIRE_REMOTE") && std::string(getenv("PIPEWIRE_REMOTE"))=="magpie-fixture","Refusing host PipeWire remote");
    require(!getenv("DISPLAY") && !getenv("WAYLAND_DISPLAY") && !getenv("WAYLAND_SOCKET"),"Refusing desktop-connected fixture");
}
struct LoopLock {
    pw_thread_loop* loop;
    explicit LoopLock(pw_thread_loop* value):loop(value){pw_thread_loop_lock(loop);}
    ~LoopLock(){pw_thread_loop_unlock(loop);}
};
struct Source {
    pw_thread_loop* loop{};pw_context* context{};pw_core* core{};pw_registry* registry{};pw_stream* stream{};pw_proxy* link{};pw_node* inputProxy{};
    spa_hook streamListener{},registryListener{},coreListener{},inputListener{};spa_source* timer{};
    uint32_t sourceNode=PW_ID_ANY,inputNode=PW_ID_ANY,sourcePort=PW_ID_ANY,inputPort=PW_ID_ANY;
    uint32_t width=37,height=23,actualWidth{},actualHeight{},colour=23,headerFlags{},pixelFormat=SPA_VIDEO_FORMAT_BGRA;
    uint64_t produced{},serial{};
    bool targetChecked{};std::atomic<bool> failed{};
    ~Source() {
        if(loop)pw_thread_loop_stop(loop);
        if(timer)pw_loop_destroy_source(pw_thread_loop_get_loop(loop),timer);
        if(link)pw_proxy_destroy(link);
        if(inputProxy){spa_hook_remove(&inputListener);pw_proxy_destroy(reinterpret_cast<pw_proxy*>(inputProxy));}
        if(stream){spa_hook_remove(&streamListener);pw_stream_destroy(stream);}
        if(registry){spa_hook_remove(&registryListener);pw_proxy_destroy(reinterpret_cast<pw_proxy*>(registry));}
        if(core){spa_hook_remove(&coreListener);pw_core_disconnect(core);}
        if(context)pw_context_destroy(context);
        if(loop)pw_thread_loop_destroy(loop);
    }
    void connectPorts() {
        if(link || !targetChecked || inputPort==PW_ID_ANY || sourcePort==PW_ID_ANY)return;
        auto* props=pw_properties_new(nullptr,nullptr);
        pw_properties_setf(props,PW_KEY_LINK_OUTPUT_PORT,"%u",sourcePort);
        pw_properties_setf(props,PW_KEY_LINK_INPUT_PORT,"%u",inputPort);
        link=static_cast<pw_proxy*>(pw_core_create_object(core,"link-factory",PW_TYPE_INTERFACE_Link,PW_VERSION_LINK,&props->dict,0));
        pw_properties_free(props);
        if(!link){failed=true;emit("ERROR link creation failed");}
    }
    void registryObject(uint32_t id,const char* type,const spa_dict* props) {
        if(!props)return;
        if(std::strcmp(type,PW_TYPE_INTERFACE_Node)==0) {
            const char* name=spa_dict_lookup(props,PW_KEY_NODE_NAME);
            const char* target=spa_dict_lookup(props,PW_KEY_TARGET_OBJECT);
            emit("NODE "+std::to_string(id)+" "+(name?name:"")+" target="+(target?target:""));
            if(name && std::string(name)=="magpie-fixture-source") {
                sourceNode=id;const char* value=spa_dict_lookup(props,PW_KEY_OBJECT_SERIAL);
                if(!value){failed=true;return;}serial=std::stoull(value);
                emit("READY "+std::to_string(id)+" "+std::to_string(serial));
            } else if(name && std::string(name)=="wayland_capture_fixture") {
                inputNode=id;
                inputProxy=static_cast<pw_node*>(pw_registry_bind(registry,id,PW_TYPE_INTERFACE_Node,PW_VERSION_NODE,0));
                if(!inputProxy){failed=true;emit("ERROR cannot inspect input node");return;}
                static const pw_node_events events=[] {pw_node_events e{};e.version=PW_VERSION_NODE_EVENTS;
                    e.info=[](void* p,const pw_node_info* info){auto& s=*static_cast<Source*>(p);if(!info->props || !(info->change_mask&PW_NODE_CHANGE_MASK_PROPS))return;
                        const char* target=spa_dict_lookup(info->props,PW_KEY_TARGET_OBJECT);
                        const char* legacy=spa_dict_lookup(info->props,"node.target");
                        emit("TARGET "+std::string(target?target:"")+" legacy="+(legacy?legacy:"")+" node="+std::to_string(s.inputNode));
                        const char* mode=getenv("MAGPIE_PIPEWIRE_FIXTURE_TARGET_MODE");
                        const bool serialMode=mode && std::string(mode)=="serial";
                        if(serialMode && (!target || std::string(target)!=std::to_string(s.serial))) {s.failed=true;emit("ERROR serial target mismatch");return;}
                        if(!serialMode && ((!target || std::string(target)!=std::to_string(s.sourceNode)) && (!legacy || std::string(legacy)!=std::to_string(s.sourceNode)))) {s.failed=true;emit("ERROR legacy target mismatch");return;}
                        for(const char* key:{"node.dont-reconnect","node.dont-fallback","node.dont-move"}) {
                            const char* value=spa_dict_lookup(info->props,key);
                            if(!value || std::string(value)!="true"){s.failed=true;emit("ERROR missing capture target constraint "+std::string(key));return;}
                        }
                        s.targetChecked=true;s.connectPorts();};return e;}();
                pw_node_add_listener(inputProxy,&inputListener,&events,this);
            }
        } else if(std::strcmp(type,PW_TYPE_INTERFACE_Port)==0) {
            const char* node=spa_dict_lookup(props,PW_KEY_NODE_ID);
            const char* direction=spa_dict_lookup(props,PW_KEY_PORT_DIRECTION);
            emit("PORT "+std::to_string(id)+" node="+(node?node:"")+" direction="+(direction?direction:""));
            if(node && direction) {
                const auto nodeId=uint32_t(std::stoul(node));
                if(nodeId==sourceNode && std::string(direction)=="out")sourcePort=id;
                if(nodeId==inputNode && std::string(direction)=="in")inputPort=id;
            }
        }
        connectPorts();
    }
    void process() {
        auto* buffer=pw_stream_dequeue_buffer(stream);if(!buffer)return;
        auto* buf=buffer->buffer;
        if(!buf || !buf->n_datas || !buf->datas[0].data || !buf->datas[0].chunk) {failed=true;emit("ERROR missing producer plane");pw_stream_queue_buffer(stream,buffer);return;}
        auto& plane=buf->datas[0];const uint32_t stride=actualWidth*4+8; // exercise row padding
        if(uint64_t(stride)*actualHeight>plane.maxsize){failed=true;emit("ERROR undersized producer plane");pw_stream_queue_buffer(stream,buffer);return;}
        auto* pixels=static_cast<uint8_t*>(plane.data);
        for(uint32_t y=0;y<actualHeight;++y)for(uint32_t x=0;x<actualWidth;++x) {
            auto* pixel=pixels+size_t(y)*stride+x*4;
            const bool bgr=pixelFormat==SPA_VIDEO_FORMAT_BGRA || pixelFormat==SPA_VIDEO_FORMAT_BGRx;
            pixel[bgr?0:2]=uint8_t(109+x%11);pixel[1]=uint8_t(67+y%13);pixel[bgr?2:0]=uint8_t(colour);pixel[3]=0;
        }
        auto* header=static_cast<spa_meta_header*>(spa_buffer_find_meta_data(buf,SPA_META_Header,sizeof(spa_meta_header)));
        if(!header){failed=true;emit("ERROR missing negotiated header");}
        else {header->flags=headerFlags;header->pts=-1;header->seq=++produced;header->dts_offset=0;}
        plane.chunk->offset=0;plane.chunk->stride=int32_t(stride);plane.chunk->size=stride*actualHeight;plane.chunk->flags=0;
        pw_stream_queue_buffer(stream,buffer);
    }
    void negotiate(const spa_pod* param) {
        if(!param){actualWidth=actualHeight=0;return;}
        spa_video_info_raw raw{};
        if(spa_format_video_raw_parse(param,&raw)<0){failed=true;emit("ERROR invalid source format");return;}
        if(raw.format!=pixelFormat){failed=true;emit("ERROR source format changed unexpectedly");return;}
        actualWidth=raw.size.width;actualHeight=raw.size.height;
        const int32_t stride=int32_t(actualWidth*4+8);
        uint8_t bytes[1024];spa_pod_builder builder=SPA_POD_BUILDER_INIT(bytes,sizeof(bytes));
        const spa_pod* params[]={
            static_cast<const spa_pod*>(spa_pod_builder_add_object(&builder,SPA_TYPE_OBJECT_ParamBuffers,SPA_PARAM_Buffers,
                SPA_PARAM_BUFFERS_buffers,SPA_POD_CHOICE_RANGE_Int(4,2,8),SPA_PARAM_BUFFERS_blocks,SPA_POD_Int(1),
                SPA_PARAM_BUFFERS_size,SPA_POD_Int(stride*int32_t(actualHeight)),SPA_PARAM_BUFFERS_stride,SPA_POD_Int(stride),
                SPA_PARAM_BUFFERS_dataType,SPA_POD_CHOICE_FLAGS_Int(1<<SPA_DATA_MemFd))),
            static_cast<const spa_pod*>(spa_pod_builder_add_object(&builder,SPA_TYPE_OBJECT_ParamMeta,SPA_PARAM_Meta,
                SPA_PARAM_META_type,SPA_POD_Id(SPA_META_Header),SPA_PARAM_META_size,SPA_POD_Int(sizeof(spa_meta_header))))};
        if(pw_stream_update_params(stream,params,2)<0){failed=true;emit("ERROR buffer negotiation failed");}
        const char* name=raw.format==SPA_VIDEO_FORMAT_BGRA?"BGRA":raw.format==SPA_VIDEO_FORMAT_RGBA?"RGBA":raw.format==SPA_VIDEO_FORMAT_BGRx?"BGRx":"RGBx";
        emit("FORMAT "+std::to_string(actualWidth)+" "+std::to_string(actualHeight)+" "+name);
    }
    void offer() {
        uint8_t bytes[1024];spa_pod_builder builder=SPA_POD_BUILDER_INIT(bytes,sizeof(bytes));
        const spa_rectangle size{width,height};const spa_fraction rate{40,1};
        const spa_pod* format=static_cast<const spa_pod*>(spa_pod_builder_add_object(&builder,SPA_TYPE_OBJECT_Format,SPA_PARAM_EnumFormat,
            SPA_FORMAT_mediaType,SPA_POD_Id(SPA_MEDIA_TYPE_video),SPA_FORMAT_mediaSubtype,SPA_POD_Id(SPA_MEDIA_SUBTYPE_raw),
            SPA_FORMAT_VIDEO_format,SPA_POD_Id(pixelFormat),SPA_FORMAT_VIDEO_size,SPA_POD_Rectangle(&size),
            SPA_FORMAT_VIDEO_framerate,SPA_POD_Fraction(&rate)));
        require(pw_stream_update_params(stream,&format,1)>=0,"Cannot offer producer format");
    }
    void start() {
        const char* pixelFormatName=getenv("MAGPIE_PIPEWIRE_FIXTURE_PIXEL_FORMAT");
        if(pixelFormatName) {
            if(std::string(pixelFormatName)=="RGBA")pixelFormat=SPA_VIDEO_FORMAT_RGBA;
            else if(std::string(pixelFormatName)=="BGRx")pixelFormat=SPA_VIDEO_FORMAT_BGRx;
            else if(std::string(pixelFormatName)=="RGBx")pixelFormat=SPA_VIDEO_FORMAT_RGBx;
            else require(std::string(pixelFormatName)=="BGRA","Unknown fixture pixel format");
        }
        pw_init(nullptr,nullptr);loop=pw_thread_loop_new("magpie-fixture-source",nullptr);require(loop,"Cannot create source loop");
        auto* pwLoop=pw_thread_loop_get_loop(loop);context=pw_context_new(pwLoop,nullptr,0);require(context,"Cannot create source context");
        core=pw_context_connect(context,nullptr,0);require(core,"Cannot connect private source");
        static const pw_core_events coreEvents=[] {pw_core_events e{};e.version=PW_VERSION_CORE_EVENTS;
            e.error=[](void* p,uint32_t,int,int result,const char* message){auto& s=*static_cast<Source*>(p);s.failed=true;emit("ERROR core "+std::to_string(result)+" "+(message?message:""));};return e;}();
        pw_core_add_listener(core,&coreListener,&coreEvents,this);
        registry=pw_core_get_registry(core,PW_VERSION_REGISTRY,0);require(registry,"Cannot watch private nodes");
        static const pw_registry_events registryEvents=[] {pw_registry_events e{};e.version=PW_VERSION_REGISTRY_EVENTS;
            e.global=[](void* p,uint32_t id,uint32_t,const char* type,uint32_t,const spa_dict* props){static_cast<Source*>(p)->registryObject(id,type,props);};
            e.global_remove=[](void* p,uint32_t id){auto& s=*static_cast<Source*>(p);if(id==s.inputNode){if(s.link){pw_proxy_destroy(s.link);s.link=nullptr;}if(s.inputProxy){spa_hook_remove(&s.inputListener);pw_proxy_destroy(reinterpret_cast<pw_proxy*>(s.inputProxy));s.inputProxy=nullptr;}s.inputNode=s.inputPort=PW_ID_ANY;s.targetChecked=false;}};return e;}();
        pw_registry_add_listener(registry,&registryListener,&registryEvents,this);
        stream=pw_stream_new(core,"Magpie isolated source",pw_properties_new(PW_KEY_NODE_NAME,"magpie-fixture-source",PW_KEY_MEDIA_CLASS,"Video/Source",nullptr));
        require(stream,"Cannot create source stream");
        timer=pw_loop_add_timer(pwLoop,[](void* p,uint64_t){auto& s=*static_cast<Source*>(p);pw_stream_trigger_process(s.stream);},this);require(timer,"Cannot create source timer");
        static const pw_stream_events events=[] {pw_stream_events e{};e.version=PW_VERSION_STREAM_EVENTS;
            e.process=[](void* p){static_cast<Source*>(p)->process();};
            e.param_changed=[](void* p,uint32_t id,const spa_pod* param){if(id==SPA_PARAM_Format)static_cast<Source*>(p)->negotiate(param);};
            e.state_changed=[](void* p,pw_stream_state,pw_stream_state state,const char* error){auto& s=*static_cast<Source*>(p);
                if(state==PW_STREAM_STATE_ERROR){s.failed=true;emit("ERROR source "+std::string(error?error:""));}
                timespec start{0,1},interval{0,25000000};
                pw_loop_update_timer(pw_thread_loop_get_loop(s.loop),s.timer,state==PW_STREAM_STATE_STREAMING?&start:nullptr,state==PW_STREAM_STATE_STREAMING?&interval:nullptr,false);};return e;}();
        pw_stream_add_listener(stream,&streamListener,&events,this);
        uint8_t bytes[1024];spa_pod_builder builder=SPA_POD_BUILDER_INIT(bytes,sizeof(bytes));
        const spa_rectangle size{width,height};const spa_fraction rate{40,1};
        const spa_pod* format=static_cast<const spa_pod*>(spa_pod_builder_add_object(&builder,SPA_TYPE_OBJECT_Format,SPA_PARAM_EnumFormat,
            SPA_FORMAT_mediaType,SPA_POD_Id(SPA_MEDIA_TYPE_video),SPA_FORMAT_mediaSubtype,SPA_POD_Id(SPA_MEDIA_SUBTYPE_raw),
            SPA_FORMAT_VIDEO_format,SPA_POD_Id(pixelFormat),SPA_FORMAT_VIDEO_size,SPA_POD_Rectangle(&size),SPA_FORMAT_VIDEO_framerate,SPA_POD_Fraction(&rate)));
        require(pw_stream_connect(stream,PW_DIRECTION_OUTPUT,PW_ID_ANY,static_cast<pw_stream_flags>(PW_STREAM_FLAG_DRIVER|PW_STREAM_FLAG_MAP_BUFFERS),&format,1)>=0,"Cannot connect source stream");
        require(pw_thread_loop_start(loop)>=0,"Cannot run source loop");
    }
    int run() {
        start();std::string command;
        while(std::cin>>command) {
            if(command=="stop")break;
            {
                LoopLock lock(loop);
                if(command=="colour")std::cin>>colour;
                else if(command=="flags")std::cin>>headerFlags;
                else if(command=="resize") {std::cin>>width>>height;require(width<=512 && height<=512 && width && height,"Invalid fixture size");offer();}
                else if(command=="pause")pw_stream_set_active(stream,false);
                else if(command=="resume")pw_stream_set_active(stream,true);
                else throw std::runtime_error("Unknown source command");
            }
            emit("ACK "+command);
        }
        return failed?1:0;
    }
};
int capture() {
    std::unique_ptr<void,decltype(&mp_capture_destroy)> instance(mp_capture_create(),mp_capture_destroy);
    require(bool(instance),"Cannot create capture");
    if(!mp_capture_start(instance.get())) {emit("START_ERROR "+std::string(mp_last_error()));return 2;}
    emit("STARTED");std::string command;const uint8_t* retained=nullptr;std::vector<uint8_t> snapshot;
    while(std::cin>>command) {
        if(command=="stop")break;
        if(command=="cancel"){mp_capture_cancel(instance.get());emit("CANCELLED");continue;}
        require(command=="frame","Unknown capture command");
        require(!retained || std::equal(snapshot.begin(),snapshot.end(),retained),"Producer modified retained ABI frame");
        const uint8_t* pixels{};uint32_t width{},height{};const int result=mp_capture_frame(instance.get(),&pixels,&width,&height);
        if(result<0){emit("FRAME_ERROR "+std::string(mp_last_error()));continue;}
        if(!result){require(!retained || std::equal(snapshot.begin(),snapshot.end(),retained),"Waiting invalidated retained ABI frame");emit("WAIT "+std::to_string(mp_capture_frame_sequence(instance.get())));continue;}
        require(width && height && pixels && uint64_t(width)*height<=16777216,"Invalid captured dimensions/storage");
        bool valid=true;
        const uint8_t colour=valid?pixels[0]:0;
        for(uint32_t y=0;y<height && valid;++y)for(uint32_t x=0;x<width;++x) {
            const auto* p=pixels+(size_t(y)*width+x)*4;
            if(p[0]!=colour || p[1]!=uint8_t(67+y%13) || p[2]!=uint8_t(109+x%11) || p[3]!=255)valid=false;
        }
        retained=pixels;snapshot.assign(pixels,pixels+size_t(width)*height*4);
        mp_capture_source_info source{};require(mp_capture_source(instance.get(),&source,sizeof(source))!=0,"Cannot query captured identity");
        require(source.flags==0 && source.window_id==0 && source.process_id==0 && source.kind==1,"Portal fabricated source identity");
        emit("FRAME "+std::to_string(mp_capture_frame_sequence(instance.get()))+" "+std::to_string(width)+" "+std::to_string(height)+" "+std::to_string(colour)+" "+(valid?"valid":"INVALID"));
    }
    return 0;
}
}
int main(int argc,char** argv) {
    try {guard();require(argc==2,"Usage: wayland_capture_fixture --source|--capture");if(std::string(argv[1])=="--source"){Source source;return source.run();}require(std::string(argv[1])=="--capture","Unknown fixture mode");return capture();}
    catch(const std::exception& ex){std::cerr<<ex.what()<<'\n';return 1;}
}
