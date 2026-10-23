#pragma once
#include "magpie.h"
#include <algorithm>
#include <cstring>
#include <string_view>
#include <cstddef>

namespace magpie {
static_assert(sizeof(mp_capture_source_info)==5400 && offsetof(mp_capture_source_info,window_id)==16 && offsetof(mp_capture_source_info,title)==24);
inline mp_capture_source_info emptySource() { mp_capture_source_info source{};source.version=1;return source; }
template<size_t N> void sourceText(char (&target)[N],std::string_view text) {
    std::memset(target,0,N);
    size_t count=std::min(text.size(),N-1);
    // Do not leave an incomplete UTF-8 scalar at the truncation boundary.
    if(count<text.size())while(count>0 && (static_cast<unsigned char>(text[count])&0xc0)==0x80)--count;
    if(count)std::memcpy(target,text.data(),count);
}
}
