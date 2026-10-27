# 原生输入映射与源身份

本阶段为真实输入路径补基础和边界保护，不表示 Windows/Linux 输入转发已完成。

原版 Windows 的 `CursorManager` 与 `ScalingWindow` 使用透明/不激活窗口、系统光标位置/裁剪和前台源跟踪维持实际输入。直接 PostMessage 鼠标/按键不能替代该路径，特别是子窗口、IME、相对鼠标及游戏。后续 Windows 移植仍需实现该原生路径与软件光标，不能把下面的模型或 GDI 捕获测试算作输入送达验收。

`input_mapping.h` 将输出图像矩形、归一化源裁剪和源坐标系分开。非拖拽事件必须在图像内；已捕获拖拽允许跨出图像，但映射结果保持在源裁剪的半开区域内。原 macOS 路径把归一化位置直接夹到 1，可能得到源右/下边界本身；现在用同一 helper 夹到该边界之前的可表示坐标。没有假定源逻辑坐标等于捕获像素，也不猜测 DPI、阴影或标题栏高度；这些真实系统单位/命中矩阵仍需目标机检查。

CPU 用例覆盖显式裁剪、负显示器原点、分数输出倍率、源中心/近边界、越界拖拽、不允许的普通越界事件、空/非法区域和非有限坐标，失败不改调用者结果。macOS 事件桥实际调用该 helper，仍保留原事件的键码、滚轮相位、点击计数和压力。

Windows GDI 在选择源时记录 HWND/PID/线程 ID；取帧前、BitBlt 后和交付前复核，元数据查询也复核同一身份。源失效时暂停 CaptureEpoch，避免旧已显示帧继续通过身份检查。实时标题查询发现最小化或当前客户区几何不匹配时，暂停代次直到新帧交付；拒绝普通过期序号本身不暂停仍然有效的新帧。相同 HWND/PID/线程全部被复用的生命周期仍需额外原生窗口销毁跟踪，不能将这个元组检查视为完全解决句柄复用。

## 一次性 Windows 捕获测试

`windows_capture_fixture` 仅构建于 Windows，并要求显式环境 opt-in；不加入默认 CTest。CI 包装器另要求 GitHub Actions 的 github-hosted Windows 环境，其他机器直接拒绝，无用户桌面回退。

测试启动独立源进程绘制固定 RGB，通过实际前台选择和 GDI API 检查 RGBA/尺寸、窗口/PID、动态标题、resize 与旧序号拒绝、最小化后必须新帧才能恢复、源进程退出和清理。job 对象把源进程寿命绑定到测试，超时杀掉父进程也会清理子进程。这里只在一次性 runner 出现测试窗口；本机 Linux 不启动测试窗口或系统选择器。产物是 `windows-capture-fixture.log/json`，显式记录没有测试 Vulkan、输入注入或真实应用兼容性。

原生 fixture 可以在最小化失败后手动恢复并继续调用 capture API；当前 Desktop 会话仍会将该错误作为停止缩放处理，自动恢复的产品行为仍是缺口。模型/语法检查也不能证明 fixture 已在 Windows 跑通。

## 本机证据与边界

Linux 最终 NativeAOT 核心、23/23 CTest 与新增分数倍率用例的最后 source 回归通过。五项中英隔离 Wayland UI 复验实际 Vulkan 输出、源/画面归属和 profiler；捕获像素/身份与指针是注入数据。Windows 三个源文件通过 MinGW 严格语法检查，CI 包装器在非托管 Windows 环境的拒绝路径已核对。报告 `artifacts/desktop/input-mapping-test-summary.json`。本提交 Windows/MSVC fixture 和新 macOS Objective-C++ 编译/事件送达仍需对应目标结果，不沿用上一提交的成功。


## 销毁事件与捕获寿命

新增 WindowsSourceWatch，按选定源的进程/线程订阅顶层窗口销毁事件。独立 native 线程持续处理 USER 消息，回调只更新该 capture 自己的原子状态；不使用全局共享 capture 指针，不向源进程注入 DLL，不改写源窗口属性。停止时唤醒自己的 kernel event，在线程内卸载 hook，再 join 和释放 handle；消息泵异常停止会让源验证失败。启动必须完成订阅，再复核源身份，重复 Start 要求创建新 capture。

