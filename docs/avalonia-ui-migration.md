# Avalonia UI 迁移工作记录

目标仍是迁移原 WinUI XAML、按参考图对齐界面与浮层，同时保留 NativeAOT 单文件。用户已明确允许字体不同；当前使用 Selawik，不再以取得 Segoe 字体作为前置条件。该 UI 阶段已结束；随后用户要求移植真正核心并采用纯 Wayland，当前功能状态见 [核心迁移记录](native-core-migration.md)。

## 已有可运行结果

- 新项目：`src/Magpie.Desktop`，独立解决方案 `Magpie.Desktop.slnx`。
- Avalonia 12.1.1 Fluent 主题；主窗口、主页、缩放模式、默认配置、设置、关于页及常用浮层已迁移。
- 快捷键窗口内模态对话框；新建配置、模式新建/重命名/删除/参数/缩放浮层；个人配置重命名/排序/删除及右键菜单；组合框选中指示条。
- 原生浮层通过 `PopupAlignment` 在显示前约束到所属窗口，避免右侧/底部被截断。验证器会检查实际弹窗边界。
- Linux x64 NativeAOT 产物：`artifacts/desktop/linux-x64/Magpie.Desktop`，约 30 MiB，发布目录只有该文件。
- 本轮 48 个浅色状态和 3 个深色状态通过；提示图标最后修正后，另复验 6 个相关状态。完整渲染状态以 `artifacts/desktop/review/results.json` 为准；深色结果位于独立目录；结果与截图分别在 `artifacts/desktop/review`、`artifacts/desktop/dark-review`。
- 真实 X11 鼠标检查通过导航、快捷键对话框打开/Esc 关闭、下拉框打开/Esc 关闭。`pointer-check.json` 还确认运行进程未加载 CoreCLR、Skia/HarfBuzz 的独立共享库。
- `home-geometry.json` 记录参考图与主页六张卡片的填充边缘测量，纵向偏差为 0–1 px；这不代表所有像素或所有界面都已经相同。

参考图：`/home/greepar/.codex/attachments/4433a7ce-bea5-492c-ba10-d0dfdb62bf5b/image-1.png`。

## 本轮源码对照

- 语言下拉按原版顺序补全为系统默认与 18 个语言；捕获方式为 Graphics Capture、Desktop Duplication、GDI、DwmSharedSurface。最小帧率、工具栏默认状态、自定义缩放默认值按原 ViewModel / Profile 定义修正。
- 迁移共享更新卡片、成功/失败/下载/安装状态、主页触控及独占全屏提示、设置重启提示、ToastPage 通知内容。全部只提供 UI 状态，不调用更新或安装服务。
- 页头支持个人配置图标；卡片与展开卡片保留原版换行阈值；920 DIP 以下收起侧栏，菜单按钮打开覆盖侧栏；590 DIP 以下收窄页面边距。最小窗口为 750×450 物理像素（150% UI）。
- 配置页按捕获方式切换警告和标题栏捕获启用状态；默认配置隐藏个人配置的启动参数。多显示器/显卡项保留预览入口，普通 UI 不假装检测硬件。
- 新增 750×750 最小宽度截图及更新/通知/条件提示预览。进度环用显式 RotateTransform 驱动，避免 NativeAOT 下反射动画注册缺失。

边界：字体按用户要求允许不同；背景已按后续要求移除渐变，使用 Fluent 中性纯色。仅本机 Linux x64 做运行验证，Windows/macOS 尚未验证。截图检查覆盖列出的场景，不能等同于其他平台的所有像素已经相同。

## UI 阶段历史验证环境

批量原生菜单检查应放在独立 X11 显示会话，避免用户桌面上的焦点变化自动关闭菜单。本轮临时从 Ubuntu 的 xvfb deb 解包到 `/tmp/magpie-xvfb`，没有安装系统包；隔离运行脚本在 `/tmp/magpie-verify-isolated.py`，使用显示 `:97` 并在结束时清理自己启动的服务器。不要假设临时进程仍存在，先查真实进程与显示 socket。

`src/Magpie.Desktop/README.md` 包含构建、截图、预览参数说明。`scripts/verify-desktop-ui.py` 的通过结果只证明其覆盖的运行/渲染/导航/弹窗边界条件，不能代替人工与源码的视觉对照。

当前 Linux 窗口使用原生 Avalonia.Wayland，旧 Xvfb 脚本不再适用。原生 Wayland 已清除 DISPLAY 复验 Home、Shortcut、SettingsLanguage、ModeTypes、NarrowPane；截图位于 `artifacts/desktop/wayland-review`。

2026-10-22：按用户要求移除主窗口模仿 Windows 的标题、标题按钮、透明圆角外框与手动拖动逻辑，改用 `WindowDecorations="Full"` 标准窗口装饰。侧栏菜单按钮保留在导航区域，页面去掉为自绘标题栏预留的顶部空白。Wayland 在桌面提供服务端装饰时使用系统标题栏；GNOME 不提供该协议，因此由 Avalonia 标准装饰回退处理标题栏、拖动和缩放，不再维护应用自己的标题栏。

同日修正大源画面的窗口化启动：当源尺寸加原版 100 DIP 边距的最小窗口超出工作区或渲染预算时，在扣除装饰后的工作区 80% 范围内等比适配，放宽最小尺寸，允许继续手动缩小。其余尺寸仍采用原有初始比例规则；尺寸失败不再触发自动全屏。AOT 核心包含 60 组大源/工作区/DPI 边界组合；五项私有 Wayland UI 检查验证 4K 源真实 Vulkan 下采样、普通窗口缩放、全屏恢复、独立配置及宽/窄主页。记录：`artifacts/desktop/window-fit-test-summary.json`。此阶段没有在用户桌面打开捕获选择器。

用户最新要求：构建/测试不得在当前桌面主动弹出窗口；需要图形回归时使用隔离的 Wayland compositor，或等待用户明确要求打开。背景只用 Fluent 纯色，不模拟 Mica 或 blur。
