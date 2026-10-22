# macOS 本机测试

这是尚未完成验收的跨平台移植分支。Linux 已做实际运行；macOS 必须在目标机完成构建、GPU 和权限测试，不能把已写代码等同于已经通过。

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
xcrun --show-sdk-path
```

## 构建和核心测试

```sh
python3 scripts/test-macos.py
```

脚本下载固定版本 MoltenVK 1.4.2 的官方 macOS public-API 包，并按固定 SHA256 校验；选择其中的 universal **静态** `libMoltenVK.a` 和 Vulkan 头文件。它们保存在忽略提交的 `.tools/` 下，用户不需要另外安装 Vulkan runtime。默认使用仓库中经过摘要校验的预编译 SPIR-V；没有 DXC 也能构建。

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
