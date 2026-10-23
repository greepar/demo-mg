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

需要 Xcode/macOS SDK（支持 ScreenCaptureKit 14+）、静态 MoltenVK 与 Vulkan 头文件。不能将动态 Vulkan 库作为“单文件”静默回退。

```sh
export MAGPIE_MOLTENVK_LIBRARY=/absolute/path/to/macOS/libMoltenVK.a
export MAGPIE_VULKAN_INCLUDE=/absolute/path/to/vulkan/include
python3 scripts/verify-platform.py --rid osx-arm64
```

静态库必须匹配架构（Intel 可用 osx-x64）；框架依赖由系统提供。ScreenCaptureKit 选源放在主线程、等待和取帧在工作线程；回调独立持有共享状态，取消通知等待者并在主线程停止流。实际权限、应用签名与 bundle 元数据仍须目标系统验收。

## CI

[Desktop 原生检查](../.github/workflows/desktop-port.yml) 现为 `main` 相关源码 push 和手动触发，使用 GitHub 托管的 Linux x64、Windows x64、macOS arm64 / x64 原生主机。原来的 self-hosted 检查没有可用 runner，不能计作目标机证据。

`verify-platform-build.py` 严格核对主机架构，在本机编译 C++/Objective-C++、发布 NativeAOT、执行三项非 GPU 原生模型/源快照 CTest 和 `--interaction-model-test`，检查单可执行文件；macOS 额外使用 `otool -L` 拒绝动态依赖自有核心/Skia/AvaloniaNative/MoltenVK。CTest 使用 JUnit 结果核对三项的实际执行，不依赖不同版本的控制台摘要格式。Windows 的 Vulkan 头文件与 loader 从固定版本的 Khronos 原始源码校验 SHA256 后构建，只在 CI 环境 PATH 提供系统 loader，不捆入应用；macOS 使用现有固定版本静态 MoltenVK。shader 输入固定 LF，并使用 POSIX 形式的摘要键，避免 Windows checkout 换行/分隔符改变摘要。

Linux 托管主机另执行原 `verify-platform.py` 的完整软件 Vulkan 回归。Windows/macOS 托管检查**不证明** GPU、源捕获、输入送达、权限、系统弹层或真实 GUI 重启，报告明确标记这些为未测试。完整目标机 GPU 回归仍使用原 `verify-platform.py`；Mac 可使用 `test-macos.py` 准备依赖并运行该完整检查。Windows 静态核心输出为 `magpie_native_static.lib`，与开发 DLL 的 `magpie_native.lib` 导入库分开，避免覆盖后意外依赖额外 DLL；发布不携带静态图形包的可选 PDB，并用 MSVC dumpbin 检查 EXE 依赖。当前具体结果以对应 CI job、`platform-build-checks/<rid>/results.json` 和原生/托管执行日志为准，不以工作流存在作为通过证据。

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


## 原版缩放模式导入、导出（2026-10-21）

缩放模式页的原导入/导出菜单已接入共用 `ScalingModeInterchange`、Avalonia StorageProvider 与真实持久化操作。支持原版 `ScalingModesService::Export` 的 `scalingModes/name/effects` 格式，也能从完整原版配置中只提取 scalingModes。不会宣称已导入 profiles、全局快捷键、自动缩放等其他原配置字段。

依据 `AppSettings::_SetInitialSettings` 建立七个原版初始预设 fixture；`verify-original-mode-fixture.py` 从当前原版源码核对名称、效果顺序/路径、显式参数、Fit 和 2x 设置，加入三平台共用发布 gate。FSR/FSRCNNX/CuNNy2/Anime4K/CRT 的目录路径按内置 shader 表映射，各系统都接受原版反斜杠和正斜杠。参数使用原 HLSL 的变量名，CRT 整数参数沿用原 `lround`，未声明的额外参数按上游忽略并保留用于导出。

