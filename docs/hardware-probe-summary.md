# TheBoostDeck 硬件探针实测摘要

日期：2026-11-16（基线）/ 2026-11-17（安装 PawnIO 后追加）  
探针：LibreHardwareMonitorLib 0.9.6  
原始数据：`hardware-probe-normal.json`、`hardware-probe-pawnio.json`

## 运行条件

- 操作系统：Windows 10 专业版 22H2。
- CPU：12th Gen Intel Core i5-12400F。
- GPU：NVIDIA GeForce RTX 4060 Ti。
- 主板：ASUS H610M-D3H/M.2 D4。
- 基线探针进程为管理员权限；文件名中的 `normal` 仅表示未主动请求提权，不代表标准用户对照。
- 基线共发现 12 个硬件节点、133 个传感器，连续采样 4 次，间隔 1 秒。

> 未安装 PawnIO 时，LibreHardwareMonitor 能枚举 CPU 负载和型号，但 CPU 温度、每核频率和封装功耗均为 `null` 或 0。安装官方 PawnIO 2.2.0 后，这三类数据恢复。

## 基线（未安装 PawnIO）

| 指标 | 实测状态 | 末次示例值 / 说明 |
|---|---|---|
| CPU 总占用 | 可读 | 10.8% |
| CPU 各核心占用 | 可读 | 12 个逻辑处理器均有数据 |
| CPU 温度 | 不可读 | Package、Core Average、Core Max 和各核心温度均为 `null` |
| CPU 频率 | 不可读 | 各核心 Clock 均为 `null` |
| CPU 功耗 | 不可靠 | Package / Cores / Memory / Platform 均返回 0；不作为真实功耗展示 |
| GPU 核心温度 | 可读 | 43°C |
| GPU Hot Spot | 可读 | 52.19°C |
| GPU 核心频率 | 可读 | 585 MHz |
| GPU 占用 | 可读 | GPU Core 36% |
| GPU Package 功耗 | 可读 | 18.86 W |
| GPU 风扇转速 | 可读 | 1209 RPM |
| GPU 专用显存 | 可读 | 总计 16380 MiB、已用 3142 MiB |
| 物理内存 | 可读 | 已用 15.01 GiB、可用 16.81 GiB |
| NVMe 温度 | 可读 | Composite 69°C，另有分区温度 |
| 机械硬盘温度 | 可读 | 46°C |
| 主板温度 | 不可读 | 未发现 SuperIO 温度节点 |
| 主板 / CPU 风扇 | 不可读 | 未发现 SuperIO 风扇节点；仅发现 GPU 风扇 |
| 整机功耗 | 不可读 | 未发现可信的整机功耗传感器 |
| 网络接口 | 可枚举 | 蓝牙、以太网及 vEthernet 等多接口，需选择活动物理接口 |

## 安装 PawnIO 2.2.0 后

- 安装包：官方 `PawnIO_setup.exe` 2.2.0.0，Authenticode 有效，SHA-256 为 `1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032`。
- 使用 `-install -silent` 静默安装；服务 `PawnIO` 存在且为 Running，安装目录 `C:\Program Files\PawnIO`。
- 新探针发现 15 个硬件节点、212 个传感器，新增 SuperIO `Nuvoton NCT6798D` 及 CPU 内部传感器。
- 页面实测恢复正常：CPU 温度 `100.0 °C`、CPU 封装功耗 `29–30 W`、CPU 平均频率约 `3.44–3.51 GHz`。
- 页面降级项从 6 项降至 1 项，剩余唯一降级为整机功耗（未暴露受支持的整机功耗传感器）。
- CPU 频率初次仍为 `--` 是应用过滤条件错误：探针名称为 `CPU Core #1`，旧代码只匹配 `Core #1`。改为包含匹配后恢复。

最后一次探针的关键值：

| 指标 | 数值 |
|---|---:|
| CPU 总占用 | 13.92% |
| CPU Package 温度 | 100 °C |
| CPU Core Average / Max | 96.17 °C / 100 °C |
| 各核心温度 | 93–100 °C |
| 各核心 Clock | 3394–3893 MHz |
| CPU Package 功耗 | 28.27 W |
| CPU Cores 功耗 | 21.76 W |
| NCT6798D Temperature #1 | 81.5 °C |
| NCT6798D Fan #1–#7 转速 | 均为 0 RPM |
| NCT6798D Fan 控制值 | 60–100% |

## CPU 100 °C 的判断

当前证据更支持“真实的高温读数”，而不是单一传感器故障：

1. CPU 数字温度传感器（DTS）的内部读数彼此一致。例如核心温度与 “Distance to TjMax” 可相互对应：核心 1 为 93 °C、距 TjMax 7 °C；核心 4 为 100 °C、距 TjMax 0 °C。
2. Intel 官方对 i5-12400F 给出的 TjMax 为 100 °C，Package 温度恰好顶在该上限。
3. 采样时 CPU 负载只有约 14%，封装功耗约 28 W，却仍接近 TjMax；这不符合正常风冷 / 水冷待机表现。
4. SuperIO 暴露的 7 个风扇转速通道全部为 0 RPM，同时风扇控制值在 60–100%。这可能表示风扇未接到对应排针、风扇停转、风扇集线器未回传转速，或传感器读数本身有边界问题，但结合 100 °C 应优先排查散热。

不能作为反证的数据：

- Windows ACPI 热区 `MSAcpi_ThermalZoneTemperature` 仅约 27.85 °C，它对应机箱 / 主板热区，不是 CPU 核温，不能直接对照。
- WMI `Win32_Processor` 只提供标称频率（约 2500 MHz）和负载，不提供核心温度。
- Windows 没有在未安装额外工具时提供独立的 CPU 核心温度接口。

尚未完成独立交叉验证（BIOS 页面、HWiNFO 等），因此最终结论保留为“高度疑似散热异常”。在确认前不应把 100 °C 当作软件显示 bug；建议先检查 CPU 风扇 / 水冷泵、散热器固定、硅脂和 BIOS 风扇曲线，必要时关机检查。

## 对首版实现的约束

1. 安装 PawnIO 后，CPU 卡片展示真实封装温度、平均核心频率和封装功耗；未安装或版本过旧时必须降级并给出驱动原因。
2. 不能把 Package 温度不可读时的 `null` 当成 0，也不能用固定演示值补齐。
3. CPU 风扇优先匹配名称包含 CPU / Processor / AIO / Pump 的 Fan 传感器；没有命名通道时回退到 SuperIO 的第一个有效 Fan 通道，并在 tooltip 中显示实际来源。0 RPM 原样显示，不当成缺失值，也不把 GPU 风扇计入 CPU 风扇。
4. 整机功耗保持独立降级；CPU / GPU 封装功耗不得相加冒充整机功耗。
5. GPU 风扇可在 GPU 卡片显示，不冒充 CPU 风扇。
6. 网络采集必须排除蓝牙、虚拟交换机和无活动接口，并避免多接口重复统计。

## 未完成的验证

- 2 小时驻留、游戏场景、睡眠唤醒和长期资源占用。
- BIOS / 独立硬件监控工具的 CPU 温度交叉验证。
- 标准用户与管理员权限的同机对照（当前环境所有交互进程均带完整管理员令牌，无法构成有效对照）。
