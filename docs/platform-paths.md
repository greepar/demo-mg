# 三平台实现与验收路径

完整目标仍为全部 Magpie 功能复刻及三平台测试；下表是当前路径和证据，不表示目标完成。

| 路径 | 实现 | 当前证据 | 仍需验证 |
| --- | --- | --- | --- |
| Linux | 原生 Avalonia Wayland；ScreenCast Portal/PipeWire；Vulkan；核心静态链接 | 本机 AOT 发布/执行、GPU 数值回归、验证层、随机链、隔离图形回归；历史 QQ 选源实时画面确认 | 最新版本真实授权/撤销/窗口变化、输入、完整原版效果和剩余功能 |
| Windows | Avalonia Win32；初始 GDI 前台窗口客户端捕获；Vulkan | Windows 目标 MinGW 严格语法检查覆盖捕获、engine 和 tests；MSVC 构建与 AOT 链接分支已编写 | Windows/MSVC 实际编译、GPU 数值、授权/窗口切换、AOT 单文件；WGC/DXGI、光标、游戏输入等仍缺 |
| macOS 14+ | Avalonia 原生 Cocoa；ScreenCaptureKit 系统选源与帧回调；静态 MoltenVK | Apple 官方 API 声明核对；Objective-C++、ARC、框架链接和 Vulkan portability 分支已编写 | macOS SDK 实际编译、签名/应用身份、系统权限、选源/取消/停止、GPU 数值与 AOT 打包；本机无 Apple SDK，尚不能宣称通过 |

## 公共要求

.NET 10 SDK、Python 3.12+、CMake、C++20 编译器。`scripts/build-native.py` 只允许当前主机 OS/架构对应的 RID，避免跨 OS 构建得到虚假的成功。`scripts/verify-platform.py --rid <RID>` 完成：原版预设 fixture 与当前源码核对、shader 校验、NativeAOT 发布、原生 GPU 回归、AOT ABI 冒烟、发布目录单可执行文件检查；不打开窗口或调用捕获授权。

shader 可用 DXC 构建；没有 DXC 的目标机使用已嵌入的 SPIR-V，其原 HLSL、适配脚本与生成头文件的 SHA256 在每次配置时校验。修改效果后必须重新编译 shader，并同步更新 manifest，不能静默使用旧产物。

## Linux

安装 Vulkan/PipeWire/GIO 开发包及 clang/lld/ninja。运行：

```sh
python3 scripts/verify-platform.py --rid linux-x64
```

隔离 UI 使用 `scripts/verify-desktop-isolated.py`；不连接用户桌面。系统 Vulkan loader/驱动、PipeWire、GLib/GIO 等属于系统依赖。

## Windows

需要 Visual Studio 2022 C++ 桌面工具链和 Windows SDK、Vulkan SDK，`VULKAN_SDK` 指向 SDK 根目录。构建脚本使用 Visual Studio 生成器，核心使用静态 CRT。运行：

```powershell
python scripts/verify-platform.py --rid win-x64
```

首个捕获实现仅 GDI，按 Start 后在延时内激活目标窗口；不声称已支持 WGC/DXGI 或受保护/GPU 内容。窗口关闭、最小化、尺寸变化及取消均有处理分支，但须在 Windows 运行验证。系统 Vulkan loader/驱动仍为外部依赖。

## macOS

新克隆仓库推荐使用 [macOS 本机测试步骤](macos-testing.md) 和 `python3 scripts/test-macos.py`：自动准备固定版本的官方静态 MoltenVK/头文件、执行下述验收命令，并生成 ad-hoc 签名的本机测试 app。预编译 shader 以经过校验的 `.h.gz` 存储，构建时解包，不需要 Git LFS 或手动取得 128 MiB 头文件。

需要 Xcode/macOS SDK（支持 ScreenCaptureKit 14+）、静态 MoltenVK 与 Vulkan 头文件。不能将动态 Vulkan 库作为“单文件”静默回退。

```sh
export MAGPIE_MOLTENVK_LIBRARY=/absolute/path/to/macOS/libMoltenVK.a
export MAGPIE_VULKAN_INCLUDE=/absolute/path/to/vulkan/include
python3 scripts/verify-platform.py --rid osx-arm64
```

静态库必须匹配架构（Intel 可用 osx-x64）；框架依赖由系统提供。ScreenCaptureKit 选源放在主线程、等待和取帧在工作线程；回调独立持有共享状态，取消通知等待者并在主线程停止流。实际权限、应用签名与 bundle 元数据仍须目标系统验收。

## CI

`.github/workflows/desktop-port.yml` 只允许手动触发；需要已配置工具链/GPU 的 self-hosted 标签 `magpie-linux-x64`、`magpie-win-x64`、`magpie-osx-arm64`。目前只是补齐可执行的验收路径，**未触发 CI，也没有 Windows/macOS 通过记录**。捕获、真实输入和图形行为要由独立平台测试证明，核心命令通过不能代替。