收到匹配的顶层/self 销毁事件后，该捕获寿命永久失效；以后相同的 HWND/PID/线程数值不能把它变回有效。子对象、其他窗口或其他源线程的销毁不会失效所选顶层窗口。新 capture 是独立寿命，旧回调不会清掉它。WinEvent 属于异步通知，尚不构成对事件送达前所有句柄竞态的原子保证，也未替代前台、子窗口/弹窗、幽灵窗口及原生输入路径的完整跟踪。

依据：[Microsoft SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook) 对注册线程消息循环及 out-of-context 回调顺序的规定。这里使用纯 native 回调，因此没有托管 delegate 移动或附带 callback DLL 的依赖。

`1f9343d8` 的 [Windows job 121588033512](https://github.com/greepar/demo-mg/actions/runs/40327539014/job/121588033512) 已实际通过原始 GDI fixture：独立源进程、绘制 RGBA、窗口/PID、标题、resize/最小化后新帧约束、源关闭与清理。这更新了上一节历史阶段的“待运行”状态，不能用来证明当前新增 watcher 已在 Windows 跑通。

当前 fixture 增加由源进程主动发出的销毁 WinEvent，窗口本身仍存活且原身份元组相同；旧 capture 必须在收到事件后拒绝帧和标题，新 capture 重新选择相同源后必须正常工作。该事件是注入的生命周期测试事件，并非模拟鼠标/键盘；实际 WinEvent 注册、消息投递、永久失效及独立重捕获均需该提交目标结果。包装器检查新增覆盖标记，旧 fixture 的成功字样不能冒充本轮通过。

本轮 Linux 最终 AOT 核心、23/23 CTest 和三项私有 Wayland UI 通过；纯模型验证相关/无关销毁、相同数值身份持续拒绝及新寿命独立性，Windows MinGW 严格语法通过。报告 `artifacts/desktop/source-lifetime-test-summary.json`。本次 watcher 的 MSVC/native 运行、实际关闭/recreate 的系统竞态和 Windows 鼠标/键盘转发继续待验收，完整目标未完成。


## Linux PipeWire 帧归属与流目标

Linux 捕获现在在向 C ABI 交付新像素时递增帧序号；生产者收到帧、覆盖未交付帧和等待不会提前递增。使用共用 CaptureEpoch：格式重协商、流暂停/断开、Portal Closed、取消、空/损坏帧先丢弃待交付画面并暂停旧状态；恢复需要新交付帧。普通同尺寸帧延迟仍允许同一状态内的已提交序号。ABI 返回的最近一次像素缓冲在等待、生产者更新或暂停时保持有效。

ScreenCast 返回 `pipewire-serial` 时，严格按 uint64 解析，通过 `PW_KEY_TARGET_OBJECT` 连接该 object.serial，连接节点参数使用 PW_ID_ANY，避免用可复用节点 ID 作为长期身份。明确存在但类型错误或为零的 serial 报错，不悄悄退回节点 ID。旧版 Portal 没有该属性时保留原始节点 ID 兼容路径；这一路径不能宣称具有 serial 的身份保证。流设置禁止默认目标回退、移动和目标销毁后重连；运行中错误会锁存到该 capture，会话关闭后不再发布画面。这些属性依赖 PipeWire/session manager 的实施，不能仅凭配置就视为真实复用竞态已验收。Portal 没有给出的 PID、窗口句柄和标题仍保持未知。

生产缓冲路径请求可选 SPA Header，检查 Header 的 corruption/gap/codec-header/delta-unit 和 Chunk 的 corrupt/empty；不把这些数据当作可交互画面。discontinuity 开始新状态，允许其自身的完整原始像素作为恢复帧。没有 Header 的旧生产者仍可发布已校验的原始像素。格式清除后不能沿用之前的尺寸解释后续缓冲。

`capture_wayland_frames` 直接调用生产路径使用的缓冲实现，构造真实 SPA/GVariant 数据结构，覆盖带偏移/行填充 BGRA、RGBA、生产者和 ABI 缓冲分离、帧序号、损坏/空数据、discontinuity、格式变化、暂停/恢复及无选源 native ABI 的取消。它没有启动 Portal、PipeWire 服务或输入注入，不能算系统捕获/输入验收。Linux CI 的非 GPU 门要求新增用例实际执行且未跳过；Windows/macOS 的适用用例数保持不变。

协议依据：[ScreenCast v6 流目标](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.ScreenCast.html)、[PipeWire stream target](https://docs.pipewire.org/page_streams.html)、[session manager 属性](https://docs.pipewire.org/page_man_pipewire-props_7.html)。Linux 输入桥仍未完成；通用 RemoteDesktop/EIS 授权并不自动解决源窗口焦点、输出遮挡、注入回到自身、源逻辑坐标和 PipeWire 像素尺寸差异。

本轮本机 Linux NativeAOT 核心、全部 24/24 CTest、托管交互模型及 SDK/AOT/单可执行文件依赖门通过。报告 `artifacts/desktop/wayland-frames-test-summary.json` 与对应 evidence 目录；没有重新验证真实捕获或输入，没有新增 UI 验收声明。NNEDI3 本轮核心通过仍不表示已解决已知偶发异常。


## Linux 实际 PipeWire 传输回归

增加 `wayland_capture_fixture` 与 `verify-capture-pipewire-isolated.py`，通过私有且不启用服务激活的 D-Bus、合成 ScreenCast 服务、真实 PipeWire daemon 和自己生成的像素流调用生产 `mp_capture_start/frame/source/frame_sequence/cancel`。OpenPipeWireRemote 真实传递 Unix FD；捕获端仍执行生产的请求订阅、连接、格式协商、内存映射和缓冲转换。没有连接用户的 PipeWire、显示服务器或真实桌面 Portal，不请求系统权限、不产生窗口或注入输入。

测试发现并修复了生产格式列表中的遗漏：SPA Enum 的第一项只是默认值，其后的值才是允许集合。原来的 `BGRA, RGBA, BGRx, RGBx` 没有把 BGRA 纳入允许集合，固定 BGRA 源报 `no more input formats`。现在默认值之后列出四种格式，BGRA 作为默认和允许值各出现一次。使用同一个当前夹具链接 `59d5e2f3` 的捕获实现可稳定复现失败；修复实现实际通过四种固定格式，日志逐项验证协商格式，不能用场景名称替代格式证据。

六个场景包括 serial 的 BGRA/RGBA/BGRx/RGBx、无 serial 的旧节点 ID 路径、错误 serial 类型拒绝。serial 场景故意返回不可用的旧节点 ID，实际节点 object.serial 保持正确；从 PipeWire node info 检查目标 serial 与禁止回退/重连/移动属性后，夹具自己建立准确的端口链接。多个场景中实际 node ID 与 object.serial 不同。这个连接器不是 WirePlumber，不等于真实 session manager 的自动连接、销毁重连或默认目标策略已验收。

每种成功格式都验证带行填充的完整 RGBA 像素、连续颜色变化、37×23→81×47→13×9→37×23 尺寸变动、生产期间/等待期间已交付 ABI 缓冲不被修改、源停止驱动后等待与恢复新帧、corrupted Header 停止交付与新帧恢复、递增交付序号、Portal 未提供的窗口/PID 保持未知。serial 场景关闭 Portal 会话后必须拒绝画面；旧版路径测试 native cancel 后拒绝画面。错误 serial 在打开远端 FD 前拒绝，所有会话 Close 与子进程退出均检查。

该测试加入 Linux `capture_portal_pipewire` CTest 和非 GPU CI 门；Linux runner 安装 PipeWire daemon、系统 PyGObject 和 dbus-daemon，输出独立 JSON/日志，Windows/macOS 仍只执行各自适用的测试。最终本机全部 25 项原生测试通过；后续夹具边界/暂停检查的单项回归也通过，NativeAOT 核心、交互模型、单可执行文件与静态自有核心依赖检查通过。证据汇总为 `artifacts/desktop/pipewire-capture-test-summary.json`；修复前失败现场在 `artifacts/desktop/pipewire-capture-before-fix`。以上只补齐合成源的真实传输证据，真实应用捕获、权限、输入送达、焦点、光标与游戏模式没有因此通过。NNEDI3 偶发异常仍未解决。