原版导出会省略默认 scalingType、scale，因此导入支持省略默认值，也支持原版导入器允许的注释、尾随逗号和 UTF-8 BOM。拒绝重复 JSON 属性、无穷数、错误类型及超出预算的文档；UTF-8 严格解码。文件限 4 MiB / 256 模式 / 每模式 16 effects，运行配置最多保存 100 自定义模式，超过容量会整次拒绝，避免部分导入。导出能完整包含七个内置模式与满额的 100 个自定义模式；文件可导出不表示当前配置能同时容纳所有模式。

未知效果、扩展 effect 属性或当前无法表示的有效配置保存为独立 `JsonElement`，不会替换成 Lanczos。禁止其 Use、参数修改和应用配置选择，开始捕获之前也校验可用性；可整体导出、复制，或由用户显式替换/删除。合法的原版空模式保留为空，按原 `ScalingService::_StartScaleImpl` 拒绝启动；新建模式 Copy from None 同样从空链开始。已知效果的额外参数和原始固定尺寸/分数绝对尺寸 metadata 随 source-generated settings JSON 保存，原始 JSON 数据不随对象析构丢失。

导入追加模式并保留模式 ID、应用引用和原选择；名字冲突自动加后缀，保存失败移除本次追加内容。本地导出先序列化，再写临时文件并替换；非本地可 seek provider 流先完成序列化，再清空并重置位置写入。该后者不具备跨 provider 的原子替换保证。预览模式在调用系统选择器之前拒绝，隔离测试不会触发真实选源/文件选择器。

对照原 `EffectDrawer::_CalcOutputSize` 修正托管输出尺寸：float 算术后按 AwayFromZero 取整，核对 5×5 的 .5/.9 倍得到 3×5；舍入后的尺寸进入资源预算检查。导入的 .05 Fit、分数绝对尺寸和用户后续修改均核对。固定输出的 CNN/RCAS 按 HLSL 固定输出规则忽略用户 scaling fields，同时保留这些字段导出。此项不代表 Renderer 的窗口模式 Fit-as-Fill / 自动追加 Bicubic 已移植，该呈现行为仍待补齐。

核心测试实际运行导入的七个预设，Nearest 与独立 CPU 像素逐字节核对；Bicubic 参数/尺寸与单独构造的原生链输出核对。另覆盖导出往返、未知效果的 JSON 语义完整保留、source-generated AOT 磁盘恢复、原选择和共享模式列表引用、保存失败回滚、100 模式导出、额外参数、分数/固定尺寸 metadata、原版整数舍入及大小/格式错误。真实系统文件选择器、Windows/macOS 运行、完整配置/应用规则迁移、自动匹配与全部效果编译器仍未验收完成。

本轮先在 AOT 基线构建完成英文 100、中文 100、深色中文 21 场景回归；随后补强 deferred effect 的编辑器字段校验，最终构建另完成英文 14、中文 14、深色中文 7 个受影响场景复测。最终 NativeAOT 单文件、五项原生 CTest、AMD 与 llvmpipe 软件设备的全部 AOT core smoke 通过。两个 UI 构建的 SHA256、最终源文件摘要及逐场景记录见 `artifacts/desktop/mode-interchange-test-summary.json`。全部图形检查使用私有 headless Wayland；截图合成与 backend 坐标仍不能证明合成器级 popup 绝对位置。没有弹出用户桌面窗口或调用真实系统文件/共享选择器。


## 窗口呈现与自动重采样（2026-10-21）

对照原 `EffectDrawer::_CalcOutputSize`、`Renderer::_ShouldAppendBicubic/_AppendBicubic/_ResizeEffects` 接入：窗口模式的 unit Fit（原 float epsilon×100）按 Fill 处理；效果链最终尺寸与 renderer rect 不同时追加 Bicubic（B=0、C=0.5）。全屏只在任一轴超出 renderer rect 时追加 Fit 降采样，较小输出保留整数物理像素居中。改变尺寸时会添加、重建或移除该系统 pass，不改写保存的用户效果链。

