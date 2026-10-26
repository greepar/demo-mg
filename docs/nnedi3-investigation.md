# NNEDI3 偶发输出缺失调查

状态：**未解决**。不得用重复通过证明首次失败不存在，也未更改原版系数、shader、精度选择或比较阈值。

已保存的失败在 AMD Radeon RX 6650 XT / RADV NAVI23：输入 7×5，输出 14×10。configured 输出的像素 64–79 和 128–139 全为 `(0,0,0,0)`，共 28 像素 / 112 通道；legacy 再次执行后这些像素正常，其余像素相同。首次差异 byte 256，最大差 255。文件位于 `artifacts/desktop/title-bar-evidence/nnedi3-mismatch/`。原版最终 pass 固定写 alpha 1，故这不是普通 RGB 浮点舍入误差；尚不能据此归因于 driver、同步、shader 调度或托管复制。

## 下次失败时保留首次输出

`Nnedi3Tests` 在第二次 legacy 渲染之前检查 alpha。发现异常时，先保留已有原生缓冲的 CPU 快照，再通过 `mp_probe_stage_readback` 提交一次独立 buffer-to-buffer GPU 复制，保存到另一块 host-visible coherent 内存；随后才运行 legacy 比较。异常原图、CPU 快照和 GPU 复制在再次渲染前立即落盘，避免第二次调用覆盖现场。GPU probe 只复制已完成的输出，不运行 shader，不改 snapshot serial 或算法 profiler sequence；普通截图/导出和实际缩放不会调用这个诊断入口。

GPU probe 在原来的 compute 写入与 transfer 读取之间建立依赖，复制后建立 transfer 写入到 host 读取的依赖并等待 fence；与普通 CPU 快照是两条独立读回路径。依据 [Khronos 同步示例](https://docs.vulkan.org/guide/latest/synchronization_examples.html)。即便 configured 和 legacy 恰好都缺失相同像素，非 opaque alpha 也会让回归失败，不能把两个错误输出相等当作正确。

## 无界面的专项入口

```sh
artifacts/desktop/linux-x64/Magpie.Desktop --nnedi3-stress 10000 artifacts/desktop/nnedi3-stress
```

此命令不加载用户设置、不启动捕获、不打开 UI。七种输入尺寸包含 7×5、31×17、33×9、65×3、1×1、17×17、32×8，交替 GPU profiling，请求 bicubic → NNEDI3 的效果/分配复用，每 16 次重建引擎。每次比较 configured 输出、已有原生 CPU 快照、独立 GPU 复制和 legacy 重渲染，要求逐字节相等及 alpha=255；验证诊断复制不伪造新的算法帧/计时，拒绝过期序号、错误长度/阶段时不修改输出。失败保存不同路径的 RGBA 和报告；报告始终保留 `intermittent_issue_resolved=false`。

## 本轮证据和限制

- 100 次、随后 1,000 次独立完整 AOT core 进程均通过，未捕获新的首次失败。
- 最终专项入口 10,000 次 / 625 个引擎通过，含 GPU 复制及参数拒绝检查。
- 提取的 Khronos layer 已通过 loader 日志确认加载；请求同步/GPU-assisted 校验的 128 次专项通过，日志没有 VUID/同步错误。这不是校验器覆盖所有问题的证明。
- 未改变运行时图像处理行为；只增加诊断、严格的 alpha 断言及失败现场保存。Window/GUI 也未被打开。

下一次复现需先比较 `configured`、`native-before-legacy` 和 `gpu-copy-before-legacy`，确定首次异常在原生输出中是否已经存在，再决定同步、读回或 shader 路径的修复。当前证据不支持全显卡数值一致或问题已修好的结论。完整三平台交互移植仍未完成。
