#pragma once
#include <algorithm>
#include <cstdint>

namespace magpie {
struct WindowRect {
    int64_t left{},top{},right{},bottom{};
    bool operator==(const WindowRect&) const=default;
    int64_t width() const {return right-left;}
    int64_t height() const {return bottom-top;}
};
enum class WindowFrameKind { NoNativeFrame,NoTitleBar,NoBorder,OnlyThickFrame,Native };
struct WindowRegionGeometry {
    WindowRect window,frame,client,uwpClient,monitor;
    bool nativeFrame{},caption{},thickFrame{},verticalScroll{},horizontalScroll{},maximized{},win11{},hasUwpClient{};
    uint32_t monitorDpi=96;
};
// Port of SrcTracker::Set / _CalcSrcRect, before user cropping. Do not use the
// client rectangle for native side/bottom edges: it excludes native scrollbars.
inline WindowFrameKind windowFrameKind(const WindowRegionGeometry& g) {
    if(!g.nativeFrame)return WindowFrameKind::NoNativeFrame;
    if(g.window.top==g.client.top)
        return g.window.left!=g.client.left&&g.window.right!=g.client.right&&g.window.bottom!=g.client.bottom?
            WindowFrameKind::NoTitleBar:WindowFrameKind::NoBorder;
    return !g.caption&&g.thickFrame?WindowFrameKind::OnlyThickFrame:WindowFrameKind::Native;
}
inline WindowRect windowCaptureRegion(const WindowRegionGeometry& g,bool titleBar) {
    auto kind=windowFrameKind(g);WindowRect region;
    int64_t border=0;
    if(!g.maximized&&kind!=WindowFrameKind::NoNativeFrame&&(kind!=WindowFrameKind::NoBorder||g.win11))
        border=g.win11?(uint64_t(g.monitorDpi)+48)/96:1;
    if(kind==WindowFrameKind::NoNativeFrame) {
        bool custom=g.caption||g.thickFrame;
        if(!custom||titleBar)region=g.window;
        else {
            region=g.client;
            auto thickness=std::min(g.client.left-g.window.left,g.window.right-g.client.right);
            if(g.verticalScroll) {region.left=g.window.left+thickness;region.right=g.window.right-thickness;}
            if(g.horizontalScroll)region.bottom=g.window.bottom-thickness;
        }
    } else if(kind==WindowFrameKind::NoTitleBar&&!titleBar&&g.hasUwpClient) {
        region=g.uwpClient;region.top=std::max(region.top,g.frame.top+border);
    } else {
        region={g.frame.left+border,g.frame.top+border,g.frame.right-border,g.frame.bottom-border};
        if(!titleBar||kind==WindowFrameKind::OnlyThickFrame)region.top=std::max(region.top,g.client.top);
    }
    if(g.maximized)region={std::max(region.left,g.monitor.left),std::max(region.top,g.monitor.top),
        std::min(region.right,g.monitor.right),std::min(region.bottom,g.monitor.bottom)};
    return region;
}
}
