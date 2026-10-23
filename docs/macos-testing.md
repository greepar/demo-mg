# macOS 本机测试

这是尚未完成验收的跨平台移植分支。Linux 已做实际运行；macOS 必须在目标机完成构建、GPU 和权限测试，不能把已写代码等同于已经通过。

## 本机实测记录（2026-10-22）

Apple M4 / macOS 27.0.1 / Xcode macOS 26.5 SDK / .NET SDK 10.0.402：

- `python3 scripts/test-macos.py` 完整通过；CTest **19/19** 通过。
- 117 个效果、702 个 FP32 用例及 17 段混合链通过；自动 FP16 执行与六种 Anime4K 模型的独立 binary16 数值参考通过。其他算法的独立参考包含 SMAA、FSRCNNX、CuNNy、NNEDI3、RAVU、SSim、CRT 等；不代表全部效果已完成数值一致性验收。
- NativeAOT 托管核心/ABI 测试、单可执行文件检查、`Magpie.app` ad-hoc 签名与严格签名验证通过。可执行文件只引用系统动态库/框架。
- 签名后的 bundle 可执行文件通过 `--capture` 界面冒烟：Cocoa 窗口打开、中文/模式列表渲染及 UI PNG 导出成功；这里的 `--capture` 是界面截图测试，不调用 ScreenCaptureKit。
- **尚未验证**：系统选源、授权/取消/撤销、实时捕获、源窗口变化、全屏/Esc、全局快捷键、实际电源申请、Intel Mac 及 macOS 14 最低版本兼容性。

### 输入转发阶段代码

本次提交还包含 macOS Cocoa 事件转发：鼠标、拖拽、滚轮、键盘、工具栏区域排除及失焦/停止释放。仅 macOS 15.2+、系统选源中的其他应用单窗口有实现；整屏源、同进程源和更早系统保持只读。需要辅助功能权限，首次点击请求，未授权时工具栏提示。捕获配置已关闭光标嵌入，输出保留本机光标。

包含上述代码的最终版本再次通过 19/19 CTest、NativeAOT/core/ABI、打包和签名校验；**这些测试不覆盖真实输入送达，点击问题和单光标修复仍待实际源应用验收**。当前尚未编写独立输入集成测试，未复刻原版相对鼠标/游戏模式、源光标外形/倍率/自动隐藏。完整已知交互缺口见 [交互缺口清单](interaction-gaps.md)。

本次修复了默认 SDK 与链接器混用、SMAA 测试强制启用未提供的 portability 扩展、缺少 GLM 测试依赖，以及 AOT/C++ 最低系统版本不一致。报告和界面截图保存在 `artifacts/platform-checks/osx-arm64/`，算法报告保存在 `artifacts/native/osx-arm64/Release/`；这些目录不提交。

## 2026-10-23 帧/源生命周期待验收变更

本轮新增代码尚未在 macOS 编译运行。换源改为按新 filter 尺寸重建 SCStream，以流身份/代次丢弃旧样本和迟到回调；像素与源元数据配对交付，截图身份取 UI 已提交帧。输入 ABI 也增加该帧窗口/PID，换源等待时暂停，匹配新画面后再启用；同一输入目标的布局刷新不再释放按键。Linux 的新帧缓冲/身份模型和 AOT/PNG 回归通过，不能替代本节实测。

目标机请重点覆盖窗口 A→B、窗口→整屏→窗口、连续快速换源、换源过程中按住键/拖拽、取消重选保持当前捕获、关闭/停止和旧回调迟到，核对截图名称/像素和输入送达的窗口一致。随后已接入帧元数据驱动的持续 resize、串行配置合并、内容矩形裁剪及单窗口阴影排除；增加帧序号/几何状态校验，暂停后需要新画面恢复输入，普通帧延迟保持长按/拖拽。Linux 增加 CPU 几何与实际 Vulkan 像素流水测试，完整 CTest 现为 23 项；macOS 新代码尚未编译运行。请实测同一源持续 resize、Retina/跨屏、窗口阴影/边框命中、最小化/恢复、快速换源期间的旧完成回调和按住输入，不能将 Linux 模型测试视作目标机通过。随后补了截图动作中的动态标题查询：仅在提交画面的窗口/PID 和源/几何帧状态匹配时查询已选 CGWindowID 的标题，返回后再次复核，缺失/异常保留已知快照，明确空标题使用回退；不以是否返回名称推断权限。Linux UI 只有注入标题与真实 PNG 验证，macOS 代码未在 SDK/系统运行。目标机应改名后立即截图、在画面静止时改名、清空标题、连续换源/resize/最小化及权限省略元数据时截图，确认名称、进程和像素仍对应同一窗口。新的 `capture_frame_mailbox` 会进入正常 CTest；应重新编译全部 native/AOT 部分，不要混用旧捕获库。

