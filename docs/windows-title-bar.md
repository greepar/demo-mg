# 标题栏与源窗口边界

按原版 `SrcTracker::Set / _CalcSrcRect` 移植标题栏开关和边界规则，接入 WGC；GDI 仍保持原版不支持该开关。Portal 和当前 ScreenCaptureKit 使用系统选源的边界，页面禁用单独开关但保留用户偏好，切回支持的后端恢复。会话开始前配置，Windows 已开始的捕获拒绝修改；启动中的设置变化留待下一次会话。保存、独立应用配置复制和原版 `captureTitleBar` / 旧 `reserveTitleBar` 迁移已接。

共用 `windows_capture_region.h` 包含原版的窗口类型分类和裁剪：原生框架、无原生框架/自绘非客户区、无标题栏、无边框、只有 thick frame，以及原生/自绘滚动条、UWP 输入子窗口、Win11 按显示器 DPI 四舍五入的可见边框、最大化时与物理显示器相交。用户自定义裁剪仍在后续共用画面处理阶段应用，不重复裁剪。WGC 的实际 frame origin 独立保留，仍包含 Win11 24H2 前的最大化捕获边界兼容规则。

生产者使用计算后的源区域做 D3D11 CopySubresourceRegion，尺寸和源/几何修订随像素交付；截图标题校验与帧接收使用相同源区域尺寸，不再把含标题栏的画面当成过期客户区。最小源区域 64×64 与原版一致。Windows 输入转发尚未完成，不能据此宣称标题栏或滚动条的真实输入命中已验收。

新增可移植原生模型验证上述裁剪规则；Windows 一次性 fixture 的第三次独立捕获启用标题栏，查询真实 DWM/客户区边界、核对画面尺寸、实际标题栏与独立绘制客户区像素，检查活动会话不能切换几何；持续 resize、最小化恢复、销毁和重捕获沿用完整严格矩阵。主机不支持 WGC 时记录 unavailable，不计作标题栏通过。真实自绘窗口/UWP、滚动条、多屏 DPI 和系统实拖尚需目标验收。

本阶段 Linux 三个原生模型通过，首次完整 AOT core 又出现已知 NNEDI3 configured/legacy 差异：112 个通道，最大差 255，首差 byte 256（0 / 150），诊断 `/tmp/magpie-nnedi3-mismatch-9c97d25a1ed54df88901cbd326ed00f3`。随后独立 core 重跑通过；这不解决该问题，没有放宽断言或改动 shader。标题栏新增的 Windows SDK/真实捕获及 macOS ABI 编译仍需对应提交 CI，完整交互目标未完成。
