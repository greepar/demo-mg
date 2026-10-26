#pragma once
#include <windows.h>
#include <atomic>
#include <cstdint>
#include <functional>
#include <memory>

namespace magpie {
// WinRT and the D3D11 immediate context live on one native MTA thread. Managed
// async capture callers may resume on another OS thread between frame requests.
class WindowsGraphicsCapture {
    struct State;
    std::unique_ptr<State> state_;
public:
    static bool supported() noexcept;
    WindowsGraphicsCapture(HWND window,bool titleBar,const std::atomic<bool>& cancelled,std::function<bool()> valid);
    ~WindowsGraphicsCapture();
    int frame(const uint8_t** rgba,uint32_t* width,uint32_t* height,uint64_t* revision);
    bool paused() const;
    uint64_t revision() const;
    void suspend();
    bool sizeMatches(uint32_t width,uint32_t height) const;
};
}
