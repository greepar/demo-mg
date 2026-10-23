#pragma once
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif
// All failures return 0/null and set a thread-local UTF-8 diagnostic.
const char* mp_last_error(void);
void* mp_create(void);
// Device snapshots enumerate usable Vulkan 1.1 compute adapters without creating engines.
// identifier is the 32-digit lowercase deviceUUID, independent of enumeration order.
typedef struct mp_device_info {
    char identifier[33];
    char name[256];
    uint32_t type; // Vulkan: 0=other, 1=integrated, 2=discrete, 3=virtual, 4=CPU.
    uint32_t vendor_id;
    uint32_t device_id;
} mp_device_info;
void* mp_devices_create(void);
void mp_devices_destroy(void* devices);
uint32_t mp_devices_count(void* devices);
int mp_devices_get(void* devices,uint32_t index,mp_device_info* info);
// null/empty selects automatically. A missing explicit UUID fails; no silent fallback.
void* mp_create_for_device(const char* identifier);
// 0=FP32, 1=automatic native FP16 with FP32 fallback, 2=require device FP16.
// Backend FP16 support requires shaderFloat16 and RTE16 float controls.
// Effects without an original FP16 capability always retain their FP32 shader.
// Legacy constructors remain FP32. Precision is immutable for engine lifetime.
void* mp_create_with_precision(const char* identifier,uint32_t precision);
int mp_fp16_supported(void* engine);
int mp_effect_uses_fp16(void* engine,uint32_t effect);
const char* mp_device_identifier(void* engine);
void mp_destroy(void* engine);
const char* mp_device_name(void* engine);
// Tightly packed RGBA8. Caller owns both buffers. Modes: 0=Nearest, 1=Lanczos, 2=Bilinear, 3=Bicubic.
// Modes 4=FSR EASU, 5=FSR RCAS, 6=reserved, 7=Anime4K Upscale S, 8=Anime4K Upscale Denoise L.
// Modes 20..23=SMAA Low/Medium/High/Ultra (three passes, original DDS, same-size output).
// Mode 24=Jinc (windowSinc, sinc; configured extra[0]=ARStrength).
// Mode 25=Anime4K bilateral mean (same-size; parameter1=intensitySigma).
// Mode 19=RAVU-Lite R2 (fixed 2x, original embedded DDS SOURCE LUT).
// Modes 15..18=Anime4K Upscale L/VL, Upscale Denoise S/VL (fixed 2x, four/eight passes).
// Mode 14=SSimDownscaler (five mixed PS/CS passes, arbitrary output, parameter1=oversharp 1..3).
// Mode 13=NNEDI3 nns16 win8x4 (R16F intermediate, two passes, fixed 2x).
// Mode 12=CRT-Geom (arbitrary output size, original 15 parameters).
// Mode 11=CuNNy-4x12-NVL (six passes, original RGBA8 feature quantization).
// Modes 9=FSRCNNX, 10=FSRCNNX LineArt (six passes with retained feature maps).
// CNN modes require exactly 2x output; RCAS keeps input size. Intermediates retain declared per-texture formats and input-relative dimensions.
// Parameter: Lanczos AR strength; Bicubic B (C=1/3 in this initial ABI).
int mp_scale(void* engine, const uint8_t* input, uint32_t width, uint32_t height,
             uint8_t* output, uint32_t output_width, uint32_t output_height,
             uint32_t mode, float anti_ringing);
// Extended parameters: Lanczos uses parameter1; Bicubic uses B and C.
int mp_scale_ex(void* engine, const uint8_t* input, uint32_t width, uint32_t height,
                uint8_t* output, uint32_t output_width, uint32_t output_height,
                uint32_t mode, float parameter1, float parameter2);
typedef struct mp_effect_step {
    uint32_t effect;
    uint32_t output_width;
    uint32_t output_height;
    float parameter1;
    float parameter2;
} mp_effect_step;
// Runs 1..16 ordered effects, with GPU-only transfers between stages.
// output_bytes must fit the final RGBA8 image. All stages validate before execution.
int mp_scale_chain(void* engine, const uint8_t* input, uint32_t width, uint32_t height,
                   uint8_t* output, uint64_t output_bytes, const mp_effect_step* steps,
                   uint32_t step_count);
// Extended stage ABI. Graph parameters 0/1 use step.parameter1/2; parameters
// 2..17 use extra[0..15]. CRT-Geom retains its original ABI: all fifteen
// parameters in extra[0..14]. Legacy entry points use defaults for extra slots.
typedef struct mp_effect_config { mp_effect_step step; float extra[16]; } mp_effect_config;
int mp_scale_chain_configured(void* engine, const uint8_t* input, uint32_t width, uint32_t height,
                   uint8_t* output, uint64_t output_bytes, const mp_effect_config* stages, uint32_t stage_count);
enum { MP_GPU_MAX_STAGES=16, MP_GPU_MAX_PASSES=256, MP_MAX_CHAIN_STAGES=17 };
// Versioned entry reserves room for sixteen user effects plus the presentation resampler.
int mp_scale_chain_configured_v2(void* engine,const uint8_t* input,uint32_t width,uint32_t height,
    uint8_t* output,uint64_t output_bytes,const mp_effect_config* stages,uint32_t stage_count);
