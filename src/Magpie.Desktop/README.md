# 三平台实现与验收路径

**在 Mac 上测试：见 [macOS 本机测试步骤](../../docs/macos-testing.md)。** 安装 .NET 10 SDK、Python 3.12+、CMake 和 Xcode 工具后，从仓库根目录运行 `python3 scripts/test-macos.py`；脚本下载并校验静态 MoltenVK，执行真实本机构建/核心测试，再生成本机测试用 `Magpie.app`。

当前状态（2026-10-22）：全部 117 个原版 HLSL 已接入运行；345 个 FP32 与 232 个原生 Float16 kernel 预编译嵌入，42 个原版 FP16 声明分支可按实际设备能力自动选择。主页的禁用 FP16 设置已接通并持久化。完整功能验收仍未完成，详见 [功能表](../../docs/feature-parity.md) 及 [平台验收路径](../../docs/platform-paths.md)；下文早期阶段记录不代表当前能力上限。

屏幕常亮控件已接通真实系统申请：设置默认关闭，缩放开始收到画面后申请，运行中切换、停止与异常均清理；Linux 通过私有 D-Bus 协议测试，Windows/macOS 原生分支仍需目标机验证。测试不改变本机电源状态。

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


缩放模式页右上角菜单已接原版 JSON 导入/导出。可导入原版导出的模式文件，或从原版 config.json 提取 scalingModes；其余全局/应用字段不会导入。导入追加模式、保留原选择，同名项自动加后缀；未知效果保存原始配置并禁用运行，可导出或显式替换。原版空模式保留为空、不可启动，不补入虚构效果。导出包含当前七个内置模式与自定义模式，本地文件写入采用临时文件后替换。自动测试禁用系统选文件窗口。


呈现：窗口模式按源比例计算 renderer rect，并自动追加 Bicubic（B=0/C=0.5）使固定 2x CNN 等输出适配该区域；全屏只对过大的输出做 Fit 降采样。静止源 resize 可以增删系统重采样段，最多 16 用户段加 1 呈现段，GPU profiler 的 v2 查询覆盖全部 17 段。图像使用整数物理像素居中。源外框/源屏幕定位、边框等比拖动和真实 DPI/多屏迁移仍待接入/验收；初始倍率和对齐见下文。


窗口配置：初始倍率支持原版 Auto、1.25/1.5/1.75/2/3x 和自定义值，默认与应用配置独立保存。收到首帧后才创建输出窗口，初始偏好只使用一次；正常拖动和全屏切换保留当前尺寸。输出对齐的九个位置已接实时布局，位置变化不重跑 Vulkan。Auto 使用当前输出显示器所报告的工作区；源外框元数据不可用时，最小留白以捕获内容尺寸回退计算，不能据此声称源窗口外框/跨显示器行为已完全一致。


四边自定义裁剪使用共用托管 ROI 规划与 RGBA 缓冲复制，随后执行原生 Vulkan；配置为物理像素，默认/应用独立保存。AOT core smoke 含原版 float/lround、64px 下限、真实 GPU 像素/静止缓存与磁盘恢复测试，隔离 UI 包含真实输出窗口和 PNG 检查。当前仅 Linux 已运行；CPU 裁剪不等于零拷贝，Windows/macOS 和真实捕获裁剪仍须目标机验收。


MagpieFX 构建现在使用共用 v4 元数据前端；全仓 117 个效果的资源图与尺寸表达式可扫描，六项 CTest 包含该前端回归。当前能实际运行的效果数量没有因此增加；通用 shader 编译、SOURCE 加载、全部纹理格式、FP16 算术和动态加载仍待完成。


NNEDI3 nns16/win8x4 已接入共用 Vulkan 两 pass 路径，原始模型为固定 2x、单通道 R16F 中间存储与二维线程组。默认配置/应用配置、工具栏、效果链及原版模式映射均使用效果 ID 13。CTest 新增独立 NNEDI3 CPU 参考，共七项；当前仍仅 Linux 实际构建运行，Windows 的语法检查不代替 MSVC/实机，macOS 仍需 SDK/目标机。其他 NNEDI3 变体、FP16 算术、完整格式与通用效果执行未因此完成。


SSimDownscaler 现在使用效果 ID 14，五个 PS/CS 混合 pass、RGBA16F/RGBA8 混合纹理和 INPUT/OUTPUT 相对中间尺寸，Oversharp 1..3 可保存并实际修改输出。CTest 共八项，含独立 16-tap 三次插值/局部统计 CPU 参考。当前只有 Linux 实测；共用 API/编译分支不代表 Windows/macOS 运行已通过。全部原效果、完整编译器和 FP16 算术仍待完成。


Anime4K 已增加 Upscale L/VL 和 Upscale Denoise S/VL（ID 15..18）。共用 Vulkan 后端覆盖其四/八 pass 与 14 张特征纹理，独立标量参考扩展到六种 Anime4K 模型、54 组输入。中间半精度存储显式采用最近偶数舍入以统一驱动转换差异；这不等于 FP16 算术。当前仍只有 Linux 实测；Windows/macOS 目标机和其余模型仍待验收。


DDS SOURCE build-time 解码与静态嵌入、Vulkan 首帧上传/只读复用已接入。新效果 RAVU-Lite R2（ID 19）使用原 5×288 RGBA16F 查找表，运行时无需外部 DDS，默认/应用/效果链和原版模式映射可选。CTest 共十项，含原始 17 份 DDS 解码边界和独立 RAVU CPU/LUT 参考。当前实际运行仍只验证 Linux；Windows/macOS、NIS/SMAA/其余 SOURCE 效果、全部格式与完整编译器仍待完成。
