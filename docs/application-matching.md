# 应用身份与配置匹配

2026-10-31 本地实现阶段，对照原版 `ProfileService::GetProfileForWindow` 与 `ParseClassName`；完整三平台自动识别/自动启动尚未完成。

Windows 的手动即时、快捷键和倒计时到期入口，在创建捕获与 Vulkan 引擎前查询实际前台窗口。对于已有原版规则或新建 Windows 窗口规则的配置，按配置列表顺序比较窗口类、打包标记和路径；命中后使用该应用的独立捕获方式、算法与参数，没有匹配项则取默认配置。启动不更改已保存的活动配置。Windows 新建配置现已接运行窗口列表与独立 WindowsRule；其他平台以及只有启动路径、没有窗口规则的既有配置仍保留手动选择方式。

窗口类规则保持原版语义：WPF `HwndWrapper[名称;;GUID]` 提取名称，Chromium 数字类后缀归一到 `Chrome_WidgetWin_1`，TeknoParrot 数字类后缀归一到 `XWindow_0`。异常后缀保留原值，大小写、空后缀以及原版 WPF 正则中的逗号接受行为均保留。`classNameRule` 是归一化后的字面身份，`*` 不代表通配符；路径和 AUMID 使用原版的严格字符串相等，不自行添加大小写、目录前缀或通配匹配。

原生前台查询只读取其他进程的 HWND/PID/TID、类名与实际可执行路径，不激活窗口、不注入输入、不显示选择器。当前仅接受已确认无 package 的桌面进程；`ApplicationFrameWindow` 与打包进程返回不支持，不能把 ApplicationFrameHost 的路径当成 UWP 应用身份。原版规则中虽保留 AUMID 且模型可比较，实际 AppX 子窗口/AUMID 解析仍未实现，不算 UWP 匹配已通过。

匹配时的 HWND/PID/TID 一起交给新建捕获会话。原生 Start 再核对身份，再建立原有寿命监听和后续代次保护；它不会在后台启动过程中重新挑选另一个前台窗口。查询时无权限、无完整路径或前台发生改变时不宣称已识别。PID/TID 核对不能证明启动监听之前、完全同一身份元组的 HWND 复用一定可检测；真实应用/复用竞争仍需目标验收。

原版 `autoScale` 的 Disabled/Fullscreen/Windowed 值和旧布尔值已转入独立偏好；非法原始数值按原版回到 Disabled。旧移植文件缺少类型化字段时可从应用 `OriginalProfile` 归档恢复；显式保存的 Disabled 等值优先，读取不改写原文件。匹配模型已区分手动查找与自动查找：自动查找先跳过禁用项，再按列表优先级匹配。Windows 桌面规则已继续接入 50ms 前台监听、自动启动与运行中切换。按原版先过滤已禁用的配置，再取第一项匹配；已检查的前台与当前源不重复启动。原生 owner 链与“主窗禁用、同进程且没有传统 enabled popup”的条件用于排除当前源的弹窗；不把同进程所有次窗口都视为弹窗。原版 `MonitorFromWindow(MONITOR_DEFAULTTONULL)` 与 `SendMessageTimeout(WM_NULL, SMTO_ERRORONEXIT, 500ms)` 判断屏幕外/加载中源，未就绪时保留重试资格；窗口身份查询与无响应检测在后台执行，不在 UI 线程等待。

Linux/macOS 不使用 Windows 路径规则、类名或 AUMID 作为本机身份。Wayland ScreenCast Portal 不提供通用的标题/PID/可执行路径，完整跨平台识别仍需相应能力与权限；本轮没有新申请权限或打开用户桌面窗口。

本地 Linux NativeAOT 单文件、交互/迁移/原生模型、两项私有 headless Wayland 倒计时界面测试和 Windows C++ 严格 MinGW 语法通过。模型覆盖原版类名边界、字面与大小写行为、打包/桌面隔离、手动/自动优先级、迁移与归档回填、显式值优先及文件原字节保持。新的 Windows hosted 原生夹具要求实际前台类/路径/PID/TID、错误 PID/TID 拒绝和指定窗口实际像素；真实 SDK/夹具结果仍待下一批集中 CI。以上本地检查不证明 Windows 匹配、自动缩放或三平台目标已经完成。


## 自动监听与会话交接（本地通过，Windows 目标待验收）

用户停止后，如果原源仍在前台，就记录该窗口为已检查；留在同一窗口不会立即重启，切换前台后可重新触发。按钮、快捷键和会话自己的工具栏 Stop 均能取消排队的自动请求；用户操作的版本号也使已在后台查询的结果失效，防止“停止刚完成又自动开始”。普通取消与自动替换分开，原生清理中的取消不冒充一次用户 Stop。

