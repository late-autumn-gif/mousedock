# 已知问题

记录当前不修或修不了、但需要明确告知的问题。按影响排序。

## 测试环境跳过项（5 项）

`tests/MouseDock.Checks` 在 Windows 11 上会跳过 5 项，输出 `SKIP`，不计入通过：

| 检查 | 原因 |
|---|---|
| Embedded taskbar uses a transparent native child | Windows 11 任务栏是 XAML 宿主，不接受原生子窗口 `SetParent` 嵌入 |
| Embedded taskbar retains native alpha composition | 同上 |
| Embedded taskbar recovers after its native host is destroyed | 同上 |
| Embedded taskbar recovers after a cross-process host exits | 同上 |
| Update waits for the old process before replacement and restart | 夹具用 `.cmd` 占位，而 `CreateApplyStartInfo` 要求被替换文件是名为 `MouseDock.exe` 的 PE；需要真实 PE 才能验证重启 |

任务栏四项在 Windows 10 / 旧版任务栏环境仍会执行。更新一项目前只有代码审查覆盖，没有自动验证。

## 设备侧

| 设备 | 状态 | 说明 |
|---|---|---|
| 迈从 A7 V2 Pro DPI | 不支持 | 设备走 11/F9 旧通道，不响应官方 `0x12 0x67`；DPI 显示 `—`，不发写入命令 |
| 迈从 A7 V3 / G7 DPI | 只读已接入 | `0x12 0x67` 解析已实现，写入命令在官方配置表里，未取到，不猜 |
| 英菲克 248A:5B2F | 归 M137 路由 | 该号段已由 M137 GetInfo 独占，公版 33 字节旁路不再接手；身份不是 M137/M139/M620/M184 时只显示接收器，不采用电量 |
| 狼蛛 F75（3554:FA09） | 已接入，待本机确认 | 官方驱动界面 `ShowPower=0`（`AULA F75 Setup v2.0` 的 `Dev\kb\F75KR\KB.ini`），但接收器固件的 FF02/0002 20 字节 Report13 通道留了命令：5 返回型号 `0300000000CD`、74 返回百分比。协议取自用户提供的 1.1.1+bac923f 构建（`F75Protocol`）反编译，见 `F75Protocol.cs`。产品串必须是 `2.4G Wireless Receiver`，中文展示名不进专用读取器 |
| VGN 蜻蜓 F1 MOBA（3554:F503） | 已接入，用户构建验证过 | FF02/0002 17/17，帧 `08 <cmd>`、校验和 `0x55-sum`；命令 1 取 CID/MID（`2,5`/`2,6`/`2,33` 才是 MOBA）、3 查在线、4 查电量、3 复核配对地址。协议取自用户提供的 1.1.1+bac923f 构建（`VgnF1Protocol`）反编译，见 `VgnF1Protocol.cs` |
| Rainy75（WOB） | 不支持 | 官方驱动是 wobwxe.com 的 JS，`scholzri/rainy75-zmk` 的 `docs/wob-driver-analysis.md` 把协议分成按键、灯效、固件三类，没有任何电量或型号查询命令；官方 OTA 工具也不含 HID 与 battery 字符串。接收器产品串是通用的 `Wireless Gaming Keyboard`，同 VID:PID 还有 VGN S99，不能按串推型号。电量显示 `—` |
| 英菲克 248A/249A:5C2E/5D2E/5E2E/5C2F | 官方表内 | 需要 `MI_02` 与 dev_id；IN9 老协议已实现，待实机 |
| 前行者 X23 SE | 协议未取到 | 官方桌面包已解到 `D:\EWEADN\extracted`，HID 帧在 native 模块，静态未定位 |
| ATK ZERO / V75X | 待实机 | 已索引的 asar 主进程是 BYKB 灯效线，ZERO 的 FF05 命令未出现在已索引文件 |
| RK R98Pro | 驱动不含该型号 | 官方 V4.6 包已用 innoextract 解开，`Dev` 下 200+ 款键盘定义无 R98/R98Pro，不在这版覆盖范围 |

## 运行时

- 常驻实例占用 Feature 通道时，`--detect` 可能读不到电量；先停常驻进程再跑。
- 迈从 A7 V2 的 Feature 通道第一次 `GetFeature` 恒回缓存帧，已改为每次重试重发查询。
- 任务栏嵌入、WPF 反射相关测试对构建环境敏感，`MouseDock.Checks` 的 5 项跳过以上文为准。
