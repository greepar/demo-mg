# Windows Graphics Capture

使用 Windows SDK 内置的 C++/WinRT 头文件，直接编入静态核心，不增加自有 runtime DLL 或单独的 .NET WinRT 运行时。D3D11、DXGI、DWM 和 Windows Runtime 为 Windows 系统依赖；NativeAOT 链接对应系统 import library。

`mp_capture_backend_mask` 返回系统可用后端，`mp_capture_create_backend` 对显式选择严格创建，`mp_capture_instance_backend` 给 profiler 返回会话实际后端。能力探测只查询 WinRT 支持情况，不创建窗口、设备或系统选择器。Windows 仍默认 GDI，WGC 单独选择；不静默回退。

WGC 的 WinRT apartment、frame pool 和 D3D11 immediate context 全部由一条 native MTA 线程持有，避免托管 async 续体换线程导致 COM 生命周期错误。FrameArrived/Closed 回调仅通过 weak signal state 通知，不持有 capture 指针。停止 join 生产者后才销毁源寿命监听器。启动、设备和运行错误传回 C ABI。

使用 `CreateForWindow`，free-threaded frame pool 收取最新 BGRA 帧，D3D11 staging readback 后转换为现有 RGBA 数据路径。当前没有 D3D11/Vulkan 外部显存零拷贝，也没有完成原版的所有源兼容处理。裁剪参照原版 `CalcWindowCapturedFrameBounds`，包括 Win11 24H2 前最大化边界差异；裁剪前后检查几何，尺寸不匹配不发布。尺寸变化重建 frame pool；最小化/隐藏清除待交付数据，恢复后重新建池并等待新帧。client-only 限制在 UI 如实保留。

异步生产使用 CaptureMailbox，等待时不更改已交付的像素缓冲。暂停/几何修订与帧在同一锁内交付；外层再次核对修订和当前客户区尺寸，旧修订不绑定为新画面。源窗口/PID/线程及永久销毁状态沿用 WindowsSourceWatch。

一次性 Windows CI 执行独立源进程 fixture，验证实际颜色/尺寸、标题、resize、最小化恢复、销毁通知、重新捕获和源退出。WGC 能力不可用则单独输出 `windows-wgc-fixture.json` 的 `status=unavailable, passed=false`，不冒充成功。能力已可用时，设备创建/启动、像素或生命周期错误都会失败；不允许用 GDI 替代。fixture 不是 Vulkan 渲染、源应用输入、真实游戏、HDR 或多屏 DPI 验收。

API 依据：[Microsoft HWND capture interop](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow)、[free-threaded frame pool](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframepool.createfreethreaded)、[SDK 中的 C++/WinRT](https://learn.microsoft.com/en-us/windows/apps/develop/cpp-winrt/intro-to-using-cpp-with-winrt)。
