#pragma once
#include "capture_epoch.h"
#include <spa/buffer/buffer.h>
#include <spa/buffer/meta.h>
#include <spa/param/video/raw.h>
#include <cstring>
#include <vector>

namespace magpie {
// Access is serialized by Capture::pixelsMutex. Producer buffers are borrowed
// only for publish(); ABI-delivered storage survives waiting and suspension.
class WaylandFrames {
    CaptureEpoch epoch_;
    std::vector<uint8_t> pending_, delivered_;
    uint32_t width_{}, height_{}, format_{};
    bool fresh_{};
public:
    void pause() { fresh_=false; epoch_.pause(); }
    void configure(const spa_video_info_raw& format) {
        pause();
        width_=format.size.width; height_=format.size.height; format_=format.format;
    }
    uint64_t sequence() const { return epoch_.sequence(); }
    bool accepts(uint64_t sequence) const { return epoch_.accepts(sequence); }
    bool publish(const spa_buffer* buffer) {
        try {
            if (!buffer || !buffer->n_datas || !buffer->datas) throw std::runtime_error("PipeWire returned no pixel plane");
            if (buffer->n_metas && !buffer->metas) throw std::runtime_error("Invalid PipeWire frame metadata");
            for (uint32_t i=0;i<buffer->n_metas;++i) {
                const auto& meta=buffer->metas[i];
                if (meta.type!=SPA_META_Header) continue;
                if (!meta.data || meta.size<sizeof(spa_meta_header)) throw std::runtime_error("Invalid PipeWire frame header");
                spa_meta_header header{};std::memcpy(&header,meta.data,sizeof(header));
                if (header.flags&SPA_META_HEADER_FLAG_DISCONT) pause();
                if (header.flags&(SPA_META_HEADER_FLAG_CORRUPTED|SPA_META_HEADER_FLAG_GAP|SPA_META_HEADER_FLAG_HEADER|SPA_META_HEADER_FLAG_DELTA_UNIT)) { pause();return false; }
            }
            const auto& plane=buffer->datas[0];
            if (!plane.chunk || !plane.data || !plane.chunk->size ||
                (plane.chunk->flags&(SPA_CHUNK_FLAG_CORRUPTED|SPA_CHUNK_FLAG_EMPTY))) { pause();return false; }
            if (!width_ || !height_ || uint64_t(width_)*height_>16777216) throw std::runtime_error("Unsupported captured frame dimensions");
            const int64_t stride=plane.chunk->stride;
            const uint64_t rowBytes=uint64_t(width_)*4;
            if (stride<int64_t(rowBytes) || uint64_t(plane.chunk->offset)+(height_-1)*stride+rowBytes>plane.maxsize ||
                uint64_t(plane.chunk->size)<(height_-1)*stride+rowBytes) throw std::runtime_error("Invalid PipeWire pixel buffer stride or length");
            const bool bgr=format_==SPA_VIDEO_FORMAT_BGRA || format_==SPA_VIDEO_FORMAT_BGRx;
            if (!bgr && format_!=SPA_VIDEO_FORMAT_RGBA && format_!=SPA_VIDEO_FORMAT_RGBx) throw std::runtime_error("Unsupported PipeWire pixel format");
            pending_.resize(size_t(width_)*height_*4);
            const auto* base=static_cast<const uint8_t*>(plane.data)+plane.chunk->offset;
            for (uint32_t y=0;y<height_;++y) for (uint32_t x=0;x<width_;++x) {
                const auto* src=base+y*stride+x*4;
                auto* dst=pending_.data()+(size_t(y)*width_+x)*4;
                dst[0]=src[bgr?2:0];dst[1]=src[1];dst[2]=src[bgr?0:2];dst[3]=255;
            }
            fresh_=true;return true;
        } catch (...) { pause();throw; }
    }
    bool deliver(const uint8_t** pixels,uint32_t* width,uint32_t* height) {
        if (!fresh_) return false;
        epoch_.deliver(width_,height_);
        delivered_.swap(pending_);fresh_=false;
        *pixels=delivered_.data();*width=width_;*height=height_;return true;
    }
};
}
