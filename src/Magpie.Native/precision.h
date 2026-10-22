#pragma once
#include <cstdint>
#include <stdexcept>
inline bool nativeHalfEnabled(uint32_t mode,bool supported) {
    if(mode>2)throw std::runtime_error("Unknown arithmetic precision mode");
    if(mode==2 && !supported)throw std::runtime_error("The selected Vulkan device does not support the required native Float16/RTE arithmetic mode");
    return mode!=0 && supported;
}
