# 理论设备复核矩阵（2026-11-08）

方法：只用官方二进制、官方配置、官方网页驱动做静态对照，不接硬件。
每条结论标注证据级别：**已验证（静态逐字节一致）** / **高可信** / **待验证** / **不支持**。

## 本轮复核完成

| 设备 | 官方出处 | 核对内容 | 结论 |
|---|---|---|---|
| 罗技 GPW 初代（046D:C088 / 4079） | `D:\罗技\GPW\pro_wireless_mouse\battery.xml`（G HUB 2026.7 depot，SHA256 `38ee9484…7714`） | 707 个放电样本、502 个电压节点。代码 `LogitechGpwBattery.Discharge` 首点 `3479mV/90234` 与官方末行 `3.479V/902.34mWh` 一致；注释记录 depot 哈希 | 已验证（静态） |
| 狼蛛 AULA SC580（089D:062F） | `D:\狼蛛\狼蛛SC580\SC580X\device\mouse\SC580X\MS.ini` | `Protocol=3`、`devVID_Wireless=0x089D`、`devPID_Wireless=0x062F`、`Identify=4d,31,33,37`（ASCII `M137`）、`Sensor=0x3315`，与代码 VID/PID、M137 身份串、Protocol 3 一致 | 已验证（静态） |
| 英菲克 IN9 / A9（248A/249A:5C2E/5D2E/5E2E/5C2F） | `D:\英菲克\Inphic Mouse\config.xml` | 四个零售型号共用同一组 VID:PID，要求 `MI_02`，型号由 `dev_id`（M909/M910/IN90/M900）决定；已落成 `InphicDeviceTable` 并进 `device.txt` | 已验证（静态） |
| 英菲克 248A:5B2F | 同上 + `INPHIC HUB` 两个二进制 | 不在官方表内；二进制里搜到的 `5B2F` 分别落在压缩数据与对话框代码常量，不是设备表 | 不支持（非 IN9 产品线） |

## 本轮未复核

| 设备 | 原因 | 下一步 |
|---|---|---|
| ATK ZERO 373B:1154/1155/124F | ATK HUB `app.asar`（3467 文件）主进程只索引到 BYKB 灯效线（373B:1241/1242/1243/1244 A98），ZERO 的 FF05/0001 与 `0x89` 命令未出现在已索引文件 | 用 asar 全量解包 + 按 `1155`/`124F`/`FF05` 再扫；或分析 `D:\ATK\official-web-20261105` 的 chunk 引用图 |
| ATK V75X 320F:5088/5055 | 同上，未在已索引文件找到 FF1C/0092 | 同上 |
| WOB Rainy 75 320F:5055/5088 | 本机无 WOB 官方驱动 | 需要官方安装包 |
| 迈从 A7 V2 Pro / A7 V3 / G7 | 官网网页驱动 `main-DCRwBIvm.js` 只有 OTA 与键位表，不含 11/F9 与 4D 现代帧；MHub 桌面端 asar 未解 | 分析 `D:\迈从\MHub-installed\resources\app.asar`，按 `0x11`/`249`/`0x4D`/`4018`/`4021` 扫 |
| 雷柏 VT7 24AE:4613/1413 | AHUB 2.0 是 Electron（210MB exe），`batStatus`/`batPercent` 未出现在外壳，协议在 asar 内 | 分析 `D:\雷柏\RAPOO AHUB 2.0\resources\app.asar` |
| VGN Neon75 Air 391D:2A05 | 本机 VHUB asar 未解 | 分析 `D:\VGN VHUB\resources\app.asar`，搜 `2A05`/`F7` |
| 前行者 X23 SE、维咖 WEIKAV、RK R98Pro、MCHOSE K87 V3 | 本机无对应官方资料 | 需要官方安装包 |

## asar 解析方法（本轮新增工具）

Electron 新版 asar 头不是标准格式：`u32 version=4` + `u32 header_size` + 8 字节长度前缀 + JSON。
`D:\OMNIDEV\Temp\asar_grep3.py` 与 `asar_ctx.py` 已能解析 `D:\ATK`、`D:\狼蛛`、`D:\迈从`、`D:\VGN VHUB` 四份 asar 并定位文件内容，可复用。