## 准备

- macOS 14 或以上；Apple Silicon 和 Intel 均有本机构建路径。
- 当前 Xcode/macOS SDK 或 Command Line Tools，已完成首次运行/许可设置。
- .NET **10 SDK**（不是仅安装 runtime）、CMake、Python **3.12+**；Ninja 可选。
- 用已登录的 GitHub 账号克隆此私有仓库，并进入仓库根目录。Apple Silicon 使用原生 arm64 终端及 SDK，避免通过 Rosetta 构建成 x64。

检查工具：

```sh
dotnet --list-sdks
python3 --version
cmake --version
xcrun --sdk macosx --show-sdk-path
```

## 构建和核心测试

```sh
python3 scripts/test-macos.py
```

脚本下载固定版本 MoltenVK 1.4.2 的官方 macOS public-API 包，并按固定 SHA256 校验；选择其中的 universal **静态** `libMoltenVK.a` 和 Vulkan 头文件。它们保存在忽略提交的 `.tools/` 下，用户不需要另外安装 Vulkan runtime。默认使用仓库中经过摘要校验的预编译 SPIR-V；没有 DXC 也能构建。

脚本通过 `xcrun --sdk macosx` 选择当前 Xcode/Command Line Tools 配套的 SDK，避免同时安装两者时混用 SDK 与链接器；如果已显式设置 `SDKROOT`，则保留该值。

SMAA 独立 CPU 参考需要 GLM 头文件。脚本会下载并校验固定版本 GLM 1.0.1，保存在 `.tools/glm/`，无需额外安装系统包。

接着运行实际 macOS CMake/CTest、NativeAOT 发布、托管核心和 ABI 检查。任何步骤失败都会停止，日志在 `artifacts/platform-checks/osx-arm64/`（Intel 对应 `osx-x64`）。最后生成 `Magpie.app` 并进行本机 ad-hoc 签名和签名检查；不会自动启动 UI 或申请屏幕共享。

如果下载较慢，可以从官方链接取得同一包，再运行：

```sh
python3 scripts/test-macos.py --archive /绝对路径/MoltenVK-macos.tar
```

官方包：https://github.com/KhronosGroup/MoltenVK/releases/download/v1.4.2/MoltenVK-macos.tar

## 界面与捕获

Apple Silicon：

```sh
open artifacts/desktop/osx-arm64/Magpie.app
```

Intel 改为 `osx-x64`。应用 bundle 内只有一个 NativeAOT 可执行文件，自己的 C++ 核心和 MoltenVK 静态链接；Cocoa、Metal 等系统框架由 macOS 提供。

先检查主窗口、设置、模式菜单，再点右侧窗口图标的 Start 测试窗口化缩放。系统选源/屏幕录制授权需要你在 Mac 上确认。先选择小窗口，确认连续更新、改变窗口大小、停止和重新启动，然后测试接近屏幕尺寸的源和全屏模式（Esc 退出）。全局快捷键、截图及电源请求仍须分别实测。

设置中的便携模式会把当前偏好复制到 `Magpie.app` 同级的 `config/desktop-settings.json`，不会写入已签名的 app 包。该文件存在时，下次启动优先使用它；关闭便携模式会先保存到用户配置位置，再移除便携文件。目录不可写时应报错并保持原模式。请核对切换后主题、应用配置、工具栏状态及重启是否保留，“定位配置”是否在 Finder 选中当前配置文件；这些本轮新增行为仍需 Mac 实测。

此脚本仅提供本机测试签名，不是 Developer ID 分发签名或公证流程。若构建失败，请保留完整 `artifacts/platform-checks/` 日志；若运行失败，从终端执行 app 中的可执行文件收集输出：

```sh
artifacts/desktop/osx-arm64/Magpie.app/Contents/MacOS/Magpie.Desktop
```