原 Windows 窗口会约束边框等比缩放；当前 Avalonia/Wayland client 可以任意变形。因此窗口呈现先按源比例计算适合 client 的 renderer rect，再在 client 中居中留边，避免源画面被明显拉伸。此适配不代表原始窗口初始倍率、工作区约束、边框等比拖动或九宫格对齐已完成。尺寸和模式改变、RenderScaling 事件进入重新规划；从实际 WindowState 区分窗口/全屏。物理 viewport 超出 16MP 预算会报错，不再静默把宽高裁到 8192。真实 DPI/多屏迁移尚未实机验收。

保留 16 个用户 stages，内部为最终呈现预留第 17 个。新增 `mp_scale_chain_configured_v2`（1..17）及 `mp_get_gpu_profile_v2`（2440 字节 / 17 stages）；旧 raw/configured API 的上限仍为 16，旧 `mp_gpu_profile` 仍是 2416 字节。旧 getter 遇到 17-stage 有效结果返回 status=3、空区间，不截取前 16 段假装总计完整；不支持 timestamp 的设备仍返回 status=2。描述符池、中间帧、CNN descriptor 和查询 metadata 均扩至 17，pass 预算仍为 256。呈现额外 pass 计入全链 64MP 资源上限。

`PresentationPlan` 同时用于真实捕获会话和隔离回放。处理器继续跳过静止重复帧，窗口变更会使计划失效；截图保存最终 GPU 输出，renderer 外围黑边不写入截图，与之前 output-image 截图语义相同。顶部工具栏为现有 overlay，不参与 GPU 图像及 PNG。

验证包含原版尺寸/浮点 epsilon、窗口/全屏和单轴溢出、CNN 固定 2x 后重采样、16+1 stages、GPU timestamp 全段、添加/移除/恢复、重复跳过和静止 resize。真实 Vulkan 输出与单独构造的 Bicubic 请求以及 CNN→Bicubic 逐次执行逐字节比较；旧 profiler buffer 后置 canary 与旧/新长度拒绝核对。私有 Wayland 输出回放另核对 PNG 像素、481×321→482×400 client 下 482×321 的源比例图像及 39 px 整数顶部边距。没有触发捕获授权或用户桌面窗口。

本轮 Linux NativeAOT 单文件、五项 CTest、无 DXC 路径、ASan/UBSan（leak 检测关闭）、Khronos 验证层和 MinGW 严格语法检查通过。追加的边界回归覆盖第 16 用户 slot 为 CNN、第 17 slot 为呈现 Bicubic，共 57 passes，并与先执行 16 段再单独重采样逐字节一致；该测试另在 sanitizer、预编译和 llvmpipe 路径复测。受影响的最终 AOT UI 英文 17、中文 17、深色中文 9 场景通过，不声称本轮重跑了全量 101 场景。最终源文件/可执行文件 SHA256 及报告：`artifacts/desktop/presentation-test-summary.json`。仍无真实捕获、DPI/多屏迁移、窗口边框约束及 Windows/macOS 实机通过证据，完整目标保持未完成。


## 初始窗口倍率与九宫格对齐（2026-10-21）

`InitialWindowedScaleFactor`、`CustomInitialWindowedScaleFactor` 和 `OutputAlignment` 接入 RuntimeSettings、source-generated JSON 和默认/应用配置。初始倍率按原 `Profile.h` 枚举顺序保留 Auto / 1.25 / 1.5 / 1.75 / 2 / 3 / Custom；自定义值保存范围 1..10，控件遵循原版 1.1 最小值，已保存的 1.0 可恢复。对齐顺序与原 `OutputAlignment` 相同；新增控件复用原 20 份 resw 标签。保存失败还原本次字段和控件状态。

`WindowSizing.Initial` 参照原 `ScalingWindow`：Auto 选择显示器限制轴的 3/4 与源尺寸 4/3 较大者；指定倍率先对宽度做 float/lround，再按源比例计算高度。最小留白为 100 DIP 转换成物理像素，支持传入准确源外框尺寸；当前捕获 ABI 未提供该外框，实际会话使用捕获内容作为回退。窗口尺寸按 Avalonia 报告的 WorkingArea、可得装饰厚度和 16MP 渲染预算限制；工作区裁限沿用原版 float/lround 规则（300×200、3x、1000×401 边界得到 602×401，而非向下截断为 601），无法满足最小留白时明确失败。Wayland 的工作区语义取决于 backend 所报告的值，未凭空推断面板/任务栏占用。