// Last successful scaling call, device-clock intervals in milliseconds. Zero stage/pass
// counts mean no valid sample; status: 0=off/no sample, 1=valid, 2=unsupported, 3=unavailable.
typedef struct mp_gpu_profile {
    uint32_t supported,enabled,status,timestamp_bits,stage_count,pass_count;
    uint64_t frame_sequence;
    double timestamp_period_ns,total_ms;
    uint32_t effect_ids[MP_GPU_MAX_STAGES],first_pass[MP_GPU_MAX_STAGES],pass_counts[MP_GPU_MAX_STAGES];
    double stage_ms[MP_GPU_MAX_STAGES],pass_ms[MP_GPU_MAX_PASSES];
} mp_gpu_profile;
typedef struct mp_gpu_profile_v2 {
    uint32_t supported,enabled,status,timestamp_bits,stage_count,pass_count;
    uint64_t frame_sequence;
    double timestamp_period_ns,total_ms;
    uint32_t effect_ids[MP_MAX_CHAIN_STAGES],first_pass[MP_MAX_CHAIN_STAGES],pass_counts[MP_MAX_CHAIN_STAGES];
    double stage_ms[MP_MAX_CHAIN_STAGES],pass_ms[MP_GPU_MAX_PASSES];
} mp_gpu_profile_v2;
int mp_get_gpu_profile_v2(void* engine,mp_gpu_profile_v2* profile);
// Legacy getter reports status=3 with no intervals when the chain exceeds sixteen stages.
// Default is disabled. Unsupported hardware is accepted but reported as unsupported.
int mp_set_gpu_profiling(void* engine,int enabled);
int mp_get_gpu_profile(void* engine,mp_gpu_profile* profile);
void* mp_capture_create(void);
// Compiled capture implementation: 1=Portal/PipeWire, 2=ScreenCaptureKit,
// 3=GDI. 4..6 reserved for GraphicsCapture/DesktopDuplication/DwmSharedSurface.
uint32_t mp_capture_backend(void);
// Delivered-frame sequence on Cocoa/GDI; 0 before delivery or on other backends.
uint64_t mp_capture_frame_sequence(void* capture);
// Version 1, caller-owned UTF-8 snapshot. Empty/unflagged fields are unknown;
// never inferred from the capturing application's own process or stream name.
// kind: 0=unknown, 1=window, 2=display, 3=virtual display.
enum { MP_SOURCE_TITLE=1,MP_SOURCE_PROCESS_NAME=2,MP_SOURCE_EXECUTABLE=4,MP_SOURCE_PID=8,MP_SOURCE_WINDOW=16 };
typedef struct mp_capture_source_info {
    uint32_t version,flags,process_id,kind;
    uint64_t window_id;
    char title[1024],process_name[256],executable_path[4096];
} mp_capture_source_info;
int mp_capture_source(void* capture,mp_capture_source_info* info,uint32_t info_size);
// Optional live title of the UI snapshot's exact window/PID. Cocoa also checks
// its geometry/frame epoch. 1=title available (including empty), 0=unavailable,
// changed source or unsupported backend. Does not select a source or prompt.
int mp_capture_snapshot_title(void* capture,uint64_t expected_window,uint32_t expected_process,uint64_t expected_sequence,mp_capture_source_info* info,uint32_t size);
void mp_capture_destroy(void* capture);
// Opens the system source picker on the calling worker thread.
int mp_capture_start(void* capture);
// Safe from another thread while start is pending.
void mp_capture_cancel(void* capture);
// Last successful chain output for each effect stage, including presentation stages.
// Snapshot serial changes on every successful execution. Copy rejects stale serials
// and wrong output lengths without modifying caller bytes; no GPU work is submitted.
typedef struct mp_stage_snapshot_info {
    uint64_t serial;
    uint32_t index,count,effect,width,height,reserved;
} mp_stage_snapshot_info;
int mp_stage_snapshot(void* engine,uint32_t index,mp_stage_snapshot_info* output,uint32_t size);
int mp_copy_stage_snapshot(void* engine,uint64_t serial,uint32_t index,uint8_t* rgba,uint64_t bytes);
// Developer pass outputs: format codes 0=RGBA16F, 1=RGBA8, 2=R16F,
// 3=RG16F, 4=R8, 5=RG8. texture=UINT32_MAX is final RGBA8 OUTPUT.
// Internal textures are raw tightly packed texels, suitable for lossless DDS.
// A non-final pass is replayed through that pass before readback, because later
// passes may have overwritten its texture. Final output is never overwritten.
typedef struct mp_pass_snapshot_info {
    uint64_t serial,bytes;
    uint32_t stage,pass,output,output_count,pass_count,texture,format,width,height,reserved;
} mp_pass_snapshot_info;
int mp_pass_snapshot(void* engine,uint32_t stage,uint32_t pass,uint32_t output,mp_pass_snapshot_info* info,uint32_t size);
int mp_copy_pass_snapshot(void* engine,uint64_t serial,uint32_t stage,uint32_t pass,uint32_t output,uint8_t* pixels,uint64_t bytes);
// Cocoa UI-thread input binding. Rects use output logical points and normalized
// source crop coordinates. 1=ready, 2=Accessibility required, 0=unsupported source,
// 3=waiting for a matching submitted frame, -1=invalid/stopped. Other platforms
// return 0. Does not request permissions.
// Expected identity must match the UI-presented frame and current stream; a
// pending source change disables forwarding until its matching frame is shown.
int mp_capture_configure_input(void* capture,void* native_window,double x,double y,double width,double height,
    double crop_x,double crop_y,double crop_width,double crop_height,double toolbar_height,
    const char* reserved_character,int reserved_key_code,uint64_t reserved_modifiers,uint64_t expected_window,uint32_t expected_process,uint64_t expected_sequence,const char* screenshot_character,int screenshot_key_code,uint64_t screenshot_modifiers);
// 1=new frame, 0=waiting for frame, -1=error. No X11 window identifiers.
// Returned pixels remain valid until the next non-waiting frame call or capture destruction.
// Waiting calls and asynchronous producer callbacks do not alter the delivered buffer.
int mp_capture_frame(void* capture, const uint8_t** rgba,
                     uint32_t* width, uint32_t* height);
#ifdef __cplusplus
}
#endif
