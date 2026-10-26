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