自动切换先请求旧会话退出，等到原生捕获已销毁后，再核对新前台、窗口/PID/TID、就绪状态及当前配置列表。目标已换、规则删除/禁用或用户发出新操作时取消排队请求；配置重排使用当时的第一项匹配。不会同时运行两个捕获会话。现有 WGC 原生销毁若挂起，交接仍会等待，不能因此宣称旧挂起已解决。倒计时在自动会话启动时取消，真实关闭主窗停止监听，隐藏到托盘保持监听。

应用页的 Disabled/Fullscreen/Windowed 选择接实际保存、回显、重开与保存失败回退，并使用原版多语言文案。只有 Windows 且具有原版桌面身份规则的应用配置可操作；其他平台与打包/未知规则显示禁用并保留偏好，默认配置不显示这个控制。新建 Windows 配置已继续接运行窗口选择器与类型化身份；仅有启动路径的旧配置不会被假装成完整窗口规则。

本机最终 AOT/单文件/迁移与六项原生模型通过；隔离 Wayland 验证 MainWindow 和 ScalingSession 的真实 start/stop/cleanup 代码，前台/就绪/捕获使用替身：就绪后重试、动态类名、窗口/全屏选择、停止抑制与切换后重启、弹窗排除、等待旧销毁、合法交接、工具栏取消排队、禁用排队规则和查询途中用户停止；另有偏好宽窄界面与倒计时回归共五项。本机不查询或操作用户桌面窗口。

Windows 下一批 hosted 生产矩阵改由实际前台查询→原版规则→自动入口开始 GDI/WGC × 窗口/全屏四条捕获/Vulkan/显示路径，要求 `NativeAutomaticChecks=3`（实际规则启动、输出模式、重复检查不重启），继续要求全部既有原生输入/光标/隐藏恢复/像素断言。测试源由独立子进程创建，规则路径来自独立 fixture 路径，非替身截图；指针初始抑制仍由原有 opt-in hosted 边界提供。源的 HWND/PID 和后端由原有生产门独立核对。这些目标结果仍待集中 CI，本地测试不证明 Win32 原生 owner/响应性判断、真实应用、权限、多屏 DPI 或跨平台自动缩放已通过。

原版 WinUI 构建/分析与 Wiki 发布流程在本移植仓库改为 `workflow_dispatch` 手动执行，防止每批 Desktop 改动另触发原版五架构/编译器任务及无关 Wiki 发布；Desktop 的 main 推送三平台四 job 验收保留，软件 Vulkan、AOT/单文件和 Windows 原生门不删除、不放宽。


## 5b2dca4c 目标结果