输出不再在捕获启动后立即创建 1280×720 等待窗口，而是在首个有效帧时取得源尺寸，直接用计算出的初始尺寸构造 Window。初始偏好在首次准备时固定，后续改偏好不重置当前窗口；进入窗口模式前尚未初始化时才做初始计算。全屏后的手动窗口尺寸恢复由实际 WindowState 路径验证。输出区域使用已知源比例 renderer rect，九宫格在 renderer 内定位，然后加上外层留边；整数除法与原 `_UpdateDestRect` 一致。对齐变更在 UI 快照中重新布局，不强制 GPU 执行。

核心测试核对 Auto、枚举次序、width-first 舍入、显式源 frame、DPI 留白、工作区/装饰/16MP 限制和不可能尺寸拒绝；九种位置与独立坐标表逐项比较。另有 source-generated 磁盘恢复及默认/应用值复制独立性测试。隔离窗口实际验证 1000×563 初始 client、应用配置 1600×900、Vulkan 恒色放大/PNG、静止 resize、全屏中九个位置的物理 bounds、对齐不重跑 GPU、恢复手动尺寸和设置控件保存。未请求真实捕获或系统文件选择器；Windows/macOS 仍需目标机运行，原版外框/源屏幕定位、等比边框拖动、裁剪与输入仍未完成。

本轮先在 AOT 基线构建完成英文 109、中文 109、深色中文 16 场景。随后修正工作区裁限的半像素取整，并把自定义数值框加宽到可读宽度；最终 AOT 另完成英文 16、中文 16、深色中文 8 个受影响场景复测。最终单可执行文件、五项原生 CTest、AMD 与 llvmpipe 全部核心 smoke 通过。原生实现本轮未修改，前轮 sanitizer/验证层证据对应的 native 源码保持一致；不伪称本轮重新执行这些检查。两个 UI 构建 SHA256、最终源码摘要与逐场景记录见 `artifacts/desktop/window-settings-test-summary.json`。真实捕获/系统共享选择器、源外框元数据和 Windows/macOS 实机均未在本轮触发/验收，完整目标仍未完成。


## 自定义源裁剪

默认与应用配置现在独立保存 `IsCroppingEnabled` 和四个 float 边距；禁用时保留边距，旧配置缺省为禁用。四个数值框、开关、展开状态和导航恢复接入真实配置保存，失败回滚。数值范围为 0..16384 物理像素；原始画面未知时只校验数值，收到画面后校验四边及剩余宽高至少 64，越界明确结束会话并报告原版 InvalidCropping 文案。

`SourceRegion` 使用交付 RGBA 帧的局部物理坐标；左/上和右/下坐标分别按 float/lround 取整，而非先取整四个边距再相减。它不推断 Wayland 桌面绝对源位置。会话在首帧先计算裁剪区域，再据其尺寸创建输出窗口。后续 UI 设置快照以裁剪宽高生成效果链和 renderer rect。`ScalingFrameProcessor` 在送入原生 Vulkan 之前逐行复制 ROI 到复用缓冲，重复帧检测只观察 ROI；没有新帧且裁剪未变时不重复复制。相同宽高但位置改变会使 GPU 缓存失效，禁用裁剪恢复完整源；静止修改不会伪造收到帧计数。当前为 CPU RGBA 裁剪，不宣称 DMA-BUF 或 GPU 零拷贝。

核心测试以独立逐像素参考检查 RGBA/stride/边界、半像素右下取整、64px 下限、框外更新跳过、框内更新渲染、静止移动/禁用、计数、非法数值和 source-generated 磁盘恢复。隔离 Wayland 另调用真实会话的首帧与规划方法，检查裁剪后的 900×500 初始 client、真实全屏、三次 Vulkan 输出与 PNG 完全相同，以及默认/应用配置和窄窗口控件。测试不调用捕获 portal/系统选择器；真实授权捕获、尺寸变化、撤销与三平台目标机行为仍需验收。


