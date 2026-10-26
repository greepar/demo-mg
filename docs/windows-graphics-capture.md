# Windows Graphics Capture

使用 Windows SDK 内置的 C++/WinRT 头文件，直接编入静态核心，不增加自有 runtime DLL 或单独的 .NET WinRT 运行时。D3D11、DXGI、DWM 和 Windows Runtime 为 Windows 系统依赖；NativeAOT 链接对应系统 import library。

`mp_capture_backend_mask` 返回系统可用后端，`mp_capture_create_backend` 对显式选择严格创建，`mp_capture_instance_backend` 给 profiler 返回会话实际后端。能力探测只查询 WinRT 支持情况，不创建窗口、设备或系统选择器。Windows 仍默认 GDI，WGC 单独选择；不静默回退。

WGC 的 WinRT apartment、frame pool 和 D3D11 immediate context 全部由一条 native MTA 线程持有，避免托管 async 续体换线程导致 COM 生命周期错误。FrameArrived/Closed 回调仅通过 weak signal state 通知，不持有 capture 指针。停止 join 生产者后才销毁源寿命监听器。启动、设备和运行错误传回 C ABI。

能力探测和会话均使用 `try_get_activation_factory` 获取局部、非缓存工厂引用，引用在本次 apartment 结束前释放。避免便捷静态调用将工厂缓存到进程级、在最后一次 `RoUninitialize` 卸载 COM 模块后再次解引用；不通过清除全局缓存影响其他组件。依据 [C++/WinRT 工厂实现](https://github.com/microsoft/cppwinrt/blob/master/strings/base_activation.h)。

使用 `CreateForWindow`，free-threaded frame pool 收取最新 BGRA 帧，D3D11 staging readback 后转换为现有 RGBA 数据路径。当前没有 D3D11/Vulkan 外部显存零拷贝，也没有完成原版的所有源兼容处理。裁剪参照原版 `CalcWindowCapturedFrameBounds`，包括 Win11 24H2 前最大化边界差异；裁剪前后检查几何，尺寸不匹配不发布。尺寸变化重建 frame pool；最小化/隐藏清除待交付数据，恢复后重新建池并等待新帧。client-only 限制在 UI 如实保留。

异步生产使用 CaptureMailbox，等待时不更改已交付的像素缓冲。暂停/几何修订与帧在同一锁内交付；外层再次核对修订和当前客户区尺寸，旧修订不绑定为新画面。外层单独拥有交给 UI 的 RGBA 缓冲，仅接受帧时复制/替换，防止已取出但被几何校验拒绝的 mailbox 帧回收 UI 仍在使用的旧缓冲；fixture 对每次 Waiting 核对上次交付缓冲。当前读回包含此额外 CPU 复制。源窗口/PID/线程及永久销毁状态沿用 WindowsSourceWatch。

窗口从暂停状态恢复时，同时重建 frame pool 和 capture session，要求新会话产生初始帧；只清空/重建帧池可能丢弃静态源唯一的恢复更新。不会靠主动修改/重画源窗口使测试通过。此恢复改动仍需对应 Windows CI 验证。fixture 的 initial/resize/restore/recapture 各阶段分别记录，Waiting 超时与 -1 捕获错误区分；不将线程上残留的上一错误当作本次超时原因，等待几何不匹配时附内容/边界/客户区信息。

调用方在 frame 或标题快照校验中观察到最小化时，也会显式通知生产者暂停，不能假定异步读回线程一定能在短暂最小化期间轮询到状态。暂停立即清空待交付帧并推进几何修订；生产者消费通知后再次清空，防止正在读回的旧帧越过暂停点。发布前在同一状态锁下复核暂停标记，恢复仍要求重启会话后的新帧。此改动在 `dfe8056a`，严格 Windows 语法检查通过，实际系统竞态仍需目标回归。

`1915b8c0` 和 `ef4dffe3` 的 Windows 日志均定位在 resized frame：格式为正确的 87/BGRA8，但底层纹理仍是 286×233，ContentSize 和记录的 pool size 已是 406×303。不是初始帧失败，也不是已证实的格式不支持。新实现对底层分配不足单独暂停并丢弃，在释放 frame/surface/texture 引用后重建帧池和会话；不会读取超出纹理分配的客户区，也不会用截断旧帧冒充成功。格式不符仍明确失败。参考 [Microsoft 帧内容、表面尺寸与重建说明](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)。该修复仍需对应 CI。

`5d237039` 的 Windows SDK/AOT 和 GDI fixture 通过，WGC fixture 在 initial frame 后以 `0xC0000005` 退出；同实现的后续 CI 在 initial frame 报 WinRT “Catastrophic failure”。因此不能称 resize 修复或恢复路径已验收，也不能把异常改记为后端不可用。已增加具体失败 API/HRESULT 及一次性 fixture 的首个访问异常模块/栈诊断，保持异常继续传递和原测试失败条件。新的 Windows CI 将真实捕获 fixture 放到原生编译之后、AOT 之前，后续 AOT/单文件检查保留，但捕获失败时不会声称其已执行。完整三平台交互仍未完成。

`0cdbd8ad` 的 [Windows 实测日志](https://github.com/greepar/demo-mg/actions/runs/40377713748) 将 E_UNEXPECTED（-2147418113）定位到 `restart capture session for resize/resume`，并非 D3D11 格式或客户端颜色错误。重启已改为关闭旧 pool、用 CreateFreeThreaded 创建全新 pool，并重新订阅 FrameArrived，再创建/配置/启动会话；不在已关闭会话的旧 pool 上创建第二个会话。此修复在 `c579228a`，需对应 Windows 实测结果确认，不能沿用此前构建或 GDI 的通过。

一次性 Windows CI 执行独立源进程 fixture，验证实际颜色/尺寸、标题、resize、最小化恢复、销毁通知、重新捕获和源退出。WGC 能力不可用则单独输出 `windows-wgc-fixture.json` 的 `status=unavailable, passed=false`，不冒充成功。能力已可用时，设备创建/启动、像素或生命周期错误都会失败；不允许用 GDI 替代。fixture 不是 Vulkan 渲染、源应用输入、真实游戏、HDR 或多屏 DPI 验收。

API 依据：[Microsoft HWND capture interop](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow)、[free-threaded frame pool](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframepool.createfreethreaded)、[SDK 中的 C++/WinRT](https://learn.microsoft.com/en-us/windows/apps/develop/cpp-winrt/intro-to-using-cpp-with-winrt)。
