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