## MagpieFX v4 共用构建前端

`scripts/magpiefx.py` 将元数据解析从各模型的正则拆分中提取为共用 IR：版本、排序名、USE/CAPABILITY、float/int 参数及单精度值、INPUT/OUTPUT、20 种格式描述、SOURCE、采样器、多个 COMMON 块和按编号排序的 CS/PS pass。保留实际 HLSL body；忽略普通/块注释里的伪元数据，处理 BOM 和 CRLF。检查重复名字、参数范围、线程维度、资源别名、只读输入/外部纹理、末 pass 独占 OUTPUT 等错误，错误携带文件及行号。未知扩展指令/能力保留警告，不悄悄启用执行功能。

`SizeExpression` 使用受限 AST 解释算术、四个尺寸常量及 abs/min/max/floor/ceil/sqrt，支持 `^` 幂和原版 lround；不执行 Python，不支持的语法明确拒绝。当前 117 个内置效果的全部尺寸表达式均覆盖；不宣称覆盖原版 muParser 的全部扩展语法。`texture_sizes` 在输出表达式求值后解析中间纹理的 OUTPUT_WIDTH/HEIGHT，SOURCE 的实际尺寸留给外部纹理加载器，不以假尺寸代替。格式表与原 `EffectDesc.h` 的枚举核对。FP16 声明只作为能力元数据，当前 adapter 仍用 float 算术；RGBA16F 存储不等于半精度算术。

单 pass 和 CNN shader 构建现实际使用该 IR：参数声明、采样方式、pass 数、输入/输出、COMMON、block/thread 和格式检查来自同一解析结果。CNN backend 仍只执行已接入的固定 2x 模型图与两类存储；不支持的参数、SOURCE、WRAP、多维线程、尺寸表达式或混合格式在后端明确拒绝。前端扫描成功不代表这些效果可运行。原始赋值语句向 Vulkan 输出的改写也仍是适配器的一部分，尚未成为通用 HLSL 编译器。

`scripts/verify-magpiefx.py` 扫描全部 117 个原效果（345 个 pass、108 个参数、623 个 texture、221 个 sampler），用独立源码计数、NNEDI3 尺寸、NIS SOURCE 及组合样例检查 IR；另有 49 个非法元数据/表达式边界。该检查已加入三平台共用 CTest，当前总计六项。CMake 将前端作为 DXC 重编译依赖，预编译 manifest 也包含前端摘要；无 DXC 发布同样检查其匹配，运行时仍不依赖 Python/DXC。此轮重新生成的是现有 12 个 HLSL 效果、33 个 kernel，不是编译完全部 117 个效果。


## NNEDI3 与逐纹理格式/尺寸

新增原始 `NNEDI3/NNEDI3_nns16_win8x4`，稳定效果 ID 为 13，CRT 的 ID 12 及现有 ABI 均保持不变。默认/应用配置、输出工具栏、效果链编辑和原版模式 JSON 映射可选该模型；固定 2x，与原始输出尺寸表达式一致。导入的原始 scale 信息保留供导出，但不覆盖这个固定尺寸。当前只接入这一 NNEDI3 变体，其他 nns/window 仍未接入。

共用 multi-pass 后端不再给整模型只存一个 UNORM bool。生成元数据为每张纹理保留格式、字节数和输入相对宽高因子；已具备 RGBA16F/RGBA8/R16F/RG16F/R8/RG8 的类型映射，实际模型回归覆盖前三类，后三类尚无实际效果验收。NNEDI3 分配 INPUT_WIDTH × (INPUT_HEIGHT*2) 的 R16F 中间纹理，各 pass 根据实际输出纹理尺寸计算 dispatch，并保留原 32×8 线程组与 groupshared/barrier。单 pass 的多个输出必须尺寸相同；完整尺寸表达式、SOURCE、PS multi-pass 和所有格式仍待实现。

