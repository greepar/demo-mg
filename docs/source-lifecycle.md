# 源窗口最小化、隐藏与恢复

原版 `ScalingWindow::_UpdateSrcState` 在源不可见/最小化时进入 repositioning，`ScalingRuntime::IsSrcRepositioning` 等待它重新可见，再恢复缩放。之前移植的 Windows C ABI 将最小化作为 -1 返回，Desktop 因而结束整个会话；native fixture 手动恢复不能证明产品能恢复。

现在 Windows GDI/WGC 的 `mp_capture_source_state` 返回 0（未选择/未知）、1（窗口可见）、2（最小化/隐藏，需等待）或 3（停止/身份丢失）。这只是窗口可用性，不是输入权限或画面就绪状态；Linux/macOS 当前返回 0，不猜测 Portal 或 ScreenCaptureKit 的窗口状态。不可见时暂停已交付帧的有效状态，WGC 同时通知异步生产者清空 pending；`mp_capture_frame` 返回 0 Waiting，保留最后交付的缓冲与序号。恢复可见之后仍必须交付新帧，旧序号不能自行恢复有效。源销毁/身份失效仍结束会话。

GDI 分开保存 DIB 的尺寸、待转换像素和 ABI 已交付像素；尺寸重建、隐藏或交付前复核失败不能改写调用者仍持有的像素。BitBlt/GetDC 失败若同时观察到源暂不可见则进入等待，真实捕获失败继续报告错误。发布前再次检查源身份、可见性和客户区尺寸。

Desktop 等待期间收回输出、关闭参数/导出菜单与下拉，取消 profiler 拖动、清除待截图和阶段导出状态，暂停 GPU 处理并释放常亮申请。会话、选定源和 Vulkan 引擎保留；持续不可见不会触发首次等帧的 30 秒超时。源重新可见后等待新帧，恢复后的等帧仍有 30 秒超时；成功时重置处理器和显示计划。只有比暂停前更新且身份一致的提交才能重新显示输出，重复像素的身份提交也能恢复。停止期间的迟到提交不重新显示窗口。等待时关闭源仍终止会话，源窗口正常关闭现在按丢失状态正常结束，不再作为启动错误提示；挂起窗口、源移动/最大化、多显示器/DPI 与其余完整状态矩阵继续待验收。

Hide 后创建的新原生窗口需要显式恢复模式：保存全屏/普通窗口状态及客户区尺寸，恢复时重新应用。Windows 普通窗口还恢复位置；Wayland 不尝试设置绝对位置。首帧前就暂停、尚无输出窗口时，不用空的旧状态覆盖初始全屏请求。恢复请求使用 ShowActivated=false，并还原原来的偏好；Windows 真实前台焦点与源应用共存尚未验收。实际鼠标键盘/光标路径仍未实现，不把此次等待行为算作输入已完成。

## 验证范围

本机 Linux 25/25 原生测试通过。最终 AOT 的五项中文私有 Wayland 场景以及两项英文生命周期场景通过，包括普通/全屏恢复、原有源身份截图、阶段导出、工具栏操作。生命周期场景使用真实 Vulkan 最近邻与 PNG 检查；覆盖改变过的窗口尺寸、初始全屏前等待、打开导出菜单后暂停、旧序号/错误源拒绝、新画面和重复画面恢复、截图/导出阻止及隐藏时停止。源可见性和身份为注入数据，没有打开用户桌面、选源或输入权限对话框。

`source_lifecycle_capture.py` 独立检查 Wayland 事件：按对象代次区分 ID 复用，要求隐藏时原生 output surface 与其导出 popup 销毁、恢复时先 ack_configure 再 attach/commit 新缓冲、停止后没有再次映射。包含普通/全屏两份日志，删除确认、缓冲提交、阶段标记或销毁证据的篡改版本必须拒绝。UI 内的 IsVisible 断言或 RenderTargetBitmap 截图本身不算原生显示/隐藏证据。

Windows 的一次性托管 fixture 已增加 GDI、WGC 和 WGC 标题栏三条路径的真实最小化/恢复、隐藏/显示、live state、保留缓冲/序号、旧状态拒绝和源丢失检查；包装器要求新增 PASS 标记，旧二进制不能冒充该覆盖。严格 MinGW 语法检查通过，本提交 MSVC/SDK 与实际 native fixture 结果仍待 CI。这个 fixture 没有运行 Avalonia 的完整生产捕获循环或注入输入，不能代替 Windows 端到端 UI/焦点验收；macOS 仅验证新增 ABI 的目标编译/模型，未增加窗口可见性探测。

本机报告为 `artifacts/desktop/source-lifecycle-test-summary.json`，UI 和协议证据在 `artifacts/desktop/source-lifecycle-review`。NNEDI3 偶发像素问题与完整交互目标继续保留。


## 一次性 Windows 生产链路联合测试

`bc2eb569` 的 Windows SDK/AOT 与 GDI/WGC/标题栏原生 fixture 已实际通过，日志包含新增 hide/show suspension 与 live source-state ABI 标记；两种 macOS 架构也通过，Linux 软件 Vulkan 回归在记录时仍在执行。这更新上一节的历史待验证状态，不等于 Avalonia 生产捕获循环或输入送达已通过。

新增 `--windows-source-lifecycle-test <source-fixture> <output-directory>` 专用入口。入口要求真实 Windows、GitHub Actions/github-hosted、显式 opt-in 和仓库固定位置的已构建源 fixture，在加载设置或初始化 Avalonia 前检查；直接使用预览场景名不能绕过。预览模式关闭配置持久化，测试不在用户桌面运行。源窗口属于测试新建的独立进程，所有控制前复核 HWND/PID；父应用退出时 native 等待注册会让源进程自行退出，正常退出另检查 exit code。

CI 包装器读取 runner 已安装 Chrome/Edge 的 x64 SwiftShader ICD，检查 PE 架构并记录 JSON/DLL SHA256，只选择测试子进程的 Vulkan 驱动；不启动浏览器、不改系统驱动、不把 SwiftShader 打包进应用。SwiftShader 的 CPU Vulkan 用途依据 [Chromium 官方说明](https://chromium.googlesource.com/chromium/src/+/main/docs/gpu/swiftshader.md)，运行器预装软件见 [官方 Windows 2022 镜像清单](https://github.com/actions/runner-images/blob/main/images/windows/Windows2022-Readme.md)。缺少驱动、初始化失败或设备名不符直接失败，不把环境缺失记成产品测试通过。

联合场景调用原样 `ScalingSession.RunAsync`，没有使用 `_previewLoop` 或注入帧/可见性。GDI/WGC 各运行普通窗口与全屏：实际读取自有 Win32 画面，经软件 Vulkan 和 Avalonia 位图显示，独立核对源窗口/PID、后端及 RGB；实际最小化/隐藏、native 输出不可见、新交付序号/画面恢复、前台源焦点与输出模式/尺寸/位置、截图、隐藏中停止或关闭源、进程清理。强制最低 30 FPS，使暂停期间 GPU 序号不变不能仅靠静态重复帧检测碰巧通过。能力探测不支持 WGC 时明确记录 unavailable/未测试，其他失败不能回退 GDI。

这一新增 Windows GUI/软件 Vulkan 联合门仍需本提交目标 CI 的实际结果，不能用本机 Linux 保护入口、AOT、模型或私有 Wayland UI 的成功代替。记录包含明确的 no input delivery / no hardware Vulkan / no full parity 标记；合成源不代表真实应用、游戏、HDR/DPI 或完整焦点矩阵通过。Windows 生产测试产物在 `artifacts/platform-build-checks/win-x64/production-lifecycle`。