Apple API 依据：[ScreenCaptureKit](https://developer.apple.com/documentation/screencapturekit)、[系统选源回调](https://developer.apple.com/documentation/screencapturekit/sccontentsharingpickerobserver)。

共用 CTest 现在包含 Anime4K S / Denoise L 的独立标量 CPU 参考：18 组输入，按原始权重、RGBA16F 中间存储及 depth-to-space 核对真实 GPU 输出。该用例无需额外 Python 包，输出报告保存在对应原生构建目录，避免并行测试覆盖其他平台/配置的记录。当前仍只有 Linux 的实际通过证据。

CTest 另包含 FSRCNNX / LineArt 六 pass 的独立标量参考：18 组、含 33×25 跨 32×24 工作块输入，覆盖 PReLU、残差和 YUV 重建。报告为构建目录下的 `fsrcnnx-reference.json`；与 Anime4K 一起在无 DXC 及 sanitizer 路径验证。


CTest 现另含 CuNNy-4x12-NVL 独立 CPU 参考：9 组，原始 RGBA8 UNORM 特征量化、六 pass 与 RGB 残差。原生链测试覆盖 CuNNy 与 RGBA16F 模型混用。报告 `cunny-reference.json`；加入测试入口不代表 Windows/macOS 已实测。


共用 CTest 增加 CRT-Geom 独立标量参考（24 组）和配置式 ABI 检查。`mp_scale_chain_configured` 的 stage 为 84 字节，旧 ABI 保持不变；CRT 96 字节 push constants 在三平台共用 Vulkan 路径。Windows/macOS 仍需实机编译、捕获和交互验收。


设备选择共用 Vulkan 1.1 deviceUUID API，列出 compute + RGBA8 sampled/linear/transfer-dst 可用设备；Automatic 沿用独显/集显优先，明确选中但消失的 UUID 会拒绝开始而不静默换设备。原生测试对每个实际枚举 adapter 分别 dispatch。Linux 已测试 AMD RX 6650 XT 与 llvmpipe 软件 adapter；多物理 GPU、热插拔和 Windows/macOS 路径尚未实测。


语言资源和重启命令属于共用托管路径：20 份原版 resw 均嵌入单文件。AOT core smoke 启动无界面的新进程读取独立语言配置，检查真实子进程的语言初始化；使用字面参数列表，覆盖 native executable / dotnet DLL 的命令构造。正常 GUI 重启生命周期和目标平台字体仍需实机验收。


Vulkan GPU 计时共用 `vkCmdWriteTimestamp` / query pool 路径，按所选 compute queue 的 `timestampValidBits` 和 `timestampPeriod` 换算；不依赖 Windows D3D 查询。默认关闭，不支持或查询不可用时不生成虚假耗时。当前只在 Linux AMD / llvmpipe 验证；不能替代 Windows/macOS 的时间戳能力和目标机运行验收。


缩放模式导入/导出使用共用托管 `ScalingModeInterchange` 和各 Avalonia 平台的 StorageProvider。原版反斜杠效果路径在各系统按同一内置 shader 表映射，不把 Windows 路径伪造为本地路径。原始七个初始预设的 fixture 会与当前 `AppSettings.cpp` 核对，再由 AOT core smoke 执行导入、Vulkan 输出、磁盘保存/失败回滚与导出。Linux 的选文件 UI 未在用户桌面触发，Windows/macOS 的实际文件选择器和运行仍须目标机验收；该路径只提取原配置的 scalingModes，不导入应用身份/自动缩放等其余字段。


窗口呈现规划和追加 Bicubic 为共用托管/Vulkan 路径，新增 v2 configured chain / timestamp ABI 为 16 用户段加系统呈现段预留第 17 段，旧 ABI 长度保持不变。共用 AOT core 和 native CTest 增加完整图像/旧 ABI canary 回归。当前仍只有 Linux 运行证据；Windows 的 MinGW 语法检查不代替 MSVC 编译/运行，macOS 仍需 SDK/目标机。各平台实际 DPI/多屏迁移、等比边框约束及初始倍率的目标机验收仍待完成。


初始窗口倍率与输出对齐为三平台共用托管路径。尺寸规划读取 Avalonia WorkingArea / RenderScaling / 可得 FrameSize；源外框缺失使用捕获内容回退。跨平台 core smoke 包含窗口倍率、九宫格、限制条件和配置磁盘恢复，UI 提供正常/全屏实际 bounds 测试，但当前这些运行证据只来自 Linux。真实 Windows/macOS、DPI/多屏迁移和源窗口外框元数据仍待验收，不能用模型测试代替。


四边自定义裁剪使用共用托管 ROI 规划与 RGBA 缓冲复制，随后执行原生 Vulkan；配置为物理像素，默认/应用独立保存。AOT core smoke 含原版 float/lround、64px 下限、真实 GPU 像素/静止缓存与磁盘恢复测试，隔离 UI 包含真实输出窗口和 PNG 检查。当前仅 Linux 已运行；CPU 裁剪不等于零拷贝，Windows/macOS 和真实捕获裁剪仍须目标机验收。


MagpieFX v4 元数据前端和全部内置源码扫描已纳入 CTest `magpiefx_frontend`，三平台共用门禁现为六项测试。DXC 构建与预编译 SPIR-V manifest 均依赖该前端；它是构建工具，不作为 AOT 运行时依赖。117 个效果的元数据/资源图通过不等于 117 个效果的 GPU 执行通过，FP16/SOURCE/格式的完整后端支持仍未完成。本轮只在 Linux 实际执行，Windows/macOS 门禁仍需各自主机。


NNEDI3 nns16/win8x4 已接入共用 Vulkan 两 pass 路径，原始模型为固定 2x、单通道 R16F 中间存储与二维线程组。默认配置/应用配置、工具栏、效果链及原版模式映射均使用效果 ID 13。CTest 新增独立 NNEDI3 CPU 参考，共七项；当前仍仅 Linux 实际构建运行，Windows 的语法检查不代替 MSVC/实机，macOS 仍需 SDK/目标机。其他 NNEDI3 变体、FP16 算术、完整格式与通用效果执行未因此完成。


SSimDownscaler 现在使用效果 ID 14，五个 PS/CS 混合 pass、RGBA16F/RGBA8 混合纹理和 INPUT/OUTPUT 相对中间尺寸，Oversharp 1..3 可保存并实际修改输出。CTest 共八项，含独立 16-tap 三次插值/局部统计 CPU 参考。当前只有 Linux 实测；共用 API/编译分支不代表 Windows/macOS 运行已通过。全部原效果、完整编译器和 FP16 算术仍待完成。


Anime4K 已增加 Upscale L/VL 和 Upscale Denoise S/VL（ID 15..18）。共用 Vulkan 后端覆盖其四/八 pass 与 14 张特征纹理，独立标量参考扩展到六种 Anime4K 模型、54 组输入。中间半精度存储显式采用最近偶数舍入以统一驱动转换差异；这不等于 FP16 算术。当前仍只有 Linux 实测；Windows/macOS 目标机和其余模型仍待验收。


DDS SOURCE build-time 解码与静态嵌入、Vulkan 首帧上传/只读复用已接入。新效果 RAVU-Lite R2（ID 19）使用原 5×288 RGBA16F 查找表，运行时无需外部 DDS，默认/应用/效果链和原版模式映射可选。CTest 共十项，含原始 17 份 DDS 解码边界和独立 RAVU CPU/LUT 参考。当前实际运行仍只验证 Linux；Windows/macOS、NIS/SMAA/其余 SOURCE 效果、全部格式与完整编译器仍待完成。

## 登录启动（2026-10-21）

设置页的开机启动开关读取实际系统登记，启用后在下次登录启动；是否隐藏主窗口取决于托盘设置及可用性，失败回退开关并显示错误，不额外保存一份可能不同步的 JSON 布尔值。托盘及隐藏启动见下节。

- Linux：遵循 XDG_CONFIG_HOME，写入 `autostart/io.github.magpie.Desktop.desktop`；关闭时删除本应用用户项，存在系统同名项时写入 Hidden=true 覆盖，绝不修改系统项。禁止覆盖无应用标记的同名文件或符号链接。GIO 对第一命令 token 中 `%%` 的解析有兼容问题；可执行文件路径含 `%` 时通过系统 `/usr/bin/env --`（或 `/bin/env --`）传递，实际 GIO 子进程已经验证空格、引号、反斜杠、百分号、美元、反引号等参数保持字面值，工作目录正确。不使用 shell。
- Windows：实现当前用户 Run 登记 `Magpie.Desktop`，Windows 命令参数转义及 260 字符限制；不会修改原版 Magpie 的计划任务、启动快捷方式或注册值。没有实现管理员免 UAC 启动，尚未在 Windows 登记/登录实测。
- macOS：写入 `~/Library/LaunchAgents/io.github.magpie.Desktop.plist`，使用 ProgramArguments、WorkingDirectory、RunAtLoad，无 KeepAlive；启用时不 bootstrap，避免当场打开第二个实例。XML 参数往返已测，launchd 的加载及下一次登录行为需在 macOS 实测。

预览/截图进程不读取或修改真实登记。新增 SettingsStartup / SettingsStartupError 场景注入临时文件后端，覆盖启用、关闭、写入失败的回退和错误提示，测试结束删除夹具。核心冒烟同样只使用临时目录；GIO 子进程只写参数回执，进入 Avalonia 前退出。没有测试真实用户的下一次登录，也没有改动当前用户自启动项。

规范依据：[XDG 自启动](https://specifications.freedesktop.org/autostart/latest/)、[Desktop Entry 命令转义](https://specifications.freedesktop.org/desktop-entry/latest-single/)、[Windows Run 登记](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)、[Apple launchd](https://developer.apple.com/library/archive/documentation/MacOSX/Conceptual/BPSystemStartup/Chapters/CreatingLaunchdJobs.html)。

本轮最终 Linux AOT 验收：10 项原生回归、核心冒烟及 15 个隔离 Wayland UI 场景通过（中文 6、英文 6、深色 3，含窄窗口与失败提示）。汇总见 `artifacts/desktop/startup-test-summary.json`，对应完整发布日志存于 `artifacts/desktop/startup-checks/`。这些测试不替代真实登录启动、Windows 注册表或 macOS launchd 验收。

## 托盘与后台窗口（2026-10-21）

使用 Avalonia 原生 TrayIcon/NativeMenu：Linux 纯 Wayland 走 D-Bus StatusNotifier/dbusmenu；Windows/macOS 走各自原生后端。托盘菜单沿用原版主窗口、全屏倒计时、窗口倒计时、退出四项及资源翻译；倒计时与托盘开关写入现有 AOT JSON。关闭主窗口时只在图标可用时隐藏，保持正在运行的缩放会话；输出窗口取消主窗口 owner，避免被一起隐藏。显式退出、系统关闭、语言重启应结束进程。

Linux 就绪检查通过独立 D-Bus 连接确认宿主已登记、RegisteredStatusNotifierItems 中确有本进程（GetConnectionUnixProcessID）且 Id 为 Magpie 的条目；约一秒检查一次。宿主或 watcher 消失时恢复隐藏主窗口，恢复后重新登记。没有宿主时关闭主窗口正常退出。Windows/macOS 当前依据原生 exporter 创建成功判断，目标系统未实测。

自启动登记新增 `--autostart`，只有设置开启且托盘确实可用才隐藏；初始化时不设置 lifetime.MainWindow，避免异步登记期间自动闪出主窗口；三秒内不可用则显示主窗口。上一版不带该参数的自启动文件/Run 值只读时仍识别，显式开启才升级，不在启动时自动改用户登记。

Avalonia 12.1.1 的 DBusTrayIconImpl.Dispose 在取消 async-void WatchAsync 时可抛未处理 TaskCanceledException。本实现复用一个 Linux 图标及 exporter 到进程结束，关闭选项只隐藏并释放 SNI 名称；其它原生后端仍 Dispose。未通过全局吞异常隐藏问题。

`scripts/verify-tray-isolated.py` 使用私有 dbus-daemon（无 service 目录，禁止自动激活系统 portal/keyring 等服务）、私有 Weston desktop-shell 和 GIO 仿真托盘宿主，直接验证真实 SNI 像素数据、dbusmenu 布局和点击，窗口显示/隐藏、隐藏启动零次 Opened、宿主消失/watcher 重启、设置关闭再开启和最终退出。缩放菜单回调只在显式 probe 中记录，不打开捕获授权；正常运行调用真实 ScalingSession。该测试不证明真实面板视觉效果、Wayland 激活焦点授予或真实登录行为。

测试环境问题：Weston 13 kiosk-shell 在反复销毁/重建 toplevel 时偶发断开，导致 Avalonia 重连和销毁等待。已使用适合多窗口生命周期的私有 desktop-shell，并在等待中检查合成器退出状态；保留失败日志供追踪。调试栈工具仅装在 `.tools/diagnostics`，不进入发布包。

本轮最终验收：Linux NativeAOT 单文件、10 项原生 GPU 回归、核心冒烟通过；私有 D-Bus/Wayland 的 5 个托盘协议/生命周期场景通过，另有 15 个隔离 UI/输出回归（中文 7、英文 5、深色 3）通过。汇总 `artifacts/desktop/tray-test-summary.json`，发布日志 `artifacts/desktop/tray-checks/`，协议日志和回执 `artifacts/desktop/tray-aot/`。Windows/macOS 托盘、真实桌面面板、系统登录、真实输入/焦点和实际捕获中的后台运行仍待目标系统验收。独立输出测试用真实 Vulkan 帧及像素截图，但未调用真实捕获源。

实现依据：[Avalonia TrayIcon](https://docs.avaloniaui.net/controls/navigation/trayicon)、[12.1.1 Wayland 托盘实现](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Wayland/WaylandTopLevelFactory.cs)、[12.1.1 D-Bus 托盘生命周期](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.FreeDesktop/DBusTrayIconImpl.cs)。

## 全局快捷键（2026-10-21）

Linux 使用 GlobalShortcuts portal v1 的 CreateSession / BindShortcuts 和 Activated / Deactivated / ShortcutsChanged，保持纯 Wayland，没有 X11 抓键回退。首页“启用全局快捷键”由用户显式发起系统确认，“停用 / 取消”关闭申请或会话；不在启动时自动弹授权对话框。当前尚未实现应用重启后的自动恢复。Windows/macOS 原生分支随后接入，见下节；仍不能宣称三平台快捷键已完成。

原版三个动作分别映射为 fullscreen / windowed / toolbar。前两个调用真实 StartScaling（零倒计时，运行中停止），工具栏调用当前会话 ToggleToolbar。系统确认的 trigger_description 单独显示，保存偏好不等于已经成功注册；门户可以调整/只接受部分/全部拒绝快捷键。按下去重直到 Deactivated，禁止不同会话、未知动作及不同 D-Bus 发送者触发；绑定后的变更、会话关闭与门户重启会清除旧状态。已注册全局工具栏键时关闭同键的输出窗口本地处理，避免一次按键触发两次；未注册时仍有本地快捷键。

三个动作的按键/修饰符写入 AOT JSON，校验 XDG keysym 映射、重复按键和非法值，修改失败回退并保留编辑对话框。更新偏好会关闭旧会话，下一次绑定必须再次显式启用。全局偏好目前存于 RuntimeSettings 根；应用配置复制进行独立深拷贝。系统实际绑定只在会话中存在，不写伪造“已注册”布尔值。

实现使用 Tmds.DBus.Protocol 静态消息 API（兼容 NativeAOT），先订阅可预测 Request 路径再发起调用，处理先于 method-return 到达的 Response。绑定请求支持取消、120 秒上限、失败后的 Request.Close / Session.Close；固定门户唯一 owner，监听 owner 变化，防止重启后沿用旧绑定。门户没有运行或没有接口时直接显示错误，不自动激活系统服务。

`scripts/verify-shortcuts-isolated.py` 在无 service activation 的私有 D-Bus 中提供真实接口夹具，覆盖成功、部分接受、用户取消、拒绝、非法动作、取消挂起请求、畸形回包、服务重启、挂起时重启和缺少接口共 10 场景。验证真实消息签名、首选触发器、快速响应竞态、系统描述、激活/释放、伪造发送者/会话过滤、ShortcutsChanged、Session.Closed、请求/会话 Close；不初始化 Avalonia、不抓真实按键、不显示系统对话框。UI ShortcutPersistence / ShortcutDuplicate 通过实际按键捕获及保存按钮验证偏好更新/重建页面/重复回退和预览授权保护；这不替代真实桌面键盘和授权测试。

接口依据：[GlobalShortcuts 官方定义](https://github.com/flatpak/xdg-desktop-portal/blob/main/data/org.freedesktop.portal.GlobalShortcuts.xml)、[Request 时序与取消](https://github.com/flatpak/xdg-desktop-portal/blob/main/data/org.freedesktop.portal.Request.xml)、[XDG 快捷键格式](https://specifications.freedesktop.org/shortcuts/latest/)。

此外 `verify-shortcuts-isolated.py --ui` 将同一私有门户连接到隔离的 Avalonia Wayland 窗口，验证 UI SynchronizationContext 下的 BindShortcuts 回包、系统 trigger_description 显示、启用状态及真实信号的去重分派。测试中两个缩放动作只记回执，阻止共享选择器；不接入真实系统总线。该场景只由私有协议 runner 显式调用，普通截图套件排除，普通 preview 仍不能申请系统快捷键。

最终 Linux AOT 验收：10 项原生回归与核心冒烟、10 个快捷键协议场景、1 个门户 UI 端到端场景、15 个其余隔离 UI 场景、5 个托盘回归通过。记录均核对最终可执行文件 SHA256，早于发布完成而启动的旧程序测试已排除并重跑。汇总见 `artifacts/desktop/shortcut-test-summary.json`，发布日志在 `artifacts/desktop/shortcut-checks/`。以上不证明真实桌面授权、物理键盘/焦点、Windows/macOS 原生快捷键或重启自动恢复完成。

## Windows/macOS 原生快捷键分支（2026-10-21，验收进行中）

三平台通过 IGlobalShortcutSession 共用同一套首页、按键偏好、启用/停用、实际绑定反馈与 fullscreen/windowed/toolbar 动作。Linux 保留 portal 路径；Windows/macOS 使用 NativeGlobalShortcuts 事务后端及系统 API，不增加第三方 DLL，也不改动原静态 C++/shader 打包路径。系统 user32/Carbon/CoreFoundation 属于 OS 依赖。

- Windows：复用 Avalonia 窗口的 Win32 消息钩子，RegisterHotKey(hwnd) 注册、WM_HOTKEY 触发、MOD_NOREPEAT 防连发，UnregisterHotKey 清理；隐藏主窗口仍可接收消息。VkKeyScanExW 按当前键盘布局解析字符及隐含修饰键，特殊键采用 Win32 VK；拒绝系统保留 F12，冲突报错。注册、解绑和钩子操作均在 UI 线程。
- macOS：Carbon GetApplicationEventTarget / InstallEventHandler，RegisterEventHotKey 的 exclusive 标志检测冲突，HotKeyPressed/Released 过滤本应用 signature/id 并去重；UnregisterEventHotKey / RemoveEventHandler 清理。TIS ASCII-capable 当前布局的 Unicode 数据副本配合 UCKeyTranslate 解析字符，不假设 QWERTY；独立特殊键/F1–F20/数字键盘映射。LOGO 映射 Command，ALT 映射 Option，系统反馈显示 macOS 名称。部分 OS 保留组合可能失败，须换键且目标系统验收。
- 事务性：整组注册或失败撤销，映射到相同本机键位的动作同样拒绝；中途取消、初始化失败也清理。键盘布局/物理键盘类型变化约一秒检查一次，清理旧注册并提示重新启用；不静默保留旧键位。实际解绑失败清空动作分派、显示清理错误及重启要求，不伪报已成功释放；Carbon 若移除回调失败会固定 delegate/framework 至进程结束，避免释放仍被系统引用的回调。

Carbon ABI 已按 Apple SDK 头文件核对：UniCharCount / ByteCount / ItemCount 是 unsigned long，在 LP64 为 64 位；UInt32 / OptionBits / OSType 为 32 位，UniChar 为 UTF16。P/Invoke 显式使用 nuint 和 U2 数组；`verify-native-shortcut-abi.py` 独立检查 10 项源声明；AOT 运行检查参数类型/宽度。CoreCLR 还检查 MarshalAs/DllImport 伪属性，NativeAOT 不保证对反射保留这些伪属性，因此不以其缺失误判实际编组失败。此检查不替代 Apple SDK 的实际编译、dyld 符号解析、事件循环与权限/保留键实测。

NativeGlobalShortcutTests 通过替身系统 API 验证注册成功、三个不同位置的冲突回滚、初始化失败、键位合并碰撞、中途取消、按下/释放去重、未知 ID、布局变化、正常/重复清理及清理失败反馈。ShortcutNativeReady / ShortcutNativeConflict / ShortcutNativeLayoutChanged 在真实隔离 Avalonia UI 中驱动同一后端，检查状态与动作回执。所有替身测试均不注册系统按键；报告明确 target runtime=false。

API 依据：[RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)、[UCKeyTranslate](https://developer.apple.com/documentation/coreservices/1390584-uckeytranslate)、Apple SDK 的 CarbonEvents.h / CarbonEventsCore.h / Events.h / TextInputSources.h / UnicodeUtilities.h / MacTypes.h 声明（本机仅作文本/ABI核对，没有 Apple SDK 编译环境）。

本轮最终 Linux AOT 包通过：原生快捷键统一后端替身测试、10 项 ABI 源声明检查、10 项原生 GPU 回归、10 个 Linux portal 协议场景、18 个隔离 UI/输出场景（含 1 个 portal UI 端到端）、5 项托盘回归。汇总 `artifacts/desktop/native-shortcut-test-summary.json`，完整发布日志 `artifacts/desktop/native-shortcut-checks/`；所有最终运行记录核对同一可执行文件 SHA256。Windows/macOS 的实际系统 API 调用仍未验证，自动重启恢复仍未实现。

## SMAA / 同尺寸多 pass 路径（2026-10-21）

最终 Linux NativeAOT 单文件包通过发布检查、AMD 与软件 Vulkan 驱动各 11 项原生回归、11 项 ASan/UBSan 回归和 Vulkan 验证层检查；两种驱动各完成 144 组 SMAA 对照。19 个中文、英文、深色及窄窗口 UI 场景均在私有 headless Wayland 中通过，逐项核对最终可执行文件 SHA256。汇总为 `artifacts/desktop/smaa-test-summary.json`，原始记录归档在 `artifacts/desktop/smaa-checks/`。本轮没有调用真实捕获选择器，没有在用户桌面显示测试窗口。MinGW 严格语法检查通过，但不代表 Windows/macOS 实机验证；完整功能复刻仍未完成。

加入原版 SMAA Low / Medium / High / Ultra，分别三 pass：亮度边缘检测、混合权重、邻域混合。两个原 DDS（AreaTex 160×560 RGBA8、SearchTex 64×16 R8）嵌入，四档共享同一份常量数据；加上 RAVU LUT，当前共三个唯一 DDS 数组、370944 字节有效载荷。此轮共 23 个实际 HLSL 效果、77 个 SPIR-V kernel，仍未完成全部效果/完整编译器。

前端执行适配从固定 2x 布尔值改为显式输入相对倍率，新增 1x 同尺寸效果，保留已有 2x 模型。PS 降为 compute 时，按输出纹理组件类型声明返回值，避免把 float2 边缘输出强行当 float4。Low/Medium 使用 RG8 + RGBA8 中间纹理，High/Ultra 使用 RG16F + RGBA16F。FP16 此处是纹理存储，运算仍为 float32；原生 FP16 算术尚未接入。

SMAA 中 `edge == 0`、横纵权重大小判断对插值精度十分敏感，硬件和软件 sampler 的实现差异可放大到明显方向差异。为跨驱动一致性，此适配用显式 clamp/load/lerp 和固定 8 位子像素权重，保留原版搜索、对角线、拐角检测和阈值。量化到 1.0 时向相邻纹素进位并把小数部分归零，避免通过 a+(b-a) 产生微小误差而使相等 UNORM 权重选错方向；UNORM / half 存储也显式规范化舍入。该采样策略属于移植适配，不声称与所有驱动原生 sampler 逐位相同。

CPU 参考测试：`scripts/verify-smaa.py` + `smaa_reference.py` 将原版 SMAA.hlsli 数学代码转换为 C++/GLM 值向量/引用，单独实现纹理 clamp、点/线性采样和格式量化，执行完整三 pass。它与生产 PS→compute lowering 相互独立，但共用原版数学代码，不能称为独立重新推导的算法。DDS 测试载荷也单独解析，未调用生产 DDS 解码器。6 种尺寸（含 1×1、奇数边界和 65×33）、6 类图像（常量、斜线、阶梯、棋盘、随机和变化 alpha）、4 档共 144 例；预先固定最大通道误差 ≤3、平均误差 ≤0.3，未为通过测试放宽。

运行该参考需要 C++20 编译器及 GLM 头文件（Linux libglm-dev、macOS glm；Windows 可用 SDK Include 或设置 `MAGPIE_GLM_INCLUDE_DIR` 指向包含 glm/ 的目录）。CTest 显式传本机 CMAKE_CXX_COMPILER，脚本适配 GCC/Clang 与 MSVC 命令行。Windows/macOS 实际编译和 GPU 对比尚未执行，不能由本机通过推断。

默认/应用配置、原版模式导入导出、链编辑和输出工具栏均可选四档 SMAA；固定输出不接受假尺寸缩放。窗口显示仍按已有规则自动追加 Bicubic，SMAA 本身不放大。托管与原生测试覆盖 DDS 复用、切换/resize、重复帧、格式字节预算、原版元数据保留、四效果十二 pass 链及计时、AOT 配置恢复；原生非法尺寸拒绝时保持调用方输出不变。


## 原版效果批量编译审计（2026-10-21）

构建适配器新增 `--effect <原版路径>`，按传入的效果资源图生成独立 SPIR-V 和 C++ 嵌入头文件，不需要先把效果注册进 UI 或运行时。默认发布清单保持原样；完整重编后的 `kernel.h` 与 SMAA 轮次逐字节一致。图适配新增无中间纹理图的编译、GetScale helper，以及通过 DXC 预处理展开 include/宏后的 OUTPUT 写入转换，解决 NIS / NVSharpen 的 NVTEX_STORE 输出。

批量入口：

```sh
python3 scripts/audit-vulkan-effects.py --dxc .tools/dxc/bin/dxc --spirv-val /path/to/spirv-val --output artifacts/magpiefx/vulkan-audit-final
```

本机全部 117 个原版效果经过实际 DXC 编译审计，其中 98 个、294 个 pass 通过 Vulkan 1.1 SPIR-V 校验；98 份嵌入头文件还通过 C++20 严格语法及 pass 数量断言。19 个效果仍失败，审计按设计返回非零，`results.json` 的 `passed` 保持 false，不把部分通过伪报为完整编译器通过。剩余包含 4 个超出当前资源图上限的效果、10 个超出两参数绑定上限的效果、3 个需要编译期参数特化的双边去噪效果，以及固定 5x/6x 的两个 xBRZ 效果。

编译审计不会证明 GPU 执行、数值正确、性能或运行时注册成功。当前实际应用仍是 23 个 HLSL 效果和 77 个发布 kernel；CRT-Geom 在现有专用路径可以运行，但尚不能通过统一图后端的两参数限制。下一步需要统一参数/特化接口、放宽并核对资源限制，再逐项做图像参考及 UI/配置接入。本轮现有原生 GPU 回归 11 项通过；没有显示测试窗口或申请捕获权限，Windows/macOS 实机执行仍未验证。

完整失败日志及源文件哈希：`artifacts/magpiefx/vulkan-audit-final/results.json`。本轮汇总：`artifacts/desktop/compiler-audit-test-summary.json`。


## 统一图后端的参数布局及 CRT-Geom 迁移（2026-10-21）

统一图 shader 的 push constant 扩展为 96 字节：输入/输出尺寸位于 0..15，parameter1/mode/parameter2/reserved 位于 16..31，四个 float4 位于 32..95。普通图参数 0/1 绑定前两个标量，参数 2..17 依次绑定 extra[0..15]；原有公开配置结构仍是 84 字节，没有改变 C ABI。生成器从原版 PARAMETER 声明生成默认值、范围和整数约束，主机在分配/提交前验证全部已声明槽；旧接口为额外槽使用原版默认值，未使用的前两个旧槽保留原有有限值和 0..1 约束。

CRT-Geom 已从专用 shader 编译分支迁入统一图后端，删除专用编译分支及手写原生参数表。它保留既有 ABI 和原版 JSON/UI：十五个参数仍全部位于 extra[0..14]。原有中心扫描线精确对齐处理也随之迁移。当前发布仍是 23 个 HLSL 效果、77 个 kernel；没有把仅编译通过的其他效果算作应用可运行效果。

无中间纹理图现可实际执行。为让效果切换后所有描述符绑定保持有效，原生帧分配一个 1x1 RGBA8 的绑定占位图；shader 不读取或写入该图。逻辑资源预算增加 4 字节，托管预算同步增加一个 RGBA8 单位。本机早期切换回归曾出现设备丢失，补齐绑定后，CRT 单独运行、CNN→CRT、CRT→Lanczos、重复帧/参数切换及验证层均通过。早期失败日志只作诊断，不列为最终通过证据。

原有独立 CPU CRT 图像参考覆盖 24 组尺寸/参数组合；AMD 和软件 Vulkan 各完成一遍，最大通道误差阈值仍为 1。原生回归增加全部 15 槽的范围/NaN/Infinity 拒绝、五个整数槽的非整数拒绝及失败时调用方字节不变检查；GPU 实际参数验证以此十五参数效果为证，不声称已执行全部十八参数模型。

全量实际编译及 SPIR-V 校验为 107/117 个效果、308 个 pass，107 份生成头文件通过 C++20 严格语法检查。剩余十个为四个超资源图上限效果、三个需要参数特化的双边滤波效果、两个固定 5x/6x 的 xBRZ 效果，以及 Jinc 的向量条件表达式语言兼容问题。审计仍返回非零并保留 passed=false，完整编译器未完成。

最终 Linux NativeAOT 单文件包、两种 Vulkan 驱动各 11 项原生回归、11 项 ASan/UBSan、Vulkan 验证层和 MinGW 严格语法检查通过。10 个中文/英文/深色/窄窗口 UI 场景在私有 Wayland 中验证同一发布包，未显示用户桌面窗口或调用捕获选择器。Windows/macOS 实机验证仍未完成。汇总 `artifacts/desktop/parameter-test-summary.json`，编译失败详情 `artifacts/magpiefx/parameter-audit/results.json`，最终日志归档 `artifacts/desktop/parameter-checks/`。

弹窗位置验收仍有明确缺口：现有预览用 `PopupRoot.PointToScreen - Window.PointToScreen` 合成弹窗，本机 Wayland 返回 0 偏移；因此本轮 UI 场景通过只证明状态/绑定和渲染成功，不能证明原生弹窗位置正确。后续须读取原生 popup 配置或使用合成器截图核验，不能用当前合成图假定位置已对齐。


## Wayland 原生弹窗坐标与预览截图修正（2026-10-21）

此前 `PopupRoot.PointToScreen - Window.PointToScreen` 在本机 Wayland 返回 0，导致预览把弹窗错误合成到左上角。这不是 CRT 参数窗的实际位置：原生协议确认其最终位置是 (987,296)、大小 429×714。预览现在输出基础画面和各 PopupRoot 的单独渲染图层，Wayland 不再使用全局坐标合成。

`wayland_popup_capture.py` 从应用自身的 `WAYLAND_DEBUG=client` 日志读取 `xdg_surface.get_popup` 关系、`xdg_popup.configure`、`ack_configure` 和 `wl_surface.commit`，仅使用截图标记之前已确认且提交的配置。嵌套弹窗按父相对坐标递归累加，核对其最终根窗口、尺寸、比例和窗口边界后再合成。缺少确认/提交、父窗口未知、尺寸不匹配、相同尺寸匹配歧义、超窗、非零窗口几何原点等情况直接拒绝，不能回退为零偏移。侧录日志、图层、JSON 坐标证据与截图均保存在各测试输出目录。

这是用合成器确认的坐标合成应用渲染图层，不是合成器帧缓冲截图；证明布局位置与原生配置相符，不证明遮挡、阴影、屏幕颜色或真实桌面面板交互。当前最终场景的 render_scaling 为 1；不能推断已覆盖其他缩放比例或 Windows/macOS。独立测试 `python3 scripts/verify-wayland-popup-capture.py` 覆盖确认/提交、嵌套、重定位、销毁、准确像素及十种无效证据拒绝。

新证据还发现普通 ComboBox 下拉在屏幕内却越出应用窗口的实际问题（重复帧菜单及窄窗口显卡/初始倍率菜单）。`PopupAlignment` 现在也为属于主窗口的 ComboBox 原生 Popup 应用窗口范围约束，仍保留现有 Flyout 和嵌套弹窗逻辑。最终测试记录见 `artifacts/desktop/popup-test-summary.json`；所有 UI 测试均在私有 headless Weston 中执行，没有用户桌面测试窗口或捕获选择器。

本轮最终同一 AOT 包完成 44 个隔离 UI 场景，核对 46 个弹窗图层的原生位置；中文完整 32 个弹窗清单、英文 6 个、深色/窄窗口/输出 6 个均通过。原生 GPU 回归 11 项及单文件发布检查通过。坐标、trace/图层/最终 PNG 哈希核对已归档；本轮未重跑 Windows/macOS 或其他缩放比例。


## 原版 117 效果全量编译与十个图的独立执行（2026-10-21）

全部 117 个原版 HLSL 效果、345 个 pass 现已通过统一构建适配器的实际 DXC 编译与 Vulkan 1.1 SPIR-V 校验。117 份生成 C++ 嵌入头文件亦通过严格 C++20 语法及 pass 数量断言。构建图上限由 16 个中间资源扩展为 32，固定输入相对倍率上限由 4 扩展为 6；原生初始化检查设备描述符、每 stage 资源数及 push constant 上限，超能力设备明确失败。既有发布清单仍是 23 个 HLSL 效果、77 个 kernel，逐字节重新生成的嵌入 shader 头文件保持一致；未把全量审计结果自动注册到 UI。

Jinc 使用显式 HLSL 2018 语义保留原版 FXC 的逐分量向量条件表达式。三个 Anime4K 双边滤波的数组维度中，SPATIAL_SIGMA 实为原版固定 1.0，并非用户参数：适配器核对原文后将 KERNELSIZE 常量折叠为 Mean=5、Median/Mode=3，保留 intensitySigma 动态绑定。此前“需要参数特化”的描述不准确，本轮已确认仅需折叠固定常量。

独立 GPU 测试进一步发现 Jinc 的归一化 Gather 和 floor(pc-.5) 在采样边界受倒数舍入影响。现在 Gather 用精确整数坐标的 Load 实现，floor 用分解为 16 位分量的整数乘积比较修正，避免要求 shaderInt64 或有符号乘法溢出；中心位置也保留精确对齐。CPU 对照保留原版第四行 src[2][3] 重复取样，未擅自修正原始方程。双边 Mean 对照同样保留原版 KERNELHALFSIZE 为 uint 导致负偏移空间权重归零的语义；这不是理想对称双边滤波的对照。早期理想滤波参考及仅中心修正的失败记录均未列入最终通过证据。

`tests/graph_fixture.cpp` 以生产 `engine.cpp` 和所选图的真实嵌入头文件构建独立测试程序。十个图在 AMD RADV、软件 llvmpipe 各完成 146 组执行：Jinc 96 组及双边 Mean 18 组有独立 CPU 像素参考（固定误差阈值 1，两种驱动本轮最大均为 0）；其余八个图各 4 组，只验证实际 dispatch、重复一致性、pass 计时、非法参数/尺寸的原子拒绝及恢复，不声称其数值已全面正确。八个图包括 ArtCNN C4F32、双边 Median/Mode、Anime4K Upscale UL / Denoise UL、k7 modernAnime FHD x2、xBRZ 5x/6x。相同十个图还完成 Khronos 验证层执行，没有 VUID；执行头文件/SPIR-V 与最终全量审计逐项核对一致。

重现入口（Linux 独立 GPU fixture 的构建脚本目前明确限制 Linux）：

```sh
python3 scripts/audit-vulkan-effects.py --dxc .tools/dxc/bin/dxc --spirv-val /path/to/spirv-val --output artifacts/magpiefx/corpus-release-audit
python3 scripts/verify-corpus-graphs.py --audit artifacts/magpiefx/corpus-release-audit --default-header src/Magpie.Native/shaders/kernel.precompiled.h --output artifacts/magpiefx/corpus-gpu
```

全量编译并不等于完整 MagpieFX 编译器或所有效果可在应用运行。运行时注册/模式与参数 UI、全部效果数值参考、完整表达式/格式/FP16 算术、实际 Windows/macOS 执行仍未完成。最终 Linux NativeAOT 单文件发布及原有 11 项 GPU 回归、11 项 ASan/UBSan 通过；Windows 分支仅通过 MinGW 严格语法检查。本轮未显示任何测试窗口或申请捕获权限，也未把上一轮 UI 结果误记为当前包的 UI 回归。本轮汇总 `artifacts/desktop/corpus-test-summary.json`，全量编译报告 `artifacts/magpiefx/corpus-release-audit/results.json`，原始执行记录归档 `artifacts/desktop/corpus-checks/`。


## Jinc / Anime4K 双边 Mean 的正式应用接入（2026-10-21）

ID 24=Jinc（任意输出尺寸，windowSinc/sinc/ARStrength 三参数）；ID 25=Anime4K Denoise Bilateral Mean（固定同尺寸，intensitySigma 强度 0.01..5）。原版 HLSL 未改，沿用上一轮已验证的整数取样/采样边界修正和固定数组常量折叠；原版 Jinc 重复取样项及 Mean 无符号空间权重语义均保留。发布清单现为 25 个实际 HLSL 效果、79 个嵌入 SPIR-V kernel，仍为 NativeAOT 单文件和静态 C++ 核心。

两个效果可从默认/应用配置、模式编辑器和输出工具栏选择，均可打开参数编辑。通用参数编辑器复用声明范围；Jinc 前两个参数传 parameter1/2，第三个 ARStrength 传配置 ABI 的 extra[0]。默认/应用配置按 `GraphParameters` 字典独立持久化，复制配置深拷贝字典；模式步骤用现有 AdditionalParameters 保留额外参数，已知参数按原版名称导入/导出。双边 Mean 的原版缩放元数据仍可往返保存，但执行固定输入尺寸。描述符占位图预算在托管/原生均为 4 字节。原生及托管校验参数范围/非有限值；旧版配置没有新字典时使用原版默认值。

`GraphFilterTests` 在真实 AOT 路径验证参数到 C ABI、直接/配置调用一致、静止修改参数重算、重复帧跳过、效果链、未知参数保留、独立应用配置和磁盘恢复。正式原生 CTest 新增 `filter_effects_reference`，用注册 ID 24/25 调用生产核心；原有独立 CPU 方程验证 96 组 Jinc 和 18 组 Mean，固定误差阈值仍为 1，本轮 AMD、软件 Vulkan 及验证层三路最大误差均为 0。CTest 现在共 12 项。脚本 `verify-filter-effects.py` 调用目标平台 native_tests，不依赖本机临时编译 fixture；Windows/macOS 的此入口仍待目标系统实际执行。

最终 Linux NativeAOT 包通过 AMD 与软件 Vulkan 各 12 项原生回归、12 项 ASan/UBSan、原有 Vulkan 验证层和新增 114 组效果验证层测试、MinGW 严格语法检查。19 个中文/英文/深色/窄窗口/真实 Vulkan 输出场景在私有 Wayland 中通过，核对同一发布包 SHA256；14 个主窗口原生弹窗图层使用已确认提交的 xdg_popup 坐标证据。输出工具栏参数菜单实际打开/编辑/关闭后，真实图重放和无损截图亦通过。没有用户桌面测试窗口或捕获选择器。

完整 117 效果/345 pass 的构建审计继续通过，但应用仍只有上述已接入的 25 个 HLSL 效果。其余效果的运行时注册、参数/模式 UI、完整数值参考、原生 FP16 运算、游戏输入/零拷贝/自动匹配，以及三平台实际验收等仍未完成。本轮记录 `artifacts/desktop/filter-test-summary.json`，原始日志和各驱动 CPU 对照归档 `artifacts/desktop/filter-checks/`。


## 稳定效果目录与托管/原生元数据统一（2026-10-21）

新增 `src/Magpie.Native/effects.json` 作为全部 117 个原版效果的稳定身份目录。保留原有 0..25 的已使用 ID（6 仍为托管 FSR 组合，无对应 HLSL），其余 92 个效果预留 26..117；它们目前 registered=false，不能据此视为已接入运行。注册状态及后端顺序被显式记录，原先逐项手写的 native EFFECTS/EFFECT_IDS 和 scalar 编译清单改为读取此表。现有 25 个 HLSL / 79 个 kernel 的生成头文件与上一轮逐字节一致，未借此扩大运行能力声明。

`effect_catalog.py` 从原始 MagpieFX / DDS 生成固定倍率、参数标签/默认/范围/步长/整数类型、pass 数和各纹理的输入/输出/常量维度与字节规格。`generate-effect-catalog.py --write` 生成 `GraphCatalog.Generated.cs`；MSBuild 在构建前核对生成文件是否最新。新效果文件必须显式加入稳定目录并保留旧 ID，不能靠排序重排已发布 ID。生成器使用能往返 binary32 的最短十进制，避免将 .01 步长写成 .009999999776...；参数定义和查找表在运行时缓存。

当前 Jinc/Mean 参数、CRT-Geom 的十五参数定义及导入名称、原版效果路径、固定倍率和图纹理字节预算已改用此生成目录。托管预算对每个中间纹理检查 16MP 上限，按实际格式字节累计并折算 RGBA8 单位；无中间纹理的图仍计入 1x1 RGBA8 描述符占位图。SSim 的交叉输入/输出尺寸、NNEDI3 的标量 R16F/双倍高度、RAVU/SMAA 的常量 SOURCE 预算由同一元数据计算，原生执行的设备限制仍由实际 Vulkan 检查。

`native_tests --effect-metadata` 在创建 GPU 引擎前导出实际编译的图表。新增 CTest `effect_catalog` 逐项比较已注册的 19 个图：ID、倍率、pass 数、纹理格式/轴/倍率/字节数，以及每个参数的 binary32 默认值、范围与整数标志。其余六个原版 scalar 效果仍走现有专用 shader 路径。该测试还拒绝 16 种错误目录或参数/纹理表不一致，包括 bool 充当整数 ID、保留/重复/越界 ID、错误注册状态、缺失效果和原生规格不匹配；测试只修改临时副本。

AOT `GraphCatalogTests` 覆盖全部 117 个身份、25 个已注册状态、92 个未注册效果的运行拒绝及原版 JSON 往返保留、参数缓存/步长和混合尺寸/格式/SOURCE 预算。原有全部效果数值测试继续执行，不能用元数据一致替代像素正确性。

最终 Linux NativeAOT 单文件包通过 AMD/软件 Vulkan 各 13 项目录/原生回归、13 项 ASan/UBSan、验证层及 MinGW 语法检查。15 个中文/英文/深色、参数浮层/配置导入/窄窗口/实际输出场景通过；最终构建包 SHA256 与这些 UI 记录一致。测试均在私有 Wayland 中运行，没有向用户桌面显示窗口或触发捕获选择器。Windows/macOS 目标系统运行仍未验证。

复验入口：

```sh
python3 scripts/generate-effect-catalog.py --native-test artifacts/native/linux-x64/Release/native_tests --self-test --output artifacts/magpiefx/effect-catalog.json
```

本轮是全量接入所需的身份/参数/资源规则统一，实际运行仍为 25 个效果。其余 92 个的运行时注册、相应入口与完整数值验证、FP16、游戏输入、零拷贝及三平台实机等仍需继续；仅修改 registered 标志并不构成这些功能的完成证明。汇总 `artifacts/desktop/catalog-test-summary.json`，日志与各驱动记录 `artifacts/desktop/catalog-checks/`。

## 全部原版效果接入实际运行（2026-10-22）

稳定目录中的 117 个原版 HLSL 效果现已全部注册运行，ID 0..25 保持不变，6 仍是托管 FSR 组合；26..117 的 92 个新增效果不再仅作为暂存元数据。实际包嵌入 345 个 SPIR-V kernel，包含 111 个图和 6 个专用 scalar 效果。已更新完整预编译头及所有原版 HLSL/HLSLI/DDS、编译适配器的摘要清单；无 DXC 的目标构建也使用同一完整语料。

原生图渲染器按效果创建描述符布局、管线和资源：保留 shader 的绑定号，只声明该效果使用的采样图和最大同时输出数，避免把全语料最大资源限制强加给每个效果。缓存保留当前链引用及有界的近期渲染器。实际支持的固定输出倍率覆盖 1x..6x、最多 18 个标量参数、32 个图纹理和 16 个内部 pass；配置 C ABI 仍为 84 字节。

默认/应用配置选择器、模式编辑器、输出工具栏均接入完整目录；配置预设共 113 项（保留原有顺序及 FSR 组合），输出工具栏共 118 个效果选项。17 参数的 CRT Easymode 使用原版范围、步长、整数类型，参数页可滚动。窄窗口下拉列表按窗口可用高度和缩放约束；私有 Wayland 的已提交原生弹窗坐标证据确认列表在窗口内。没有参数的效果不显示空参数菜单。

读取旧配置时，已实现的原版暂存步骤会自动恢复为可执行效果，保留原版缩放元数据和未知参数；未知效果或带未知结构扩展的效果继续保持暂存，避免丢失数据。AOT 核心测试覆盖全部 117 个默认效果的尺寸、参数槽及模式导入导出稳定性，以及旧配置升级和幂等性。

新增 `all_effects_execution` CTest 使用生产核心，117 个效果各执行 1×1、7×5、17×13 输入及两种图案，共 702 个案例；检查真实内部 pass、重复执行字节一致、输出尺寸/不透明 alpha 和输入变化响应。另以 17 个不同效果构建混合链，实际 62 个内部 pass，与分段执行逐字节一致。这些是执行与稳定性证据，不能替代每个效果的独立数值参考。原有独立参考阈值保持不变，新增 92 个效果的完整逐像素参考尚未完成。

最终 Linux NativeAOT 单文件包含静态自有 C++ 核心及全部 shader/资源。AMD、软件 Vulkan 以及 ASan/UBSan 各通过 14 项 CTest；全部 702 案例及混合链通过 Vulkan 验证层。Windows 分支通过 MinGW 严格语法检查，Windows/macOS 的目标系统运行仍未验证。16 个中文/英文/深色/窄窗口及真实 GPU 输出 UI 场景在私有 headless Wayland 中通过，12 个主窗口原生弹窗图层均有坐标证据，并核对同一最终可执行文件摘要。未向用户桌面打开测试窗口或捕获选择器。

本轮汇总与日志为 `artifacts/desktop/all-effects-test-summary.json` 和 `artifacts/desktop/all-effects-checks/`。完整目标仍未完成：全部效果数值验收、运行时自定义编译器、原生 FP16 运算、输入/光标、零拷贝、应用自动匹配、更新以及 Windows/macOS 实机测试等仍需继续。

## LumaSharpen / LCAS 独立数值参考与取样修正（2026-10-22）

新增 `scripts/verify-sharpen-effects.py`，用独立 Python 标量方程调用生产核心的 `--original-effect-fixture`，不执行生成的 shader 代码作为参考。LumaSharpen 覆盖四种采样模式、强度 0/默认/最大、clamp 为 0/小值/最大、半径 0/默认/非整数/最大；LCAS 覆盖强度 0/.5/1 以及同尺寸、放大和缩小。输入覆盖 1×1、7×5、17×13、33×25，黑/白/常量/随机/交叉边缘图案及非不透明输入 alpha，共 405 个案例，输出 alpha 必须为 255。误差上限固定为每通道 1 个 8-bit 色阶。

独立参考实际发现硬件线性采样精度差异被锐化放大：LCAS 在 AMD 上出现 3 色阶误差，LumaSharpen 在软件 Vulkan 上出现 2 色阶误差。原版 HLSL、参数及滤波方程不变；构建适配器仅将这两个效果的 INPUT 线性取样与既有 SSim/FSRCNNX/RAVU 路径一样，改为四次显式 Load、夹紧边界及浮点双线性计算，消除硬件亚像素精度差异。修正后 AMD 和软件 Vulkan、Khronos 验证层的全部 405 个案例均通过，最大误差为 1。clamp=0、纯黑 LCAS 等边界也在真实 GPU 路径执行。

此测试作为 `sharpen_effects_reference` 加入三平台共用 CTest，正式回归增至 15 项；测试只需目标平台的 native_tests 和 Python 标准库。三平台工作流追加保留原生 JSON/日志，便于检查逐案例结果；Windows/macOS 实际运行仍待目标机。所有 117 个效果的全量执行测试和既有数值参考继续执行。验证记录为 `artifacts/desktop/sharpen-checks/`；本次修正后重新生成完整预编译头及摘要，并用无 DXC 回退路径发布 NativeAOT 单文件。

本次最终包另外通过 18 个私有 Wayland UI 场景（中文 11、英文 4、深色 3），12 个主窗口原生弹窗图层有坐标证据。新增的 LumaSharpen/LCAS 输出场景实际编辑参数，执行 GPU 效果与自动 Bicubic，验证重复帧跳过、静止窗口改尺寸、整数渲染边界和无损截图。LCAS 输出夹具显式使用 2x 参数，以检查自动追加 Bicubic；其默认 Fit 可直接适配视口。所有 UI 记录核对同一最终 AOT 包摘要，最终包与 15 项三路回归、405 项三路独立参考和验证层记录见 `artifacts/desktop/sharpen-test-summary.json`；`all-effects-test-summary.json` 已同步当前包证据。完整功能复刻目标保持未完成。

## FineSharp 五 pass 与 AdaptiveSharpen 独立参考（2026-10-22）

新增 `finesharp_reference.py` 和 `verify-finesharp.py`，独立按像素实现 FineSharp 的五个阶段：高斯 RemoveGrain11、再次高斯加中值、非线性差分和均衡、XSharpen 修复、边缘控制和 YUV→RGB。每个中间纹理实际按 binary16 最近偶数舍入，参考保留原版色彩矩阵及部分排序网络。原版 `sort9_partial2` 的第八槽不总是数学上的第二大值，使用完整排序替换会改变原版输出；参考按原版网络执行，源码保持不变。

180 个案例覆盖 1×1、7×5、17×13、33×25 以及 3×576/3×577 输入，后两者实际验证 BT.601/BT.709 分支边界；五种黑/白/常量/随机/交叉边缘图案，六组默认/零值/最大/独立开关/组合参数。测试夹具的单轴上限从 128 扩到 768，同时保留输入总像素 16384 上限，以允许高而窄的矩阵边界案例。最大误差上限仍为每通道 1 个 8-bit 色阶；AMD 最大误差 1，软件 Vulkan 最大误差 0。

新增 `adaptive_sharpen_reference.py` 和 `verify-adaptive-sharpen.py`，独立实现原版 AdaptiveSharpen 的 25 点采样、亮度、边缘压缩、自适应负拉普拉斯权重及 tanh 软限制。96 个案例覆盖四种含奇数/跨工作块的尺寸，黑/白/常量/随机/交叉边缘/单点脉冲六种图案及四档强度（0/.3/.8/2）。AMD 与软件 Vulkan 全部逐像素一致。两种参考均验证输出 alpha 固定为 255，输入使用非不透明 alpha；生产夹具还验证重复执行、实际内部 pass 计时、各参数非有限值拒绝、失败输出不变与恢复。

FineSharp 和 AdaptiveSharpen 均通过 Khronos 验证层的全部新增案例。两项参考以 `finesharp_reference` 和 `adaptive_sharpen_reference` 加入三平台共同 CTest，原生回归现为 17 项。连同 LumaSharpen/LCAS，`src/Effects/Sharpen` 目录的四个原版效果共覆盖 681 个独立数值案例；NIS/NVSharpen 等其他目录效果仍需要各自的完整参考。Windows/macOS 目标机执行仍未验证。

本轮只补测试夹具与独立参考，运行核心及已发布 NativeAOT 单文件的源码/嵌入资源和可执行文件摘要均与上一轮一致。沿用上一轮 18 个隔离 UI 场景的同一包证据。原始记录见 `artifacts/desktop/sharpen-complete-checks/`，最终汇总见 `artifacts/desktop/sharpen-complete-test-summary.json`；其他未完成的核心编译、FP16、输入、零拷贝、自动匹配、更新及三平台实机验收保持在功能表中。

## 原生 Float16 算术、设备协商与 UI（2026-10-22）

原始语料声明 `CAPABILITY FP16` 的 42 个效果现有独立精度版本：以 SM 6.2、`-enable-16bit-types`、原版 `MP_FP16` 分支及真实 `half` / half 向量矩阵编译。保留全部 345 个 FP32 kernel，另嵌入 232 个 Float16 kernel，共 577 个；FP32 SPIR-V 与之前逐字节一致。完整构建和无 DXC 回退均包含两套资源，最终用户不需要额外 shader 文件或编译器。`verify-fp16-shaders.py` 逐 kernel 核对 Float16 capability、16-bit 类型、RTE16 执行模式及实际指令，并对全部 577 个模块执行 spirv-val。40 个效果含原生半精度算术；原版 NIS/NVSharpen 的半精度分支使用半精度系数转换，其主运算仍按原版使用 float，不能将它们宣称为全程半精度算术。

Vulkan 实例按 loader 能力使用最高 1.2（仍支持 1.1 设备）。通过核心 1.2 或 KHR 扩展查询并启用 shaderFloat16，另查询 RTE16 浮点控制；1.1 设备显式启用所需 KHR 扩展。后端可用条件是 shaderFloat16 和 RTE16 均支持。新 C API `mp_create_with_precision(identifier,mode)` 中 0=FP32、1=自动/回退、2=强制原生 Float16；精度在引擎生命周期内固定。`mp_fp16_supported` 查询本后端要求，`mp_effect_uses_fp16` 查询实际效果分支。旧构造入口保留 FP32 行为，配置 ABI 仍为 84 字节。没有 FP16 声明的效果在自动模式下继续使用 FP32。

实际比对发现，AMD 与软件 Vulkan 对 `mad` 的融合/分步执行选择不同，会让多层 CNN 的结果出现额外差异。中间纹理回读核对了两种执行模型。现在原版 MulAdd 辅助函数在 FP16 版本中明确执行两次真实半精度运算，乘法与加法分别舍入，并以 precise/NoContraction 保留这两个步骤；所有半精度模块显式要求 RTE16。没有把 CNN 累加悄悄改回 FP32。此规则允许在两种驱动间使用同一独立参考，也意味着 MulAdd 采用两个半精度指令而非单个融合指令；性能仍需具体设备基准。原版源文件和模型系数保持不变。原版 Fma 的实现允许不同融合行为，见 [Khronos GLSL.std.450 Fma 规则](https://registry.khronos.org/SPIR-V/specs/unified1/GLSL.std.450.html)；舍入能力和模式见 [KHR 浮点控制](https://github.com/KhronosGroup/Vulkan-Docs/blob/main/appendices/VK_KHR_shader_float_controls.adoc)。

软件 Vulkan 另有 RGB skip connection 的 UNORM 线性采样精度差异。在 FP16 Anime4K 的该路径中，使用显式 Load/浮点双线性插值保留原来的插值方程和边界行为，消除硬件过滤的额外量化。独立参考扩展到原版六种 Anime4K 的 54 个四/八 pass 案例，分别舍入 half 权重、输入激活、乘积、累加、中间纹理及最终 MF4 转换，并保留原版 RGB 残差连接。FP32 参考和原有误差阈值不变。

应用会话默认按设备及原版声明自动选择 FP16；主页高级设置中的“禁止在着色器中使用 FP16”已绑定真实配置，替换原先隐藏区域的空控件。设置全局生效，保存在 AOT JSON 中，下次启动缩放时应用；当前会话精度保持固定。AOT 核心测试验证全部 117 个身份的实际精度选择、42 个声明、两种偏好的磁盘恢复，以及 FSR EASU/RCAS 分段与组合执行逐字节一致。

共同 CTest 新增 `precision_policy` 与 `fp16_runtime`，总计 19 项。前者覆盖不支持能力时的自动回退、强制拒绝、未知模式与空 ABI；后者实际查询目标设备，验证旧入口/自动/强制策略，执行全部 117 个效果的 702 个自动精度案例及 17 段混合链，在支持设备上再执行 54 个独立 FP16 数值案例。不支持的目标设备会明确记录 FP16 数值/执行未测，并检查真实回退，不将跳过冒充 FP16 成功。运行失败会写终态 failed，避免留下错误的 running 报告。

本机 AMD RADV 与 llvmpipe 均支持上述精度路径；缺少 Float16/RTE16 的真实物理设备尚未实测，回退策略与 ABI 拒绝已测试。Windows/macOS 的目标系统执行仍待验收。完整语料的 FP16 独立数值参考、运行时自定义效果编译、输入/光标、零拷贝、自动匹配、更新等仍未完成。本轮记录在 `artifacts/desktop/fp16-checks/`，最终汇总在 `artifacts/desktop/fp16-test-summary.json`。

最终验收：AMD、软件 Vulkan 和 ASan/UBSan 各 19 项 CTest 全部通过；两种驱动的自动精度路径均实际选择 42 个 FP16 声明分支，并通过 702 个全效果执行案例和 17 段混合链。六种 Anime4K 的 54 个 FP16 独立参考在 AMD、软件 Vulkan、内存检查及验证层中均通过，最大误差 1；默认 FP32 参考继续通过。577 个 SPIR-V 模块全部通过验证，独立编译入口另覆盖 FSR 两项、NIS、Anime4K 与无 FP16 声明的 Jinc，5 个效果/8 个原始 pass/15 个精度 kernel 均编译验证通过。实际 native 图表核对 111 项和 17 项损坏元数据拒绝通过，Windows 分支通过更新后的 MinGW 严格语法检查。

最终 NativeAOT 包另通过 15 个私有 Wayland UI 场景（中文 8、英文 4、深色 3），包括精度偏好的两次切换、页面恢复、窄窗口、FP16 与 FP32 实际输出、参数浮层及模式链。5 个主窗口原生弹窗图层具有已提交坐标证据；所有场景核对同一最终可执行文件摘要。没有向用户桌面显示测试窗口或捕获选择器。最终包为 81,897,448 字节，SHA256 `07e6eb0bdd1a8a06f9b3193c00aeb50c890f18f5afcdd17fc1f809986f7f4a2a`。检查记录与当前源码摘要见 `artifacts/desktop/fp16-test-summary.json`，`all-effects-test-summary.json` 同步此包状态；完整功能目标仍未完成。

## 缩放期间屏幕常亮与生命周期（2026-10-22）

原版 `KeepScreenOnHelper` 在缩放窗口创建后申请 DISPLAY_REQUIRED / SYSTEM_REQUIRED，并在窗口销毁时释放；`AppSettings` 默认值是 false。本次把原来界面上固定为 true 的空开关改为真实 `KeepScreenOn` 偏好，默认关闭，支持配置保存/旧配置默认值、页面恢复及运行中切换。偏好是全局项，只有实际缩放收到第一帧、输出窗口创建后才申请常亮，倒计时、选择器、未收到画面及仅修改设置时都不申请。

Linux 使用纯 D-Bus 的 `org.freedesktop.portal.Inhibit.Inhibit`，flags=12（Suspend/Idle），理由为本地化的应用描述，返回的 Request 保持整个缩放生命周期；关闭时调用 Request.Close 并断开其专属连接。此方法的成功路径不等待 Response，避免将生命周期 Request 当成普通交互请求而永久等待。方法和信号绑定实际服务唯一名，检查返回路径与本客户端的 token 路径一致，拒绝外来路径；忽略其他发送者的信号。观察服务消失/替换，失效后更新状态并重试，普通不可用错误最多每 30 秒重试。相关协议见 [XDG Inhibit](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.Inhibit.html) 与 [Request 生命周期](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.Request.html)。Linux 对 Suspend 请求及用户主动休眠的处理取决于桌面策略，尚未进行真实系统策略验收。

Windows 使用句柄作用域的 PowerCreateRequest / PowerSetRequest（DisplayRequired=0、SystemRequired=1），避免异步线程切换破坏线程作用域的常亮状态。显式清除两个请求并关闭句柄；部分申请失败时关闭已有句柄回滚，关闭失败保留所有权以重试，已清除的请求不会重复递减。REASON_CONTEXT 在支持的 64 位目标上显式为 32 字节，版本/标志/UTF16 指针偏移为 0/4/8，理由内存在 PowerCreateRequest 返回后释放。签名、枚举、结构布局通过 MinGW 实际 Windows SDK-header 编译检查。系统请求的策略与限制见 [Microsoft PowerSetRequest](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-powersetrequest)。Windows 实际运行仍未验证。

macOS 使用 IOPMAssertionCreateWithName 创建 PreventUserIdleDisplaySleep，level=255；Apple 明确此断言同时阻止空闲系统休眠。使用真实 CFString UTF8 及对应 CFRelease，停止时 IOPMAssertionRelease；释放失败保留 ID，以便重试。声明中的 IOReturn/AssertionID 为 32 位，CF 引用为指针宽度。此路径已编写并通过事务替身和 ABI 声明检查，不能代替 macOS SDK/实机运行。接口与策略见 [Apple IOPMLib 声明](https://raw.githubusercontent.com/apple-oss-distributions/IOKitUser/main/pwr_mgt.subproj/IOPMLib.h) 和 [AssertionLevelOn](https://developer.apple.com/documentation/iokit/1557096-assertion/kiopmassertionlevelon)。

申请/释放由 `ScreenAwakeController` 管理，帧工作线程只提交异步更新，不等待 portal 服务响应。修改偏好会取消旧的待完成申请；成功但迟到的申请立即释放。服务拒绝或不可用时缩放继续，输出工具栏显示简短提示，详细原因写日志。停止、捕获异常、等待中的取消均进入同一生产 finally 清理路径。暂时释放失败会重试，持续失败的对象由后台清理任务保留所有权继续释放，不重新申请。操作系统的电源策略可影响实际效果；本实现不修改用户电源配置。

AOT 核心测试覆盖关闭时零系统调用、同一请求跨帧复用、失效/恢复/退避、重复禁用/停止、取消后的迟到申请清理、释放失败重试和保留所有权，以及 Windows 双请求部分失败回滚、清除/关闭失败、macOS 释放失败及设置磁盘往返。`verify-power-abi.py` 检查九项原生声明，并可使用 `--windows-cxx` 验证实际 Windows 头文件。该 ABI 检查加入三平台共同 `verify-platform.py`；Windows/macOS 仍需在原生宿主实际构建并运行。

私有协议测试 `verify-screen-awake-isolated.py` 使用临时 dbus-run-session，验证普通无 Response 成功、可选成功信号、开关重申请、伪造信号、方法拒绝、早到拒绝信号、外来路径、版本不支持、等待时取消/服务重启、运行中切换服务所有者及关闭失败后的客户端断开，共 12 个场景。过程中发现并修正了通知值与 completion 的区分，避免其他 D-Bus 名称变化被误当异常而破坏连接。所有检查均为真实线消息往返，未在用户桌面总线上申请常亮。

隔离 UI 测试以受保护的合成捕获循环进入真实 ScalingSession 的 Stop/finally 路径，验证运行时关闭/重新启用、待申请时工作循环继续、不可用提示以及捕获失败后的释放；系统后端使用明确的测试替身。完整运行需要的实际捕获和系统电源策略仍应在目标平台验收，不能把这些协议/替身测试称为实际屏幕常亮成功。复验入口：

```sh
python3 scripts/verify-screen-awake-isolated.py --app artifacts/desktop/linux-x64/Magpie.Desktop
python3 scripts/verify-power-abi.py --output artifacts/desktop/power-abi.json
```

当前核验记录见 `artifacts/desktop/awake-test-summary.json` 和 `artifacts/desktop/awake-checks/`。没有向用户桌面打开测试窗口、捕获/共享选择器，也没有改变本机的屏幕或休眠状态。

最终记录：当前桌面的只读属性查询返回 Inhibit portal 版本 3，未向其发送 Inhibit 请求。最终 AOT 包通过 12 个私有协议场景、15 个隔离 UI 场景（中文 8、英文 4、深色 3）及九项 power ABI 检查/Windows SDK-header 编译；设置/资源事务/释放故障恢复在最终 AOT 核心测试中通过。所有协议/UI 记录核对同一最终包摘要。当前 AMD 原生 19 项回归通过；原生渲染器、shader 和 native_tests 与 FP16 基线逐字节/摘要一致，软件 Vulkan 与 ASan 的既有 19 项证据按此范围沿用。Windows/macOS 的真实原生电源请求与 Linux 的实际息屏/休眠行为仍未验收。

最终文件为 82,043,720 字节，SHA256 `cd67f50b9be51c95063d41b747770d25d9cff7d3b262c0db1c9ded5c1b2646d9`；`awake-test-summary.json` 保留本轮范围和实机测试缺口，`all-effects-test-summary.json` 已同步当前包。完整 Magpie 功能目标继续保持未完成。
