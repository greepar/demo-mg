cmake_minimum_required(VERSION 3.20)
project(MagpieNative LANGUAGES CXX)
if(MSVC)
  # All test executables that link the static archive must use its CRT too.
  # In particular source_info_tests and capture_geometry_tests cannot mix /MD
  # with magpie_native's /MT when built on a native Windows host.
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()
find_package(Vulkan REQUIRED)
find_package(Python3 REQUIRED COMPONENTS Interpreter)
set(DXC_EXECUTABLE "" CACHE FILEPATH "Path to Microsoft dxc")
set(GENERATED "${CMAKE_CURRENT_BINARY_DIR}/generated")
file(GLOB_RECURSE EFFECT_CATALOG_INPUTS CONFIGURE_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/*.hlsl" "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/*.hlsli" "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/*.dds")
file(MAKE_DIRECTORY "${GENERATED}")
if(DXC_EXECUTABLE)
  add_custom_command(OUTPUT "${GENERATED}/kernel.h"
    COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/compile-vulkan-shader.py"
      --dxc "${DXC_EXECUTABLE}" --output "${GENERATED}"
    DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/effects.json"
            "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/effect_catalog.py"
            ${EFFECT_CATALOG_INPUTS}
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/Jinc.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/Anime4K/Anime4K_Denoise_Bilateral_Mean.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/compile-vulkan-shader.py"
            "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/multipass_shaders.py"
            "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/magpiefx.py"
            "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/dds_source.py"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/SMAA/SMAA_Low.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/SMAA/SMAA_Medium.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/SMAA/SMAA_High.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/SMAA/SMAA_Ultra.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/SMAA/SMAA.hlsli"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/SMAA/AreaTex.dds"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/SMAA/SearchTex.dds"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/RAVU/RAVU_Lite_R2.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/RAVU/prescalers.hlsli"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/RAVU/ravu_lite_lut2_f16.dds"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/Anime4K/Anime4K_Upscale_L.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/Anime4K/Anime4K_Upscale_VL.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/Anime4K/Anime4K_Upscale_Denoise_S.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/Anime4K/Anime4K_Upscale_Denoise_VL.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/SSimDownscaler.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/NNEDI3/NNEDI3_nns16_win8x4.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/NNEDI3/prescalers.hlsli"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/CuNNy2/CuNNy-4x12-NVL.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/FSRCNNX/FSRCNNX.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/FSRCNNX/FSRCNNX_LineArt.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/Anime4K/Anime4K_Upscale_S.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/Anime4K/Anime4K_Upscale_Denoise_L.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/Lanczos.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/Nearest.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/CRT/CRT_Geom.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/Bilinear.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/Bicubic.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/FSR/FSR_EASU.hlsl"
            "${CMAKE_CURRENT_SOURCE_DIR}/../Effects/FSR/FSR_RCAS.hlsl"
    VERBATIM)
else()
  execute_process(COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/verify-precompiled-shaders.py" --extract-header "${GENERATED}/kernel.h"
    RESULT_VARIABLE SHADERS_VALID)
  if(NOT SHADERS_VALID EQUAL 0)
    message(FATAL_ERROR "Precompiled shaders are stale")
  endif()
endif()
add_library(magpie_native STATIC engine.cpp "${GENERATED}/kernel.h")
set_target_properties(magpie_native PROPERTIES POSITION_INDEPENDENT_CODE ON)
target_compile_features(magpie_native PUBLIC cxx_std_20)
if(MSVC)
  target_compile_options(magpie_native PRIVATE /W4 /WX /EHsc)
  set_property(TARGET magpie_native PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
  # Keep the static core distinct from the development DLL's magpie_native.lib
  # import library. Otherwise a multi-config Release build overwrites the archive
  # and NativeAOT silently links an extra runtime DLL instead of embedding C++.
  set_property(TARGET magpie_native PROPERTY ARCHIVE_OUTPUT_NAME magpie_native_static)
else()
  target_compile_options(magpie_native PRIVATE -Wall -Wextra -Werror -Wno-missing-field-initializers)
endif()
target_include_directories(magpie_native PRIVATE "${GENERATED}" PUBLIC include)
target_link_libraries(magpie_native PUBLIC Vulkan::Vulkan)
if(CMAKE_SYSTEM_NAME STREQUAL "Linux")
  find_package(PkgConfig REQUIRED)
  pkg_check_modules(PORTAL REQUIRED IMPORTED_TARGET gio-unix-2.0 libpipewire-0.3)
  target_sources(magpie_native PRIVATE capture_wayland.cpp)
  target_link_libraries(magpie_native PUBLIC PkgConfig::PORTAL)
elseif(WIN32)
  target_sources(magpie_native PRIVATE capture_windows.cpp windows_graphics_capture.cpp windows_pointer.cpp)
  target_link_libraries(magpie_native PUBLIC user32 advapi32 gdi32 d3d11 dxgi dwmapi shcore ole32 windowsapp)
elseif(APPLE)
  enable_language(OBJCXX)
  target_sources(magpie_native PRIVATE capture_macos.mm input_macos.mm)
  set_source_files_properties(capture_macos.mm input_macos.mm PROPERTIES COMPILE_OPTIONS "-fobjc-arc")
  set_target_properties(magpie_native PROPERTIES OBJCXX_STANDARD 20)
  target_link_libraries(magpie_native PUBLIC "-framework AppKit" "-framework ScreenCaptureKit"
    "-framework CoreMedia" "-framework CoreVideo" "-framework Metal" "-framework QuartzCore"
    "-framework IOSurface" "-framework IOKit")
else()
  message(FATAL_ERROR "Unsupported capture platform")
endif()
if(CMAKE_BUILD_TYPE STREQUAL "Debug" OR CMAKE_CONFIGURATION_TYPES)
  # Development only. NativeAOT links the archive directly and ships no .so.
  add_library(magpie_native_shared SHARED shared.cpp)
  set_target_properties(magpie_native_shared PROPERTIES OUTPUT_NAME magpie_native)
  if(MSVC)
    set_target_properties(magpie_native_shared PROPERTIES WINDOWS_EXPORT_ALL_SYMBOLS ON MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    # Export scanning must see the C ABI object files, not only an opaque .lib archive.
    target_sources(magpie_native_shared PRIVATE engine.cpp capture_windows.cpp windows_graphics_capture.cpp windows_pointer.cpp "${GENERATED}/kernel.h")
    target_include_directories(magpie_native_shared PRIVATE "${GENERATED}" include)
    target_compile_features(magpie_native_shared PRIVATE cxx_std_20)
    target_compile_options(magpie_native_shared PRIVATE /W4 /WX /EHsc)
    target_link_libraries(magpie_native_shared PRIVATE Vulkan::Vulkan user32 advapi32 gdi32 d3d11 dxgi dwmapi shcore ole32 windowsapp)
  elseif(APPLE)
    target_link_libraries(magpie_native_shared PRIVATE magpie_native)
    target_link_options(magpie_native_shared PRIVATE "LINKER:-force_load,$<TARGET_FILE:magpie_native>")
  else()
    target_link_libraries(magpie_native_shared PRIVATE -Wl,--whole-archive magpie_native -Wl,--no-whole-archive)
  endif()
endif()
include(CTest)
if(BUILD_TESTING)
  add_executable(capture_geometry_tests capture_geometry_tests.cpp)
  target_include_directories(capture_geometry_tests PRIVATE include)
  target_compile_features(capture_geometry_tests PRIVATE cxx_std_20)
  target_compile_definitions(capture_geometry_tests PRIVATE MAGPIE_GEOMETRY_GPU=1)
  target_link_libraries(capture_geometry_tests PRIVATE magpie_native)
  add_test(NAME capture_resize_geometry COMMAND capture_geometry_tests)
  add_test(NAME capture_resize_vulkan COMMAND capture_geometry_tests --gpu)
  set_tests_properties(capture_resize_vulkan PROPERTIES TIMEOUT 30)
  add_executable(capture_mailbox_tests capture_mailbox_tests.cpp)
  target_include_directories(capture_mailbox_tests PRIVATE include)
  target_compile_features(capture_mailbox_tests PRIVATE cxx_std_20)
  add_test(NAME capture_frame_mailbox COMMAND capture_mailbox_tests)
  add_executable(cursor_pixels_tests cursor_pixels_tests.cpp)
  target_compile_features(cursor_pixels_tests PRIVATE cxx_std_20)
  add_test(NAME cursor_pixels COMMAND cursor_pixels_tests)
  add_executable(source_info_tests source_info_tests.cpp)
  target_link_libraries(source_info_tests PRIVATE magpie_native)
  add_test(NAME capture_source_metadata COMMAND source_info_tests)
  if(CMAKE_SYSTEM_NAME STREQUAL "Linux")
    add_executable(wayland_capture_fixture wayland_capture_fixture.cpp)
    target_link_libraries(wayland_capture_fixture PRIVATE magpie_native)
    add_test(NAME capture_portal_pipewire COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/verify-capture-pipewire-isolated.py"
      --fixture "$<TARGET_FILE:wayland_capture_fixture>" --output "${CMAKE_CURRENT_BINARY_DIR}/pipewire-capture")
    set_tests_properties(capture_portal_pipewire PROPERTIES TIMEOUT 120)
    target_compile_options(wayland_capture_fixture PRIVATE -Wall -Wextra -Werror -Wno-missing-field-initializers)
    add_executable(wayland_frames_tests wayland_frames_tests.cpp)
    target_link_libraries(wayland_frames_tests PRIVATE magpie_native)
    target_compile_options(wayland_frames_tests PRIVATE -Wall -Wextra -Werror -Wno-missing-field-initializers)
    add_test(NAME capture_wayland_frames COMMAND wayland_frames_tests)
    set_tests_properties(capture_wayland_frames PROPERTIES ENVIRONMENT "DBUS_SESSION_BUS_ADDRESS=unix:path=/nonexistent/magpie-no-session-bus;DISPLAY=;WAYLAND_DISPLAY=")
  endif()
  if(WIN32)
    add_executable(windows_capture_fixture windows_capture_fixture.cpp)
    target_link_libraries(windows_capture_fixture PRIVATE magpie_native)
    target_compile_definitions(windows_capture_fixture PRIVATE _CRT_SECURE_NO_WARNINGS)
  endif()
  if(UNIX AND NOT APPLE)
    set_tests_properties(capture_source_metadata PROPERTIES ENVIRONMENT "DBUS_SESSION_BUS_ADDRESS=unix:path=/nonexistent/magpie-no-session-bus;DISPLAY=")
  endif()
  add_executable(native_tests tests.cpp)
  target_include_directories(native_tests PRIVATE "${GENERATED}")
  target_link_libraries(native_tests PRIVATE magpie_native)
  add_test(NAME all_effects_execution COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/verify-all-effects.py" --native-test "$<TARGET_FILE:native_tests>" --output "${CMAKE_CURRENT_BINARY_DIR}/all-effects-execution.json")
  add_test(NAME precision_policy COMMAND "$<TARGET_FILE:native_tests>" --precision-policy)
  add_test(NAME fp16_runtime COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/verify-fp16-runtime.py" --native-test "$<TARGET_FILE:native_tests>" --output "${CMAKE_CURRENT_BINARY_DIR}/fp16-runtime.json")
  set_tests_properties(fp16_runtime PROPERTIES TIMEOUT 1500)
  set_tests_properties(all_effects_execution PROPERTIES TIMEOUT 1260)
  add_test(NAME effect_catalog COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/generate-effect-catalog.py" --self-test --native-test "$<TARGET_FILE:native_tests>" --output "${CMAKE_CURRENT_BINARY_DIR}/effect-catalog.json")
  set_tests_properties(effect_catalog PROPERTIES TIMEOUT 30)
  add_test(NAME filter_effects_reference COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/verify-filter-effects.py" --native-test "$<TARGET_FILE:native_tests>" --output "${CMAKE_CURRENT_BINARY_DIR}/filter-effects-reference.json")
  set_tests_properties(filter_effects_reference PROPERTIES TIMEOUT 120)
  add_test(NAME sharpen_effects_reference COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/verify-sharpen-effects.py" --native-test "$<TARGET_FILE:native_tests>" --output "${CMAKE_CURRENT_BINARY_DIR}/sharpen-effects-reference.json")
  set_tests_properties(sharpen_effects_reference PROPERTIES TIMEOUT 180)
  add_test(NAME finesharp_reference COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/verify-finesharp.py" --native-test "$<TARGET_FILE:native_tests>" --output "${CMAKE_CURRENT_BINARY_DIR}/finesharp-reference.json")
  set_tests_properties(finesharp_reference PROPERTIES TIMEOUT 180)
  add_test(NAME adaptive_sharpen_reference COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/verify-adaptive-sharpen.py" --native-test "$<TARGET_FILE:native_tests>" --output "${CMAKE_CURRENT_BINARY_DIR}/adaptive-sharpen-reference.json")
  set_tests_properties(adaptive_sharpen_reference PROPERTIES TIMEOUT 120)
  if(MSVC)
    set_target_properties(native_tests PROPERTIES MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
  endif()
  add_test(NAME smaa_reference COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/verify-smaa.py" --cxx "${CMAKE_CXX_COMPILER}" --native-test "$<TARGET_FILE:native_tests>" --output "${CMAKE_CURRENT_BINARY_DIR}/smaa-reference.json")
  set_tests_properties(smaa_reference PROPERTIES TIMEOUT 120)
  add_test(NAME dds_source COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/verify-dds-source.py" --output "${CMAKE_CURRENT_BINARY_DIR}/dds-source.json")
  add_test(NAME ravu_reference COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/verify-ravu.py" --native-test "$<TARGET_FILE:native_tests>" --output "${CMAKE_CURRENT_BINARY_DIR}/ravu-reference.json")
  set_tests_properties(dds_source ravu_reference PROPERTIES TIMEOUT 120)
  add_test(NAME ssim_reference COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/verify-ssim.py" --native-test "$<TARGET_FILE:native_tests>" --output "${CMAKE_CURRENT_BINARY_DIR}/ssim-reference.json")
  set_tests_properties(ssim_reference PROPERTIES TIMEOUT 120)
  add_test(NAME nnedi3_reference COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/verify-nnedi3.py" --native-test "$<TARGET_FILE:native_tests>" --output "${CMAKE_CURRENT_BINARY_DIR}/nnedi3-reference.json")
  set_tests_properties(nnedi3_reference PROPERTIES TIMEOUT 120)
  add_test(NAME magpiefx_frontend COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/verify-magpiefx.py" --output "${CMAKE_CURRENT_BINARY_DIR}/magpiefx-frontend.json")
  set_tests_properties(magpiefx_frontend PROPERTIES TIMEOUT 30)
  add_test(NAME vulkan_scaling COMMAND native_tests)
  add_test(NAME anime4k_reference COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/verify-anime4k.py" --native-test "$<TARGET_FILE:native_tests>" --output "${CMAKE_CURRENT_BINARY_DIR}/anime4k-reference.json")
  add_test(NAME fsrcnnx_reference COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/verify-fsrcnnx.py" --native-test "$<TARGET_FILE:native_tests>" --output "${CMAKE_CURRENT_BINARY_DIR}/fsrcnnx-reference.json")
  add_test(NAME cunny_reference COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/verify-cunny.py" --native-test "$<TARGET_FILE:native_tests>" --output "${CMAKE_CURRENT_BINARY_DIR}/cunny-reference.json")
  add_test(NAME crt_reference COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/../../scripts/verify-crt.py" --native-test "$<TARGET_FILE:native_tests>" --output "${CMAKE_CURRENT_BINARY_DIR}/crt-reference.json")
  set_tests_properties(vulkan_scaling anime4k_reference fsrcnnx_reference cunny_reference crt_reference PROPERTIES TIMEOUT 120)
endif()
