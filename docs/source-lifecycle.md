# 源窗口最小化、隐藏与恢复

原版 `ScalingWindow::_UpdateSrcState` 在源不可见/最小化时进入 repositioning，`ScalingRuntime::IsSrcRepositioning` 等待它重新可见，再恢复缩放。之前移植的 Windows C ABI 将最小化作为 -1 返回，Desktop 因而结束整个会话；native fixture 手动恢复不能证明产品能恢复。

现在 Windows GDI/WGC 的 `mp_capture_source_state` 返回 0（未选择/未知）、1（窗口可见）、2（最小化/隐藏，需等待）或 3（停止/身份丢失）。这只是窗口可用性，不是输入权限或画面就绪状态；Linux/macOS 当前返回 0，不猜测 Portal 或 ScreenCaptureKit 的窗口状态。不可见时暂停已交付帧的有效状态，WGC 同时通知异步生产者清空 pending；`mp_capture_frame` 返回 0 Waiting，保留最后交付的缓冲与序号。恢复可见之后仍必须交付新帧，旧序号不能自行恢复有效。源销毁/身份失效仍结束会话。

GDI 分开保存 DIB 的尺寸、待转换像素和 ABI 已交付像素；尺寸重建、隐藏或交付前复核失败不能改写调用者仍持有的像素。BitBlt/GetDC 失败若同时观察到源暂不可见则进入等待，真实捕获失败继续报告错误。发布前再次检查源身份、可见性和客户区尺寸。

Desktop 等待期间收回输出、关闭参数/导出菜单与下拉，取消 profiler 拖动、清除待截图和阶段导出状态，暂停 GPU 处理并释放常亮申请。会话、选定源和 Vulkan 引擎保留；持续不可见不会触发首次等帧的 30 秒超时。源重新可见后等待新帧，恢复后的等帧仍有 30 秒超时；成功时重置处理器和显示计划。只有比暂停前更新且身份一致的提交才能重新显示输出，重复像素的身份提交也能恢复。停止期间的迟到提交不重新显示窗口。等待时关闭源仍终止会话，源窗口正常关闭现在按丢失状态正常结束，不再作为启动错误提示；挂起窗口、源移动/最大化、多显示器/DPI 与其余完整状态矩阵继续待验收。

Hide 后创建的新原生窗口需要显式恢复模式：保存全屏/普通窗口状态及客户区尺寸，恢复时重新应用。Windows 普通窗口还恢复位置；Wayland 不尝试设置绝对位置。首帧前就暂停、尚无输出窗口时，不用空的旧状态覆盖初始全屏请求。恢复请求使用 ShowActivated=false，并还原原来的偏好；源恢复后的前台保留已由后述四场景核对，初始缩放、工具栏点击、应用切换和子窗口的完整焦点路径仍未对齐。实际鼠标键盘/光标路径仍未实现，不把此次等待行为算作输入已完成。

## 验证范围

本机 Linux 25/25 原生测试通过。最终 AOT 的五项中文私有 Wayland 场景以及两项英文生命周期场景通过，包括普通/全屏恢复、原有源身份截图、阶段导出、工具栏操作。生命周期场景使用真实 Vulkan 最近邻与 PNG 检查；覆盖改变过的窗口尺寸、初始全屏前等待、打开导出菜单后暂停、旧序号/错误源拒绝、新画面和重复画面恢复、截图/导出阻止及隐藏时停止。源可见性和身份为注入数据，没有打开用户桌面、选源或输入权限对话框。

`source_lifecycle_capture.py` 独立检查 Wayland 事件：按对象代次区分 ID 复用，要求隐藏时原生 output surface 与其导出 popup 销毁、恢复时先 ack_configure 再 attach/commit 新缓冲、停止后没有再次映射。包含普通/全屏两份日志，删除确认、缓冲提交、阶段标记或销毁证据的篡改版本必须拒绝。UI 内的 IsVisible 断言或 RenderTargetBitmap 截图本身不算原生显示/隐藏证据。

