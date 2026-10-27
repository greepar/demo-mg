#pragma once
#include <gio/gio.h>
#include <pipewire/pipewire.h>
#include <stdexcept>
#include <string>

namespace magpie {
struct WaylandStreamTarget {
    uint32_t node;
    std::string serial;
    static WaylandStreamTarget parse(uint32_t node,GVariant* properties) {
        if (!properties || !g_variant_is_of_type(properties,G_VARIANT_TYPE_VARDICT)) throw std::runtime_error("Invalid portal stream properties");
        WaylandStreamTarget target{node,{}};
        auto* value=g_variant_lookup_value(properties,"pipewire-serial",nullptr);
        if (!value) return target; // ScreenCast before v6: only the node ID exists.
        const bool valid=g_variant_is_of_type(value,G_VARIANT_TYPE_UINT64);
        const uint64_t serial=valid?g_variant_get_uint64(value):0;
        g_variant_unref(value);
        if (!serial) throw std::runtime_error("Invalid portal PipeWire serial");
        target.serial=std::to_string(serial);target.node=PW_ID_ANY;
        return target;
    }
};
}
