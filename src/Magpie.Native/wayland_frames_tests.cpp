#include "wayland_frames.h"
#include "wayland_stream_target.h"
#include "magpie.h"
#include <array>
#include <iostream>
#include <limits>

static void require(bool value,const char* message) {if(!value)throw std::runtime_error(message);}
int main() {
    try {
        GVariantBuilder builder;
        auto target=[&](GVariant* serial) {
            g_variant_builder_init(&builder,G_VARIANT_TYPE_VARDICT);
            if(serial)g_variant_builder_add(&builder,"{sv}","pipewire-serial",serial);
            auto* properties=g_variant_ref_sink(g_variant_builder_end(&builder));
            try {auto result=magpie::WaylandStreamTarget::parse(42,properties);g_variant_unref(properties);return result;}
            catch (...) {g_variant_unref(properties);throw;}
        };
        const auto legacy=target(nullptr);
        require(legacy.node==42 && legacy.serial.empty(),"Legacy portal target lost node identity");
        const auto serial=target(g_variant_new_uint64(std::numeric_limits<uint64_t>::max()));
        require(serial.node==PW_ID_ANY && serial.serial=="18446744073709551615","64-bit serial was truncated or node ID still selected");
        for(auto* invalid:{g_variant_new_uint64(0),g_variant_new_uint32(42),g_variant_new_string("42")}) {
            bool rejected=false;try{target(invalid);}catch(const std::runtime_error&){rejected=true;}
            require(rejected,"Malformed serial silently fell back to reusable node ID");
        }
        magpie::WaylandFrames frames;const uint8_t* pixels=nullptr;uint32_t width=0,height=0;
        require(!frames.deliver(&pixels,&width,&height) && frames.sequence()==0,"Unselected stream fabricated a frame");
        spa_video_info_raw format{};format.format=SPA_VIDEO_FORMAT_BGRA;format.size={2,2};frames.configure(format);
        std::array<uint8_t,32> bytes={99,99,99,99,3,2,1,0,6,5,4,0,88,88,88,88,9,8,7,0,12,11,10,0};
        spa_chunk chunk{};chunk.offset=4;chunk.size=20;chunk.stride=12;
        spa_data data{};data.data=bytes.data();data.maxsize=bytes.size();data.chunk=&chunk;
        spa_meta_header header{};spa_meta meta{};meta.type=SPA_META_Header;meta.size=sizeof(header);meta.data=&header;
        spa_buffer buffer{};buffer.n_datas=1;buffer.datas=&data;buffer.n_metas=1;buffer.metas=&meta;
        const std::array<uint8_t,16> expected={1,2,3,255,4,5,6,255,7,8,9,255,10,11,12,255};
        require(frames.publish(&buffer) && frames.sequence()==0,"Producer advanced delivered sequence");
        require(frames.deliver(&pixels,&width,&height) && width==2 && height==2 && frames.sequence()==1,"Initial delivery failed");
        auto stable=pixels;
        require(std::equal(expected.begin(),expected.end(),pixels),"Padded offset BGRA conversion failed");
        frames.publish(&buffer);frames.publish(&buffer);
        require(std::equal(expected.begin(),expected.end(),stable) && frames.sequence()==1,"Producer mutated delivered storage/sequence");
        frames.deliver(&pixels,&width,&height);
        require(frames.sequence()==2 && frames.accepts(1) && frames.accepts(2) && !frames.accepts(3),"Ordinary frame latency broke epoch");
        stable=pixels;
        for(uint32_t flag:{SPA_META_HEADER_FLAG_CORRUPTED,SPA_META_HEADER_FLAG_GAP,SPA_META_HEADER_FLAG_HEADER,SPA_META_HEADER_FLAG_DELTA_UNIT}) {
            frames.publish(&buffer);header.flags=flag;
            require(!frames.publish(&buffer) && !frames.deliver(&pixels,&width,&height) && !frames.accepts(2),"Invalid frame retained pending pixels or accepted old input");
            require(std::equal(expected.begin(),expected.end(),stable),"Rejected metadata modified delivered storage");header.flags=0;
        }
        frames.publish(&buffer);frames.deliver(&pixels,&width,&height);const auto before=frames.sequence();
        header.flags=SPA_META_HEADER_FLAG_DISCONT;frames.publish(&buffer);
        require(!frames.accepts(before),"Discontinuity left old submitted frame valid");
        frames.deliver(&pixels,&width,&height);header.flags=0;
        require(!frames.accepts(before) && frames.accepts(before+1),"Discontinuity did not establish fresh epoch");
        for(int32_t flag:{SPA_CHUNK_FLAG_CORRUPTED,SPA_CHUNK_FLAG_EMPTY}) {
            frames.publish(&buffer);chunk.flags=flag;
            require(!frames.publish(&buffer) && !frames.deliver(&pixels,&width,&height),"Empty/corrupt chunk was published");chunk.flags=0;
        }
        auto reject=[&] {frames.publish(&buffer);};
        frames.publish(&buffer);chunk.stride=7;bool rejected=false;
        try{reject();}catch(const std::runtime_error&){rejected=true;}
        require(rejected && !frames.deliver(&pixels,&width,&height),"Bad stride left pending pixels");chunk.stride=12;
        frames.publish(&buffer);meta.size=1;rejected=false;
        try{reject();}catch(const std::runtime_error&){rejected=true;}
        require(rejected && !frames.deliver(&pixels,&width,&height),"Truncated header accepted");meta.size=sizeof(header);
        frames.publish(&buffer);frames.configure(format);
        require(!frames.deliver(&pixels,&width,&height),"Renegotiation retained old-format pending pixels");
        format.format=SPA_VIDEO_FORMAT_RGBA;format.size={1,1};frames.configure(format);chunk.stride=4;chunk.size=4;
        frames.publish(&buffer);frames.deliver(&pixels,&width,&height);
        require(width==1 && height==1 && pixels[0]==3 && pixels[2]==1 && pixels[3]==255,"RGBA resize conversion incorrect");
        const auto sequence=frames.sequence();stable=pixels;frames.pause();
        require(!frames.accepts(sequence) && !frames.deliver(&pixels,&width,&height) && stable[0]==3,"Suspension corrupted delivered storage");
        frames.publish(&buffer);frames.deliver(&pixels,&width,&height);
        require(frames.sequence()==sequence+1 && !frames.accepts(sequence),"Resume accepted pre-suspension frame");
        // Unselected native ABI must not connect to the user's portal or display.
        void* capture=mp_capture_create();require(capture!=nullptr,"Cannot create native capture");
        require(mp_capture_frame_sequence(capture)==0,"Native ABI fabricated sequence");
        mp_capture_cancel(capture);require(mp_capture_frame(capture,&pixels,&width,&height)==-1,"Cancelled capture delivered frame");
        mp_capture_destroy(capture);
        std::cout<<"WAYLAND FRAMES PASS: serial/legacy targeting, padded BGRA/RGBA, delivered epochs, corruption/discontinuity/format/suspension and unselected ABI. No live portal/input tested.\n";
        return 0;
    }catch(const std::exception& error){std::cerr<<error.what()<<'\n';return 1;}
}