Windows 的一次性托管 fixture 已增加 GDI、WGC 和 WGC 标题栏三条路径的真实最小化/恢复、隐藏/显示、live state、保留缓冲/序号、旧状态拒绝和源丢失检查；包装器要求新增 PASS 标记，旧二进制不能冒充该覆盖。严格 MinGW 语法检查通过，本提交 MSVC/SDK 与实际 native fixture 结果仍待 CI。这个 fixture 没有运行 Avalonia 的完整生产捕获循环或注入输入，不能代替 Windows 端到端 UI/焦点验收；macOS 仅验证新增 ABI 的目标编译/模型，未增加窗口可见性探测。

本机报告为 `artifacts/desktop/source-lifecycle-test-summary.json`，UI 和协议证据在 `artifacts/desktop/source-lifecycle-review`。NNEDI3 偶发像素问题与完整交互目标继续保留。


## 一次性 Windows 生产链路联合测试

`bc2eb569` 的 Windows SDK/AOT 与 GDI/WGC/标题栏原生 fixture 已实际通过，日志包含新增 hide/show suspension 与 live source-state ABI 标记；两种 macOS 架构也通过，Linux 软件 Vulkan 回归在记录时仍在执行。这更新上一节的历史待验证状态，不等于 Avalonia 生产捕获循环或输入送达已通过。

新增 `--windows-source-lifecycle-test <source-fixture> <output-directory>` 专用入口。入口要求真实 Windows、GitHub Actions/github-hosted、显式 opt-in 和仓库固定位置的已构建源 fixture，在加载设置或初始化 Avalonia 前检查；直接使用预览场景名不能绕过。预览模式关闭配置持久化，测试不在用户桌面运行。源窗口属于测试新建的独立进程，所有控制前复核 HWND/PID；父应用退出时 native 等待注册会让源进程自行退出，正常退出另检查 exit code。

