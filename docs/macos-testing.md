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

此脚本仅提供本机测试签名，不是 Developer ID 分发签名或公证流程。若构建失败，请保留完整 `artifacts/platform-checks/` 日志；若运行失败，从终端执行 app 中的可执行文件收集输出：

```sh
artifacts/desktop/osx-arm64/Magpie.app/Contents/MacOS/Magpie.Desktop
```