中间资源预算按每张纹理真实字节数折算，不再一律按输入大小乘半精度/UNORM 倍数。NNEDI3 中间资源为每个输入像素 4 字节（双倍高度、每 texel 2 字节），与托管预算一致。逐模型懒建 pipeline 并检查格式和线程组能力，避免新增效果的 pipeline 阻断其他已可运行效果；设备初始化只启用实际报告支持的扩展存储格式特性。可用格式按实际 Vulkan format properties 检查；参考 [Vulkan formats](https://docs.vulkan.org/spec/latest/chapters/formats.html)。这仍是 float32 算术配合 R16F 存储，不宣称 FP16 算术。

独立 CPU 参考从原始文件提取两层各 16 个神经元权重，标量实现亮度预测、R16F 量化、双线性 UV 和 RGB 重建，测试小图、非方图及跨 32/64×8/16 工作块。原 shader 的取样表达式混合 int group_base 和 uint x/y；CPU 参考明确保留无符号下溢后再 float 转换/CLAMP 的边缘行为，没有把它改成有符号左上边缘复制。依据 [HLSL usual arithmetic conversions](https://microsoft.github.io/hlsl-specs/specs/5-Expr.html)，实际 Windows/D3D 编译输出仍须目标机核对，不能用本机 DXC/SPIR-V 验证冒充该证据。

新增 `nnedi3_reference` 后 CTest 为七项。原生回归同时检查 NNEDI3/R16F 与 CuNNy/UNORM 混合链、资源复用/切换、失败不改调用者输出及两 pass GPU 计时；托管 core smoke 检查实际 Vulkan、重复帧、静止呈现尺寸修改、原版模式导入/导出和 AOT 默认/应用磁盘恢复。隔离 UI 检查默认选择/导航恢复、原版导入后的效果链编辑、窄窗口和实际 NNEDI3→Bicubic 输出/PNG。所有 UI 都使用私有 Wayland，不调用捕获或系统选择器。


NNEDI3 跨驱动验收另发现：无符号下溢产生约 2^32 的坐标，直接交给 CLAMP sampler 时，AMD 与 llvmpipe 的大坐标转换行为不同。适配层仅对 NNEDI3 的 `texture` 宏先做 normalized [0,1] clamp，再使用原采样器；保留源程序的无符号边缘语义，同时避开驱动内部极大坐标转换。初始 CPU 参考错误地使用有符号左上边缘，已按原表达式和 HLSL 转换规则修正；阈值始终为 max≤2、mean<0.2，未为驱动差异放宽。最终 AMD 九组 max≤1，llvmpipe 九组 max≤2。实际 Windows/D3D 和 macOS/MoltenVK 尚未验收。


## SSimDownscaler：PS/CS 混合 pass 与输出相关中间尺寸

新增原始 `SSimDownscaler.hlsl`，稳定效果 ID 14，保留原五个 pass：PS 三次插值、PS 垂直二阶矩、PS 水平二阶矩、CS 局部均值/方差比、CS 最终重建。与先前固定 2x 模型不同，它接受实际效果链指定的任意输出尺寸；用途为图像缩小，尺寸规则仍遵循原版任意输出定义。Oversharp 为原 float 参数，默认 1、范围 1..3，默认/应用参数、原版模式 JSON、效果编辑器和输出工具栏均接入。

生成器现在支持单输出 PS pass 转 compute：按 pass 实际输出纹理尺寸生成规范化像素中心坐标，调用原 PassN 返回值并写入对应的存储纹理。CS body 继续保留并加边界写入保护。纹理尺寸元数据由输入/输出四个轴和整数倍数组成，支持 INPUT_WIDTH × OUTPUT_HEIGHT 等混合尺寸；尚不等于全部 muParser 表达式。SSim 的 L2 为输入宽 × 输出高 RGBA16F，L2_2/MR 为输出尺寸 RGBA16F，POSTKERNEL 为输出尺寸 RGBA8。运行时按真实尺寸/格式分配并分别计费，托管预算与 native 相同：2×输入宽×输出高 + 5×输出宽×输出高 个 RGBA8 资源单位。输出变化时 frame 重建覆盖相关中间纹理；同尺寸参数变化只重新执行 GPU。

SSim 预滤波中有九点采样与 RGBA8 量化。硬件线性采样的亚像素精度差异会被后续方差锐化放大；适配层对这个效果的 INPUT 线性采样使用四个 clamped Load 和 lerp 实现等价数学插值，避免不同 Vulkan sampler 的有限权重精度。独立 CPU 参考用 16 个三次插值点、可分二阶矩、half/UNORM 量化、标量局部统计和重建计算；未照抄 GPU 工作组或 Gather 代码。原九点优化与独立 16 点数学参考的一致性通过 AMD / llvmpipe 实际像素验证。最初 max≤4/mean<0.5 的阈值未通过，修正采样后收紧为 max≤2/mean<0.2；没有以放宽阈值掩盖差异。

新增 `ssim_reference` 后 CTest 为八项。覆盖常色/梯度/随机、1×1、奇数尺寸、跨 16 像素工作块、非方缩小、等尺寸和放大、Oversharp 默认/最大/中间值。Native 测试包含五 pass 时间戳、SSim→Bicubic GPU 链、复用、换尺寸恢复、参数非法失败不改输出。托管 core 含真实 GPU、重复帧、静止改参数/改尺寸、原版导入/导出、资源预算及默认/应用磁盘恢复。隔离 UI 验证控件/浮层/窄窗口和实际输出工具栏/PNG，无捕获或系统选择器。

这个后端仍不是完整 MagpieFX 编译器：PS 多输出、任意尺寸表达式、外部 SOURCE、WRAP、所有格式与 FP16 算术和剩余效果仍未完成。仅本机 Linux 实际运行，Windows 语法检查不等于 MSVC/D3D 或目标机验证，macOS 仍需 SDK/实机。


## Anime4K Upscale L/VL、Denoise S/VL

新增四个原始 Anime4K 模型，ID 15..18 分别为 Upscale L、Upscale VL、Upscale Denoise S、Upscale Denoise VL，均固定 2x；已有 Upscale S / Denoise L 和其他 ID 保持不变。L 和 Denoise S 为四 pass，VL/Denoise VL 为八 pass、14 张 RGBA16F 特征纹理（输入尺寸）。原有共用后端直接执行原始 HLSL 权重/资源图；纹理预算分别为每输入像素 8/28/4/28 个 RGBA8 单位。默认/应用配置、工具栏、效果链编辑及原版模式 JSON 映射均可选。

VL 最后一 pass 是 14 路特征纹理的逐像素 1×1 聚合，不是普通 3×3 卷积。独立 CPU 参考从每个显式 SampleLevel 位置解析资源与坐标，支持多个 COMMON/卷积构造形式、四/八 pass 和 28 项聚合，并以原始系数执行标量卷积、RGB 残差和 depth-to-space。六种模型各九组常色/梯度/随机、1×1/奇数/跨工作块，合计 54 组。旧有 1 色阶误差阈值保留，不能把编译成功当作数值验收。

Vulkan 浮点格式转换的舍入方式未规定（[规范](https://docs.vulkan.org/spec/latest/chapters/fundamentals.html)），扩展模型使 llvmpipe 的半精度中间写入差异更明显。共用多 pass adapter 在写 R16F/RG16F/RGBA16F 前显式按 IEEE binary16 最近偶数舍入，图像存储拿到可精确表达的值；包含正规数进位、subnormal 和溢出/Inf/NaN 分支。算术仍为 float32，不宣称 FP16 算术。既有 NNEDI3/SSim/FSRCNNX 等也需要随此共用变更重新回归。


扩展软件 Vulkan 全量回归另发现 FSRCNNX 的 33×25 输入原 Gather 坐标位于 texel 边界，倒数乘法误差会选错 2×2 邻域并造成最大 255 色阶偏差。适配层将该 source loop 的 Gather 改为 `blockStart+pos-2` 的 clamped 整数 Load，原始卷积/权重和资源布局不变。修正大幅偏差后，最后一步 INPUT→UV 的硬件线性插值仍产生少数 2 色阶差异；与 SSim 相同，改为数学等价的四个 Load + lerp 后，llvmpipe 的原有 FSRCNNX/LineArt 18 组全部达到最大 1 色阶。既有 1 色阶阈值未修改；相关失败诊断和最终全量门禁分开保留，不能将旧失败记录当作最终状态。


本轮最终构建运行八项 CTest，AMD 与 llvmpipe 都完整通过；Anime4K 六模型 54 组的最大误差分别为 0/1，FSRCNNX/LineArt 软件 18 组最大误差为 1。无 DXC 路径与 ASan/UBSan 八项通过，泄漏检测仍按既有驱动测试路径关闭；Vulkan 验证层及 Windows MinGW 严格语法检查通过。Windows/MSVC、macOS SDK/捕获/实机均未执行，不算三平台实际验收。现有 16MP 单纹理、64MP RGBA8 单位聚合预算仍生效，VL 在大分辨率下可能被预算拒绝；未开展大分辨率/持续帧率测试。


## DDS SOURCE 与 RAVU-Lite R2

新增 build-time `dds_source.py`：检查 DDS magic/结构长度、像素掩码、单个 2D 图像、尺寸/负载/row pitch，拒绝压缩、数组、cube、volume 和 mip chain。支持当前原始 DDS 的 legacy float FOURCC、RGBA8/R8 掩码及指定 DX10 非压缩格式，解出紧密行数据；padding 被去除。`verify-dds-source.py` 独立核对仓库 17 份 DDS 原始 payload，另有 26 个错误、DX10 和 padded row 测试。解码支持某种资产格式不等于全部对应 Vulkan 格式/效果已接入。

共用生成器对 SOURCE 路径解析后限制在 Effects 树内，核对声明格式与文件格式。所需 DDS 的像素字节、宽高、格式嵌入原生 header，SOURCE 的尺寸不随捕获源/输出尺寸变化。CMake 和预编译 manifest 包含解码器、实际接入的原始 DDS/HLSL/include 摘要；运行时无需磁盘 DDS、DXC 或 Python。当前实际嵌入并用于新效果的是原 `ravu_lite_lut2_f16.dds`（5×288 RGBA16F，11520 字节），不宣称已嵌入全部 17 份资源。

Vulkan 首次创建该 frame 时分配 device-local 图像和 coherent staging buffer；同一 queue 记录 transfer copy、transfer→compute 屏障，再由 shader 只读采样。fence 确认后释放 staging 并标记已上传。后续帧保留 SOURCE 的内容和 GENERAL layout，不像可写中间纹理那样每帧 discard；尺寸/模型变更重建 frame 时重新上传。SOURCE 内存按实际固定尺寸计入预算。当前共用 descriptor 的备用 storage view 仍要求该图像格式具备 storage capability，尽管 SOURCE 本身没有 shader 写入；后续扩展只采样格式时需拆分备用 storage view，不能将这项额外限制说成所有 SOURCE 格式都可用。

新效果 ID 19 为原 RAVU-Lite R2，固定 2x，单个 CS pass 和 32×8 threads。源码张量/特征值分类、原半精度 LUT、成对 3×3 滤波及 UV 重建保持；INPUT 线性采样使用算术四 Load 插值，避免硬件插值精度差异。默认/应用配置、工具栏、效果链、原版模式导入/导出均已接。独立 CPU 参考直接按原 DDS header/payload 解出 binary16 系数，不依赖生产 DDS 解码器，标量实现梯度张量、角度/强度/一致性分类、LUT 过滤和 RGB 重建，覆盖 12 组含跨 64×16 输出工作块的输入。Native/托管测试另检查重复帧、SOURCE 复用、切换链/恢复、单 pass 计时、固定资产预算、非法输出原子性及 AOT 配置恢复。

CTest 增加 `dds_source` / `ravu_reference`，共十项。NIS、SMAA、其余 RAVU 及其他 SOURCE 效果尚未因此完成；通用编译器、所有格式/采样地址、FP16 算术仍未完成。Windows/macOS 的 SOURCE 上传与运行须目标机验收，Linux 的语法/共享代码不能代替。