CI 包装器读取 runner 已安装 Chrome/Edge 的 x64 SwiftShader ICD，检查 PE 架构并记录 JSON/DLL SHA256，非提权进程通过环境选择该驱动；提权进程在一次性 VM 内按标准 HKLM x64 ICD 注册方式临时登记，finally 恢复精确原值，不改其他驱动。不会在用户机器执行该步骤，不启动浏览器，也不把 SwiftShader 打包进应用。SwiftShader 的 CPU Vulkan 用途依据 [Chromium 官方说明](https://chromium.googlesource.com/chromium/src/+/main/docs/gpu/swiftshader.md)，运行器预装软件见 [官方 Windows 2022 镜像清单](https://github.com/actions/runner-images/blob/main/images/windows/Windows2022-Readme.md)。缺少驱动、初始化失败或设备名不符直接失败，不把环境缺失记成产品测试通过。

联合场景调用原样 `ScalingSession.RunAsync`，没有使用 `_previewLoop` 或注入帧/可见性。GDI/WGC 各运行普通窗口与全屏：实际读取自有 Win32 画面，经软件 Vulkan 和 Avalonia 位图显示，独立核对源窗口/PID、后端及 RGB；实际最小化/隐藏、native 输出不可见、新交付序号/画面恢复、前台源焦点与输出模式/尺寸/位置、截图、隐藏中停止或关闭源、进程清理。强制最低 30 FPS，使暂停期间 GPU 序号不变不能仅靠静态重复帧检测碰巧通过。能力探测不支持 WGC 时明确记录 unavailable/未测试，其他失败不能回退 GDI。

这一新增 Windows GUI/软件 Vulkan 联合门仍需本提交目标 CI 的实际结果，不能用本机 Linux 保护入口、AOT、模型或私有 Wayland UI 的成功代替。记录包含明确的 no input delivery / no hardware Vulkan / no full parity 标记；合成源不代表真实应用、游戏、HDR/DPI 或完整焦点矩阵通过。Windows 生产测试产物在 `artifacts/platform-build-checks/win-x64/production-lifecycle`。


`e19d36c8` 的首次联合验证已实际执行并失败在 vkCreateInstance（VK_ERROR_INCOMPATIBLE_DRIVER），还没有任何生产画面证据；源进程已正常清理。不能将该失败算作图形/焦点通过。根据 [Khronos 驱动发现文档](https://github.com/KhronosGroup/Vulkan-Loader/blob/main/docs/LoaderDriverInterface.md#exception-for-elevated-privileges)，补充 token elevation、ICD manifest 与完整 loader 日志，并为提权 runner 临时登记标准 ICD 注册值、最后恢复；不改生产引擎版本或把失败降成跳过。后续目标结果仍待验证。


## 351eff44 实际目标结果

[Windows job 122399243582](https://github.com/greepar/demo-mg/actions/runs/40563711659/job/122399243582) 已实际通过生产联合门。产物明确记录 GDI/WGC × 普通窗口/全屏四个 passed，WGC 没有跳过；设备为 SwiftShader Device (Subzero)。新帧序号分别为 2→17→24、2→9→17、1→14→20、1→8→14，均覆盖原生最小化/恢复和隐藏/显示。四条路径的 native HWND 隐藏、恢复后源前台保留、暂停 GPU 序号、截图、正常源退出和清理通过；普通窗口还验证了源隐藏时关闭不报错，全屏验证隐藏时 Stop。此结果补上该限定生产循环矩阵的实际证据，不扩大为任意应用、输入、DPI 或初始焦点已对齐。

## Windows 初始显示焦点与源键盘

原版 `ScalingWindow` 使用 `WS_EX_NOACTIVATE`，并在意外获得前台时把焦点还给源；它不把普通 Esc/F12 变成全局停止/截图键。移植版首次显示现在请求 `ShowActivated=false`，避免输出替换源的键盘焦点；Windows 标题不再承诺源获得焦点时按 Esc 停止。输出真正获得焦点时的现有本地处理仍保留。后续还对本输出 HWND 设置 `WS_EX_NOACTIVATE`，通过 Avalonia 样式回调保持后续样式更新，并处理 `WM_MOUSEACTIVATE → MA_NOACTIVATE`：点击仍应到达输出控件，同时保留源前台。没有更改主窗口的激活规则。工具栏键盘编辑、意外前台纠正和完整子窗口策略仍未达到原版行为。

一次性 hosted Windows 联合门增加初始显示以及两次恢复后的实际 `SendInput` 检查：B、Esc、F12 的按下和释放，由自有 Win32 源窗口收到的消息计数证明，B 还检查 `TranslateMessage` 产生的字符。注入前检查平台/runner/专用入口、源 HWND/PID/前台身份和未按住的修饰键；不使用 PostMessage 伪造键盘到达，也不向用户桌面回退。见 [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput) 和 [INPUT 布局](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-input)。该新增门仍待本提交 Windows CI 实际执行；`NativeKeyboardChecks=3` 才覆盖三个阶段，旧报告不能代替。有限的源原生键盘测试不等于鼠标映射、IME、Raw Input、剪贴板、子窗口、游戏输入或全平台输入已经验收，广义 `NativeInputDeliveryTested` 继续为 false。

鼠标激活回归在同三个阶段向输出客户区的实际可见点发送系统按下/释放，要求 Avalonia 真正收到两种指针事件，随后源仍为前台且 B/Esc/F12 继续送达源。注入前用 WindowFromPoint/GetAncestor 核对实际命中当前自有输出，没有按住左键；保存并恢复测试 cursor 位置。`NativeOutputClickChecks=3` 是独立的门，不能只靠样式位或合成 Avalonia 事件判通过。该测试证明输出点击不抢键盘焦点，不证明点击已经映射到源控件；后者仍缺。本轮追加实现及点击检查需对应目标 CI，不能套用前一提交的结果。Avalonia 接入使用 [公开 Win32Properties API](https://docs.avaloniaui.net/api/avalonia/controls/win32properties)。

`b34e68ea` 的 [Windows job](https://github.com/greepar/demo-mg/actions/runs/40628594500/job/122619391301) 已实际通过首次显示/两次恢复的原生键盘阶段，产物四条路径均 `InitialForegroundPreserved=true`、`NativeKeyboardChecks=3`、`SourceExitedCleanly=true`，WGC 没有跳过。该提交没有输出真实点击阶段，不能用它证明后续 `1d0ea02e` 的 `NativeOutputClickChecks` 已通过。

`1d0ea02e` 的 [Windows job](https://github.com/greepar/demo-mg/actions/runs/40629095223/job/122620992981) 新增点击门实际失败：GDI 普通窗口首次点击/键盘通过，但最小化恢复后的可见命中检查拒绝注入；源仍是前台且正常退出。未把这次失败算成鼠标激活已通过。对照原版创建缩放窗口时传入源 HWND 所有者，移植版补入 native owned-window 关系，并在 Avalonia Show 重置 owner 后按已显示帧重新建立；建立前复核当前 capture epoch、HWND/PID，不修改外部窗口。仅当前台确为源时以 NOACTIVATE 恢复输出层级，不设全局置顶。不靠重试、改测试位置或放宽命中断言掩盖失败。见 [Win32 owned windows](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features#owned-windows)。该修复仍需后续目标运行证明。

`30d24577` 的 [Windows job](https://github.com/greepar/demo-mg/actions/runs/40629746244/job/122623110211) 已实际确认初始输出 owner/可见点及按下释放，但点击后的前台/鼠标状态断言失败，整条门仍未通过。核对 [Avalonia 12.1.1 AppWndProc](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Windows/Avalonia.Win32/WindowImpl.AppWndProc.cs)：鼠标按下在 MA_NOACTIVATE 之外仍主动 SetFocus，`ShouldTakeFocusOnClick` 是内部实现的保护属性，公开 Win32Properties 没有关闭此行为的选项。移植版接入原版 `_UpdateSrcState` 的意外焦点纠正规则：仅当本输出实际取得前台，并且所显示源的 capture epoch/窗口/PID 仍有效时，在 WM_SETFOCUS 中同步恢复源前台；其他应用在前台时不执行。保留原有点击消息与严格实测门，不模拟控件点击或吞掉输入，也没有修改 NuGet 缓存/反射内部成员。该补充仍需对应 CI 证明，失败数据保留。

`ffcc6b93` 的 [Windows job](https://github.com/greepar/demo-mg/actions/runs/40630798967/job/122626444408) 已实际通过 GDI 普通窗口的三个点击/键盘阶段、两次隐藏恢复和正常关闭。随后全屏场景的底部客户区点 `(512,758)` 命中其他原生窗口，源仍为前台且左键未按住，测试拒绝注入。整次联合门仍失败，不能计作全屏/WGC 点击已通过；没有改测试点避开遮挡。

## 前台层级与禁用置顶

对照原版 `_CalcTopmostState`，新增源为置顶窗口时继承置顶；否则仅在源确为前台、可用且没有 enabled popup，并且未勾选“禁用置顶”时临时置顶输出。[GetWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindow) 返回 owner 自身或本输出时不当作源应用对话框。失去前台时撤销自动置顶，返回源的窗口层级。原版开发者 `disableTopmost` 已接 UI、即时应用、持久化/恢复、失败回退和原版配置迁移；原有文件缺少字段时为 false。Linux/macOS 保留偏好但禁用该控制，未冒充实现其系统窗口层级管理。原版自有 HWND 的 TreatAsDesktopFullscreen shell hint 同步设置并在原生销毁时移除，不操作系统任务栏窗口。另修复运行统计刷新重新加回 Windows “Esc to stop” 标题的问题。

16ms UI 定时器只更新窗口策略，不提交 GPU；WGC 静态源没有新帧时仍可跟随前台和偏好变化，隐藏/结束后停止处理，关闭/会话清理显式释放。继续保留 owner 和原生点击命中验证，新增四场景“禁用自动置顶、继承自有源置顶、返回普通层、重新启用、切到另一独立自有应用、键盘不偷跑/不换捕获源、回到原源”实际门。独立应用必须保持前台且原生客户区可见；本提交目标 CI 仍待执行，不能凭代码或模型通过宣布实际层级已完成。管理员/不同完整性级别、UWP、真实系统弹层、源弹窗和多显示器仍缺专项验证；尚未完整复刻原版异步 DWM/批量 Z-order 分支。

`568c0760` 的 [Windows job](https://github.com/greepar/demo-mg/actions/runs/40632450922/job/122631737883) 已实际通过初始点击、禁用自动置顶及源置顶继承，但独立前台应用客户区被输出遮挡，整次门失败。随后移植原版批量 demotion 顺序到静态 C++ 核心：读取前台/自身 token 的完整性级别，常规分支批量撤销置顶并靠近源，再仅为仍然相同的前台 HWND/PID/线程恢复顶部；较高/未知完整性级别分支等待 DWM 合成、使用 NOOWNERZORDER 调整源和输出，不尝试移动该前台窗口。C ABI 只接受本进程顶层、owner 为当前 capture 存活源的输出；源身份不匹配或已丢失返回 unavailable，错误输出拒绝，其他平台返回不支持。增加 Advapi32 系统库链接，不增加自有动态库。严格独立应用可见性/键盘断言保持，目标 CI 仍待本提交。DWM 等待当前在 UI 策略调用内执行，尚未做到原版异步后台调度，较高权限及其他真实应用矩阵仍未验收。

`54e59c6c` 的 [Windows job](https://github.com/greepar/demo-mg/actions/runs/40633735965/job/122635812917) 已实际通过 GDI 普通窗口全部门，以及全屏初始点击、置顶偏好、源置顶继承和独立前台应用可见性/键盘；全屏最小化恢复后底部命中仍失败，WGC 尚未执行到，不能算完整通过。后续恢复路径不再只看 Avalonia 保存的 Topmost 值：比较原生 WS_EX_TOPMOST，并在重显、owner 重建和模式变化时重新施加一次当前有效的原生层级。当前源前台时显式 NOACTIVATE 恢复上层；其他应用前台时仍走撤销/批量顺序，未改为永久置顶。原生命中点与输入断言保持，失败诊断补充命中/输出 exStyle。本次修复仍待对应目标运行证明。

`bda833b6` 的 [Windows job](https://github.com/greepar/demo-mg/actions/runs/40634717491/job/122638913643) 没有执行到新的 UI 联合门：GDI 与普通 WGC 原生 fixture 通过，WGC 标题栏 fixture 在 shown-after-hidden 阶段后超时。捕获本身、后续校验还是析构卡住尚未区分，不能把此前同源码通过或重跑通过当作此间歇问题已修复。旧 wrapper 的 caption 报告还继承了普通 WGC 的 exit_code=0 / wgc_readback_tested=true；passed=false 和 timeout 才是该次标题栏结论。现已重置各场景独立的 readback/退出字段，保留原始失败产物。

新增仅在 GitHub-hosted + explicit fixture opt-in 下启用的 native 5s 阶段/线程等待链日志：不获取 capture/mailbox/epoch 锁，不改变 45/90s 超时、失败判定和重试次数，不做内存转储；[WCT](https://learn.microsoft.com/en-us/windows/win32/debug/wct-reference) 权限不足或不支持的等待明确打印诊断错误，不能据此推断没有死锁。超时消息先于 fixture 析构输出，便于分辨清理卡住。WGC 每个读取/丢弃帧补显式 Close，且在纹理/映射引用释放后归还 frame buffer，见 [帧生命周期说明](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture#process-capture-frames)；这项资源释放完善尚不证明超时根因已解决。同时补原版全屏进入 topmost band 后显式 HWND_TOP 的步骤，不以 HWND_TOPMOST 代替带内排序。这些新实现均需本提交目标结果，完整目标继续未完成。

原生 GDI/WGC/标题栏 fixture 另增加四轮实际 hide/show，独立检查保留缓冲、暂停前旧序号不能恢复、每轮新交付帧和像素/标题栏尺寸；wrapper 要求新的 repeated hide/show 标记，不能沿用旧可执行文件的成功。这些是对新超时的针对性覆盖，重复通过也不单独构成间歇问题已定位/修复的证明。

`950eff2d` 的 [Windows job](https://github.com/greepar/demo-mg/actions/runs/40637449293/job/122647575209) 已实际通过新增四轮 GDI/WGC/标题栏原生回归；GDI 普通窗口全部联合门仍通过。全屏恢复后的底部命中失败继续存在：输出原生 exStyle=0x08240008，命中窗口 exStyle=0x88，说明输出仍在 topmost band，不能再将原因简单归为 Topmost 位丢失。间歇原生超时在本次未触发，尚未定位/排除。

对照原版 CreateWindowEx 的 owned-window 规则，移植输出不再另带 WS_EX_APPWINDOW / 任务栏条目；主页窗口保持原样。WM_SHOWWINDOW 中在原生可见之前建立当前有效捕获源 owner，并按原版 WM_CREATE 解除跨线程拥有关系的隐式输入队列附加。首次赋 owner 使用实际捕获身份/代次，不提前修改 UI 已呈现帧/截图快照。隐藏时解除 owner，防止源恢复自动显示旧输出；仅新帧提交后的 Show 才重新建立关系。联合门增加原生 APPWINDOW 位/owner 校验，并在实际 stage semaphore 阻塞 200ms 时恢复真实源，要求输出仍为 native hidden，随后放行新画面并保持原来的底部点击/键盘检查。失败会记录窗口 class、rect、exStyle，不以换点击位置或隐藏系统任务栏规避。此追加实现需对应 Windows 结果，完整目标未完成。

Avalonia 12.1.1 的 SetFullScreen 在模式改变时调用 Shell MarkFullscreen，但 Hide/Show 不一定再次改变模式。移植在正确 owner 的重显/模式恢复后，通过 [ITaskbarList2::MarkFullscreenWindow](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-itaskbarlist2-markfullscreenwindow) 重新通知本进程的输出；不标记源应用或直接操作任务栏窗口，Shell 不可用返回 unsupported。接口复核 capture 寿命、进程、顶层和 owner，COM 激活后再次校验，COM 对象释放先于平衡 apartment 初始化。原生未选源/其他平台不调用 Shell；hosted 测试要求该通知实际成功，同时仍要求真实底部命中和原生输入送达。另按原版输出窗口样式取消独立最小化/最大化按钮，不新增替源窗口最小化的行为；源窗口的最小化/隐藏流程继续实测。NativeAOT 静态链接边界不变，新增行为仍需对应 Windows 联合结果。

该 runner 的 token elevation=true，使用标准临时 HKLM ICD 登记后软件 Vulkan 正常运行，最后 registry_value_restored=true。未更改 shader、Vulkan API 版本或数值断言来绕过失败。原生 GDI/WGC/标题栏 fixture、Windows SDK/AOT/模型/单文件检查也继续通过；该次四平台 CI 已全部完成并成功，包括 Linux 软件 Vulkan 回归及两个 macOS 架构。汇总为 `artifacts/desktop/windows-production-target-summary.json`，下载产物保留在 `artifacts/desktop/windows-production-target-351`。已查看实际输出 PNG，图像为独立源绘制的颜色。鼠标/键盘送达、软件光标、游戏模式和完整三平台目标继续未完成。
