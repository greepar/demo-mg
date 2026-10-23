#include "source_info.h"
#include <iostream>
#include <stdexcept>
#include <memory>
#include <string>

void require(bool value,const char* message) { if(!value)throw std::runtime_error(message); }
int main() {
    try {
        char shortText[5];magpie::sourceText(shortText,"AB\xf0\x9f\x98\x80Z");require(std::string(shortText)=="AB","Truncation split a UTF-8 scalar");
        char exact[7];magpie::sourceText(exact,"AB\xf0\x9f\x98\x80Z");require(std::string(exact)=="AB\xf0\x9f\x98\x80","Exact scalar boundary was truncated incorrectly");
        char one[1];magpie::sourceText(one,"text");require(one[0]==0,"Single-byte field is not terminated");
        char title[1024];magpie::sourceText(title,std::string(1022,'a')+"\xf0\x9f\x98\x80");require(std::strlen(title)==1022,"Long metadata split a UTF-8 scalar");
        std::unique_ptr<void,decltype(&mp_capture_destroy)> capture(mp_capture_create(),mp_capture_destroy);require(bool(capture),mp_last_error());
        require(mp_capture_frame_sequence(capture.get())==0,"Unselected capture fabricated a delivered-frame sequence");
        mp_capture_source_info titleSnapshot=magpie::emptySource();
        require(mp_capture_snapshot_title(capture.get(),1,2,3,&titleSnapshot,sizeof(titleSnapshot))==0,"Unselected capture returned a foreign live title");
        require(mp_capture_snapshot_title(capture.get(),1,2,3,&titleSnapshot,sizeof(titleSnapshot)-1)==0,"Live title accepted an invalid ABI size");
        require(mp_capture_snapshot_title(nullptr,1,2,3,&titleSnapshot,sizeof(titleSnapshot))==0,"Null capture returned a live title");
        require(mp_capture_snapshot_title(capture.get(),1,2,3,nullptr,sizeof(titleSnapshot))==0,"Null live-title output accepted");
        struct Guarded { mp_capture_source_info source;uint64_t guard; } value{magpie::emptySource(),0x123456789abcdef0ull};
        require(mp_capture_source(capture.get(),&value.source,sizeof(value.source))==1,mp_last_error());
        require(value.guard==0x123456789abcdef0ull,"Source snapshot wrote beyond the ABI buffer");
        require(value.source.version==1 && value.source.flags==0 && value.source.kind==0 && value.source.process_id==0 && value.source.window_id==0,"Unselected capture fabricated metadata");
        require(value.source.title[0]==0 && value.source.process_name[0]==0 && value.source.executable_path[0]==0,"Unknown metadata strings are not empty");
        require(mp_capture_source(capture.get(),&value.source,sizeof(value.source)-1)==0,"Invalid ABI size accepted");
        require(mp_capture_source(nullptr,&value.source,sizeof(value.source))==0,"Null capture accepted");
        require(mp_capture_source(capture.get(),nullptr,sizeof(value.source))==0,"Null output accepted");
        std::cout<<"SOURCE INFO PASS: 5400-byte ABI, UTF-8 truncation, unselected real capture, buffer guard and invalid arguments; no source picker.\n";return 0;
    }catch(const std::exception& error){std::cerr<<error.what()<<'\n';return 1;}
}
