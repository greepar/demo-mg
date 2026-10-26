# Windows Graphics Capture

使用 Windows SDK 内置的 C++/WinRT 头文件，直接编入静态核心，不增加自有 runtime DLL 或单独的 .NET WinRT 运行时。D3D11、DXGI、DWM 和 Windows Runtime 为 Windows 系统依赖；NativeAOT 链接对应系统 import library。

`mp_capture_backend_mask` 返回系统可用后端，`mp_capture_create_backend` 对显式选择严格创建，`mp_capture_instance_backend` 给 profiler 返回会话实际后端。能力探测只查询 WinRT 支持情况，不创建窗口、设备或系统选择器。Windows 仍默认 GDI，WGC 单独选择；不静默回退。

WGC 的 WinRT apartment、frame pool 和 D3D11 immediate context 全部由一条 native MTA 线程持有，避免托管 async 续体换线程导致 COM 生命周期错误。FrameArrived/Closed 回调仅通过 weak signal state 通知，不持有 capture 指针。停止 join 生产者后才销毁源寿命监听器。启动、设备和运行错误传回 C ABI。

能力探测和会话均使用 `try_get_activation_factory` 获取局部、非缓存工厂引用，引用在本次 apartment 结束前释放。避免便捷静态调用将工厂缓存到进程级、在最后一次 `RoUninitialize` 卸载 COM 模块后再次解引用；不通过清除全局缓存影响其他组件。依据 [C++/WinRT 工厂实现](https://github.com/microsoft/cppwinrt/blob/master/strings/base_activation.h)。

使用 `CreateForWindow`，free-threaded frame pool 收取最新 BGRA 帧，D3D11 staging readback 后转换为现有 RGBA 数据路径。当前没有 D3D11/Vulkan 外部显存零拷贝，也没有完成原版的所有源兼容处理。裁剪参照原版 `CalcWindowCapturedFrameBounds`，包括 Win11 24H2 前最大化边界差异；裁剪前后检查几何，尺寸不匹配不发布。尺寸变化重建 frame pool；最小化/隐藏清除待交付数据，恢复后重新建池并等待新帧。client-only 限制在 UI 如实保留。

异步生产使用 CaptureMailbox，等待时不更改已交付的像素缓冲。暂停/几何修订与帧在同一锁内交付；外层再次核对修订和当前客户区尺寸，旧修订不绑定为新画面。外层单独拥有交给 UI 的 RGBA 缓冲，仅接受帧时复制/替换，防止已取出但被几何校验拒绝的 mailbox 帧回收 UI 仍在使用的旧缓冲；fixture 对每次 Waiting 核对上次交付缓冲。当前读回包含此额外 CPU 复制。源窗口/PID/线程及永久销毁状态沿用 WindowsSourceWatch。

窗口从暂停状态恢复时，同时重建 frame pool 和 capture session，要求新会话产生初始帧；只清空/重建帧池可能丢弃静态源唯一的恢复更新。不会靠主动修改/重画源窗口使测试通过。此恢复改动仍需对应 Windows CI 验证。fixture 的 initial/resize/restore/recapture 各阶段分别记录，Waiting 超时与 -1 捕获错误区分；不将线程上残留的上一错误当作本次超时原因，等待几何不匹配时附内容/边界/客户区信息。

一次性 Windows CI 执行独立源进程 fixture，验证实际颜色/尺寸、标题、resize、最小化恢复、销毁通知、重新捕获和源退出。WGC 能力不可用则单独输出 `windows-wgc-fixture.json` 的 `status=unavailable, passed=false`，不冒充成功。能力已可用时，设备创建/启动、像素或生命周期错误都会失败；不允许用 GDI 替代。fixture 不是 Vulkan 渲染、源应用输入、真实游戏、HDR 或多屏 DPI 验收。

API 依据：[Microsoft HWND capture interop](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow)、[free-threaded frame pool](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframepool.createfreethreaded)、[SDK 中的 C++/WinRT](https://learn.microsoft.com/en-us/windows/apps/develop/cpp-winrt/intro-to-using-cpp-with-winrt)。