[Desktop run 41203808960](https://github.com/greepar/demo-mg/actions/runs/41203808960) 的四个 job 均 success；同一 head_sha 的运行列表中只有这一套 Desktop workflow，旧 WinUI/分析/Wiki 没有额外触发。[Windows job 124544717111](https://github.com/greepar/demo-mg/actions/runs/41203808960/job/124544717111) 原始 JSON 已核对：GDI/WGC × 窗口/全屏四例 State=passed、Error=null，每例 NativeAutomaticChecks=3，原有指针/光标/恢复门通过；GDI、WGC、带标题栏 WGC 的原生 fixture 每例 profile_selection_checks=4、最大化 5、无边框 4、恢复 16。限定汇总为 `artifacts/desktop/automatic-scaling-target-summary.json`。这证明本次实际前台/规则/启动/模式/重复轮询，不证明原生强制交接、真实应用、3D/IME、多屏或所有自动交互完成，也未解决旧 WGC/NNEDI3 间歇问题。

## 运行窗口新建配置（本地验收，目标待验收）

Windows 新建配置在后台枚举外部桌面窗口，按原版过滤：不可见、WS_EX_TRANSPARENT、DWM 框读取失败、宽高都小于 50、无标题、cloaked，以及 Progman/Xaml_WindowedPopupClass。窗口标题使用原版 CompareStringEx 的用户区域、忽略大小写/宽度与 linguistic casing 排序；默认名称取可用进程文件描述，否则保留标题。列表不激活窗口、不捕获、不注入输入。打包窗口和本进程窗口暂不列为可用源；真实应用图标与原版名称填充上下文菜单仍待补齐。

候选列表是有明确销毁边界的原生只读快照。用户选中快照后保存路径与归一化类名，已存在的同一应用/类规则从列表排除；创建时再次检查是否出现重复规则。和原版一样，保存的是持久身份，不要求该窗口一直活着，更不按陈旧 HWND 重新选择一个应用。永久 WindowsRule 仅含 Packaged/PathOrAumid/WindowClass，HWND/PID/TID 不写入配置；原版归档 OriginalProfile 仅用于真实导入，不拿生成的数据冒充原始文档。

不同应用的配置允许同名，按独立 ID 管理；这修正了早期移植比原版更严格的名称唯一限制。复制选定配置的设置/参数时，不复制它的窗口身份。新规则直接用于手动/自动查找和应用页能力判断，不再仅限于导入配置。

表单支持加载、无可用源、错误、刷新、默认名称、单候选自动选择和多候选保持未选择；过期刷新结果不覆盖新结果。Linux/macOS 保留原有本机可执行文件输入，不枚举假 Wayland 窗口；普通隔离预览也不偷偷查询真实桌面。Windows 持久规则和本机启动字段分别保存，跨系统直拷原生配置的启动路径处理仍不是本轮验收范围。

本机 NativeAOT 平台/模型、源 ABI、配置复制/保存失败回退、归一化去重、同名身份、AOT 序列化及 MinGW 严格语法通过。六项私有 Wayland UI 通过，覆盖选窗宽/窄表单和嵌套下拉、空/错误/过期刷新、实际创建、重开过滤、单选/默认名、并发重复拒绝与恢复、混合新规则/原版规则自动生命周期和旧手填流程。图像已查看；下拉必须有两层真实 native popup。UI 候选数据为替身，不能当作 Windows 枚举实测。新增 Windows 原生夹具要求独立真实源出现在快照中，核对 HWND/PID/TID/class/path 与自进程排除，保留原有像素/超时条件，尚待下一批集中验收。AppX/UWP、真实图标/名称菜单、其他平台身份识别以及真实应用/多屏/DPI 继续未完成。


## 选窗图标与名称菜单（2026-11-02 本地阶段）

桌面候选读取真实窗口图标，沿用原版的小/大窗口图标、类图标与 owner 回退顺序，再尝试可执行文件资源和串行的 IShellItemImageFactory。WM_GETICON 查询放在后台并使用有界 SendMessageTimeout，避免无响应应用阻塞 UI；像素读取前后核对 HWND/PID/TID，窗口身份改变则返回无图标。窗口图标按 GetIconInfo/GetDIBits 读取颜色与掩码，按原版 alpha/掩码语义转为预乘 RGBA；Shell 位图路径同原版无条件预乘其 alpha，不擅自猜测是否已预乘。仍未覆盖打包应用图标、仅掩码的单色应用图标或所有 Shell handler 的实际兼容性。

新 C++ 文件进入自有静态归档，Windows NativeAOT 显式链接系统 shell32；没有新增自有动态库。托管层复制像素后立即释放原生快照，在 UI 线程创建位图；刷新/弹出关闭后释放位图。无图标时保留通用窗口占位符。普通隔离预览的候选和图标来自替身，不查询真实桌面。导航栏与应用页标题的图标尚未接这套读取器，不把候选图标进展记作全 UI 图标完成。

名称输入右键菜单接原版多语言的剪切/复制/粘贴/撤销/重做/全选，以及桌面应用的“填入进程名”（实际为原版 DefaultProfileName，即优先文件描述）和“填入窗口标题”。按当前文本选择/撤销状态显隐；没有选中窗口时隐藏名称填充项。替换名称后光标移到末尾并聚焦，值未变化时不重写文本。Enter 按钮走相同实际创建处理器。打包应用尚不可选，因此没有显示假的“填入应用名”动作。

本机 NativeAOT/单文件和六项原生模型已通过，其中独立像素向量检查 BGRA→RGBA、alpha 舍入、透明掩码及无掩码不透明回退。隔离 UI 检查实际菜单动作、末尾光标、Enter 创建、图像占位替换、刷新释放，并核对宽窄两层 native popup。已查看窄名称菜单图。Windows 新增独立源的 2×2 alpha class icon，要求逐字节符合独立像素、错误进程拒绝与 application_icon_passed；本机仅严格 MinGW 语法通过，SDK/实际图标和候选枚举继续待下一批集中 CI。旧 WGC/NNEDI3 间歇问题不因这些本地检查改变状态。

最终本地证据：`artifacts/desktop/window-icons-platform-verified.log`、`window-icons-ui-verified.log`、`window-icons-mingw-verified.log`。六项私有 UI 为名称菜单宽/窄、窗口下拉宽/窄、自动生命周期和旧手填路径流程；嵌套菜单均要求两层 native popup。原生 Windows SDK/像素报告仍待目标运行；本轮没有推送或触发 Actions。
