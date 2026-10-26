# 相对原版 Windows / WinUI 2 的交互缺口

核对日期：2026-10-22。范围为本仓库 `Magpie.Desktop` 跨平台移植实现，对照原版 `HomeViewModel`、`ProfileViewModel`、`SettingsViewModel`、`AboutViewModel`、`CursorManager` 和当前 Avalonia 页面/会话代码。以下是当前源码审计确认的已知缺口，不把截图、构建成功或替身测试当作真实交互验收，也不承诺未实测场景不存在其他问题。

## 2026-10-23 目标机检查补充

`02102163` 的 GitHub macOS arm64 / x64 job 已通过原生 SDK / Objective-C++ 编译、NativeAOT 链接、三项非 GPU 原生测试和托管交互模型、单可执行文件及静态依赖检查。详见 [macOS 证据与范围](macos-testing.md#2026-10-23-托管主机编译模型证据)。这些新增证据更新下文历史阶段的“尚未在 SDK 编译”状态；真实输入、源捕获、权限、GUI 和本轮 GPU 仍不因此通过。Windows 目标检查已修复 shader 路径/换行、MSVC 成员遮蔽/测试 CRT 用法、静态库与开发 DLL 导入库同名覆盖及测试夹具的 Linux env 路径假设。`43c43669` 的 Windows job 已通过原生 SDK/C++、AOT、三项非 GPU CTest 和全部托管交互模型，但因第三方 PDB 发布残留未过单文件检查；`a65e5825` 的 Windows、macOS arm64/x64 job 已通过发布规则修复后的原生编译/AOT/非 GPU 模型和单文件依赖检查。后续截图快捷键变更需要对应提交重新验证。下载的 EXE 导入表已核对无自有核心/Skia/HarfBuzz DLL，但不代表 GPU 或 GUI 验收通过。

本机 AMD 并行回归捕获过一次 NNEDI3 configured/legacy 像素不一致；随后 3 次隔离 core 和 50 次与原生 Vulkan 用例重叠的 core 重复均通过，尚未定位首次失败原因。失败日志保留，新增不改变断言的 RGBA/逐通道差异诊断；没有放宽算法比较或将重复通过当作问题已解决。

`2d575adf` 的逐 pass 导出提交已通过 [四个原生 CI job](https://github.com/greepar/demo-mg/actions/runs/39950969742)：Windows x64、macOS arm64/x64、Linux x64。该证据补充开发者导出阶段提交时的待编译状态；目标机真实选源、输入、GPU 读回和菜单指针验收仍未因此通过。后续托管 profiler 变更需对应提交重新构建验证。

`250af84c` 的平均计时提交现已通过 [四平台 CI](https://github.com/greepar/demo-mg/actions/runs/40319664664)；后续浮层/信息提交仍需各自对应验证，CI 不替代真实系统交互。

## 2026-10-26 Graphics Capture 接入阶段

`86d31d18` 的 [Windows job](https://github.com/greepar/demo-mg/actions/runs/40331175994/job/121600682989) 已通过 SDK/AOT/单文件检查及新增真实 GDI fixture，包括 out-of-context 销毁 WinEvent、相同身份永久失效和独立重捕获；该生命周期事件由测试源进程主动发出，不是强制系统复用 HWND 的证据。

新增可选择的 Windows Graphics Capture 实现：独立 native MTA 线程持有 WinRT/D3D11，使用 HWND 建立单窗口会话、free-threaded frame pool、最新帧读回客户区并交给现有 Vulkan 链路。支持尺寸变化时重建帧池，暂停/几何变化后拒绝旧帧；异步生产者与 C ABI 已交付缓冲分离。后端能力位掩码、显式创建和实例查询已接入配置、迁移校验、会话及 profiler，显式请求不可用后端不会回退 GDI。GDI 仍是 Windows 默认；Graphics Capture 需在当前系统支持时手动选择。

这更新后文历史审计中的“WGC 未移植”为“实现已接入，目标验证待完成”。Linux AOT 核心及三个捕获模型测试已通过；`15ca1931` 的四平台 CI 已完成：Linux、macOS arm64/x64 成功，Windows SDK/AOT/单文件及 GDI fixture 成功；WGC fixture 收到首帧后报纹理格式/尺寸不匹配，尚未通过。后续诊断提交 `10ca4322` 的 GDI 继续通过，WGC 等帧超时被 fixture 误报为此前的最小化错误；已修复阶段化超时诊断，增加内容/边界/客户区信息，并在暂停恢复时同时重建会话，防止静态源唯一恢复帧被帧池清空丢失。恢复修复仍待 CI 验证，不能计作 WGC 已通过。托管 Windows 若能力探测不支持 WGC，fixture 记录 unavailable，**不计作 WGC 验收通过**。测试窗口仅允许一次性 GitHub-hosted runner，本机仅私有 headless Wayland。WGC 当前仍只输出客户区，标题栏开关保持禁用；DXGI/DWM、源窗口特殊样式兼容、GPU 零拷贝、多屏 DPI/HDR 和真实游戏兼容性仍缺。完整交互清单未完成。

实现与证据范围见 [Windows Graphics Capture](windows-graphics-capture.md)。

## 本次 macOS 阶段提交的边界

- Apple M4 本机 SDK 编译、CTest 19/19、FP32/FP16 GPU 回归、AOT 核心/ABI、单可执行文件和 app 本机签名验证通过。
- 新增 Cocoa 本地事件监视及向选定进程的鼠标/键盘转发代码；工具栏区域排除、当前裁剪/图像布局映射、失焦/停止时释放按键已接线，但**尚无真实源应用的输入送达验收或自动集成测试**。不能称为“不能点击”已经修好。
- `showsCursor=NO` 避免把捕获光标叠加在本机指针后面；**最终版本的实时捕获双光标回归尚未完成**。目前保留本机指针，没有复刻原版光标绘制器。
- 输入只为 macOS **15.2+** 系统选择的其他应用单窗口编写；macOS 14–15.1、整屏源和同进程源保持只读。权限不满足时显示辅助功能提示，首次点击请求权限。
- Linux/Windows 没有新增输入实现；共用 ABI 在这两个平台明确返回不支持。本次未在 Linux/Windows 重新构建或运行。

## 输入、光标、焦点和退出

| 项目 | 当前状态 / 剩余工作 |
| --- | --- |
| 点击、双击、右键、中键、侧键、移动、拖拽、滚轮 | macOS 转发代码已写；需要真实应用验证按钮命中、双击计数、拖拽跨边界、精确滚轮/惯性、按键组合。Linux/Windows 尚未实现跨平台输出窗口输入映射。 |
| 坐标映射 | 已按输出图像 bounds 和源裁剪比例接线；Retina、缩放倍率、黑边/裁剪、源窗口标题栏/阴影、源 resize、负坐标及跨屏的真实命中精度未验收。 |
| 键盘、修饰键与组合键 | macOS 复制原生键码/flags，预留 Esc/F12/工具栏快捷键；需要验证长按、重复键、左右修饰键、快捷键冲突及源应用是否真正收到。 |
| 文本输入与剪贴板 | 普通按键转发不等于 IME 支持；中文输入法、组合字符/死键、候选窗口、复制/粘贴/剪切、系统编辑菜单尚未验收，未实现专门的文本输入桥接。 |
| 焦点与窗口激活 | 未对齐原版前台源窗口跟踪、源弹窗/菜单/子窗口命中、激活与遮挡、应用切换、失焦恢复。向目标 PID 发事件不能直接视为完整焦点管理。 |
| 退出和资源清理 | macOS 已写失焦/停止释放及移除监听；需实测按住鼠标/键盘时 Stop、Esc、关闭、源崩溃、权限撤销，确保无卡键/卡拖拽及意外事件。 |
| 相对鼠标 / 3D 游戏模式 | 未移植原版相对运动、捕获/锁定、边界穿越、速度补偿和游戏输入路径；3D Game Mode 开关未接后端。 |
| 光标外形与可见性 | 未同步源应用箭头、文本、手形、缩放边框、忙碌及自定义/游戏光标；未复刻原版系统光标隐藏/恢复和独立软件绘制。 |
| 光标倍率 / 插值 | 预设、自定义、跟随源窗口、最近邻/双线性没有后端和持久化；目前 CursorScale 只切换自定义输入框的可见性。 |
| 光标自动隐藏 / 速度 | 延时隐藏、恢复、按倍率调整速度没有后端和持久化。 |
| 触控 / 手写笔 / 多点触摸 | Touch 支持开关和说明没有真实输入适配；不能把鼠标事件当作触控支持。 |
| 画面与输入目标一致性 | 已为输入 C ABI 增加 UI 已提交帧的窗口/PID 身份和捕获帧序号。macOS 按源/几何状态保留序号区间：正常帧延迟允许同一状态内已显示帧继续操作，换源/resize/暂停则需状态更新后的已提交帧，避免每帧更新反复释放长按/拖拽。换源先释放旧输入，等待匹配画面时暂停并显示等待提示。相同源反复配置不再释放所有按键/重置焦点，避免布局/工具栏刷新中断拖拽或长按。Linux 可移植帧缓冲模型验证代次、身份、非窗口拒绝；UI 验证等待提示/恢复，macOS 原生事件送达和真实换源尚未验收。 |
| macOS 输入权限与可用范围 | 提示/请求代码已接，授权、拒绝、撤销、重新授权和重启后的身份恢复未实测；较早系统及整屏源的交互未实现。 |

## 捕获、窗口、显示器和启动缩放

| 项目 | 当前状态 / 剩余工作 |
| --- | --- |
| 捕获方式选择 | 已接运行时能力位掩码、显式后端创建及会话实际后端查询。Linux Portal/PipeWire、macOS ScreenCaptureKit、Windows GDI；Windows 系统支持时额外允许选择 WGC。配置/迁移保存独立偏好，不可用项保留且禁用；启动在创建引擎/捕获/选择器之前校验，不静默替换。多后端策略、NativeAOT 持久化和 Linux 隔离 UI 已验证；WGC 目标证据见本页 2026-10-26 更新，DXGI/DWM 仍未移植。 |
| 捕获标题栏 | 当前实现仍禁用独立开关。原版允许 Graphics Capture / Desktop Duplication；移植的 WGC 暂时与 GDI 一样只输出客户区，Portal / ScreenCaptureKit 使用系统边界。原版标题栏开关、边框/阴影的裁剪和输入坐标一致性未完成。 |
| 系统选源与权限生命周期 | macOS 已有选源/取消/停止代码，但选择窗口/整屏、拒绝/撤销、重新授权、快速取消/重启和更换源的完整矩阵未验收。用户已运行捕获，但不足以代表该矩阵通过。 |
| 源窗口生命周期 | 最小化、隐藏、关闭、崩溃、最大化、全屏、尺寸/位置变化、重新出现和源弹窗需要三平台验收；macOS 换源已改成按新 filter 的尺寸重建流，用流身份/代次隔离旧样本与迟到启动/错误回调；非完整帧不发布，Stopped 报告源停止，取消重选保留现有流。相关状态/缓冲模型已做 Linux 验证，macOS 系统选源实测仍缺。已接同一源连续 resize：从每帧 contentRect / scaleFactor / contentScale 恢复自然像素尺寸，串行 updateConfiguration 并合并最新请求；按内容矩形裁剪有行填充的 BGRA，配置忽略单窗口阴影。旧配置的缩放/不完整帧等待匹配尺寸后才发布，Blank/Suspended 或空内容暂停输入，恢复需新的匹配帧。Retina/分数倍率、连续变大/变小/改回原尺寸、旧回调及裁剪→Vulkan 已有 Linux 模型/像素验证；macOS 新 Objective-C++ 未在 SDK 编译运行，真实持续 resize、跨屏 DPI、阴影/标题栏命中仍需目标机验收。截图时的动态标题查询已接入 Windows/macOS：限定已提交画面的窗口/PID 与源/几何状态，查询前后复核身份；系统不给标题时保留已知快照，明确空标题采用本地化回退。真实窗口改名/权限变化仍需目标机验收。 |
| 初始位置与缩放边框 | 已有 Auto/预设/自定义初始尺寸、比例与九宫格；准确源外框、跟随源位置、等比边框拖动及原版 hit-test 行为未补齐。 |
| 多显示器 | 未接原版“最近显示器 / 相交显示器 / 全部显示器”策略；UI 隐藏。不同 DPI、跨屏迁移、热插拔、旋转、负坐标与主屏切换未验收。 |
| 全屏与 macOS Spaces | Cocoa 全屏窗口路径存在；全屏切换/恢复、Spaces、菜单栏/Dock、原版置顶/焦点及 Esc 退出的真实行为未验收。 |
| 最大化窗口允许缩放 | 主页开关没有绑定/保存或对应源窗口检查策略。 |
| 模拟独占全屏 | Toggle 只控制警告显示，没有原生行为；各平台需定义实际可用替代。 |
| 禁用 DirectFlip / 置顶策略 | DirectFlip 是 Windows 专用语义，移植 Toggle 未适配；开发者“禁用置顶”等也未接后端，输出窗口未对齐原版层级策略。 |
| 倒计时 | 已将待启动计时与捕获会话分离：按原版同模式再次点击取消、另一模式重置时长；25 ms UI tick 只负责刷新，剩余秒数/进度取单调时钟，接近到期时只启动一次。主页已接确定进度环和向上取整秒数、活动模式“取消”标签；即时缩放入口取消旧计时，Stop/真正关窗清理计时器。会话结束回调核对所属实例，避免旧回调清掉新状态。Linux AOT 时钟模型与隔离真实按钮处理器/自然到期测试通过，捕获使用替身，未打开系统选源；三平台真实快捷键、托盘计时、源应用聚焦/选源、正在缩放时主页按钮状态与原版完整对应仍待验收。 |
| Windows 捕获兼容性 | GDI 已通过一次性 Windows CI 的实际绘制源/生命周期测试。WGC 原生实现与配置/会话链路已接，SDK/AOT 通过；初次 fixture 的访问异常通过局部 COM 工厂修复后，重复探测和 GDI 回归成功，WGC 仍在首帧后的纹理格式/尺寸检查失败，待诊断修复。DXGI Desktop Duplication、DwmSharedSurface、特殊窗口兼容、真实 GPU/游戏、多屏 DPI/HDR 和完整 Vulkan 捕获渲染验收仍缺。 |

## 工具栏、截图、模式与应用配置

| 项目 | 当前状态 / 剩余工作 |
| --- | --- |
| 工具栏初始状态 | 已接通主页、默认/应用配置中的全屏/窗口独立 Off / Always Show / Auto Hide，下拉编辑/复制/保存/回显/失败回退已接，默认为 Auto Hide。输出开始时取对应初始状态；运行中快捷键按原版 Off → Always Show → Auto Hide 循环，不覆盖下一次会话的初始偏好。Linux AOT 持久化与六种模式/状态组合通过；Windows/macOS 真实键盘/显示仍待验收。 |
| 工具栏自动显隐 | 已按原版 `_CalcToolbarAlpha` 的 10 DIP 内全显、随后 40 DIP 距离淡出接通指针移入/移出；控件拖拽、键盘焦点及原生下拉打开时保持显示。完全透明时禁用命中，macOS 输入桥同步排除条件；关窗停止计时器。已补固定按钮：常显/自动隐藏切换并与工具栏快捷键动作同步，不改下次会话的初始偏好；关闭按钮左键停止缩放，右键完成点击后只隐藏工具栏，保留帧循环并可由快捷键动作恢复。使用原版多语言固定/关闭/右键说明，关闭提示包含相应全屏/窗口快捷键。鼠标按钮焦点不再使自动隐藏永久保持显示，键盘焦点及文本编辑仍保留交互保护。Linux NativeAOT 隔离窗口/全屏验证真实控件辅助功能 Toggle/Invoke、合成指针按下/松开路由、帧循环继续、初始偏好不变和停止只清理一次；指针坐标为注入数据，事件按 Avalonia 12 最外层呈现根换算标题栏偏移。已补工具栏空白/只读区域的原生窗口移动请求，直接传递原始指针事件给 Avalonia `Window.BeginMoveDrag`，不写绝对 Position；全屏/最小化、已处理事件、非左键、工具栏外、隐藏/透明区域，以及按钮/下拉/文本/数字/滑块/滚动条等控件及模板子项不会启动移动。原生请求返回或失败后清除本地控件拖拽标记，避免 Wayland 抓取指针后收不到 mouse-up 而持续保持显隐保护；失败有提示并允许重试。Linux AOT 的合成路由/原始事件身份及排除矩阵通过，原生移动调用是替身，尚无系统实拖证据。移动/resize 全生命周期与透明度冻结、精确 overlay 布局和三平台真实指针/键盘/原生下拉交互仍未完成。原版此处按距离淡出，不是固定秒数延迟。 |
| 原版完整 overlay / profiler | 已接普通模式截图按钮右键导出各效果真实输出（含实际尺寸、源身份、文件命名及失败重试）；详见 [中间结果导出](intermediate-export.md)。开发者逐 pass/多纹理菜单及原始 DDS/最终 PNG 读回已接，Linux GPU 与中英隔离 UI 通过；当前提交的 Windows/macOS 和真实交互仍待验证。有模式/参数、截图、停止、FPS 和 GPU pass 计时；已接原版严格超过 500 ms 更新、跳过零耗时的 pass 平均统计、序号去重和原始 pass 描述，见 [profiler 统计](profiler-statistics.md)。已补真实平均耗时比例条、效果/pass 切换、原始描述/百分比提示、行/色块悬停联动、键盘选择及窄窗口滚动；普通模式与单 pass 的切换限制按原版执行。独立浮层已接标题拖动、关闭、原版默认定位/贴边比例持久化与位置迁移；实际 GPU/捕获后端标签、FPS/动态统计与开发者 FP16 标识已接；真实三平台交互仍未完整验收。 |
| 截图真实元数据 | 已新增 5400 字节版本化 C ABI 源快照并接入真实截图 `%WT%` / `%PN%`，取消固定 `Capture` / `Magpie` 占位。Windows 用已选 HWND 查询当前标题、PID 和实际可执行路径（校验 HWND/PID）；macOS 15.2+ 单窗口用系统选源的 SCWindow/owningApplication 和 NSRunningApplication 获取快照，重选时更新，较早系统保留未知字段。Linux 只使用 ScreenCast 返回的 source_type；通用 Portal 不提供标题/PID/可执行路径，因此不猜测 PipeWire 流名称或捕获进程身份，使用本地化“窗口/屏幕/捕获画面、未知应用”回退。Linux AOT ABI/PNG 与 Windows SDK 语法检查已过；已将元数据改为随捕获帧交付，并在 UI 提交位图时保存该帧快照；截图不再用可能已前进到新源的捕获器覆盖画面身份，重复像素也能更新源身份而不重复 GPU 执行，未知新源不会继承旧应用名称。当前标题只允许在同一窗口/PID 和有效几何帧状态下刷新：Windows GetWindowTextW，macOS 对选定 WindowID 查询 Core Graphics 元数据；查询前后复核状态，只覆盖当前这张截图的标题，不替换进程/路径/画面快照。元数据被系统省略时沿用已知快照，不推断权限也不请求授权；空标题与“无数据”分别处理。Linux 原生帧缓冲/输入身份模型、实际 Vulkan/PNG 以及排队提交前后快照验证已过；Windows/macOS 动态标题查询的代码已接入，真实重选/帧归属一致性及三平台真实截图元数据仍需目标机验收，Linux 完整身份能力仍缺。目录选择、打开目录、裁剪及错误提示在 macOS/Windows 的真实操作仍待验收。 |
| 应用自动识别 / 自动缩放 | 配置 CRUD、复制、独立值、手动选择存在；只有路径匹配辅助函数，没有连接窗口身份监听/自动切换或启动缩放。AutoScale 控件隐藏且未接。 |
| 新建配置的源选择 | 当前手填/选可执行文件；原版运行窗口枚举、应用图标/身份、打包应用，以及 macOS `.app` bundle 身份/启动语义尚未完整适配。 |
| 配置导入兼容 | 已接设置页选文件→迁移报告→备份、替换并重启；转换支持的全局偏好、独立默认/应用配置及原版模式下标引用，保留完整原始 JSON。旧别名/全局 overlay 设置可迁移；错误数据拒绝，未选择模式保持不可运行。Windows 身份和原始参数保留并显示，未选择本机程序时启动/定位禁用；不把 Windows 路径拼成 Linux/macOS 路径。未实现字段逐项报告，不静默当成已生效。备份保留原字节，进程启动失败回滚，检测其他写入者后不覆盖。Linux AOT 文件事务/原始数据往返与隔离宽窄 UI 通过；原生文件选择及真实 GUI 重启尚未验收，Windows/macOS 本轮编译/运行缺失。自动匹配/打包身份、原始启动参数跨系统转换及未实现偏好的实际语义仍缺，详见 [迁移说明](configuration-import.md)。 |
| 模式编辑 / 参数 / 导入导出 | 增删改、排序、参数、链和 JSON 已接；macOS/Windows 的真实文件选择器、原生弹层、键盘操作、焦点、剪贴板及错误恢复尚未完成交互矩阵。 |
| 自定义运行时效果 | 内置 117 个效果已有 GPU 执行；用户动态加载/编译 shader、自定义 SOURCE、编译错误定位/恢复及相关交互仍缺。 |

## 系统集成、设置、更新和界面一致性

| 项目 | 当前状态 / 剩余工作 |
| --- | --- |
| 全局快捷键 | 已补原版截图快捷键偏好（默认 Alt+Shift+S）、主页编辑、四项注册/冲突事务和缩放会话截图队列；本地回退与全局绑定去重，F12 保留；macOS 将截图组合键预留给本地动作。Linux 协议路径、Windows RegisterHotKey、macOS Carbon 和配置事务已有；目标桌面真实注册/冲突、键盘布局、权限、重启恢复及源应用/工具栏共存仍需验收。 |
| 托盘 / 菜单栏 | 共用生命周期及 Linux 隔离测试已有；macOS 菜单栏图标、菜单、关闭后驻留、隐藏启动/恢复及运行中退出的完整实测未完成；Windows 同样待验收。 |
| 登录启动 | Linux XDG、Windows Run、macOS LaunchAgent 已写；macOS/Windows 登录后真实启动、bundle 路径、移动/更新后的恢复、关闭开关和失败提示未验收。 |
| 保持屏幕常亮 | 设置、真实系统后端、事务替身/ABI 已有；macOS/Windows 实际系统申请和释放、睡眠策略，以及 Linux 真实桌面策略仍未验收。 |
| 主题 | 已补齐 System / Light / Dark 偏好持久化、启动前加载、即时应用、设置页回显及保存失败回退。旧配置默认为跟随系统；系统未报告配色时使用默认浅色资源，避免空白/黑色页面。Linux AOT 三种偏好的新进程恢复、无效保存保护和隔离宽/窄 UI 已通过；Windows/macOS 系统主题变更和真实重启交互仍待目标机验收。 |
| 语言与文案 | 20 种原版资源/偏好/子进程初始化已有；新增移植文案并未全部翻译，真实 GUI 语言重启、字体、截断/窄窗口、系统对话框语言仍需验收。 |
| 便携模式 / 配置目录 | 已接通开关、完整当前配置迁移、失败回退、启动选址与定位。便携配置为应用旁 `config/desktop-settings.json`，文件存在即在下次启动选用便携模式；不依赖启动工作目录。开启保留用户目录原配置，关闭先写回用户目录再删除便携文件，写入/删除失败保持原活动位置。macOS `.app` 的便携目录位于包外，避免破坏签名。定位优先 Linux FileManager1.ShowItems、Windows Explorer /select、macOS open -R；不支持时回退打开所属目录。Linux AOT 文件事务、新进程/移动恢复、三项私有 D-Bus 和五项隔离 UI 通过；真实文件管理器调用、只读安装目录及 Windows/macOS 本轮实机验收仍待完成。此配置文件不替代原版 config.json，完整原版数据兼容仍见配置导入项。 |
| 总以管理员运行 | Toggle 没有实现；Windows 提权策略与 macOS/Linux 对应的权限模型未适配。 |
| 自动更新全流程 | 检查更新按钮、自动检查/预览版开关未接服务/保存；下载、取消、重试、进度、安装、重启、发行说明和稍后提醒仅有部分展示组件，缺真实流程。版本显示仍写死，未与 bundle/仓库版本统一。 |
| 反馈 / 帮助 / 外链 | 已对照原版接通 About 报错/建议/讨论整卡片、Repository / FAQ / Contribution / License、Touch Learn More 八个入口；使用 Avalonia 平台 Launcher，触控帮助按简中/其他语言选择原版地址。全卡片是真正可聚焦按钮，保留键盘/辅助功能 Invoke，系统拒绝打开时显示提示并恢复按钮。Linux 隔离 UI 验证八个入口的实际 Invoke 和拒绝路径，未启动浏览器；三平台真实默认浏览器调用及键盘/屏幕阅读器实测仍待验收。 |
| 开发者选项 | 只有 FP16、重复帧、统计、最低 FPS、GPU 计时等已接；日志定位、Debug、Benchmark、禁用置顶/效果缓存/字体缓存、保存效果源码、warnings-as-errors，以及主页 Inline Params 开关均未接完整后端或保存。 |
| 原生 UI 手感 | Avalonia 页面外观迁移不等于 WinUI 2 交互一致；原生弹层定位/遮挡、键盘导航、Tab 顺序、默认按钮、辅助功能语义、屏幕阅读器、不同 DPI 的控件命中和状态反馈尚未全面验收。 |
| 实际呈现性能 | CPU 回读/拷贝、UI bitmap 提交仍在；零拷贝、低延迟呈现、长时间源捕获、GPU/显示器移除、设备错误恢复未完成，不能只以 GPU 回归 FPS 代表实际交互延迟。 |

## 截图快捷键补齐

原版 `AppSettings` 默认 `Alt+Shift+S` 已接入第四项全局动作和主页快捷键编辑。快捷键偏好与默认/应用复制分离，原版 `shortcuts.takeScreenshot` 可迁移；四项重复键校验和原生注册失败回滚包含截图项。编辑后取消旧注册，需明确重新启用。运行中系统激活只给当前缩放会话排队截图，停止后忽略；按当前输出位图和源身份保存，不重新选源。输出窗口无全局绑定时采用配置组合键，有绑定时不重复执行本地动作；兼容原有 F12。

macOS Cocoa 输入桥增加截图预留键的字符/原生键码/修饰键，截图组合不转发源应用。C ABI 同步更新，必须重新编译托管/原生部分；不混用旧库。Linux 原生归档和 AOT 同步发布，截图路由用实际 Avalonia KeyDown/对话框、PNG 文件和像素回读验证；系统输入使用替身/私有 D-Bus。最终 Linux NativeAOT 核心、23 项原生 CTest、7 项中英隔离 UI、10 项私有 D-Bus 和实际 UI 上下文的 Portal 注册测试通过，报告 `artifacts/desktop/screenshot-shortcut-test-summary.json`。三平台真实系统注册、输出焦点与源应用共存、权限/布局变化的截图行为仍待验收；这不补齐其余输入/光标/自动缩放缺口。

## 截图请求与源身份提交顺序修复

此前 `Present` 写入新位图后会直接处理截图队列，而生产捕获回调随后才 `CommitPresentedSource`，在两者之间可能用旧窗口名保存新画面。已移除原始位图提交的队列副作用，共用 `PresentCapturedFrame` 在同一个 UI turn 依次提交位图、源窗口/PID/帧序号，再保存排队截图。重复像素的换源走 `CommitUnchangedFrameSource`，更新身份后才处理队列；拒绝/停止的提交不改变源快照，停止后的队列只清空不落盘。

Linux 最终 AOT 核心与六项中英隔离 UI 通过，实际 Vulkan 与 PNG 检查覆盖旧画面仍在等待时的截图、排队换源截图的名称/像素、重复帧身份更新且 GPU 次数不增加、原始 `Present` 不偷跑队列、停止后提交/排队请求拒绝，并复验动态标题、截图快捷键和已有输出 PNG 行为。身份/像素/按键为注入数据，实际未选源 native capture 用于证明不读取超前元数据；没有打开用户桌面/选源，不等于真实三平台生命周期通过。报告 `artifacts/desktop/screenshot-order-test-summary.json`。原生代码本轮无改动，未重复宣称新原生测试结果。

## 普通模式中间结果导出阶段

已增加执行序号保护的 native 阶段输出快照，读取现有完成的 host-coherent 输出，不增加 GPU 执行或额外维护每帧 CPU 副本。工作线程渲染及 UI 像素/身份提交与导出互斥，UI 异步等待，不阻塞工作线程完成提交。工具栏截图右键菜单按实际链显示效果/尺寸；菜单打开保护自动隐藏，导出保留源信息和阶段后缀，错误/停止/链变更可恢复。此处为普通模式阶段的历史验证范围；后续开发者逐 pass / 多纹理实现和验证见 [中间结果导出](intermediate-export.md#开发者逐-pass--多纹理导出)。

最终 Linux NativeAOT 核心覆盖真实 nearest→bicubic→NNEDI3 阶段缓冲、独立像素、序号更新、过期/长度/索引拒绝、profiler 不增加执行和 Unicode 文件名预算；23/23 CTest 通过。五项中文、两项英文隔离 UI 验证实际菜单 handler、PNG 像素/尺寸/名称、等待新提交身份、错误重试/停止，并复验普通截图、换源截图及已有原生下拉。新增精确视觉树 capture transform 支持装饰窗口内容偏移，解析器拒绝缺失/越界证据，已查看实际合成菜单截图。用户桌面/选源未打开，源身份和输入仍为替身，录制菜单在主测试窗口重新生成；三平台真实输出右键和 GPU 读回仍待验收。报告 `artifacts/desktop/stage-export-test-summary.json`。

## 开发者中间结果导出阶段

开发者模式持久化、原版配置迁移、111 个 graph 的原始 pass 描述/输出纹理子菜单及内部 DDS/最终 PNG 导出已接。内部纹理按原始格式读回；被后续 pass 复用的纹理重新执行至所选 pass，不冒用最终纹理内容。沿用显示帧互斥门、执行序号、源身份/命名和错误恢复，内部重放有额外 GPU 工作但不制造显示帧/FPS/profiler 样本。

Linux 最终 AOT 核心、23/23 原生测试、六种 DDS 独立解码及七项中英宽/窄私有 Wayland UI 通过。15 个 GPU pass 输出覆盖多输出、复用纹理重放、五种实际可写格式、NNEDI3 偶数行独立亮度期望、最终缓冲保留与正常渲染恢复。UI 实际 handler 导出七个文件并与 native 原始数据比较；输入、源身份和指针为注入。修复三级菜单底部越界，保留三层 native popup 的 ack/commit 坐标边界检查并查看合成图；取证菜单在关闭功能测试输出后重新生成。Khronos layer 加载的核心检查未报告 VUID/同步错误。报告 `artifacts/desktop/pass-export-test-summary.json`，细节见 [中间结果导出](intermediate-export.md)。新提交 Windows/macOS 编译/实际 GPU/菜单交互尚未验收；其他开发者空控件、完整 overlay/profiler 和整份目标仍未完成。

## 2026-10-26 Profiler 平均计时阶段

对齐原版严格超过 500 ms 才发布、当前帧计入下一区间、跳过零/极短耗时并保留旧有效值的统计。每次成功画面提交喂入对应 GPU 样本，汇总/视图切换按执行序号去重；改变效果/pass 映射、关闭、不可用或执行序号回退会清空旧统计。拒绝的已停止帧不更新面板；效果总计不混入尾段 barrier/query 时间，逐 pass 文本增加原始描述。

最终 Linux AOT 核心、模型和七项中英隔离 Wayland UI 通过，共用生产提交路径验证真实 Vulkan 三帧采样、50 次重复刷新、延迟均值、开关和停止；已查看实际可见计时文本截图。录制器等待 popup 原生渲染帧，外部 ack/commit 证据门保持严格。报告 `artifacts/desktop/profiler-averages-test-summary.json`，详见 [统计说明](profiler-statistics.md)。本轮原生源码未改，无新增原生 CTest 声明；Windows/macOS 当前提交待 CI 与实际交互。浮动 profiler 布局、时间条/悬停、拖动及所有真实平台缺口继续保留，整体目标未完成。

## 2026-10-26 Profiler 时间条阶段

实际平均 GPU 数据已驱动比例条/耗时列表：按原版普通/开发者与单效果/多 pass 规则提供切换和条显隐，保留原始 pass 描述、效果总计和尾段分离。色块显示适配实际宽度，悬停/键盘选中联动耗时行、百分比及真实提示框。颜色采用原版调色板并按链稳定分配，具体随机顺序不复刻。设置从主页变化后，输出开关和面板同步；没有新 GPU 样本时关闭开发者模式也会收回 pass 控件。周期更新保留 pass 键盘焦点，计时区域不进入拖窗 caption 路径。

最终 Linux AOT 核心、模型及八项中英隔离 UI 通过，使用实际 CuNNy→nearest Vulkan 时间戳验证比例布局、原始说明、实际按钮/焦点与合成悬停路由、窄窗滚动/边界和关闭清理。四个宽/窄中英真实输出提示框具有外部 Wayland ack/commit 及内容边界证据，已查看最终宽/窄图；录制等待正常 fade-in 完成。报告 `artifacts/desktop/profiler-timeline-test-summary.json`。捕获像素/身份/指针为注入，不能当作真实系统输入与三平台 GPU 验收；本轮原生源码不变，未重复声明 CTest。独立浮动窗口布局、拖动、GPU/捕获元信息及完整交互仍缺，详见 [profiler 说明](profiler-statistics.md)。

## 2026-10-26 Profiler 独立浮层阶段

Profiler 与工具栏分离为输出内容区内的面板，按原版默认 310 DIP 宽、最高 500 DIP、右边距 60 DIP 与垂直居中定位。标题使用本地 pointer capture 拖动，释放按原版贴边/中心比例保存；取消/失焦恢复原位置，输出 resize 保持贴靠。关闭按钮关闭统计并同步开关；隐藏工具栏保留 profiler。位置持久化与原版 overlay.windows.profiler 配置迁移已接，未知浮层仍报告未应用。

Cocoa 输入新增独立 profiler 本地命中矩形，拖动时临时排除整个输出，结束恢复；区域未配置成功时绑定空源并暂停转发。可移植谓词验证清空、半开边界、负尺寸和非有限值；Linux/Windows API 返回不支持，未假装增加这两个平台的输入桥。真实 Cocoa 鼠标、按键和源应用事件送达仍待验收。

最终 Linux AOT 核心、模型、23/23 CTest 与八项中英隔离 UI 通过，覆盖实际浮层 handler、抓取/释放/取消与位置、输出 resize、独立显隐、实际 Vulkan 时间条及已确认提交的提示框；指针/捕获源为注入。已查看宽/窄实际输出截图，报告 `artifacts/desktop/profiler-floating-test-summary.json`。Windows/macOS 新 C ABI 与 Objective-C++ 待本提交 CI/目标机验证，不能沿用上一提交的成功。GPU/捕获设备元信息、FP16 标签及完整真实平台交互仍缺，整体目标保持未完成。

## 2026-10-26 Profiler 会话信息阶段

补入实际引擎设备名、已有捕获的 native 实际后端、区间 FPS 与动态预测统计。无捕获对象的注入输出明确显示无捕获会话，未产生性能区间则等待，不以设置偏好伪造执行状态。profile stage 的 FP16 字段来自实际 engine pipeline 查询，只在开发者模式显示；平均值保留精度字段，精度改变清空旧统计。新增信息计入浮层高度预算，窄窗仍受内容边界约束。

Linux 最终 AOT 核心/模型与八项中英隔离 UI 通过；核心同时验证自动/禁用精度实际引擎的 profile 标签，UI 用自动精度 CuNNy/nearest 真 Vulkan 和实际性能区间，检查 GPU 名称、无源不假冒后端、FPS、FP16 标签、浮层位置与已提交 tooltip。动态计数规则/后端名称为模型，未打开用户桌面/捕获选源。报告 `artifacts/desktop/profiler-info-test-summary.json`，详见 [profiler 信息](profiler-statistics.md#会话信息与精度标识)。本轮无原生源码变更，不重复声明新 CTest；三平台真实捕获、输入、光标、多屏及完整交互目标继续未完成。

## 2026-10-26 输入映射/Windows 源保护阶段

新增可移植裁剪坐标映射并实际接入 macOS：越界拖拽保持源裁剪半开边界内，普通越界/非法参数拒绝；修正原来归一化夹到 1 后落在源右/下边界本身的数学问题。Windows GDI 在选择、取帧和标题查询中复核 HWND/PID/线程；最小化或几何失配暂停提交代次，直到新帧到达，过期序号拒绝不破坏有效新帧。相同元组全部复用的窗口生命周期仍需原生销毁跟踪。

新增仅一次性托管 Windows CI 允许的独立源窗口/GDI 测试，覆盖真实绘制像素、源身份、改名、resize、最小化后新帧与销毁；不进入本机/default CTest，没有用户桌面回退。该 fixture 当前待本提交目标机执行，不能先算通过；它也不包含鼠标/键盘转发。Desktop 最小化错误后的自动恢复仍未完成。原版 Windows 需要透明/非激活输出与原生光标路径，不用 PostMessage 的近似实现替代原版输入。

本机 Linux AOT 核心、23/23 CTest、最终分数倍率/source 用例及五项中英私有 Wayland UI 通过；Windows MinGW 严格语法通过，CI 包装器的非目标环境拒绝通过。报告 `artifacts/desktop/input-mapping-test-summary.json`，详见 [映射说明与待验收边界](input-mapping.md)。新 macOS/Windows SDK 与实际事件/捕获结果待相应 CI/目标机验证。Windows/Linux 输入转发、光标及整个三平台目标继续未完成。

## 2026-10-26 Windows 源销毁监听阶段

`1f9343d8` 的 Windows 原生 job 已通过一次性实际 GDI fixture，日志确认独立源绘制像素/尺寸、窗口/PID、标题、resize、新帧和最小化/恢复约束及关闭清理；该证据不包含鼠标/键盘输入，也不证明 Desktop 自动恢复已完成。

新增按源进程/线程过滤的 out-of-context WinEvent watcher 与私有消息线程。匹配顶层/self 销毁后永久关闭该捕获寿命；相同数值身份不能复活，旧状态与新捕获隔离。停止时唤醒、在注册线程卸载、join 并释放 event，消息泵异常也拒绝源。新测试通过仍存活源主动发出销毁事件，验证真实 WinEvent 投递带来的永久拒绝及独立重捕获，包装器要求新增标记；本提交 Windows runtime 结果仍待 CI，不能沿用旧 fixture 成功。通知为异步，仍不声称覆盖事件送达前一切 HWND 重用竞态。

Linux 最终 AOT 核心、23/23 CTest 和三项隔离 UI 通过；原子寿命模型和 Windows 严格语法通过。报告 `artifacts/desktop/source-lifetime-test-summary.json`，详见 [销毁监听与证据边界](input-mapping.md#销毁事件与捕获寿命)。未打开用户桌面，Windows/Linux 原生输入转发及完整三平台交互仍未完成。

## 后续建议顺序

2026-10-22 后续实现记录：本轮主题与外链使用三平台共用托管代码，接入现有目标机 AOT 构建路径；本机 Linux NativeAOT 核心与七项私有 Wayland UI 场景通过。截图已查看浅色 About 和深色 Settings，新增链接拒绝提示有真实 xdg_popup 图层证据。记录为 `artifacts/desktop/interaction-settings-test-summary.json`、`artifacts/desktop/interaction-settings-review/` 和 `artifacts/desktop/interaction-settings-en/`。这不是整份清单完成记录，也不把已有 macOS 编译记录计作本轮变更的 macOS 验收。

随后工具栏阶段：共用状态枚举/原版循环和淡出方程、主页/配置保存与六种输出初始组合已接；最终 Linux AOT 核心和五项隔离 UI 场景通过，包括原有 Vulkan 输出/托盘继续呈现检查。宽/窄配置截图已查看。报告 `artifacts/desktop/toolbar-test-summary.json` 与 `artifacts/desktop/toolbar-review/`。没有打开用户桌面窗口/捕获选择器；原生鼠标/键盘/输入送达和 Windows/macOS 本轮运行尚未验收。

2026-10-23 配置目录阶段：Linux AOT 核心覆盖便携/用户切换、移动后的配置选择、新进程恢复、不可写目标与删除失败不丢所有权；三平台定位命令使用字面参数，Linux 在私有 D-Bus 验证带空格/中文/#/?/& 的文件 URI、服务拒绝和服务缺失。五项私有 Wayland UI 覆盖真实开关处理器、迁移回显、写入失败回退、定位请求/拒绝恢复，并复验主题/工具栏偏好。报告 `artifacts/desktop/configuration-test-summary.json`；截图 `artifacts/desktop/configuration-review/`，协议 `artifacts/desktop/configuration-reveal/`。没有修改真实配置目录、调用用户文件管理器或打开桌面测试窗口。

源元数据阶段：新增 `capture_source_metadata` CTest，三平台共用结构大小/偏移、UTF-8 完整字符截断、真实未选源 capture 和缓冲区边界/参数错误均有检查；Linux AMD CTest 20/20、最终 AOT 核心、四项私有 Wayland UI 通过。截图 PNG 流程验证源名、Unicode/非法字符、重名保护、元数据更新与未知回退，选定源身份使用注入数据，不等同于真实应用元数据验收。Windows SDK 的捕获分支和新增测试通过 MinGW 严格语法检查。报告 `artifacts/desktop/source-metadata-test-summary.json`，没有打开用户桌面窗口或共享选择器，本轮 macOS 新代码未在目标机编译运行。

倒计时阶段：共用 .NET 单调时钟模型验证时长快照、向上取整、模式重置、取消、延迟 tick 只完成一次与非法参数。Linux 最终 AOT 核心及五项隔离 UI 场景通过，其中宽/窄主页通过实际按钮 handler 驱动计时、取消、即时启动入口、自然到期和合成捕获清理；复验等待捕获/捕获失败的常亮释放路径。截图最终冻结计时器，避免截图慢时落入真实选源。报告 `artifacts/desktop/countdown-test-summary.json`。原版依据 `HomeViewModel::_ToggleTimer`、`ScalingService::SecondsLeft/_CountDownTimer_Tick`；Windows/macOS 本轮运行仍未验收。

捕获后端阶段：新增 `mp_capture_backend` C ABI，接通配置保存、复制及实际启动校验；修复选择事件中重建下拉列表导致 Avalonia 索引失效，成功选择只刷新状态，失败恢复延后至事件完成。Linux 最终 AOT 核心、六项中文宽/窄及两项英文私有 Wayland UI 通过，包含实际选择处理器、外来后端配置保留、显式恢复、禁用项与启动前拒绝；启动测试在引擎/捕获创建前设守卫，不使用真实捕获源。Windows 捕获源通过 MinGW 严格语法检查，尚未运行；macOS 新查询没有目标机编译运行证据。报告 `artifacts/desktop/capture-policy-test-summary.json`，本轮没有打开用户桌面窗口/共享选择器。完整 WGC/DXGI/DWM、标题栏切换等仍是后续工作，不能将明确禁用当作功能移植完成。

工具栏固定/关闭阶段：接入 ToggleButton 与关闭叉号，按原版 `OverlayDrawer` 的固定、左键停止、右键隐藏语义实现；右键在松开时隐藏，让当前鼠标手势完整留在工具栏，避免 Cocoa 桥接后半段释放事件落到源应用。工具栏命中区域变化时即时更新现有 macOS 输入排除配置，但本轮没有在 macOS 实测输入。Linux 最终 NativeAOT 核心与五项私有 Wayland UI（中文工具栏动作/六种初始模式状态/真实 Vulkan 输出/托盘，英文工具栏动作）通过。动作使用实际 Toggle/Invoke provider 和合成指针路由，源帧循环为替身，不等同于系统鼠标或真实源应用交互通过。检查窗口/全屏输出截图中的固定/关闭按钮；报告 `artifacts/desktop/toolbar-actions-test-summary.json`，未打开用户桌面窗口或捕获选择器。Windows/macOS 本轮运行未验收，完整 overlay、拖动和移动/resize 冻结仍缺。

工具栏拖动阶段：依据原版 `ScalingWindow::WM_NCHITTEST` 的 overlay caption 区域规则，为当前工具栏非交互区域接入 `Window.BeginMoveDrag`；保留事件及其平台 cookie，让各平台原生窗口后端处理移动。按钮、下拉和数字输入等交互区域通过完整视觉祖先链识别，避免点击内部标签/箭头时误拖窗口。Linux 最终 NativeAOT 核心、四项中文私有 Wayland UI（拖动请求、固定/关闭动作、初始状态/淡出、真实 Vulkan 输出）及一项英文拖动请求场景通过。移动后端为可拒绝/重试的测试替身，只证明路由、原始事件传递、排除条件和局部状态清理；不能将其作为 GNOME/KDE/Hyprland 或 Windows/macOS 的系统实拖验收。报告 `artifacts/desktop/toolbar-move-test-summary.json`，没有打开用户桌面窗口或捕获选择器。原版移动/resize 期间冻结透明度尚未完成。

帧与源归属阶段：新增带代次的双缓冲 `CaptureMailbox`，将像素、尺寸与源快照一起交付；生产者和重选不改动已经返回给 C ABI 调用者的缓冲。macOS 选源回调切回主线程，换源使用新 filter/尺寸重建 SCStream，旧流样本、停止错误及启动完成回调不能污染新流。只处理 SCFrameStatusComplete，Stopped 进入错误收尾；取消重选保留当前捕获。三平台共用 UI 按已提交位图保存源身份，截图与输入不再读取超前的源信息；输入 ABI 增加期望窗口/PID，等待匹配帧期间禁用旧目标并给出单独等待提示，相同输入目标重复配置保持按键状态。Linux 创建未选源对象延后到 Start 才连接 Portal，总线缺失不妨碍快照/销毁测试。最终 Linux NativeAOT 核心、21/21 CTest、ASan/UBSan、Windows SDK 严格语法及五项中英隔离 UI 通过。新增 UI 使用真实未选源 native 对象和实际 Vulkan/PNG，选定身份/像素及输入等待状态为注入数据；未打开选源或用户桌面窗口。macOS 新 Objective-C++ 没有目标 SDK 编译或系统运行证据，不能将 Linux 模型测试算成 macOS 生命周期完成。报告 `artifacts/desktop/frame-source-test-summary.json`。API 依据：[Apple 系统选源/新流说明](https://developer.apple.com/videos/play/wwdc2023/10136/)、[帧元数据](https://developer.apple.com/documentation/screencapturekit/scstreamframeinfo)。

捕获尺寸/几何阶段：新增可移植内容矩形及异步 resize 状态机，恢复自然分辨率并串行合并配置，防止旧尺寸被继续放大、旧流完成回调回写新流。macOS 帧回调接入原生 `updateConfiguration`、内容裁剪与单窗口阴影排除；Blank/Suspended/暂时空内容暂停输入。增加帧序号 C ABI，UI 随位图提交保存序号，源/尺寸变化后的第一批帧建立新的有效区间；普通帧延迟不会反复释放长按/拖拽。新增 CPU `capture_resize_geometry` 和实际 GPU `capture_resize_vulkan`，验证带行填充 BGRA 的内容裁剪后进入 Vulkan，2x nearest 的每个 RGBA 通道与独立数组期望一致。Linux 最终 NativeAOT 核心、23/23 CTest、几何模型 ASan/UBSan、Windows SDK 语法及五项中英隔离 UI 通过。还统一了 MSVC 测试目标的 /MT，避免新测试/源快照测试与静态库的 CRT 混用；没有 Windows/MSVC 或 macOS 新代码的目标机编译/运行证据。报告 `artifacts/desktop/capture-resize-test-summary.json`。无用户桌面窗口、真实选源或原生事件送达测试。API/单位依据：[Apple 配置更新](https://developer.apple.com/documentation/screencapturekit/scstream/updateconfiguration(_:completionhandler:))、[阴影设置](https://developer.apple.com/documentation/screencapturekit/scstreamconfiguration/ignoreshadowssinglewindow)、[WebKit 当前元数据处理](https://github.com/WebKit/WebKit/blob/main/Source/WebCore/platform/mediastream/cocoa/ScreenCaptureKitCaptureSource.mm)。

动态截图标题阶段：新增可选 `mp_capture_snapshot_title` C ABI，只有已提交画面的同一窗口/PID 才能刷新标题；macOS 复核当前源/几何帧状态，Windows GDI 新增同一套帧/几何序号，并用当前客户区尺寸与捕获尺寸复核，最小化或尚未提交的 resize 画面不接受实时标题。Windows 查询 HWND caption，macOS 查询已选 WindowID 的公开元数据，不查当前前台窗口，也不为标题请求额外权限。刷新不制造帧/增加 FPS，不改变位图或缓存源身份；仅格式化当前截图名称时采用新标题。明确空字符串清除旧标题，系统未提供/异常/错窗口/错进程保留已知快照；不以元数据缺失推断权限状态。抽出共享 CaptureEpoch，补充正常帧延迟、实际尺寸不匹配、无需显式 pause 的 resize 以及恢复边界验证，修正内容矩形近整数抖动。Linux 最终 NativeAOT 核心、23/23 CTest、几何/帧状态 ASan/UBSan、Windows SDK 严格语法及五项中英隔离 UI 通过；UI 使用实际未选源 native 对象及注入实时标题，验证真实 PNG 名称/像素、Unicode/空标题、外来/复用身份拒绝和失败回退。Windows 实际窗口与 macOS SDK/系统查询没有本轮运行证据。报告 `artifacts/desktop/source-title-test-summary.json`。API 依据：[Microsoft GetWindowTextW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowtextw)、[Apple 窗口元数据](https://developer.apple.com/documentation/coregraphics/cgwindowlistcopywindowinfo(_:_:))、[Apple 对权限推断的说明](https://developer.apple.com/forums/thread/839069?answerId=898801022)。

原版配置迁移阶段：新增共用解析器、原始字段归档、离线迁移命令和设置页审查/确认流程。原版 `_LoadProfile` 的未选择模式会拒绝缩放，迁移不将其误作直接效果；每个应用从独立局部默认值开始，再解析原始模式下标。支持旧属性别名及 overlay 全局迁移。新增启动请求失败的原字节回滚、无旧文件恢复及并发写入保护测试；报告未应用设置和不可用身份，不猜测平台路径。Linux 最终 NativeAOT 核心和中英私有 Wayland UI 验证文件选择取消/错误、审查取消、实际辅助功能 Invoke、重启拒绝后的恢复/重试、备份及导入应用的启动/目录禁用。文件选择和进程启动为隔离替身，不等于三平台原生选择器或 GUI 重启通过；新进程创建后的初始化故障尚无健康检查。报告 `artifacts/desktop/original-config-test-summary.json`，未打开用户桌面窗口/选源或重启用户实例。完整目标仍未完成。

1. 用独立源应用验证 macOS 点击/拖拽/滚轮/键盘、单光标和停止清理，补真实输入集成测试；先解决当前最直接的使用阻塞。
2. 补源窗口元数据、尺寸/焦点生命周期、多屏/DPI、自动匹配与工具栏状态。
3. 对所有空控件实现后端/保存，或在不适用的平台明确禁用并说明，消除“可操作但无效果”。
4. 完成文件选择/模式编辑/配置/快捷键/托盘/启动/常亮的三平台真实交互验收。
5. 补更新、自定义效果和高级游戏/触控能力；Windows 专用项按平台语义适配，最后做与原版逐项对照回归。

审计依据：[原版 Home](../src/Magpie/HomeViewModel.idl)、[Profile](../src/Magpie/ProfileViewModel.idl)、[Settings](../src/Magpie/SettingsViewModel.idl)、[About](../src/Magpie/AboutViewModel.idl)、[CursorManager](../src/Magpie.Core/CursorManager.cpp)，以及 [Avalonia 页面](../src/Magpie.Desktop/Views/)、[RuntimeSettings](../src/Magpie.Desktop/Core/RuntimeSettings.cs)、[ScalingSession](../src/Magpie.Desktop/Core/ScalingSession.cs)、[macOS 输入](../src/Magpie.Native/input_macos.mm)。
