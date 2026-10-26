#include "magpie.h"
#include "error.h"
#include "source_info.h"
#include <gio/gio.h>
#include <gio/gunixfdlist.h>
#include <pipewire/pipewire.h>
#include <spa/param/video/format-utils.h>
#include <spa/pod/builder.h>
#include <atomic>
#include <chrono>
#include <cstring>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <thread>
#include <vector>
#include <unistd.h>

namespace {
constexpr const char* service = "org.freedesktop.portal.Desktop";
constexpr const char* desktop = "/org/freedesktop/portal/desktop";
constexpr const char* screenCast = "org.freedesktop.portal.ScreenCast";
template<class T> struct Unref { void operator()(T* object) const { if (object) g_object_unref(object); } };
using Variant = std::unique_ptr<GVariant, decltype(&g_variant_unref)>;
Variant variant(GVariant* value) { return Variant(value, g_variant_unref); }
std::runtime_error diagnostic(const char* operation, GError* error) {
    std::string text = std::string(operation) + ": " + (error ? error->message : "unknown error");
    if (error) g_error_free(error);
    return std::runtime_error(text);
}
struct ContextScope {
    GMainContext* context;
    explicit ContextScope(GMainContext* c) : context(c) { g_main_context_push_thread_default(context); }
    ~ContextScope() { g_main_context_pop_thread_default(context); }
};
struct Capture {
    GMainContext* context = g_main_context_new();
    std::unique_ptr<GDBusConnection, Unref<GDBusConnection>> bus;
    std::unique_ptr<GCancellable, Unref<GCancellable>> cancellable{g_cancellable_new()};
    std::atomic<bool> cancelled{false}, closed{false};
    std::string session;
    guint closedSubscription{};
    pw_thread_loop* loop{};
    pw_context* pwContext{};
    pw_core* core{};
    pw_stream* stream{};
    spa_hook listener{};
    spa_video_info_raw format{};
    std::mutex pixelsMutex;
    std::vector<uint8_t> pixels, delivered;
    uint32_t width{}, height{}, deliveredWidth{}, deliveredHeight{};
    bool fresh{};
    uint32_t sourceKind{};
    std::string streamError;
    static inline std::once_flag pipewireInit;
    ~Capture() {
        if (loop) pw_thread_loop_stop(loop);
        if (stream) { spa_hook_remove(&listener); pw_stream_destroy(stream); }
        if (core) pw_core_disconnect(core);
        if (pwContext) pw_context_destroy(pwContext);
        if (loop) pw_thread_loop_destroy(loop);
        if (bus) {
            if (closedSubscription) g_dbus_connection_signal_unsubscribe(bus.get(), closedSubscription);
            if (!session.empty()) {
                auto reply = variant(g_dbus_connection_call_sync(bus.get(), service, session.c_str(), "org.freedesktop.portal.Session", "Close", nullptr, nullptr, G_DBUS_CALL_FLAGS_NONE, 3000, nullptr, nullptr));
            }
        }
        g_main_context_unref(context);
    }
    void initialize() {
        ContextScope scope(context);
        GError* error{};
        bus.reset(g_bus_get_sync(G_BUS_TYPE_SESSION, cancellable.get(), &error));
        if (!bus) throw diagnostic("Connect to desktop portal", error);
    }
    static void options(GVariantBuilder& builder, const char* token) {
        g_variant_builder_init(&builder, G_VARIANT_TYPE_VARDICT);
        g_variant_builder_add(&builder, "{sv}", "handle_token", g_variant_new_string(token));
    }
    Variant request(const char* method, GVariant* parameters, const std::string& token) {
        struct Response { bool done{}; uint32_t status{}; GVariant* results{}; } response;
        auto sender = std::string(g_dbus_connection_get_unique_name(bus.get())).substr(1);
        for (auto& c : sender) if (c == '.') c = '_';
        std::string path = "/org/freedesktop/portal/desktop/request/" + sender + "/" + token;
        const guint subscription = g_dbus_connection_signal_subscribe(bus.get(), service, "org.freedesktop.portal.Request", "Response", path.c_str(), nullptr, G_DBUS_SIGNAL_FLAGS_NONE,
            [](GDBusConnection*, const gchar*, const gchar*, const gchar*, const gchar*, GVariant* result, gpointer data) {
                auto& r = *static_cast<Response*>(data);
                g_variant_get(result, "(u@a{sv})", &r.status, &r.results); r.done = true;
            }, &response, nullptr);
        GError* error{};
        auto reply = variant(g_dbus_connection_call_sync(bus.get(), service, desktop, screenCast, method, parameters,
            G_VARIANT_TYPE("(o)"), G_DBUS_CALL_FLAGS_NONE, 30000, cancellable.get(), &error));
        if (!reply) { g_dbus_connection_signal_unsubscribe(bus.get(), subscription); throw diagnostic(method, error); }
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::minutes(5);
        while (!response.done && !cancelled && std::chrono::steady_clock::now() < deadline) {
            while (g_main_context_iteration(context, false)) {}
            std::this_thread::sleep_for(std::chrono::milliseconds(10));
        }
        g_dbus_connection_signal_unsubscribe(bus.get(), subscription);
        auto result = variant(response.results);
        if (!response.done) {
            auto close = variant(g_dbus_connection_call_sync(bus.get(), service, path.c_str(), "org.freedesktop.portal.Request", "Close", nullptr, nullptr, G_DBUS_CALL_FLAGS_NONE, 3000, nullptr, nullptr));
            throw std::runtime_error(cancelled ? "Capture selection cancelled" : "Capture selection timed out");
        }
        if (response.status != 0) throw std::runtime_error(response.status == 1 ? "Capture selection cancelled" : "Desktop portal rejected screen capture");
        return result;
    }
    void start() {
        ContextScope scope(context);
        if (!session.empty()) throw std::runtime_error("Capture session has already started");
        if (!bus) initialize();
        std::string token = "magpie" + std::to_string(std::chrono::steady_clock::now().time_since_epoch().count());
        GVariantBuilder builder;
        options(builder, (token + "create").c_str());
        g_variant_builder_add(&builder, "{sv}", "session_handle_token", g_variant_new_string(token.c_str()));
        auto created = request("CreateSession", g_variant_new("(a{sv})", &builder), token + "create");
        const char* handle{};
        if (!g_variant_lookup(created.get(), "session_handle", "&s", &handle)) throw std::runtime_error("Portal returned no session handle");
        session = handle;
        closedSubscription = g_dbus_connection_signal_subscribe(bus.get(), service, "org.freedesktop.portal.Session", "Closed", session.c_str(), nullptr, G_DBUS_SIGNAL_FLAGS_NONE,
            [](GDBusConnection*, const gchar*, const gchar*, const gchar*, const gchar*, GVariant*, gpointer data) { static_cast<Capture*>(data)->closed = true; }, this, nullptr);
        options(builder, (token + "sources").c_str());
        g_variant_builder_add(&builder, "{sv}", "types", g_variant_new_uint32(3)); // monitor or window
        g_variant_builder_add(&builder, "{sv}", "multiple", g_variant_new_boolean(false));
        g_variant_builder_add(&builder, "{sv}", "cursor_mode", g_variant_new_uint32(2)); // embedded cursor
        auto selected = request("SelectSources", g_variant_new("(oa{sv})", session.c_str(), &builder), token + "sources");
        options(builder, (token + "start").c_str());
        auto started = request("Start", g_variant_new("(osa{sv})", session.c_str(), "", &builder), token + "start");
        auto streams = variant(g_variant_lookup_value(started.get(), "streams", G_VARIANT_TYPE("a(ua{sv})")));
        if (!streams || g_variant_n_children(streams.get()) != 1) throw std::runtime_error("Portal must return exactly one selected source");
        auto first = variant(g_variant_get_child_value(streams.get(), 0));
        auto nodeValue = variant(g_variant_get_child_value(first.get(), 0));
        const uint32_t node = g_variant_get_uint32(nodeValue.get());
        auto properties=variant(g_variant_get_child_value(first.get(),1));uint32_t sourceType{};
        if(g_variant_lookup(properties.get(),"source_type","u",&sourceType)) {
            std::lock_guard lock(pixelsMutex);
            sourceKind=sourceType==2?1:sourceType==1?2:sourceType==4?3:0;
        }
        g_variant_builder_init(&builder, G_VARIANT_TYPE_VARDICT);
        GError* error{}; GUnixFDList* descriptors{};
        auto remote = variant(g_dbus_connection_call_with_unix_fd_list_sync(bus.get(), service, desktop, screenCast, "OpenPipeWireRemote",
            g_variant_new("(oa{sv})", session.c_str(), &builder), G_VARIANT_TYPE("(h)"), G_DBUS_CALL_FLAGS_NONE, 30000, nullptr, &descriptors, cancellable.get(), &error));
        std::unique_ptr<GUnixFDList, Unref<GUnixFDList>> fdList(descriptors);
        if (!remote) throw diagnostic("OpenPipeWireRemote", error);
        gint index; g_variant_get(remote.get(), "(h)", &index);
        int fd = g_unix_fd_list_get(fdList.get(), index, &error);
        if (fd < 0) throw diagnostic("Read PipeWire descriptor", error);
        connectPipewire(fd, node);
    }
    void connectPipewire(int fd, uint32_t node) {
        // pw_context_connect_fd consumes the descriptor, even on failure.
        std::call_once(pipewireInit, [] { pw_init(nullptr, nullptr); });
        loop = pw_thread_loop_new("magpie-capture", nullptr);
        if (!loop) { close(fd); throw std::runtime_error("Cannot create PipeWire loop"); }
        pwContext = pw_context_new(pw_thread_loop_get_loop(loop), nullptr, 0);
        if (!pwContext) { close(fd); throw std::runtime_error("Cannot create PipeWire context"); }
        core = pw_context_connect_fd(pwContext, fd, nullptr, 0);
        if (!core) throw std::runtime_error("Cannot connect to authorized PipeWire remote");
        stream = pw_stream_new(core, "Magpie window capture", pw_properties_new(PW_KEY_MEDIA_TYPE, "Video", PW_KEY_MEDIA_CATEGORY, "Capture", PW_KEY_MEDIA_ROLE, "Screen", nullptr));
        if (!stream) throw std::runtime_error("Cannot create PipeWire capture stream");
        static const pw_stream_events events = [] {
            pw_stream_events e{}; e.version = PW_VERSION_STREAM_EVENTS;
            e.state_changed = [](void* data, pw_stream_state, pw_stream_state state, const char* message) {
                if (state == PW_STREAM_STATE_ERROR || state == PW_STREAM_STATE_UNCONNECTED) {
                    auto& c = *static_cast<Capture*>(data); std::lock_guard lock(c.pixelsMutex);
                    c.streamError = message ? message : "PipeWire capture stream disconnected";
                }
            };
            e.param_changed = [](void* data, uint32_t id, const spa_pod* param) {
                if (id != SPA_PARAM_Format || !param) return;
                auto& c = *static_cast<Capture*>(data);
                if (spa_format_video_raw_parse(param, &c.format) < 0) return;
                uint8_t storage[512]; spa_pod_builder b = SPA_POD_BUILDER_INIT(storage, sizeof(storage));
                const spa_pod* buffers = static_cast<const spa_pod*>(spa_pod_builder_add_object(&b, SPA_TYPE_OBJECT_ParamBuffers, SPA_PARAM_Buffers,
                    SPA_PARAM_BUFFERS_dataType, SPA_POD_CHOICE_FLAGS_Int((1 << SPA_DATA_MemPtr) | (1 << SPA_DATA_MemFd))));
                pw_stream_update_params(c.stream, &buffers, 1);
            };
            e.process = [](void* data) { static_cast<Capture*>(data)->process(); };
            return e;
        }();
        pw_stream_add_listener(stream, &listener, &events, this);
        uint8_t storage[1024]; spa_pod_builder b = SPA_POD_BUILDER_INIT(storage, sizeof(storage));
        const spa_pod* parameters = static_cast<const spa_pod*>(spa_pod_builder_add_object(&b, SPA_TYPE_OBJECT_Format, SPA_PARAM_EnumFormat,
            SPA_FORMAT_mediaType, SPA_POD_Id(SPA_MEDIA_TYPE_video), SPA_FORMAT_mediaSubtype, SPA_POD_Id(SPA_MEDIA_SUBTYPE_raw),
            SPA_FORMAT_VIDEO_format, SPA_POD_CHOICE_ENUM_Id(4, SPA_VIDEO_FORMAT_BGRA, SPA_VIDEO_FORMAT_RGBA, SPA_VIDEO_FORMAT_BGRx, SPA_VIDEO_FORMAT_RGBx)));
        const auto flags = static_cast<pw_stream_flags>(PW_STREAM_FLAG_AUTOCONNECT | PW_STREAM_FLAG_MAP_BUFFERS);
        if (pw_stream_connect(stream, PW_DIRECTION_INPUT, node, flags, &parameters, 1) < 0) throw std::runtime_error("Cannot connect selected PipeWire stream");
        if (pw_thread_loop_start(loop) < 0) throw std::runtime_error("Cannot start PipeWire capture loop");
    }
    void process() noexcept {
        pw_buffer* buffer = pw_stream_dequeue_buffer(stream);
        if (!buffer) return;
        try {
            if (!buffer->buffer || !buffer->buffer->n_datas) throw std::runtime_error("PipeWire returned no pixel plane");
            const auto& plane = buffer->buffer->datas[0];
            const uint32_t w = format.size.width, h = format.size.height;
            if (!plane.chunk || !plane.data || !plane.chunk->size) { pw_stream_queue_buffer(stream, buffer); return; }
            if (!w || !h || uint64_t(w) * h > 16777216) throw std::runtime_error("Unsupported captured frame dimensions");
            const int64_t stride = plane.chunk->stride;
            const uint64_t rowBytes = uint64_t(w) * 4;
            if (stride < int64_t(rowBytes) || uint64_t(plane.chunk->offset) + (h - 1) * stride + rowBytes > plane.maxsize ||
                uint64_t(plane.chunk->size) < (h - 1) * stride + rowBytes) throw std::runtime_error("Invalid PipeWire pixel buffer stride or length");
            bool bgr = format.format == SPA_VIDEO_FORMAT_BGRA || format.format == SPA_VIDEO_FORMAT_BGRx;
            if (!bgr && format.format != SPA_VIDEO_FORMAT_RGBA && format.format != SPA_VIDEO_FORMAT_RGBx) throw std::runtime_error("Unsupported PipeWire pixel format");
            std::lock_guard lock(pixelsMutex);
            pixels.resize(size_t(w) * h * 4);
            auto* base = static_cast<const uint8_t*>(plane.data) + plane.chunk->offset;
            for (uint32_t y = 0; y < h; ++y) for (uint32_t x = 0; x < w; ++x) {
                const auto* src = base + y * stride + x * 4;
                auto* dst = pixels.data() + (size_t(y) * w + x) * 4;
                dst[0] = src[bgr ? 2 : 0]; dst[1] = src[1]; dst[2] = src[bgr ? 0 : 2]; dst[3] = 255;
            }
            width = w; height = h; fresh = true;
        } catch (const std::exception& ex) { std::lock_guard lock(pixelsMutex); streamError = ex.what(); }
        pw_stream_queue_buffer(stream, buffer);
    }
    int frame(const uint8_t** rgba, uint32_t* w, uint32_t* h) {
        while (g_main_context_iteration(context, false)) {}
        if (closed) throw std::runtime_error("Screen sharing was stopped by the desktop");
        if (cancelled) throw std::runtime_error("Capture was stopped");
        std::lock_guard lock(pixelsMutex);
        if (!streamError.empty()) throw std::runtime_error(streamError);
        if (!fresh) return 0;
        delivered = pixels; deliveredWidth = width; deliveredHeight = height; fresh = false;
        *rgba = delivered.data(); *w = deliveredWidth; *h = deliveredHeight;
        return 1;
    }
};
}
extern "C" void* mp_capture_create() {
    try { return new Capture(); }
    catch (const std::exception& ex) { magpie::error = ex.what(); return nullptr; }
}
extern "C" void mp_capture_destroy(void* capture) { delete static_cast<Capture*>(capture); }
extern "C" uint32_t mp_capture_backend(void) { return 1; }
extern "C" uint32_t mp_capture_backend_mask(void) { return 1u<<1; }
extern "C" uint32_t mp_capture_instance_backend(void* capture) { return capture?1:0; }
extern "C" void* mp_capture_create_backend(uint32_t backend) {
    if(backend!=0&&backend!=1) {magpie::error="The requested capture backend is not available on Wayland";return nullptr;}
    return mp_capture_create();
}
extern "C" int mp_capture_snapshot_title(void*,uint64_t,uint32_t,uint64_t,mp_capture_source_info*,uint32_t) { return 0; }
extern "C" int mp_capture_source(void* capture,mp_capture_source_info* info,uint32_t size) {
    if(!capture||!info||size!=sizeof(*info)){magpie::error="Invalid source metadata arguments";return 0;}
    auto& c=*static_cast<Capture*>(capture);std::lock_guard lock(c.pixelsMutex);
    *info=magpie::emptySource();info->kind=c.sourceKind;
    // ScreenCast does not provide a portable source title/PID/executable.
    return 1;
}
extern "C" void mp_capture_cancel(void* capture) {
    if (auto* c = static_cast<Capture*>(capture)) { c->cancelled = true; g_cancellable_cancel(c->cancellable.get()); }
}
extern "C" int mp_capture_start(void* capture) {
    try { if (!capture) throw std::runtime_error("Capture is not initialized"); static_cast<Capture*>(capture)->start(); return 1; }
    catch (const std::exception& ex) { magpie::error = ex.what(); return 0; }
}
extern "C" int mp_capture_frame(void* capture, const uint8_t** rgba, uint32_t* width, uint32_t* height) {
    try {
        if (!capture || !rgba || !width || !height) throw std::runtime_error("Invalid capture arguments");
        return static_cast<Capture*>(capture)->frame(rgba, width, height);
    } catch (const std::exception& ex) { magpie::error = ex.what(); return -1; }
}
