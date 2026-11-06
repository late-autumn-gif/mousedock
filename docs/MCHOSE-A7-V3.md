> 2026-11-06：已接入新的按物理实例MHUB专用路径，替代本文历史全局枚举读取；当前启用范围及更严格的身份/回包边界见 [本轮适配](MCHOSE-MODERN-SCOPED.md)。尚无本轮实机读数。

# MCHOSE A7 V3 电量适配记录

更新日期：2026-11-03。状态：已按官方网页驱动的静态协议实现适配；尚未取得反馈者这只设备的实机电量回包，不能把编译或解析器测试当作实机验证。

## 反馈定位

`MouseDock-report-20261103-145734.zip` 中的鼠标是 `3837:1018 MCHOSE A7 V3`，连接方式为 2.4G。报告列出了两个厂商 HID 集合：

| 集合 | Usage Page / Usage | 输入 / 输出 / Feature 长度 | 本次用途 |
| --- | --- | --- | --- |
| MI_02 / COL01 | `FF0B / 00C4` | `61 / 61 / 61` | 不发送 64 字节电量帧 |
| MI_02 / COL02 | `FF01 / 0001` | `64 / 64 / 64` | MHUB 新协议 Output / Input 查询 |

旧实现仅发送 Legacy Feature `11 F9` 请求，该请求曾用于 `5253:1021`，不能据此认定 A7 V3 已受到支持。反馈报告只有接口元数据和“读不到”的状态，没有原始请求、回包或电量真值。

## 一手证据

官方入口是 [MCHOSE HUB](https://www.mchose.store/pages/mchose-hub)，其页面配置中的网页驱动地址为 [M HUB Web](https://www.mchose.com.cn/#/home)。本次读取了下列静态资源，没有执行驱动、连接外设或发送配置命令：

- 主入口：[main-D66zm79W.js](https://www.mchose.com.cn/assets/main-D66zm79W.js)，动态导入下面的业务资源。
- 业务资源：[main-B_prkphY.js](https://www.mchose.com.cn/assets/main-B_prkphY.js)，SHA-256：`233287E93E4EFE5AABF72053AE07B648A5068178BEB1E6C00AA01E654779530C`。

下面的位置是该资源按 UTF-8 解码后、JavaScript 字符串中的零起始 UTF-16 字符偏移，便于用符号定位。它们不是文件字节偏移，也不表示执行轨迹。

| 符号或表 | 字符偏移 | 静态观察 |
| --- | ---: | --- |
| `HID4D_REPORT_ID` / `buildHid4dReportData` | 453149 / 453481 | 报告 ID `0x4D`、协议版本 1；头、声明长度及可选 XOR 校验的定义 |
| `isValidHid4dFrame` | 454864 | 声明长度最大 55；仅当 flags 的 bit 0 置位时要求并验证 XOR 校验 |
| `getCommandBuffer` | 1839105 | 鼠标新协议实际构造 64 字节帧；默认 flags=1、bizCode=0、sequence=0 |
| `deviceInfoToObj` | 1839708 | 设备信息中 VID、PID、连接状态、充电状态和电量的字段偏移 |
| `waitCommand` / `mcReadCommand` | 1842003 / 1842810 | 输出报告查询后等待输入报告，按命令号匹配；输入事件数据不含报告 ID |
| `readDeviceInfo` | 1852186 | 电量信息查询命令为 `0x0900` |
| `newMouseWirelessPids` | 1870273 | `1014`、`1018` 等属于无线接收器 PID |
| `mouseDiffConfig` 的 A7 V3 表 | 1874865 | 各 A7 V3 型号的本体 PID 与共享接收器 PID 的对应关系 |

可交叉检查的公开独立实现是 [MCHOSE-local-hub 的协议说明](https://github.com/zynox-xonyz/MCHOSE-local-hub/blob/main/PROTOKOLL.md)及其 [index.html](https://github.com/zynox-xonyz/MCHOSE-local-hub/blob/main/index.html)（读取时 blob SHA `d04e82387d781304ae6c2c96bc8371d82cd7825d`）。该项目把 A7 V3 列为新协议，并明确注明尚未实机测试。本次以官方资源核对字段与校验规则，未照搬其宽松回包解析。

## 本次使用的电量帧

查询命令仅为“读取设备信息” `0x0900`。包含报告 ID 的完整帧长为 64 字节：

```text
4D 01 01 00 00 09 00 00 08 + 55 个 00
```

| 含报告 ID 的偏移 | 含义 |
| --- | --- |
| 0 | 报告 ID，`4D` |
| 1 | 协议版本，`01` |
| 2 | flags；bit 0 表示存在 XOR 校验 |
| 3 | 载荷长度 N |
| 4–5 | 命令号，小端；设备信息为 `00 09` |
| 6 | bizCode |
| 7 | sequence |
| 8 开始 | 载荷 |
| 8+N | flags bit 0 置位时的 XOR 校验，覆盖字节 2 至 7+N |

设备信息回包按同一头部定位载荷。载荷中的 `0–1` 是 VID、`2–3` 是 PID，均为小端；`10` 是官方命名的 `connectStatus`，`11` 是 `chargeStatus`，`12` 是百分比，`13` 是 `gameMode`。只读取电量所需的最小载荷长度为 13。

本次校验报告 ID、版本、命令、声明长度、设备标识、百分比范围，以及 flags 声明存在时的校验和。查询本身没有载荷，因此请求回显不能通过至少 13 字节的电量载荷检查。不能从全零回包或任意 0–100 的字节猜电量。

官方 UI 把 `chargeStatus == 1` 用作充电状态。本次仅接受已知的 0/1 状态；其它值保留为不支持的应答，不能据此证明官方状态枚举仅有两个值。`connectStatus` 的完整状态定义、flags 其它位及 bizCode 的错误语义尚未确认，本次不以猜测的取值拒绝回包。

## 接收器与本体 PID

官方表把同一型号的有线本体和共享无线接收器并列。为避免只接受接收器 PID 而拒绝本体身份，本次对 `3837:1014` / `3837:1018` 查询允许下列 A7 V3 本体 PID，并仍要求 VID 为 `3837`。这是根据官方型号表设置的兼容规则；本次没有捕获到这些设备的实际电量回包。

| 型号 | 本体 PID | 共享无线接收器 PID |
| --- | --- | --- |
| MCHOSE A7 V3 | `4030` | `1014`、`1018` |
| MCHOSE A7 V3 Pro | `4031` | `1014`、`1018` |
| MCHOSE A7 V3 Pro+ | `4032` | `1014`、`1018` |
| MCHOSE A7 V3 Ultra | `403D` | `1014`、`1018` |
| MCHOSE A7 V3 Ultra+ | `4033` | `1014`、`1018` |
| MCHOSE A7 V3-X | `403A` | `1014`、`1018` |
| MCHOSE A7 V3 Magnetic | `403F` | `1014`、`1018` |

其它型号即使共享接收器 PID，也不能仅凭接收器编号被命名成 A7 V3。`101C` 在官方表中用于 K5 V2 等，本次不根据 A7 V3 的报告扩展该型号。

## 实机验收仍需确认

编译和构造帧测试只能验证实现与已确认帧结构一致。反馈者应运行修复版，在鼠标唤醒时核对它与官方驱动的百分比；随后分别检查低电量、充电、满电、休眠、拔掉接收器和重新连接。若仍读不到，需要新版本导出的读取诊断；若回包不在已确认协议内，保留未知状态并继续分析，不编造百分比。
