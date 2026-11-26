# MouseDock 设备协议清单

核对日期 2026-11-26。下列实机结果来自本项目保存的测试或用户反馈。电量和 DPI 分别判定。电量读数、充电状态、DPI 读数、切档、写档值的验证范围各自独立。

| 设备及连接 | 电量 | DPI | 项目证据 |
|---|---|---|---|
| 迈从 A7 V3 Pro，3837:1018，2.4G | 实机通过，96% | 实机通过，读取、切档、回读、恢复 1600 → 3200 → 1600；任意档值改写待实机 | [电量](MCHOSE-MODERN-SCOPED.md) · [DPI](MOUSE-DPI.md) |
| 迈从 A7 V3 Pro，3837:4031，有线 | 实机通过，97% 充电中 | 理论实现，读档和切档 | [电量及 DPI](MOUSE-DPI.md) |
| 迈从 A7 Ultra，5253:1020，2.4G | 用户实机通过 | 理论实现，读档和切档 | [设备反馈](A7-ULTRA-M2-V5-HE.md) · [DPI](MOUSE-DPI.md) |
| MonsGeek M2 V5 HE，3151:5038，2.4G | 用户实机通过，接收器缓存显示为待刷新 | 不适用 | [反馈记录](A7-ULTRA-M2-V5-HE.md) |
| 迈从 A7 V2 Ultra+／A7 V2 Pro，各自 3837:100B／100A，2.4G | 实机通过，85%／83% | 理论实现，仅读取 | [设备记录](SUPPORTED-DEVICES.md) · [DPI](MOUSE-DPI.md) |
| 狼蛛 SC580，089D:062F，M137 实时确认 | 实机通过，82%，未充电 | 理论实现，读档和切档 | [设备记录](SUPPORTED-DEVICES.md) · [DPI](MOUSE-DPI.md) |
| VGN Neon75 Air，391D:2A05，2.4G | 实机通过，82% | 不适用 | [F7 实机记录](VGN-NEON75-AIR.md) |
| ROG 夜魔 X，0B05:1ACE，配对型号 1C25 | 实机通过，50% | 不适用 | [设备记录](SUPPORTED-DEVICES.md) |
| 罗技初代 GPW（C088 有线／C539 配对 4079）、G603（C539）、GPW2（C547） | 按各自身份进入 HID++ 读取 | 理论实现，按设备模式读取或修改当前 DPI | [电量](ORIGINAL-GPW.md) · [DPI](MOUSE-DPI.md) |
| 罗技 G304X，046D:C547，2.4G | 理论实现，HID++ 直接百分比与状态，离线通过，待实机 | 待核对 | [电量与身份校验](ORIGINAL-GPW.md#g304x-24g) |

### 前行者 S7／S9 系列

适用范围为 VID 372E 下列出的七个型号。有线及 2.4G 电量须通过 HID 产品名称、Feature Report 3 描述符和连续型号 UUID 校验。接收器还须报告在线。充电状态未知，电量待实机。S9 Ultra 的部分接收器响应格式仍需取证，当前会拒绝格式不匹配的报文。DPI 读数、切档和写档均待研究。

## 设备及连接方式

### 三份清单核对（2026-11-26）

对照输入为 `2.4G新增支持清单.md`、`兼容清单.txt`、`品牌电量支持.md`。基线为正式版 v1.2.4 及其后的主分支 `6be3b3f`。同一编号可能对应不同传输角色或共享接收器，电量、名称识别与 DPI 分别核对。

| 设备 | 本轮结果 | 验证条件 |
|---|---|---|
| VGN 蜻蜓 3 MasterGT、Y2 V2 MasterGT、F1 V2 SE+、F2 Turbo／Turbo+、3 MINI SE+ | 六个精确身份接入现有电量读取 | 同一物理实例的唯一 MI01 `FF02:2` 通道，17 字节双向 Report 8。连续两次有效电量回复，前后确认在线。待实机 |
| 雷柏 VT2 RT V2、VT2 RT MASTER V2（`24AE:1B20`、`24AE:1B21`） | 2.4G 被动电量 | 唯一 MI01 厂商通道的 19／20 字节 Report 7，连续两条有效通知。充电未知，待实机 |
| ROG 夜魔 Extreme 20 周年版（普通 Omni `0B05:1ACE`、配对 `1DA4`） | 配对型号和电量 | 采用现有键盘通道，前后核对配对表。待实机 |
| EXX 伽马 75（`1CA6:1C2D`、`1CA6:1C37`、`1CA6:1C45`） | 型号识别 | 完整产品串 `EXXGamma75`。电量协议待研究 |

基线已包含 RAWM 新增接收器、Sora V2 `AE12–AE16`、ATK HECHI `1147/1148`、Neon75 Turbo、黑爵 `FDFD`、艾石头 `502B`、瓦尔基里 `A313/A316`、西伯利亚 K06S `F002` 和指定雷蛇鼠标的读取入口。对应硬件验证等级见各设备记录。

IPI 飘88 Pro、VGN N75 V2、Rainy75 V2、Pulsar 扩展分支、NinjaForce、Sora 4K 及新增耳机协议仍需核对各自报文。狼蛛 GM5 系列接收器 `248A:5B4A` 已有被动 Report 09 记录，仍需确认接口、报文长度、电量字段和离线样本。瓦尔基里 `402D/A223` 仍需验证通过同一接收器区分在线键盘和鼠标。

### ROG 2.4G 接收器扩展

| 接收器 | 新增设备路径 | 验证状态 |
|---|---|---|
| SPEEDNOVA 8K `0B05:1AD0` | 鼠标经 MI02 `FF01:1` 的唯一 Report 03 双向集合取得两次符合 Windows 原生长度的有效电量回复。与已识别的键盘分别显示。配对鼠标型号和充电状态未知 | 离线回归通过，待实机 |
| 普通 Omni `0B05:1ACE` | 夜魔 `1A85`、游侠 II 96 `1AB0`、夜魔 Extreme `1B42`、夜魔 Extreme SE `1CF1`。唯一已知键盘配对且前后配对表一致时读取电量 | 配对回归通过，待实机 |

### Pulsar 有线电量扩展

Xlite V3、X2 V2、X2H 和 X2A 的 USB 直连身份分别为 `3710:1401` 至 `3710:1404`。复用现有 Sonix 读取器，要求同一实例具有唯一 MI03 `FFFF:1` 集合和 65 字节无编号 Feature。两轮有效读数后显示设备报告的百分比，充电状态未知。接口和报文已通过离线回归，四款设备待实机。对应无线接收器及 DPI 以各自记录为准。

同一型号的有线、接收器和配对身份可分别占一行。每行独立记录电量与 DPI。

- **理论实现**表示 MouseDock 已有对应读取或设置路径，实际设备须满足表中身份和接口条件。
- **实机通过**表示本项目有该连接方式、该项操作的实测或用户复测记录。
- **待核对**表示该型号与项目实现的对应关系尚未确认。**待研究**表示协议仍需取证。**未开放**表示当前不提供该操作。

| 型号或配对身份 | 连接或适用范围 | 电量 | DPI |
|---|---|---|---|
| ATK F1 Ultimate 0x373B:0x1031 | 接收器 | 理论实现 | 理论实现（按身份读改） |
| ATK F1 Ultimate 0x373B:0x102E | 有线直连 | 理论实现 | 理论实现（按身份读改） |
| ATK A9 Ultimate 0x373B:0x11D9 | 接收器 | 理论实现 | 理论实现（按身份读改） |
| ATK A9 Ultimate 0x373B:0x11B6 | 有线直连 | 理论实现 | 理论实现（按身份读改） |
| VGN F2 Pro Max 0x3554:0xFB3E | 接收器 | 理论实现 | 未开放 |
| VGN F2 Pro Max 0x3554:0xFB3D | 有线直连 | 理论实现 | 未开放 |
| COMPX 接收器（型号待确认） 0x3554:0xF517 | 接收器 | 理论实现 | 未开放 |
| 塞洛斯 V12M 0x395A:0x0006 | 接收器 | 理论实现 | 理论实现（按身份读改） |
| ATK Zero 0x373B:0x11D8 | 接收器 | 理论实现 | 理论实现（按身份读改） |
| ATK 8K 接收器（型号待确认） 0x373B:0x1278 | 接收器 | 理论实现 | 理论实现（按身份读改） |
| 雷柏 VT9S V2 0x24AE:0x1453 | 接收器 | 理论实现 | 理论实现（按身份读改） |
| ATK Zero 0x373B:0x1155 | 接收器 | 待核对 | 待核对 |
| ATK Zero 0x373B:0x124F | 接收器 | 待核对 | 待核对 |
| ATK Zero 0x373B:0x1154 | 有线直连 | 待核对 | 待核对 |
| Sora V2 0x1915:0xAE8A | 接收器 | 理论实现 | 待核对 |
| Sora V2 0x1915:0xAE1C | 接收器 | 理论实现 | 待核对 |
| Sora V2 0x1915:0xAE11 | 有线直连 | 理论实现 | 待核对 |
| Sora V2 8K 0x1915:0xAE8C | 接收器 | 理论实现 | 待核对 |
| VXE R1 Pro Max 0x3554:0xF58A | 接收器 | 理论实现 | 理论实现（按身份读改） |
| VXE R1 Pro Max 0x3554:0xF58C | 有线直连 | 理论实现 | 理论实现（按身份读改） |
| VXE R1 SE+ 0x3554:0xF58E | 接收器 | 理论实现 | 理论实现（只读） |
| VXE R1 SE+ 0x3554:0xF58F | 有线直连 | 理论实现 | 理论实现（按身份读改） |
| VGN F1 Pro 0x3554:0xF503 | 接收器 | 理论实现 | 待核对 |
| VGN F1 Pro 0x3554:0xF502 | 有线直连 | 理论实现 | 待核对 |
| Scyrox V6 / V8 0x3554:0xF5F7 | 接收器 | 待核对 | 待核对 |
| Scyrox V6 / V8 0x3554:0xF5F6 | 有线直连 | 理论实现 | 待核对 |
| Sora V2 0x1915:0xAE12 | 有线直连 | 理论实现 | 待核对 |
| Sora V2 0x1915:0xAE13 | 有线直连 | 理论实现 | 待核对 |
| Sora V2 8K 0x1915:0xAE14 | 有线直连 | 理论实现 | 待核对 |
| Sora V2 8K 0x1915:0xAE15 | 有线直连 | 理论实现 | 待核对 |
| Sora V2 8K 0x1915:0xAE16 | 有线直连 | 理论实现 | 待核对 |
| Pulsar X2A Wired 0x3710:0x1404 | 有线直连 | 理论实现 | 待核对 |
| Pulsar X2H Wired 0x3710:0x1403 | 有线直连 | 理论实现 | 待核对 |
| Pulsar X2 V2 Wired 0x3710:0x1402 | 有线直连 | 理论实现 | 待核对 |
| Pulsar Xlite V3 Wired 0x3710:0x1401 | 有线直连 | 理论实现 | 待核对 |
| 雷柏 V700DIY-98 长续航版 0x24AE:0x1924 | 以记录中的接口为准 | 待核对 | 未开放 |
| ATK V75X 0x320F:0x5088 | 2.4G | 待核对 | 未开放 |
| 无线键盘（8FFF） 0x1A2C:0x8FFF | 2.4G | 待核对 | 未开放 |
| VGN 蜻蜓 3 SE 0x391D:0x1A01 | 2.4G | 待核对 | 未开放 |
| 2.4G 键盘接收器（型号待核验） 0x36B0:0x3002 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 共享无线键盘接收器 0x372E:0x106C | 以记录中的接口为准 | 待核对 | 未开放 |
| IPI 飘88 Pro 接收器 0x372E:0x10C5 | 2.4G | 待核对 | 未开放 |
| IPI 雾 0x372E:0x10F9 | 以记录中的接口为准 | 待核对 | 未开放 |
| 索尼 INZONE H9 / H7 接收器 0x054C:0x0E53 | 以记录中的接口为准 | 待核对 | 未开放 |
| ATK N9 Ultra 0x373B:0x1204 | 以记录中的接口为准 | 待核对 | 未开放 |
| ATK N9 Pro 0x373B:0x11CB | 以记录中的接口为准 | 待核对 | 未开放 |
| ATK N9 V2 Ultimate 0x373B:0x1262 | 以记录中的接口为准 | 待核对 | 未开放 |
| ATK N9 V2 ULTRA 0x373B:0x12D6 | 以记录中的接口为准 | 待核对 | 未开放 |
| 西伯利亚 X7 系列 / 8K 接收器 0x36F9:0xAA20 | 以记录中的接口为准 | 待核对 | 未开放 |
| 前行者 X23 Pro（老款） 0x04F3:0x026F | 以记录中的接口为准 | 待核对 | 未开放 |
| 前行者 X23 星闪版 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 X23 Max 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 X23 SE 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 X23 Pro 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 X23 碳纤维版 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 X23L 碳纤维版 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 X23 星闪版 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 X23 Max 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 X23 SE 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 X23 Pro 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 X23 碳纤维版 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 X23L 碳纤维版 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| HECATE G3M Pro 升级版 0x35BB:0xD3FB | 以记录中的接口为准 | 待核对 | 未开放 |
| 前行者 X23 Max 0x3554:0xF5A0 | 有线 | 待核对 | 未开放 |
| 前行者 X23 Max 0x3554:0xF59F | 2.4G | 待核对 | 未开放 |
| 前行者 X23 Max 0x3554:0xF5B2 | 2.4G | 待核对 | 未开放 |
| 前行者 X23 Max 0x3554:0xF5B3 | 有线 | 待核对 | 未开放 |
| 前行者 X23 Max 0x3554:0xF5B5 | 2.4G | 待核对 | 未开放 |
| 前行者鼠标接收器 0x3554:0xFB35 | 2.4G | 待核对 | 未开放 |
| 前行者有线鼠标 0x3554:0xFB36 | 有线 | 待核对 | 未开放 |
| 2.4G 鼠标接收器 0x089D:0x062F | 以记录中的接口为准 | 待核对 | 未开放 |
| EXX 阿尔法 87 0x372E:0x10EB | 有线 | 理论实现 | 未开放 |
| EXX 阿尔法 87 0x258A:0x02D8 | 2.4G | 理论实现 | 未开放 |
| 达尔优 FLEX87 0x260D:0x0211 | 以记录中的接口为准 | 待核对 | 未开放 |
| 达尔优共享接收器（待确认键盘） 0x260D:0x0037 | 2.4G | 待核对 | 未开放 |
| ROG Omni 键盘 0x0B05:0x1ACE | 以记录中的接口为准 | 待核对 | 未开放 |
| HECATE G5 MAX 无线版 0x35BB:0xA00B | 以记录中的接口为准 | 待核对 | 未开放 |
| HECATE GX03 ULTRA 0x35BB:0xA217 | 以记录中的接口为准 | 待核对 | 未开放 |
| 迈从 G87 V2 (8K) 无线键盘 0x3837:0x3033 | 以记录中的接口为准 | 待核对 | 未开放 |
| 迈从 G75 V2 (8K) 无线键盘 0x3837:0x3033 | 以记录中的接口为准 | 待核对 | 未开放 |
| 迈从 K99 V3 0x3837:0x3033 | 以记录中的接口为准 | 待核对 | 未开放 |
| CHERRY MX 2.0S Pro (2.4G) 0x046A:0x01E0 | 以记录中的接口为准 | 待核对 | 未开放 |
| CHERRY MX 2.0S Pro (USB) 0x046A:0x01DE | 以记录中的接口为准 | 待核对 | 未开放 |
| 西伯利亚 T10 无线版 0x36F9:0xF001 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 西伯利亚 K06S 0x36F9:0xF002 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 西伯利亚 T7 0x36F9:0xF008 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 雷蛇 BlackShark V3 0x1532:0x057A | 以记录中的接口为准 | 待核对 | 未开放 |
| 雷蛇 BlackShark V3 Pro 0x1532:0x0577 | 以记录中的接口为准 | 待核对 | 未开放 |
| 雷蛇 BlackShark V3 Pro PS 0x1532:0x101B | 以记录中的接口为准 | 待核对 | 未开放 |
| 西伯利亚 T10 无线版 0x36F9:0xF010 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 玄武 2.0 / VGN 共用编号接收器 0x040B:0xF779 | 以记录中的接口为准 | 待核对 | 未开放 |
| VGN Siren V2 0x391D:0x0003 | 以记录中的接口为准 | 待核对 | 未开放 |
| VGN 周听系列 0x372E:0x10BB | 以记录中的接口为准 | 待核对 | 未开放 |
| AULA G9 0x38A6:0x0030 | 以记录中的接口为准 | 理论实现 | 未开放 |
| MCHOSE V9 Pro 2 0x3837:0x6011 | 以记录中的接口为准 | 待核对 | 未开放 |
| MCHOSE V9 Turbo+ 0x3837:0x600A | 以记录中的接口为准 | 待核对 | 未开放 |
| MCHOSE R9 0x3837:0x6013 | 以记录中的接口为准 | 待核对 | 未开放 |
| MCHOSE V9 Pro 0x291D:0x385D | 以记录中的接口为准 | 理论实现 | 未开放 |
| 迈从 G87 无线键盘 0x41E4:0x2001 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 迈从 G87 V2 (1K) 无线键盘 0x3837:0x201B | 以记录中的接口为准 | 理论实现 | 未开放 |
| JBL Quantum 910 Wireless 0x0ECB:0x2088 | 以记录中的接口为准 | 待核对 | 未开放 |
| HyperX Cloud Alpha 2 0x03F0:0x08BE | 以记录中的接口为准 | 待核对 | 未开放 |
| Lofree Hyzen 0x388D:0x0025 | 以记录中的接口为准 | 待核对 | 未开放 |
| VGN V98 Pro 0x320F:0x5088 | 以记录中的接口为准 | 待核对 | 未开放 |
| VGN N75 Pro 0x320F:0x5088 | 以记录中的接口为准 | 待核对 | 未开放 |
| VGN / VXE V87 0x320F:0x5088 | 以记录中的接口为准 | 待核对 | 未开放 |
| VGN V98 Pro V2 0x320F:0x5088 | 以记录中的接口为准 | 待核对 | 未开放 |
| VGN VXE75 0x320F:0x5088 | 以记录中的接口为准 | 待核对 | 未开放 |
| VGN S99 0x320F:0x5088 | 以记录中的接口为准 | 待核对 | 未开放 |
| Razer BlackWidow V3 Pro 0x1532:0x025C | 以记录中的接口为准 | 待核对 | 未开放 |
| Razer BlackShark V2 Pro (2020) 0x1532:0x0528 | 以记录中的接口为准 | 待核对 | 未开放 |
| Razer BlackShark V2 Pro (2023) 0x1532:0x0557 | 以记录中的接口为准 | 待核对 | 未开放 |
| 雷蛇 旋风黑鲨 V2 极速版 0x1532:0x0565 | 2.4G 接收器/底座 | 待核对 | 未开放 |
| Razer BlackShark V2 Pro (2023) 0x1532:0x0555 | 以记录中的接口为准 | 待核对 | 未开放 |
| Razer BlackShark V2 Pro (2023) 0x1532:0x0556 | 以记录中的接口为准 | 待核对 | 未开放 |
| Razer Barracuda Pro 0x1532:0x053A | 以记录中的接口为准 | 待核对 | 未开放 |
| HyperX Cloud III Wireless 0x03F0:0x05B7 | 以记录中的接口为准 | 理论实现 | 未开放 |
| HyperX Cloud III Wireless 0x03F0:0x0C9D | 以记录中的接口为准 | 理论实现 | 未开放 |
| HyperX Cloud II Wireless 0x03F0:0x018B | 以记录中的接口为准 | 理论实现 | 未开放 |
| HyperX Cloud II Core Wireless 0x03F0:0x069F | 以记录中的接口为准 | 理论实现 | 未开放 |
| HyperX Cloud II Core Wireless 0x03F0:0x0995 | 以记录中的接口为准 | 理论实现 | 未开放 |
| iKF-King G 0x38B1:0x3412 | 2.4G | 待核对 | 未开放 |
| iKF-V11 Pro 驱动版 0x38B1:0x3411 | 2.4G | 待核对 | 未开放 |
| iKF-影刺 Pro 0x38B1:0x3418 | 2.4G | 待核对 | 未开放 |
| iKF V11 Pro 2.0 / Pro⁺ 0x38B1:0x3419 | 2.4G | 待核对 | 未开放 |
| MOONDROP Gaming Maestro (GM01) 0x35D8:0x012D | 以记录中的接口为准 | 待核对 | 未开放 |
| MOONDROP GM01 Pro 0x35D8:0x0133 | 以记录中的接口为准 | 待核对 | 未开放 |
| HyperX Cloud III Wireless 0x03F0:0x0FCC | 以记录中的接口为准 | 理论实现 | 未开放 |
| ROG 棱镜 2 / Delta II 0x0B05:0x1AFA | 以记录中的接口为准 | 理论实现 | 未开放 |
| ROG 棱镜 2 - KJP 0x0B05:0x1D41 | 以记录中的接口为准 | 待核对 | 未开放 |
| ROG 破风 PELTA 0x0B05:0x1B84 | 以记录中的接口为准 | 理论实现 | 未开放 |
| HyperX 飓风炫彩版 / Cloud Flight 2 0x03F0:0x0AC1 | 以记录中的接口为准 | 待核对 | 未开放 |
| HyperX Cloud III S Wireless 0x03F0:0x02CC | 以记录中的接口为准 | 理论实现 | 未开放 |
| HyperX Cloud III S Wireless 0x03F0:0x06BE | 以记录中的接口为准 | 理论实现 | 未开放 |
| 迈从 G75 V2 无线键盘 0x41E4:0x2001 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 迈从 K99 一代无线键盘 0x05AC:0x024F | 以记录中的接口为准 | 理论实现 | 未开放 |
| VGN V98 Pro V3 0x258A:0x024F | 以记录中的接口为准 | 待核对 | 未开放 |
| VGN V98 Pro V3 0x258A:0x027F | 以记录中的接口为准 | 待核对 | 未开放 |
| VGN V98 Pro V4 8K 0x391D:0x2A03 | 以记录中的接口为准 | 待核对 | 未开放 |
| VGN V98 Pro V4 1K 0x391D:0x2A04 | 以记录中的接口为准 | 待核对 | 未开放 |
| VGN V98 Pro V4 8K TIMI 0x391D:0x2A0A | 以记录中的接口为准 | 待核对 | 未开放 |
| 无线键盘（FA09） 0x3554:0xFA09 | 以记录中的接口为准 | 待核对 | 未开放 |
| 无线键盘（待确认型号） 0x3554:0xFA09 | 以记录中的接口为准 | 待核对 | 未开放 |
| 无线键盘（型号待确认） 0x3554:0xFA09 | 以记录中的接口为准 | 待核对 | 未开放 |
| 无线键盘（FA09） 0x3554:0xFA09 | 以记录中的接口为准 | 待核对 | 未开放 |
| 2.4G 键盘接收器 0x05AC:0x024F | 以记录中的接口为准 | 待核对 | 未开放 |
| 狼蛛 F98 Pro V3 0x05AC:0x024F | 以记录中的接口为准 | 待核对 | 未开放 |
| 狼途 LT75 Pro 0x05AC:0x024F | 以记录中的接口为准 | 待核对 | 未开放 |
| 狼途 LT75 0x05AC:0x024F | 以记录中的接口为准 | 待核对 | 未开放 |
| 狼途 LT95 / LT95 Pro 0x05AC:0x024F | 以记录中的接口为准 | 待核对 | 未开放 |
| ROG 夜魔 / Azoth 0x0B05:0x1A85 | 以记录中的接口为准 | 待核对 | 未开放 |
| ROG Azoth X 0x0B05:0x1C24 | 以记录中的接口为准 | 待核对 | 未开放 |
| ROG Azoth X 0x0B05:0x1C25 | 以记录中的接口为准 | 待核对 | 未开放 |
| LEADJOY 兼容手柄 0x4131:0x3519 | 以记录中的接口为准 | 待核对 | 未开放 |
| 飞智 八爪鱼 5 0x37D7:0x2501 | 以记录中的接口为准 | 待核对 | 未开放 |
| 飞智 黑武士 5 Pro 0x37D7:0x2401 | 以记录中的接口为准 | 待核对 | 未开放 |
| Sony DualShock 4 0x054C:0x05C4 | 以记录中的接口为准 | 理论实现 | 未开放 |
| Sony DualShock 4 v2 0x054C:0x09CC | 以记录中的接口为准 | 理论实现 | 未开放 |
| Sony DualShock 4 0x054C:0x05C5 | 以记录中的接口为准 | 理论实现 | 未开放 |
| Sony DualShock 4 无线适配器 0x054C:0x0BA0 | 以记录中的接口为准 | 理论实现 | 未开放 |
| Sony DualSense 0x054C:0x0CE6 | 以记录中的接口为准 | 理论实现 | 未开放 |
| Sony DualSense Edge 0x054C:0x0DF2 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 八位堂 SF30 Pro 0x2DC8:0x6000 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 八位堂 SF30 Pro 0x2DC8:0x6100 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 八位堂 SN30 Pro 0x2DC8:0x6001 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 八位堂 SN30 Pro 0x2DC8:0x6101 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 八位堂 Pro 2 0x2DC8:0x6003 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 八位堂 Pro 2 0x2DC8:0x6006 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 八位堂 Pro 3 0x2DC8:0x6009 | 以记录中的接口为准 | 理论实现 | 未开放 |
| Nintendo Joy-Con (L) 0x057E:0x2006 | 以记录中的接口为准 | 待核对 | 未开放 |
| Nintendo Joy-Con (R) 0x057E:0x2007 | 以记录中的接口为准 | 待核对 | 未开放 |
| Nintendo Switch Pro Controller 0x057E:0x2009 | 以记录中的接口为准 | 待核对 | 未开放 |
| EPOS H3PRO 0x1395:0x0303 | 以记录中的接口为准 | 待核对 | 未开放 |
| 戴灵 G3 / G4 0x3654:0x4255 | 以记录中的接口为准 | 待核对 | 未开放 |
| VGN Neon75 0x3151:0x502F | 以记录中的接口为准 | 待核对 | 未开放 |
| VGN Neon75 0x3151:0x5038 | 以记录中的接口为准 | 待核对 | 未开放 |
| VGN Neon75 超竞版 0x3151:0x5055 | 以记录中的接口为准 | 待核对 | 未开放 |
| VGN Neon75 Turbo 0x391D:0x2A07 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 艾石头无线键盘（MG68 系列） 0x3151:0x5038 | 以记录中的接口为准 | 理论实现 | 未开放 |
| WEIKAV NUT75 0x0C45:0xFEF9 | 以记录中的接口为准 | 待核对 | 未开放 |
| AJAZZ 2.4G 接收器 0x3151:0x4027 | 以记录中的接口为准 | 待核对 | 未开放 |
| 2.4G 鼠标接收器 0x248A:0x5C2F | 以记录中的接口为准 | 待核对 | 未开放 |
| 2.4G 鼠标接收器 0x249A:0x5C2F | 以记录中的接口为准 | 待核对 | 未开放 |
| AJAZZ 2.4G 鼠标接收器 0x248A:0x5B2F | 以记录中的接口为准 | 待核对 | 未开放 |
| 海盗船无线接收器（配对型号待读取） 0x1B1C:0x1B62 | 以记录中的接口为准 | 待核对 | 未开放 |
| 多彩 M700 Ultra / Pro+ 2.4G 0x320F:0x228A | 以记录中的接口为准 | 待核对 | 未开放 |
| 1D57:FA65 无线鼠标 0x1D57:0xFA65 | 以记录中的接口为准 | 待核对 | 未开放 |
| 1D57:FA60 无线鼠标 0x1D57:0xFA60 | 以记录中的接口为准 | 待核对 | 未开放 |
| ROYUAN 无线键盘 0x3151:0x4015 | 以记录中的接口为准 | 待核对 | 未开放 |
| WOB Rainy 75 0x320F:0x5055 | 以记录中的接口为准 | 待核对 | 未开放 |
| 前行者 S9 SE 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 S9 SE 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 A6 Pro 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 A6 Pro 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 A6 Max 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 A6 Max 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 A7 SE 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 A7 SE 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 A7 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 A7 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 A7 Pro 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 A7 Pro 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 E7 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 E7 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 E7 Pro 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 E7 Pro 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 E9 Carbon 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 E9 Carbon 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 E9 Pro Carbon 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 E9 Pro Carbon 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 E9 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 E9 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 E11 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 E11 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 E11 Pro 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 E11 Pro 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 A6 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 G2SE A 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 G2SE A 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 A6 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 S7 Plus 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 S7 Plus 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 S2 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 S2 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 S7 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 S7 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 S7 SE 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 S7 SE 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者 G5 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 G5 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 前行者有线鼠标 0x3554:0xF515 | 有线 | 待核对 | 未开放 |
| 前行者鼠标接收器 0x3554:0xF517 | 2.4G | 理论实现 | 未开放 |
| 前行者鼠标接收器 0x3554:0xFB45 | 2.4G | 待核对 | 未开放 |
| 前行者有线鼠标 0x3554:0xFB47 | 有线 | 待核对 | 未开放 |
| 前行者鼠标接收器 0x3554:0xFB48 | 2.4G | 待核对 | 未开放 |
| 名龙星球 MX300 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 名龙星球 MX301 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 名龙星球 MX301 PRO 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 名龙星球 MX301 PRO Gold 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 名龙星球 MX301 PRO Lite 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 名龙星球 MX301 PRO V2 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 名龙星球 MX302 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 名龙星球 MX306 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| VGN 蜻蜓 3 大师版 0x391D:0x1A05 | 以记录中的接口为准 | 待核对 | 未开放 |
| VGN 蜻蜓 F2 大师版 (MasterGT) 0x391D:0x102D | 以记录中的接口为准 | 待核对 | 未开放 |
| Pulsar Fusion2 1K 接收器 0x3710:0x5402 | 以记录中的接口为准 | 理论实现 | 待核对 |
| Pulsar Fusion2 8K 接收器 0x3710:0x5403 | 以记录中的接口为准 | 理论实现 | 待核对 |
| 迈从 G98 V3 0x3837:0x1015 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 迈从 K87 V3 0x3837:0x1015 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 狼途 M8 无线鼠标 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 迈从 G3 A 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| FREQ 2.4G 鼠标接收器 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 狼蛛 V9 Pro 0x373B:0x1058 | 2.4G | 待核对 | 未开放 |
| AULA 无线鼠标 0x38A6:0x28F9 | 2.4G | 待核对 | 待核对 |
| 320F 无线键盘 0x320F:0x5088 | 2.4G | 待核对 | 未开放 |
| 无线耳机（0D8C:0334） 0x0D8C:0x0334 | 2.4G | 待核对 | 未开放 |
| ATK A87 Pro 0x373B:0x111E | 以记录中的接口为准 | 理论实现 | 未开放 |
| ATK A87 Pro 0x373B:0x111F | 以记录中的接口为准 | 理论实现 | 未开放 |
| 前行者 S9 Pro 0x372E:0x1029 | 有线直连 | 理论实现（电量百分比） | 待研究 |
| 前行者 S9 接收器（型号待确认） 0x372E:0x1026 | 2.4G | 理论实现（电量百分比） | 待研究 |
| 前行者 S9 接收器（型号待确认） 0x372E:0x103D | 2.4G | 理论实现（电量百分比） | 待研究 |
| 索尼 INZONE H5 0x054C:0x0EBF | 以记录中的接口为准 | 待核对 | 未开放 |
| 瓦尔基里 VK75 0x05AC:0x024F | 以记录中的接口为准 | 理论实现 | 未开放 |
| 共享无线键盘接收器（型号待确认） 0x3151:0x5026 | 以记录中的接口为准 | 待核对 | 未开放 |
| 瓦尔基里 VK M5 Pro 接收器 0x374A:0xA329 | 以记录中的接口为准 | 待核对 | 未开放 |
| X75 / X87 协议键盘 0x0C45:0xFEFE | 以记录中的接口为准 | 理论实现 | 未开放 |
| 056D:C077 无线键盘 0x056D:0xC077 | 以记录中的接口为准 | 待核对 | 未开放 |
| VGN Neon75 Air 0x391D:0x2A05 | 以记录中的接口为准 | 实机通过（82%） | 未开放 |
| HECATE G3M Pro 0x320F:0x706E | 以记录中的接口为准 | 待核对 | 未开放 |
| Keychron Ultra-Link 8K（接收器，型号待确认） 0x3434:0xD028 | 2.4G 接收器 | 理论实现 | 未开放 |
| Keychron M5 0x3434:0xD048 | USB 有线 | 理论实现 | 未开放 |
| NZXT Lift Elite Wireless 0x1E71:0x2101 | 2.4G 接收器 | 理论实现 | 未开放 |
| NZXT Lift Elite（USB） 0x1E71:0x2129 | USB 有线 | 理论实现 | 未开放 |
| 瓦尔基里 VK99 0x05AC:0x024F | 以记录中的接口为准 | 理论实现 | 未开放 |
| 瓦尔基里 VK M2 接收器 0x374A:0xA308 | 以记录中的接口为准 | 理论实现 | 未开放 |
| ROG SPEEDNOVA 8K 接收器 0x0B05:0x1AD0 | 2.4G，键盘与鼠标独立读取 | 理论实现 | 未开放 |
| 瓦尔基里 VK M2 有线 0x374A:0xA304 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 瓦尔基里 VK M2 接收器 0x374A:0xA305 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 瓦尔基里 VK M2 有线 0x374A:0xA307 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 瓦尔基里键盘接收器 0x374A:0xA263 | 以记录中的接口为准 | 待核对 | 未开放 |
| 瓦尔基里键盘接收器 0x374A:0xA278 | 以记录中的接口为准 | 待核对 | 未开放 |
| 瓦尔基里键盘接收器 0x374A:0xA281 | 以记录中的接口为准 | 待核对 | 未开放 |
| USB 鼠标 0x248A:0x5C2E | 以记录中的接口为准 | 待核对 | 未开放 |
| USB 鼠标 0x248A:0x5D2E | 以记录中的接口为准 | 待核对 | 未开放 |
| USB 鼠标 0x248A:0x5E2E | 以记录中的接口为准 | 待核对 | 未开放 |
| 2.4G 无线键盘 0x05AC:0x024F | 以记录中的接口为准 | 理论实现 | 未开放 |
| AULA 无线鼠标 0x38A6:0x2F1A | 2.4G | 待核对 | 待核对 |
| AULA 无线鼠标 0x38A6:0x28FD | 2.4G | 待核对 | 待核对 |
| AULA 无线鼠标 0x38A6:0x28E9 | 2.4G | 待核对 | 待核对 |
| 达尔优 A98（USB） 0x260D:0x0051 | USB 有线直连 | 待核对 | 未开放 |
| ATK A98 0x373B:0x114D | 有线 | 理论实现 | 未开放 |
| ATK A98 0x373B:0x114E | 2.4G | 理论实现 | 未开放 |
| ATK A98 Pro 0x373B:0x1151 | 有线 | 理论实现 | 未开放 |
| ATK A98 Pro 0x373B:0x1152 | 2.4G | 理论实现 | 未开放 |
| ATK A87 Ultra 0x373B:0x1231 | 有线 | 理论实现 | 未开放 |
| 狼蛛 F87 Pro V2 C 0x0C45:0xFEFC | 2.4G | 理论实现 | 未开放 |
| 狼蛛 F87 Pro V2 D 0x0C45:0xFEFE | 2.4G | 理论实现 | 未开放 |
| 狼蛛 F87 Pro V2 Z 0x0C45:0xFEFE | 2.4G | 理论实现 | 未开放 |
| 狼蛛 F108 Pro V2 0x0C45:0xFEFE | 2.4G | 理论实现 | 未开放 |
| 狼蛛 F108 Pro V2 C 0x0C45:0xFEFC | 2.4G | 理论实现 | 未开放 |
| 狼蛛 NOVA75 Pro 0x0C45:0xFEFC | 2.4G | 理论实现 | 未开放 |
| Epomaker × 狼蛛 F75 V2 0x0C45:0xFEFC | 2.4G | 理论实现 | 未开放 |
| 狼蛛 F75 V2 0x0C45:0xFEFC | 2.4G | 理论实现 | 未开放 |
| 狼蛛 NOVA98 0x0C45:0xFEFC | 2.4G | 理论实现 | 未开放 |
| 狼蛛 F75 Master 0x0C45:0xFEF9 | 2.4G | 理论实现 | 未开放 |
| WOB Rainy75 V2 0x36B0:0x3071 | 2.4G | 待核对 | 未开放 |
| WOB Rainy75 V2 0x36B0:0x3075 | USB | 待核对 | 未开放 |
| HECATE G3 MAX 无线版 0x35BB:0xA226 | 以记录中的接口为准 | 待核对 | 未开放 |
| HECATE G3 无线版 0x35BB:0xA220 | 以记录中的接口为准 | 待核对 | 未开放 |
| 无线鼠标（5045） 0x3151:0x5045 | 以记录中的接口为准 | 待核对 | 未开放 |
| ROG 龙骑士二代 / CLAYMORE II 0x0B05:0x196B | 以记录中的接口为准 | 待核对 | 未开放 |
| VGN Dragonfly 3 MasterGT 0x391D:0x1A1A | 以记录中的接口为准 | 理论实现 | 未开放 |
| VGN Dragonfly Y2 V2 MasterGT 0x391D:0x1A2F | 以记录中的接口为准 | 理论实现 | 未开放 |
| VGN Dragonfly F1 V2 SE+ 0x391D:0x1A32 | 以记录中的接口为准 | 理论实现 | 未开放 |
| VGN Dragonfly F2 Turbo+ 0x391D:0x1A31 | 以记录中的接口为准 | 理论实现 | 未开放 |
| VGN Dragonfly F2 Turbo 0x391D:0x1A30 | 以记录中的接口为准 | 理论实现 | 未开放 |
| VGN Dragonfly 3 MINI SE+ 0x391D:0x1A33 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 前行者 S7/S9 接收器 0x372E:0x1026 | 2.4G | 理论实现（电量百分比） | 待研究 |
| 前行者 S7/S9 接收器 0x372E:0x1026 | 2.4G | 理论实现（电量百分比） | 待研究 |
| 前行者 S7/S9 接收器 0x372E:0x1026 | 2.4G | 理论实现（电量百分比） | 待研究 |
| 前行者 S9 Ultra 0x372E:0x1027 | 有线直连 | 理论实现（电量百分比） | 待研究 |
| 前行者 S7/S9 接收器 0x372E:0x103D | 2.4G | 理论实现（电量百分比） | 待研究 |
| 前行者 S7/S9 接收器 0x372E:0x103D | 2.4G | 理论实现（电量百分比） | 待研究 |
| 前行者 S7/S9 接收器 0x372E:0x103D | 2.4G | 理论实现（电量百分比） | 待研究 |
| 前行者 S9 Max 0x372E:0x105E | 有线直连 | 理论实现（电量百分比） | 待研究 |
| 前行者 S7/S9 接收器 0x372E:0x106E | 2.4G | 理论实现（电量百分比） | 待研究 |
| 前行者 S7/S9 接收器 0x372E:0x106E | 2.4G | 理论实现（电量百分比） | 待研究 |
| 前行者 S7 Pro 0x372E:0x107B | 有线直连 | 理论实现（电量百分比） | 待研究 |
| 前行者 S7 Max 0x372E:0x107C | 有线直连 | 理论实现（电量百分比） | 待研究 |
| 前行者 S7 Ultra 0x372E:0x108A | 有线直连 | 理论实现（电量百分比） | 待研究 |
| 前行者 S9 Ultra+ 0x372E:0x108D | 有线直连 | 理论实现（电量百分比） | 待研究 |
| 前行者 X23Pro 0xA8A4:0x2255 | 有线 | 待核对 | 未开放 |
| 前行者 X23Pro 0xA8A5:0x2255 | 2.4G | 待核对 | 未开放 |
| 竹林鸟烛龙 ZG4 0x3654:0x5055 | 2.4G | 待核对 | 未开放 |
| 狼蛛 SC580 SE 0x248D:0x5B5F | 2.4G接收器 | 待核对 | 未开放 |
| Pulsar X2A V3 0x3710:0x3404 | 以记录中的接口为准 | 待核对 | 待核对 |
| Pulsar Xlite V4 0x3710:0x3401 | 以记录中的接口为准 | 待核对 | 待核对 |
| Pulsar X2H V3 0x3710:0x3403 | 以记录中的接口为准 | 待核对 | 待核对 |
| Pulsar X2 V3 0x3710:0x3402 | 以记录中的接口为准 | 待核对 | 待核对 |
| Pulsar Feinmann F01 0x3710:0x3413 | 以记录中的接口为准 | 待核对 | 待核对 |
| Pulsar X3 0x3710:0x3409 | 以记录中的接口为准 | 待核对 | 待核对 |
| Pulsar X3 LHD 0x3710:0x3410 | 以记录中的接口为准 | 待核对 | 待核对 |
| Pulsar Fusion receiver 0x3710:0x5404 | 以记录中的接口为准 | 待核对 | 待核对 |
| 索尼 INZONE H9 II 0x054C:0x0FA8 | 以记录中的接口为准 | 待核对 | 未开放 |
| Logitech G PRO X 3 LIGHTSPEED 0x046D:0x0B45 | 以记录中的接口为准 | 待核对 | 未开放 |
| ASTRO A50 Gen 4（PC 模式） 0x9886:0x002C | 以记录中的接口为准 | 待核对 | 未开放 |
| Jabra Link 380 配对耳机 0x0B0E:0x24C7 | 以记录中的接口为准 | 待核对 | 未开放 |
| Jabra Link 390 配对耳机 0x0B0E:0x2E57 | 以记录中的接口为准 | 待核对 | 未开放 |
| Jabra Evolve2 65 Flex（USB） 0x0B0E:0x2519 | 以记录中的接口为准 | 待核对 | 未开放 |
| HyperX Cloud Flight S 0x0951:0x16EA | 以记录中的接口为准 | 待核对 | 未开放 |
| HyperX Cloud Flight S 0x0951:0x16EB | 以记录中的接口为准 | 待核对 | 未开放 |
| HyperX Cloud Stinger 2 Wireless 0x03F0:0x0D93 | 以记录中的接口为准 | 待核对 | 未开放 |
| SteelSeries Arctis 9 0x1038:0x12C4 | 以记录中的接口为准 | 待核对 | 未开放 |
| Sony PULSE 3D 0x054C:0x0D5E | 以记录中的接口为准 | 待核对 | 未开放 |
| HyperX Cloud Flight Wireless（HP） 0x03F0:0x0E90 | 以记录中的接口为准 | 待核对 | 未开放 |
| HyperX Cloud Flight for PS 0x0951:0x1749 | 以记录中的接口为准 | 待核对 | 未开放 |
| Corsair Virtuoso XT 无线 0x1B1C:0x0A64 | 以记录中的接口为准 | 待核对 | 未开放 |
| Corsair Virtuoso XT USB 0x1B1C:0x0A62 | 以记录中的接口为准 | 待核对 | 未开放 |
| Corsair Virtuoso SE 无线 0x1B1C:0x0A3E | 以记录中的接口为准 | 待核对 | 未开放 |
| Corsair Virtuoso SE USB 0x1B1C:0x0A3D | 以记录中的接口为准 | 待核对 | 未开放 |
| SteelSeries Arctis 7 0x1038:0x1260 | 以记录中的接口为准 | 待核对 | 未开放 |
| SteelSeries Arctis 7 (2019) 0x1038:0x12AD | 以记录中的接口为准 | 待核对 | 未开放 |
| Razer BlackShark V2 HyperSpeed (USB cable) 0x1532:0x056E | USB 直连 | 待核对 | 未开放 |
| Razer Kraken V4 Pro (OLED dock / 2.4G) 0x1532:0x0568 | 2.4G 接收器/底座 | 待核对 | 未开放 |
| Razer Kraken V4 Pro (USB cable) 0x1532:0x0567 | USB 直连 | 待核对 | 未开放 |
| VGN N75 V2 0x391D:0x2A01 | 2.4G | 待核对 | 未开放 |
| 雷柏 VE3 0x24AE:0x7204 | 2.4G | 待核对 | 未开放 |
| 富勒 G90 NearLink 0x9978:0x7001 | 2.4G | 待核对 | 未开放 |
| 富勒 G90-N11 NearLink 0x9978:0x7000 | 2.4G | 待核对 | 未开放 |
| 富勒 G90-N11 NearLink 0x9978:0x7002 | 2.4G | 待核对 | 未开放 |
| Razer Kraken V4 (2.4G) 0x1532:0x056C | 2.4G 接收器 | 待核对 | 未开放 |
| 瓦尔基里 VK M3 接收器 0x374A:0xA313 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 瓦尔基里 VK M3 Pro 接收器 0x374A:0xA316 | 以记录中的接口为准 | 理论实现 | 未开放 |
| 无线接收器（402D） 0x3151:0x402D | 以记录中的接口为准 | 待核对 | 未开放 |
| 无线接收器（A223） 0x374A:0xA223 | 以记录中的接口为准 | 待核对 | 未开放 |
| 黑爵 AK980 系列 0x0C45:0xFEFE | 2.4G 接收器 | 理论实现 | 未开放 |
| 无线键盘（FDFD） 0x0C45:0xFDFD | 2.4G 接收器 | 理论实现 | 未开放 |
| 黑爵 AK820 MAX 0x05AC:0x024F | 2.4G 接收器 | 理论实现 | 未开放 |
| 狼蛛 F75 HE 0x0C45:0xFEFE | 2.4G | 理论实现 | 未开放 |
| 狼蛛 MINI 60 HE PRO 0x0C45:0xFEFE | 2.4G | 理论实现 | 未开放 |
| 狼蛛 MINI 68 HE PRO 0x0C45:0xFEFE | 2.4G | 理论实现 | 未开放 |
| 狼蛛 MINI60HE MAX 0x0C45:0xFEFC | 2.4G | 理论实现 | 未开放 |
| 狼蛛 MINI68 HE MAX 0x0C45:0xFEFC | 2.4G | 理论实现 | 未开放 |
| 无线键盘（502B） 0x3151:0x502B | 2.4G | 理论实现 | 未开放 |
| 罗技共享键盘接收器 | 以记录中的接口为准 | 待核对 | 未开放 |
| 觅声 白羊座Pro 0x39A5:0x3D21 | 以记录中的接口为准 | 待核对 | 未开放 |
| 觅声 天蝎底座版 0x3A95:0x3EE3 | 以记录中的接口为准 | 待核对 | 未开放 |
| 觅声 阿尔法Pro 0x39A5:0x3FF1 | 以记录中的接口为准 | 待核对 | 未开放 |
| 觅声 天蝎座 0x3206:0x7015 | 以记录中的接口为准 | 待核对 | 未开放 |
| 雷柏 MT760 0x24AE:0x1870 | 2.4G接收器 | 理论实现 | 未开放 |
| 雷柏 MT760 Mini 0x24AE:0x1871 | 2.4G接收器 | 理论实现 | 未开放 |
| 雷柏 MT560 0x24AE:0x1872 | 2.4G接收器 | 理论实现 | 未开放 |
| 雷柏 MT550 0x24AE:0x1874 | 2.4G接收器 | 理论实现 | 未开放 |
| 雷柏 MT750 0x24AE:0x1876 | 2.4G接收器 | 理论实现 | 未开放 |
| 雷柏 MT760 Infinity 0x24AE:0x186C | 2.4G接收器 | 理论实现 | 未开放 |
| RAWM HS Receiver 0x1915:0x2346 | 2.4G接收器 | 理论实现 | 未开放 |
| RAWM HS Receiver 0x1915:0x234B | 2.4G接收器 | 理论实现 | 未开放 |
| RAWM Receiver 0x1915:0x233C | 2.4G接收器 | 理论实现 | 未开放 |
| RAWM HS Receiver 0x1915:0x233B | 2.4G接收器 | 理论实现 | 未开放 |
| RAWM DH8 0x1915:0x2336 | 2.4G接收器 | 理论实现 | 未开放 |
| RAWM USB Receiver 8Kn 0x1915:0x2335 | 2.4G接收器 | 理论实现 | 未开放 |
| RAWM USB Receiver 0x1915:0x232A | 2.4G接收器 | 理论实现 | 未开放 |
| RAWM USB Receiver 4K 0x1915:0x232B | 2.4G接收器 | 理论实现 | 未开放 |
| RAWM ES21Pro 配对身份 ES21Pro / 0x2352 | 2.4G配对鼠标 | 理论实现 | 未开放 |
| RAWM ES21Pro(AkamiKarubi Model) 配对身份 ES21Pro(AkamiKarubi Model) / 0x2349 | 2.4G配对鼠标 | 理论实现 | 未开放 |
| RAWM G11Pro 配对身份 G11Pro / 0x2344 | 2.4G配对鼠标 | 理论实现 | 未开放 |
| RAWM G11Plus 配对身份 G11Plus / 0x2343 | 2.4G配对鼠标 | 理论实现 | 未开放 |
| RAWM LEVIATHAN V4 配对身份 LEVIATHAN V4 / 0x234A | 2.4G配对鼠标 | 理论实现 | 未开放 |
| RAWM ES11 配对身份 ES11 / 0x2341 | 2.4G配对鼠标 | 理论实现 | 未开放 |
| RAWM SA-SL01C 配对身份 SA-SL01C / 0x2340 | 2.4G配对鼠标 | 理论实现 | 未开放 |
| RAWM ER21 配对身份 ER21 / 0x233F | 2.4G配对鼠标 | 理论实现 | 未开放 |
| RAWM SA-SH01S 配对身份 SA-SH01S / 0x233E | 2.4G配对鼠标 | 理论实现 | 未开放 |
| RAWM G11 配对身份 G11 / 0x233A | 2.4G配对鼠标 | 理论实现 | 未开放 |
| RAWM ES21 配对身份 ES21 / 0x2333 | 2.4G配对鼠标 | 理论实现 | 未开放 |
| RAWM ER21M 配对身份 ER21M / 0x2339 | 2.4G配对鼠标 | 理论实现 | 未开放 |
| RAWM ER21Pro 配对身份 ER21Pro / 0x2338 | 2.4G配对鼠标 | 理论实现 | 未开放 |
| RAWM ES21M 配对身份 ES21M / 0x2337 | 2.4G配对鼠标 | 理论实现 | 未开放 |
| RAWM ES21Pro 配对身份 ES21Pro / 0x2334 | 2.4G配对鼠标 | 理论实现 | 未开放 |
| RAWM SA-MH01Pro 配对身份 SA-MH01Pro / 0x2331 | 2.4G配对鼠标 | 理论实现 | 未开放 |
| RAWM SA-SH01Pro 配对身份 SA-SH01Pro / 0x2332 | 2.4G配对鼠标 | 理论实现 | 未开放 |
| RAWM SA-SH01 配对身份 SA-SH01 / 0x232E | 2.4G配对鼠标 | 理论实现 | 未开放 |
| RAWM SA-SL01 配对身份 SA-SL01 / 0x232D | 2.4G配对鼠标 | 理论实现 | 未开放 |
| RAWM SA-MH01 配对身份 SA-MH01 / 0x232C | 2.4G配对鼠标 | 理论实现 | 未开放 |
| RAWM SA-ML01 配对身份 SA-ML01 / 0x2329 | 2.4G配对鼠标 | 理论实现 | 未开放 |
| Ninjutso Sora V3 接收器 0x093A:0xEB02 | 接收器 | 该行仅列 DPI | 待核对 |
| Ninjutso Sora V3 0x093A:0xE010 | 鼠标直连 | 该行仅列 DPI | 待核对 |
| Ninjutso NinjaForce 接收器 0x093A:0xEB01 | 接收器 | 该行仅列 DPI | 待核对 |
| Ninjutso NinjaForce 鼠标 0x093A:0xE020 | 鼠标直连 | 该行仅列 DPI | 待核对 |
| Ninjutso NinjaForce 接收器 0x093A:0xEB03 | 接收器 | 该行仅列 DPI | 待核对 |
| Ninjutso NinjaForce 鼠标 0x093A:0xE021 | 鼠标直连 | 该行仅列 DPI | 待核对 |
| Ninjutso Sora 4K USB 0x3554:0xF51A | 鼠标直连 | 该行仅列 DPI | 待核对 |
| Ninjutso Sora 4K receiver 0x3554:0xF51B | 接收器 | 该行仅列 DPI | 待核对 |
| Pulsar X2A Wired 0x3710:0x1404 | 鼠标直连 | 该行仅列 DPI | 待核对 |
| Pulsar X2H Wired 0x3710:0x1403 | 鼠标直连 | 该行仅列 DPI | 待核对 |
| Pulsar X2 V2 Wired 0x3710:0x1402 | 鼠标直连 | 该行仅列 DPI | 待核对 |
| Pulsar Xlite V3 Wired 0x3710:0x1401 | 鼠标直连 | 该行仅列 DPI | 待核对 |
| Pulsar X2A V3 0x3710:0x3404 | 鼠标直连 | 该行仅列 DPI | 待核对 |
| Pulsar Xlite V4 0x3710:0x3401 | 鼠标直连 | 该行仅列 DPI | 待核对 |
| Pulsar X2H V3 0x3710:0x3403 | 鼠标直连 | 该行仅列 DPI | 待核对 |
| Pulsar X2 V3 0x3710:0x3402 | 鼠标直连 | 该行仅列 DPI | 待核对 |
| Pulsar Feinmann F01 0x3710:0x3413 | 鼠标直连 | 该行仅列 DPI | 待核对 |
| Pulsar X3 0x3710:0x3409 | 鼠标直连 | 该行仅列 DPI | 待核对 |
| Pulsar X3 LHD 0x3710:0x3410 | 鼠标直连 | 该行仅列 DPI | 待核对 |
| Pulsar Fusion receiver 0x3710:0x5402 | 接收器 | 该行仅列 DPI | 待核对 |
| Pulsar Fusion receiver 0x3710:0x5403 | 接收器 | 该行仅列 DPI | 待核对 |
| Pulsar Fusion receiver 0x3710:0x5404 | 接收器 | 该行仅列 DPI | 待核对 |
| Pulsar eS Xlite 0x3710:0x9604 | 鼠标直连 | 该行仅列 DPI | 待核对 |
| Pulsar eS receiver 0x3710:0x5602 | 接收器 | 该行仅列 DPI | 待核对 |
| Pulsar vMouse mouse 0x3710:0x9605 | 鼠标直连 | 该行仅列 DPI | 待核对 |
| Pulsar ZywOo receiver 0x3710:0x5405 | 接收器 | 该行仅列 DPI | 待核对 |
| Pulsar ZywOo 0x3710:0x7401 | 鼠标直连 | 该行仅列 DPI | 待核对 |
| Pulsar X2 CrazyLight Mini 配对身份 87:1 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Mini 配对身份 87:2 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Mini 配对身份 87:3 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Mini 配对身份 87:4 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Mini 配对身份 87:5 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Mini 配对身份 87:6 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Mini 配对身份 87:9 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Mini 配对身份 87:10 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Mini T1 Edition 配对身份 87:12 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Mini T1 Edition 配对身份 87:13 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Mini PRX Edition 配对身份 87:14 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Mini Boardzy Edition 配对身份 87:15 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Mini Randomfrankp Edition 配对身份 87:16 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight 5th Anniversary Edition 配对身份 87:41 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight WildScape Desert Edition 配对身份 87:42 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Mini Forest 配对身份 87:43 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Mini WildScape Ocean Edition 配对身份 87:44 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Mini Pokemon Pikachu Edition 配对身份 87:85 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Mini Pokemon Mew-Two FF Edition 配对身份 87:95 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Mini PRX Pacific Gold Edition 配对身份 87:96 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Mini Shibuki 配对身份 87:130 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Mini Yuni 配对身份 87:131 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Mini Mashiro 配对身份 87:132 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar Xlite CrazyLight Mini 配对身份 87:17 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar Xlite CrazyLight Mini 配对身份 87:18 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar Xlite CrazyLight Rock 配对身份 87:116 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X3 CrazyLight Mini 配对身份 87:19 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X3 CrazyLight Mini 配对身份 87:20 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X3 LHD CrazyLight Mini 配对身份 87:21 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X3 LHD CrazyLight Mini 配对身份 87:22 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2H CrazyLight Mini 配对身份 87:23 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2H CrazyLight Mini 配对身份 87:24 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2H CrazyLight Volcano 配对身份 87:117 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar Xlite CrazyLight Medium Rock 配对身份 87:118 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2H CrazyLight Medium Volcano 配对身份 87:119 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2N CrazyLight Mini 配对身份 87:25 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2N CrazyLight Mini 配对身份 87:26 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 Mini Pulsar By You XS-1 配对身份 87:28 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2H Medium Pulsar By You XS-1 配对身份 87:30 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2A Medium Pulsar By You XS-1 配对身份 87:31 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2A Mini Pulsar By You XS-1 配对身份 87:32 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar Xlite Mini Pulsar By You XS-1 配对身份 87:33 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar Xlite Large Pulsar By You XS-1 配对身份 87:35 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2H CrazyLight Medium 配对身份 87:29 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2H CrazyLight Medium 配对身份 87:76 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar Xlite CrazyLight Medium 配对身份 87:34 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar Xlite CrazyLight Medium 配对身份 87:71 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar TenZ Signature Edition 配对身份 87:7 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar TenZ 配对身份 87:8 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar TenZ Signature RED 配对身份 87:37 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar TenZ Signature Edition 配对身份 87:40 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar JV-X Pro Series 配对身份 87:38 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Aizawa Ema 配对身份 87:11 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Asumi Sena 配对身份 87:45 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Hanabusa Lisa 配对身份 87:46 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Ichinose Uruha 配对身份 87:47 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Kaga Sumire 配对身份 87:48 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Kisaragi Ren 配对身份 87:49 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Kogara Toto 配对身份 87:50 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Komori Met 配对身份 87:51 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Kurumi Noah 配对身份 87:52 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Kaga Nazuna 配对身份 87:53 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Nekota Tsuna 配对身份 87:54 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Kaminari Qpi 配对身份 87:55 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Sendo Yuuhi 配对身份 87:56 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Shinomiya Runa 配对身份 87:57 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Shiranami Ramune 配对身份 87:58 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Tachibana Hinano 配对身份 87:59 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Tosaki Mimi 配对身份 87:60 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Tsumugi Kokage 配对身份 87:61 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Yakumo Beni 配对身份 87:62 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Yano Kuromu 配对身份 87:63 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Yumeno Akari 配对身份 87:64 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Amayui Moka 配对身份 87:65 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X VSPO! Choya Hanabi 配对身份 87:66 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar Susanto-X Pro Series 配对身份 87:39 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar Lab. X2F 配对身份 87:36 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar Lab. X2F 5th Anniversary Edition 配对身份 87:67 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar ZywOo The Chosen Mouse Gen.2 配对身份 87:68 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar ZywOo The Chosen Mouse Gen.2 配对身份 87:69 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar ZywOo The Chosen Mouse Gen.2 配对身份 87:100 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar ZywOo The Chosen Mouse Gen.3 Mini 配对身份 87:133 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar ZywOo The Chosen Mouse Gen.3 Mini 配对身份 87:134 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar ZywOo The Chosen Mouse Gen.3 Medium 配对身份 87:135 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar ZywOo The Chosen Mouse Gen.3 Medium 配对身份 87:136 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Medium 配对身份 87:27 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Medium 配对身份 87:70 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Medium The Finals Edition 配对身份 87:79 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Medium THE GRAND MAJOR 2025 Edition 配对身份 87:80 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Medium Bruce Lee 85th Edition 配对身份 87:81 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Medium PRX Pacific Gold Edition 配对身份 87:83 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Medium Pokemon Pikachu Edition 配对身份 87:84 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Mini Pokemon Mew-Two FF Edition 配对身份 87:86 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Medium Forest 配对身份 87:109 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X3 CrazyLight Medium 配对身份 87:72 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X3 CrazyLight Medium 配对身份 87:73 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2N CrazyLight Medium 配对身份 87:77 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2N CrazyLight Medium 配对身份 87:78 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Mini Bruce Lee 85th Edition 配对身份 87:82 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Medium BlueArchive Hoshino Edition 配对身份 87:87 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Medium BlueArchive Shiroko Edition 配对身份 87:88 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Medium BlueArchive Nonomi Edition 配对身份 87:89 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight Medium BlueArchive Hoshino FF Edition 配对身份 87:90 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X3 CrazyLight Desert 配对身份 87:102 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X3 CrazyLight Medium Desert 配对身份 87:103 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2N CrazyLight Mini Ocean 配对身份 87:105 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2N CrazyLight Medium Ocean 配对身份 87:106 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X3 LHD CrazyLight Medium 配对身份 87:74 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X3 LHD CrazyLight Medium 配对身份 87:75 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X3 LHD CrazyLight Medium Desert 配对身份 87:104 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar Feinmann F01 Noctua Edition 配对身份 87:101 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar TenZ 2.0 Mini Misty Blue 配对身份 87:107 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar TenZ Signature Edition 2.0 Mini 配对身份 87:110 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar TenZ Signature Edition 2.0 Medium 配对身份 87:122 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar TenZ 2.0 Medium Misty Blue 配对身份 87:123 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar CID87 MID108 配对身份 87:108 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar CID87 MID113 配对身份 87:113 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar CID87 MID115 配对身份 87:115 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar CID87 MID121 配对身份 87:121 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2F CrazyLight Black 配对身份 87:126 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar CID87 MID137 配对身份 87:137 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar CID87 MID138 配对身份 87:138 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar CID87 MID139 配对身份 87:139 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar CID87 MID140 配对身份 87:140 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar CID87 MID142 配对身份 87:142 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar CID87 MID143 配对身份 87:143 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar CID87 MID144 配对身份 87:144 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar CID87 MID145 配对身份 87:145 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight 2 Mini 2026 PRX Edition 配对身份 87:146 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2 CrazyLight 2 Medium 2026 PRX Edition 配对身份 87:147 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar CID87 MID148 配对身份 87:148 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar CID87 MID149 配对身份 87:149 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar CID87 MID111 配对身份 87:111 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar CID87 MID141 配对身份 87:141 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar BUZZ-X Pro Series - MOON GRAY 配对身份 87:92 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar CARPE-X Pro Series - CARP ORANGE 配对身份 87:94 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar eS FS-1 Medium. Wireless Gaming mouse 配对身份 87:91 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar eS FS-1 GT Silver 配对身份 87:127 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar eS FS-1 Tournament Red 配对身份 87:128 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar eS FS-1 Tournament Blue 配对身份 87:129 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar STA-X Pro Series - STEALTH BLACK 配对身份 87:93 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2H CrazyLight Mini BadSeedTech Edition 配对身份 87:97 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar X2H CrazyLight Medium BadSeedTech Edition 配对身份 87:98 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar Jinggg-X Pro Series 配对身份 87:99 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar Susanto-X 2.0 Large Black 配对身份 87:112 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar CID87 MID114 配对身份 87:114 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar Susanto-X 2.0 Large Phantom Jungle 配对身份 87:120 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar Susanto-X 2.0 Medium Phantom Jungle 配对身份 87:124 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar Susanto-X 2.0 Medium Black 配对身份 87:125 | 对应协议有线或接收器 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar Xlite Wireless 配对身份 6:1 | 对应协议有线或接收器 | 该行仅列 DPI | 待核对 |
| Pulsar Xlite Mini Wireless 配对身份 6:2 | 对应协议有线或接收器 | 该行仅列 DPI | 待核对 |
| Pulsar X2 Mini Wireless 配对身份 6:3 | 对应协议有线或接收器 | 该行仅列 DPI | 待核对 |
| Pulsar X2 Wireless 配对身份 6:4 | 对应协议有线或接收器 | 该行仅列 DPI | 待核对 |
| Pulsar X2 Mini Bruce Lee 配对身份 6:5 | 对应协议有线或接收器 | 该行仅列 DPI | 待核对 |
| Pulsar X2 Bruce Lee 配对身份 6:6 | 对应协议有线或接收器 | 该行仅列 DPI | 待核对 |
| Pulsar Xlite V3 配对身份 6:7 | 对应协议有线或接收器 | 该行仅列 DPI | 待核对 |
| Pulsar X2A Wireless 配对身份 6:8 | 对应协议有线或接收器 | 该行仅列 DPI | 待核对 |
| Pulsar X2A Mini Wireless 配对身份 6:9 | 对应协议有线或接收器 | 该行仅列 DPI | 待核对 |
| Pulsar X2V2 Wireless 配对身份 6:10 | 对应协议有线或接收器 | 该行仅列 DPI | 待核对 |
| Pulsar X2V2 Mini Wireless 配对身份 6:11 | 对应协议有线或接收器 | 该行仅列 DPI | 待核对 |
| Pulsar X2H Wireless 配对身份 6:12 | 对应协议有线或接收器 | 该行仅列 DPI | 待核对 |
| Pulsar X2H Mini Wireless 配对身份 6:13 | 对应协议有线或接收器 | 该行仅列 DPI | 待核对 |
| Pulsar Xlite V3 Mini 配对身份 6:14 | 对应协议有线或接收器 | 该行仅列 DPI | 待核对 |
| Pulsar Xlite V3 Large 配对身份 6:15 | 对应协议有线或接收器 | 该行仅列 DPI | 待核对 |
| Pulsar Xlite V3 Custom Optical 配对身份 6:16 | 对应协议有线或接收器 | 该行仅列 DPI | 待核对 |
| Pulsar Xlite V3 Custom Mech 配对身份 6:17 | 对应协议有线或接收器 | 该行仅列 DPI | 待核对 |
| Pulsar X2 Custom Optical 配对身份 6:18 | 对应协议有线或接收器 | 该行仅列 DPI | 待核对 |
| Pulsar X2 Custom Mech 配对身份 6:19 | 对应协议有线或接收器 | 该行仅列 DPI | 待核对 |
| Pulsar X2 Custom Optical 配对身份 6:20 | 对应协议有线或接收器 | 该行仅列 DPI | 待核对 |
| Pulsar X2 Custom Mech 配对身份 6:21 | 对应协议有线或接收器 | 该行仅列 DPI | 待核对 |
| Pulsar cMouse 接口 0x3710:0x3414 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3415 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3501 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3502 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3503 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3504 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3505 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3506 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3507 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3508 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3509 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3510 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3511 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3512 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3513 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3514 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3515 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3516 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3517 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3518 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3519 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3520 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3521 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3522 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3523 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3524 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3525 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3526 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3527 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3528 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3529 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3601 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3603 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3604 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3605 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3606 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3607 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3608 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3609 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3610 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x3611 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x5406 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x5407 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x5501 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x5502 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x5503 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x5504 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x5601 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x5603 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x7501 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x7502 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x7503 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x7504 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x7505 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x7506 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x7507 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x7508 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x7509 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x7510 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x7601 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x7602 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x7603 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x7604 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x7605 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x9502 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x9503 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0x9601 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0xF004 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar cMouse 接口 0x3710:0xF005 | 须实际配对确认 | 该行仅列 DPI | 理论实现（读／切／改） |
| Pulsar Nordic 系列 0x3554:0xF507 | wired | 该行仅列 DPI | 待核对 |
| Pulsar Nordic 系列 0x3554:0xF508 | receiver | 该行仅列 DPI | 待核对 |
| Pulsar Nordic 系列 0x3554:0xF509 | receiver | 该行仅列 DPI | 待核对 |
| Pulsar X2 Areson 0x25A7:0xFA7B | wired | 该行仅列 DPI | 待核对 |
| Pulsar X2 Areson 0x25A7:0xFA7C | receiver | 该行仅列 DPI | 待核对 |
| AULA L9 配对身份 38A6:28F7 | 已登记 FFA8 接收器 | 该行仅列 DPI | 待核对 |
| AULA L9 Ultra 配对身份 38A6:28FB | 已登记 FFA8 接收器 | 该行仅列 DPI | 待核对 |
| AULA SC800 Max 配对身份 38A6:2EE0 | 已登记 FFA8 接收器 | 该行仅列 DPI | 待核对 |
| AULA SC880Ultra 配对身份 38A6:28FF | 已登记 FFA8 接收器 | 该行仅列 DPI | 待核对 |
| AULA SC880MAX 配对身份 38A6:28E7 | 已登记 FFA8 接收器 | 该行仅列 DPI | 待核对 |
| AULA SC900V2 Ultra 配对身份 38A6:2EF4 | 已登记 FFA8 接收器 | 该行仅列 DPI | 待核对 |
| AULA SC900V2 Pro 配对身份 38A6:2EFE | 已登记 FFA8 接收器 | 该行仅列 DPI | 待核对 |
| AULA SC900V2 MAX 配对身份 38A6:2EF6 | 已登记 FFA8 接收器 | 该行仅列 DPI | 待核对 |
| AULA V9Ultra+ 配对身份 38A6:2EEE | 已登记 FFA8 接收器 | 该行仅列 DPI | 待核对 |
| AULA V9Ultra 配对身份 38A6:2EF2 | 已登记 FFA8 接收器 | 该行仅列 DPI | 待核对 |
| AULA X9Ultra 配对身份 38A6:2F14 | 已登记 FFA8 接收器 | 该行仅列 DPI | 待核对 |
| AULA 昆仑 X5 MAX 配对身份 38A6:28E8 | 已登记 FFA8 接收器 | 该行仅列 DPI | 待核对 |
| AULA 昆仑 X5 Ultra 配对身份 38A6:2F08 | 已登记 FFA8 接收器 | 该行仅列 DPI | 待核对 |
| AULA L5 Ultra 配对身份 38A6:2F0C | 已登记 FFA8 接收器 | 该行仅列 DPI | 待核对 |
| AULA 昆仑 X5 MINI 配对身份 38A6:2F10 | 已登记 FFA8 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 A8 WB Ultra 0x3554:0xFB36 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 A8 WB Ultra 0x3554:0xFB35 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 A8 WB Ultra 0x3554:0xFB36 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 A8 WB Ultra 0x3554:0xFB35 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 S9 Pro v2 0x3554:0xF515 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 S9 Pro v2 0x3554:0xF517 | 接收器 | 该行仅列 DPI | 理论实现（按身份读改） |
| 前行者 S9 Ultimate 0x3554:0xFB47 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 S9 Ultimate 0x3554:0xFB45 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 S9 Ultimate 0x3554:0xFB48 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 S9 Ultra v2 0x3554:0xF515 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 S9 Ultra v2 0x3554:0xF517 | 接收器 | 该行仅列 DPI | 理论实现（按身份读改） |
| 前行者 S9V2 Master 0x3554:0xF515 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 S9V2 Master 0x3554:0xF517 | 接收器 | 该行仅列 DPI | 理论实现（按身份读改） |
| 前行者 S9V2 Master+ 0x3554:0xF515 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 S9V2 Master+ 0x3554:0xF517 | 接收器 | 该行仅列 DPI | 理论实现（按身份读改） |
| 前行者 S9V2 Turbo 0x3554:0xFB36 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 S9V2 Turbo 0x3554:0xFB35 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 S9V2 Ultimate 0x3554:0xF515 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 S9V2 Ultimate 0x3554:0xF517 | 接收器 | 该行仅列 DPI | 理论实现（按身份读改） |
| 前行者 X23 Zhuoyue 0x3554:0xFB36 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 X23 Zhuoyue 0x3554:0xFB35 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 Z7 0x3554:0xF515 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 Z7 0x3554:0xF517 | 接收器 | 该行仅列 DPI | 理论实现（按身份读改） |
| 前行者 Z7 Master 0x3554:0xF515 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 Z7 Master 0x3554:0xF517 | 接收器 | 该行仅列 DPI | 理论实现（按身份读改） |
| 前行者 X23 Max 0x3554:0xF5A0 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 X23 Max 0x3554:0xF5A0 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 X23 Max 0x3554:0xF59F | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 X23 Max 0x3554:0xF59F | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 X23 Max 0x3554:0xF59F | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 X23 Max 0x3554:0xF59F | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 X23 Max 0x3554:0xF5B2 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 X23 Max 0x3554:0xF5B2 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 X23 Max 0x3554:0xF5B2 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 X23 Max 0x3554:0xF5B2 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 X23 Max 0x3554:0xF5B3 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 X23 Max 0x3554:0xF5B3 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 X23 Max 0x3554:0xF5B5 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 X23 Max 0x3554:0xF5B5 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 X23 Max 0x3554:0xF5B5 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 X23 Max 0x3554:0xF5B5 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 S7 Max 0x372E:0x107C | 有线直连 | 理论实现（电量百分比） | 待研究 |
| 前行者 S7 Max 0x372E:0x103D | 接收器 | 理论实现（电量百分比） | 待研究 |
| 前行者 S7 Max 0x372E:0x106E | 接收器 | 理论实现（电量百分比） | 待研究 |
| 前行者 S7 Max 0x372E:0x1026 | 接收器 | 理论实现（电量百分比） | 待研究 |
| 前行者 S7 Pro 0x372E:0x107B | 有线直连 | 理论实现（电量百分比） | 待研究 |
| 前行者 S7 Pro 0x372E:0x103D | 接收器 | 理论实现（电量百分比） | 待研究 |
| 前行者 S7 Pro 0x372E:0x1026 | 接收器 | 理论实现（电量百分比） | 待研究 |
| 前行者 S7 Ultra 0x372E:0x108A | 有线直连 | 理论实现（电量百分比） | 待研究 |
| 前行者 S7 Ultra 0x372E:0x103D | 接收器 | 理论实现（电量百分比） | 待研究 |
| 前行者 S7 Ultra 0x372E:0x106E | 接收器 | 理论实现（电量百分比） | 待研究 |
| 前行者 S7 Ultra 0x372E:0x1026 | 接收器 | 理论实现（电量百分比） | 待研究 |
| 前行者 S9 Max 0x372E:0x105E | 有线直连 | 理论实现（电量百分比） | 待研究 |
| 前行者 S9 Max 0x372E:0x1026 | 接收器 | 理论实现（电量百分比） | 待研究 |
| 前行者 S9 Max 0x372E:0x103D | 接收器 | 理论实现（电量百分比） | 待研究 |
| 前行者 S9 Pro 0x372E:0x1029 | 有线直连 | 理论实现（电量百分比） | 待研究 |
| 前行者 S9 Pro 0x372E:0x1026 | 接收器 | 理论实现（电量百分比） | 待研究 |
| 前行者 S9 Pro 0x372E:0x103D | 接收器 | 理论实现（电量百分比） | 待研究 |
| 前行者 S9 Ultra 0x372E:0x1027 | 有线直连 | 理论实现（电量百分比） | 待研究 |
| 前行者 S9 Ultra 0x372E:0x1026 | 接收器 | 理论实现（电量百分比） | 待研究 |
| 前行者 S9 Ultra 0x372E:0x103D | 接收器 | 理论实现（电量百分比） | 待研究 |
| 前行者 S9 Ultra+ 0x372E:0x108D | 有线直连 | 理论实现（电量百分比） | 待研究 |
| 前行者 S9 Ultra+ 0x372E:0x1026 | 接收器 | 理论实现（电量百分比） | 待研究 |
| 前行者 S9 Ultra+ 0x372E:0x103D | 接收器 | 理论实现（电量百分比） | 待研究 |
| 前行者 A6 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 A6 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 A6 Max 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 A6 Max 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 A6 Pro 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 A6 Pro 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 A7 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 A7 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 A7 Pro 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 A7 Pro 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 A7 SE 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 A7 SE 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 E11 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 E11 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 E11 Pro 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 E11 Pro 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 E7 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 E7 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 E7 Pro 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 E7 Pro 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 E9 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 E9 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 E9 Carbon 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 E9 Carbon 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 E9 Pro Carbon 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 E9 Pro Carbon 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 G2SE A 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 G2SE A 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 S2 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 S2 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 S7 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 S7 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 S7 Plus 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 S7 Plus 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 S7 SE 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 S7 SE 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 S9 SE 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 S9 SE 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 X23 Carbon 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 X23 Carbon 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 X23 Carbon 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 X23 Carbon 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 X23 Max 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 X23 Max 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 X23 SE 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 X23 SE 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 X23 星闪 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 X23 星闪 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 X23L Carbon 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 X23L Carbon 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 X23Pro 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 X23Pro 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 X23Pro 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 X23Pro 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 X23SE 0xA8A4:0x2255 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 X23SE 0xA8A5:0x2255 | 接收器 | 该行仅列 DPI | 待核对 |
| 前行者 Z7 Max 0x1209:0x5A86 | 有线直连 | 该行仅列 DPI | 待核对 |
| 前行者 Z7 Max 0x1209:0x5A87 | 接收器 | 该行仅列 DPI | 待核对 |
| VT0 0x24AE:0x1410 | 接收器 | 该行仅列 DPI | 待核对 |
| VT3S 0x24AE:0x1411 | 接收器 | 该行仅列 DPI | 待核对 |
| VT3 0x24AE:0x1412 | 接收器 | 该行仅列 DPI | 待核对 |
| VT7 0x24AE:0x1413 | 接收器 | 该行仅列 DPI | 待核对 |
| VT9 0x24AE:0x1414 | 接收器 | 该行仅列 DPI | 待核对 |
| VT1 0x24AE:0x1415 | 接收器 | 该行仅列 DPI | 待核对 |
| VT7MAX 0x24AE:0x1416 | 接收器 | 该行仅列 DPI | 待核对 |
| VT3MAX 0x24AE:0x1417 | 接收器 | 该行仅列 DPI | 待核对 |
| VT9MAX 0x24AE:0x1418 | 接收器 | 该行仅列 DPI | 待核对 |
| VT7Air 0x24AE:0x1419 | 接收器 | 该行仅列 DPI | 待核对 |
| VT3Air 0x24AE:0x141A | 接收器 | 该行仅列 DPI | 待核对 |
| VT9Air 0x24AE:0x141B | 接收器 | 该行仅列 DPI | 待核对 |
| VT7Air MAX 0x24AE:0x141C | 接收器 | 该行仅列 DPI | 待核对 |
| VT3Air MAX 0x24AE:0x141D | 接收器 | 该行仅列 DPI | 待核对 |
| VT9Air MAX 0x24AE:0x141E | 接收器 | 该行仅列 DPI | 待核对 |
| VT0 MAX 0x24AE:0x1420 | 接收器 | 该行仅列 DPI | 待核对 |
| VT3S MAX 0x24AE:0x1421 | 接收器 | 该行仅列 DPI | 待核对 |
| VT2 0x24AE:0x1422 | 接收器 | 该行仅列 DPI | 待核对 |
| VT2 MAX 0x24AE:0x1423 | 接收器 | 该行仅列 DPI | 待核对 |
| VT1 MAX 0x24AE:0x1444 | 接收器 | 该行仅列 DPI | 待核对 |
| VT0Air MAX MASTER 0x24AE:0x1447 | 接收器 | 该行仅列 DPI | 待核对 |
| VT0 MAX MASTER 0x24AE:0x144A | 接收器 | 该行仅列 DPI | 待核对 |
| VT3S MAX MASTER 0x24AE:0x144B | 接收器 | 该行仅列 DPI | 待核对 |
| VT6 0x24AE:0x1451 | 接收器 | 该行仅列 DPI | 待核对 |
| VT6MAX 0x24AE:0x1452 | 接收器 | 该行仅列 DPI | 待核对 |
| VT9S V2 0x24AE:0x1453 | 接收器 | 该行仅列 DPI | 理论实现（按身份读改） |
| VT7S V2 0x24AE:0x1459 | 接收器 | 该行仅列 DPI | 待核对 |
| VT4 V2 0x24AE:0x145B | 接收器 | 该行仅列 DPI | 待核对 |
| VT2S V2 0x24AE:0x145D | 接收器 | 该行仅列 DPI | 待核对 |
| VT7 V2 0x24AE:0x1460 | 接收器 | 该行仅列 DPI | 待核对 |
| VT3 V2 0x24AE:0x1462 | 接收器 | 该行仅列 DPI | 待核对 |
| VT3S V2 0x24AE:0x1464 | 接收器 | 该行仅列 DPI | 待核对 |
| VT9 V2 0x24AE:0x1466 | 接收器 | 该行仅列 DPI | 待核对 |
| VT2 V2 0x24AE:0x1468 | 接收器 | 该行仅列 DPI | 待核对 |
| VT3 MAX MASTER V2 0x24AE:0x146D | 接收器 | 该行仅列 DPI | 待核对 |
| VT3S MAX MASTER V2 0x24AE:0x146E | 接收器 | 该行仅列 DPI | 待核对 |
| VT7 MAX MASTER V2 0x24AE:0x1470 | 接收器 | 该行仅列 DPI | 待核对 |
| VT7S MAX MASTER V2 0x24AE:0x1471 | 接收器 | 该行仅列 DPI | 待核对 |
| VT9 MAX MASTER V2 0x24AE:0x1473 | 接收器 | 该行仅列 DPI | 待核对 |
| VT9S MAX MASTER V2 0x24AE:0x1474 | 接收器 | 该行仅列 DPI | 待核对 |
| VT2S MAX MASTER V2 0x24AE:0x1476 | 接收器 | 该行仅列 DPI | 待核对 |
| VT0Air MAX 0x24AE:0x14A1 | 接收器 | 该行仅列 DPI | 待核对 |
| VT3SAir MAX 0x24AE:0x14A2 | 接收器 | 该行仅列 DPI | 待核对 |
| VT2K MAX 0x24AE:0x14A4 | 接收器 | 该行仅列 DPI | 待核对 |
| VT3PRO V2 0x24AE:0x14A5 | 接收器 | 该行仅列 DPI | 待核对 |
| VT3S PRO V2 0x24AE:0x14A6 | 接收器 | 该行仅列 DPI | 待核对 |
| VT7PRO V2 0x24AE:0x14A7 | 接收器 | 该行仅列 DPI | 待核对 |
| VT7S PRO V2 0x24AE:0x14A8 | 接收器 | 该行仅列 DPI | 待核对 |
| VT9PRO V2 0x24AE:0x14A9 | 接收器 | 该行仅列 DPI | 待核对 |
| VT9S PRO V2 0x24AE:0x14AA | 接收器 | 该行仅列 DPI | 待核对 |
| VT7K Air MAX V2 0x24AE:0x14AB | 接收器 | 该行仅列 DPI | 待核对 |
| VT2K Air MAX V2 0x24AE:0x14AC | 接收器 | 该行仅列 DPI | 待核对 |
| VT3Air MAX V2 0x24AE:0x1B0C | 接收器 | 该行仅列 DPI | 待核对 |
| VT3SAir MAX V2 0x24AE:0x1B0D | 接收器 | 该行仅列 DPI | 待核对 |
| VT7Air MAX V2 0x24AE:0x1B0E | 接收器 | 该行仅列 DPI | 待核对 |
| VT7SAir MAX V2 0x24AE:0x1B0F | 接收器 | 该行仅列 DPI | 待核对 |
| VT9Air MAX V2 0x24AE:0x1B10 | 接收器 | 该行仅列 DPI | 待核对 |
| VT9SAir MAX V2 0x24AE:0x1B11 | 接收器 | 该行仅列 DPI | 待核对 |
| VXE R1S+ 0x373B:0x1084 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE V3 NK 0x373B:0x10E7 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 SE NK 0x373B:0x1135 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK U2 SE NK 0x373B:0x1149 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK Y9 SE NK 0x373B:0x115D | 有线直连 | 该行仅列 DPI | 待核对 |
| DRAGONFLY F1 V2 SE NK 0x373B:0x114C | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1 S NK 0x373B:0x121F | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE X3 0x373B:0x1248 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK Dragonfly F1 PRO 0x3554:0xF502 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK Dragonfly F1 PRO MAX JOJO 0x3554:0xF506 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK Dragonfly Dragonfly F1S 0x3554:0xF50A | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1 PRO MAX 0x3554:0xF58C | 有线直连 | 该行仅列 DPI | 理论实现（按身份读改） |
| VXE R1SE+ 0x3554:0xF58F | 有线直连 | 该行仅列 DPI | 理论实现（按身份读改） |
| MAD R 0x373B:0x103F | 有线直连 | 该行仅列 DPI | 待核对 |
| MAD R + 0x373B:0x104B | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 Lite 0x373B:0x113E | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 S NK 0x373B:0x1106 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Plus NK 0x373B:0x1115 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK U2 Plus NK 0x373B:0x110D | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK Y9 Plus NK 0x373B:0x115B | 有线直连 | 该行仅列 DPI | 待核对 |
| DRAGONFLY F1 V2 Plus NK 0x373B:0x114A | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1 UlTRA 0x373B:0x11D2 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1SE+ Silent 0x373B:0x12BF | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 NK 0x373B:0x1116 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1E 0x373B:0x113F | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 0x373B:0x1157 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK U2 0x373B:0x11AA | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 SE NK 0x373B:0x1107 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1 NK 0x373B:0x1065 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1 NK 0x373B:0x1066 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1 NK 0x373B:0x10C7 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1 NK 0x373B:0x10C8 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE V3 PRO NK 0x373B:0x10E8 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK U2 NK 0x373B:0x110E | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK Y9 NK 0x373B:0x115C | 有线直连 | 该行仅列 DPI | 待核对 |
| DRAGONFLY F1 V2 NK 0x373B:0x114B | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 X 0x373B:0x11A0 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE X3 Pro 0x373B:0x1249 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE X3 Turbo 0x373B:0x124A | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 NB 0x373B:0x1245 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Pro Max 0x373B:0x1117 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK U2 Pro Max 0x373B:0x1158 | 有线直连 | 该行仅列 DPI | 待核对 |
| DRAGONFLY F1 V2 PRO MAX 0x373B:0x11AB | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultra Max 2.0 0x373B:0x11DD | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultra Max 2.0 Tri-Mode 0x373B:0x11F2 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 Ultra Max 2.0 0x373B:0x11ED | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 Ultra Max 2.0 0x373B:0x11EC | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK U2 Ultra Max 2.0 0x373B:0x11F7 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1 Ultra 0x373B:0x11F9 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 PRO 0x373B:0x1014 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 PRO MAX 0x373B:0x1015 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 PRO 0x373B:0x1016 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 PRO MAX 0x373B:0x1017 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK Z1 PRO 0x373B:0x1018 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK Z1 PRO MAX 0x373B:0x1019 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK U2 MINI 0x373B:0x1086 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK U2 Ultra 0x373B:0x10AC | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK Y9 PRO MAX 0x373B:0x10FD | 有线直连 | 该行仅列 DPI | 待核对 |
| MAD R MAJOR 0x373B:0x103E | 有线直连 | 该行仅列 DPI | 待核对 |
| MAD R MAJOR+ 0x373B:0x104C | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 Ultra Max 2.0 0x373B:0x12FF | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 Ultimate 0x373B:0x102E | 有线直连 | 该行仅列 DPI | 理论实现（按身份读改） |
| ATK X1 Ultra 0x373B:0x102F | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK FIERCE X 0x373B:0x1030 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 Ultra 0x373B:0x103C | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 Ultimate 0x373B:0x103D | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 EXTREME 0x373B:0x1045 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK Z1 Ultra 0x373B:0x104A | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 LEVIATAN 0x373B:0x105B | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK U2 Ultimate 0x373B:0x1087 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 EXTREME 0x373B:0x10E9 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultra 0x373B:0x1119 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 NB ULTRA 0x373B:0x11F3 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultimate 0x373B:0x11B6 | 有线直连 | 该行仅列 DPI | 理论实现（按身份读改） |
| ATK A9 Air 0x373B:0x120A | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK Y9 Ultimate 0x373B:0x11CF | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 Ultimate 2.0 0x373B:0x11E4 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 Ultimate 2.0 0x373B:0x11E7 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK Duckbill Ultra 0x373B:0x11A6 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK Duckbill EXTREME 0x373B:0x1230 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK ZERO EX 0x373B:0x11CC | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 EXTREME 2.0 0x373B:0x11E5 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 EXTREME GT 0x373B:0x120B | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 EXTREME 2.0 0x373B:0x11E3 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 LEVIATAN 2.0 0x373B:0x11F0 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK U2 Mini 2.0 0x373B:0x11F6 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK Z1 Ultimate 2.0 0x373B:0x1201 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK U2 Ultimate 2.0 0x373B:0x11F5 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK Ghost EXTREME 0x373B:0x120D | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK Ghost Ultimate 0x373B:0x120E | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK ZERO 0x373B:0x124E | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Mini 0x373B:0x1256 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X EXTREME 0x373B:0x1266 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 Ultra Max 2.0 0x373B:0x1274 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 Ultra Max 2.0 0x373B:0x126E | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultra Max 2.0 Tri-Mode 0x373B:0x1270 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultra Max 2.0 0x373B:0x126F | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK U2 Plus NK 0x373B:0x126C | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1 0x373B:0x1271 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1 PRO 0x373B:0x1272 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1 PRO MAX 0x373B:0x1273 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Mini 0x373B:0x125A | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK U2 Pro Max 0x373B:0x126D | 有线直连 | 该行仅列 DPI | 待核对 |
| Flow 7 0x373B:0x12D7 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 V3 ULTIMATE 0x373B:0x12FA | 有线直连 | 该行仅列 DPI | 待核对 |
| MAD R 0x373B:0x132B | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 S NK 0x373B:0x131A | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Mini + 0x373B:0x1269 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Mini + 0x373B:0x128E | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK ZERO EX + 0x373B:0x1289 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 Ultimate 2.0 + 0x373B:0x1286 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 V3 EXTREME + 0x373B:0x129B | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 V3 LEVIATAN + 0x373B:0x129D | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Air + 0x373B:0x1293 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultimate + 0x373B:0x1292 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK U2 Mini 2.0 + 0x373B:0x128D | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK U2 Ultimate 2.0 + 0x373B:0x128C | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK PHANTOM ULTIMATE 0x373B:0x1296 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 EXTREME 2.0 + 0x373B:0x1285 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 Air + 0x373B:0x12A2 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK Ghost Ultimate + 0x373B:0x128B | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK Ghost EXTREME + 0x373B:0x128A | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 Ultimate 2.0 + 0x373B:0x129C | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 Air + 0x373B:0x127C | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 V3 ULTIMATE + 0x373B:0x12A7 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 NB ULTRA + 0x373B:0x12AB | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK Y9 Ultimate + 0x373B:0x12AF | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK Z1 Ultimate 2.0 + 0x373B:0x12B0 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Air + 0x373B:0x127F | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultimate + 0x373B:0x127E | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 V3 ULTIMATE + 0x373B:0x12A8 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 V3 ULTIMATE + 0x373B:0x12A9 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Wireless Charging（充电接收底座） 0x373B:0x12C6 | 充电接收底座转发 | 该行仅列 DPI | 待核对 |
| ATK X1 Ultimate 2.0 Silent 0x373B:0x12B4 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultimate Silent 0x373B:0x12B3 | 有线直连 | 该行仅列 DPI | 待核对 |
| Mouse 0x373B:0x1340 | 有线直连 | 该行仅列 DPI | 待核对 |
| Mouse 0x373B:0x1341 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 V3 PIKACHU Edition 0x373B:0x132A | 有线直连 | 该行仅列 DPI | 待核对 |
| Mouse 0x373B:0x12FB | 有线直连 | 该行仅列 DPI | 待核对 |
| Mouse 0x373B:0x133B | 有线直连 | 该行仅列 DPI | 待核对 |
| Mouse 0x373B:0x1316 | 有线直连 | 该行仅列 DPI | 待核对 |
| Mouse 0x373B:0x12F9 | 有线直连 | 该行仅列 DPI | 待核对 |
| Mouse 0x373B:0x12F7 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1SE+ 0x373B:0x1358 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1SE+ Silent 0x373B:0x1359 | 有线直连 | 该行仅列 DPI | 待核对 |
| Mouse 1K Dongle 0x3554:0xF503 | 接收器 | 该行仅列 DPI | 待核对 |
| Mouse 4K Dongle 0x3554:0xF505 | 接收器 | 该行仅列 DPI | 待核对 |
| Mouse 1K Dongle 0x3554:0xF50B | 接收器 | 该行仅列 DPI | 待核对 |
| Mouse 1K Dongle 0x3554:0xF58A | 接收器 | 该行仅列 DPI | 理论实现（按身份读改） |
| Mouse 4K Dongle 0x3554:0xF58D | 接收器 | 该行仅列 DPI | 待核对 |
| Mouse 1K Dongle 0x3554:0xF58E | 接收器 | 该行仅列 DPI | 理论实现（只读） |
| MAD 1K Dongle 0x373B:0x104D | 接收器 | 该行仅列 DPI | 待核对 |
| VXE Mouse 1K Dongle 0x3554:0xFB12 | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 1k dongle   0x373B:0x1085 | 接收器 | 该行仅列 DPI | 待核对 |
| R1 NK 0x373B:0x1067 | 接收器 | 该行仅列 DPI | 待核对 |
| R1 NK 0x373B:0x1068 | 接收器 | 该行仅列 DPI | 待核对 |
| R1 NK 0x373B:0x1069 | 接收器 | 该行仅列 DPI | 待核对 |
| R1 NK 0x373B:0x106A | 接收器 | 该行仅列 DPI | 待核对 |
| NK Mouse Dongle 0x373B:0x10C9 | 接收器 | 该行仅列 DPI | 待核对 |
| R1 NK 0x373B:0x10CA | 接收器 | 该行仅列 DPI | 待核对 |
| R1 NK 0x373B:0x10CB | 接收器 | 该行仅列 DPI | 待核对 |
| R1 NK 0x373B:0x10CC | 接收器 | 该行仅列 DPI | 待核对 |
| ATK Mouse 1K Dongle 0x373B:0x101A | 接收器 | 该行仅列 DPI | 待核对 |
| ATK Mouse 8K Dongle 0x373B:0x101B | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse -8K NANO dongle 0x373B:0x111A | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse -8K NANO dongle 0x373B:0x111B | 接收器 | 该行仅列 DPI | 待核对 |
| ATK Mouse 8K Dongle 0x373B:0x11A7 | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 4k dongle 0x373B:0x11A9 | 接收器 | 该行仅列 DPI | 待核对 |
| MAD 8K Dongle 0x373B:0x1040 | 接收器 | 该行仅列 DPI | 待核对 |
| ATK 8K Dongle Light Version 0x373B:0x1031 | 接收器 | 该行仅列 DPI | 理论实现（按身份读改） |
| ATK 8K Dongle Light Version 0x373B:0x1159 | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k dongle 0x373B:0x115E | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse -8K TypeC dongle 0x373B:0x11C8 | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse-8K NANO dongle 0x373B:0x11CE | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse -8K TypeC dongle 0x373B:0x11CD | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k dongle-H 0x373B:0x11D8 | 接收器 | 该行仅列 DPI | 理论实现（按身份读改） |
| Wireless mouse 8k dongle-L 0x373B:0x11D9 | 接收器 | 该行仅列 DPI | 理论实现（按身份读改） |
| Wireless mouse 8k dongle-L 0x373B:0x1278 | 接收器 | 该行仅列 DPI | 理论实现（按身份读改） |
| Wireless mouse 8k NANO dongle-L 0x373B:0x11FE | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k NANO dongle-L 0x373B:0x127A | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k NANO dongle-L 0x373B:0x11FD | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k NANO dongle-L 0x373B:0x1279 | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse -8K TOWER dongle 0x373B:0x11F4 | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse -1K TOWER dongle 0x373B:0x1246 | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k dongle-H 0x373B:0x12CC | 接收器 | 该行仅列 DPI | 待核对 |
| Flow 7 0x373B:0x11B3 | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k NANO dongle-M 0x373B:0x12E9 | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k dongle-M 0x373B:0x1303 | 接收器 | 该行仅列 DPI | 待核对 |
| ATK Wireless Charging dongle（充电接收底座） 0x373B:0x12DA | 充电接收底座转发 | 该行仅列 DPI | 待核对 |
| ATK Wireless Charging-S dongle（充电接收底座） 0x373B:0x1342 | 充电接收底座转发 | 该行仅列 DPI | 待核对 |
| 塞洛斯 V8 系列接收器 0x3554:0xF5F4 | 接收器 | 该行仅列 DPI | 待核对 |
| 塞洛斯 V8 系列接收器 0x3554:0xF5F7 | 接收器 | 该行仅列 DPI | 待核对 |
| 塞洛斯 V8 系列 0x3554:0xF5F6 | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU Crystal receiver 0x3554:0xF50D | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU Crystal receiver 0x3554:0xF510 | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU Crystal wired 0x3554:0xF50F | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1 S NK 0x373B:0x124D | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 SE NK 0x373B:0x1222 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE V3 NK 0x373B:0x1213 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE V3 NK 0x373B:0x12D2 | 有线直连 | 该行仅列 DPI | 待核对 |
| Mouse 0x373B:0x1366 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1 S NK 0x373B:0x1310 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1 S NK 0x373B:0x135B | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 SE 3.0 NK 0x373B:0x130F | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 SE 3.0 NK 0x373B:0x134D | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R2 SE NK 0x373B:0x12B1 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 NK 0x373B:0x1223 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE V3 PRO NK 0x373B:0x1214 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1 NK 0x373B:0x124C | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 SE Nearlink 0x373B:0x1299 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE V3 Plus 0x373B:0x1215 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Plus NK 0x373B:0x1224 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Pro Max 2.0 NK 0x373B:0x11DB | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Pro Max 2.0 NK 0x373B:0x1301 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Plus NK 0x373B:0x1300 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK ZERO 0x373B:0x1154 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultimate 3.0 0x373B:0x1211 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Mini 0x373B:0x1268 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK Ghost Ultimate 0x373B:0x123A | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Plus NK 0x373B:0x1263 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Pro Max 2.0 NK 0x373B:0x1264 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 NB ULTRA 0x373B:0x12D0 | 有线直连 | 该行仅列 DPI | 待核对 |
| Mouse 0x373B:0x12E1 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultra Max 2.0 + 0x373B:0x128F | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK U2 Plus NK 0x373B:0x12E3 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 V3 Ultra Max 0x373B:0x1307 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1 PRO MAX 2.0 NK 0x373B:0x129A | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultimate 0x373B:0x1314 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Plus 3.0 NK 0x373B:0x1337 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Plus 3.0 NK 0x373B:0x134F | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Pro Max 3.0 NK 0x373B:0x1338 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Pro Max 3.0 NK 0x373B:0x1350 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Air 0x373B:0x1343 | 有线直连 | 该行仅列 DPI | 待核对 |
| Mouse 0x373B:0x134C | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK ZERO + 0x373B:0x129E | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultimate + 0x373B:0x1287 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultimate + 0x373B:0x12F1 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Air + 0x373B:0x1288 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK PHANTOM ULTIMATE 0x373B:0x12CE | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK PHANTOM ULTIMATE 0x373B:0x1312 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 ULTIMATE 2.0+ 0x373B:0x12D9 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 Ultra Max 2.0 + Tri-Mode 0x373B:0x12DE | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 Air+ 0x373B:0x12EC | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 V3 ULTIMATE+ 0x373B:0x1304 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 V3 ULTIMATE+ 0x373B:0x1308 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK F1 V3 ULTIMATE 0x373B:0x1344 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Mini + 0x373B:0x1251 | 有线直连 | 该行仅列 DPI | 待核对 |
| Mouse 0x373B:0x1324 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK ZERO PLUS GENGAR EDITION 0x373B:0x1331 | 有线直连 | 该行仅列 DPI | 待核对 |
| Mouse 0x373B:0x133C | 有线直连 | 该行仅列 DPI | 待核对 |
| Mouse 0x373B:0x12D8 | 有线直连 | 该行仅列 DPI | 待核对 |
| Mouse 0x373B:0x130B | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE V3 PRO NK 0x373B:0x1306 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 3.0 NK 0x373B:0x1305 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 3.0 NK 0x373B:0x134E | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK X1 SE NK 0x373B:0x130E | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1 SE+ 0x373B:0x12DC | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1 SE+ 0x373B:0x1368 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE V3 PRO Max 0x373B:0x127B | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1 NK 0x373B:0x1311 | 有线直连 | 该行仅列 DPI | 待核对 |
| VXE R1 NK 0x373B:0x135C | 有线直连 | 该行仅列 DPI | 待核对 |
| Mouse 0x373B:0x1369 | 有线直连 | 该行仅列 DPI | 待核对 |
| Mouse 0x373B:0x1367 | 有线直连 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k dongle-L1 0x373B:0x1155 | 接收器 | 该行仅列 DPI | 待核对 |
| NK mouse 8K NANO dongle 0x373B:0x11DC | 接收器 | 该行仅列 DPI | 待核对 |
| NK mouse NANO dongle 0x373B:0x1216 | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k dongle-L0 0x373B:0x124F | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k dongle-M1 0x373B:0x1290 | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k NANO dongle-M1 0x373B:0x1291 | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse -8K TOWER dongle 0x373B:0x12D1 | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k NANO dongle-M1 0x373B:0x12CD | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k NANO dongle-M1 0x373B:0x1315 | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k NANO dongle-L 0x373B:0x12AF | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k dongle-M1 0x373B:0x12F5 | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k dongle-G 0x373B:0x12EA | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k dongle-G 0x373B:0x1332 | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k dongle-O 0x373B:0x1302 | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k NANO dongle-A1 0x373B:0x133E | 接收器 | 该行仅列 DPI | 待核对 |
| NK mouse NANO dongle 0x373B:0x133F | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k dongle-M1 0x373B:0x1346 | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k NANO dongle-M1 0x373B:0x1347 | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k NANO dongle-M1 0x373B:0x1348 | 接收器 | 该行仅列 DPI | 待核对 |
| Wireless mouse 8k dongle 0x373B:0x134B | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU Maya X 0x373E:0x001D | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU Maya X 0x373E:0x001E | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU TACHI 0x37B0:0x000B | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU TACHI 0x37B0:0x000C | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU INCA 0x37B0:0x000F | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU INCA 0x37B0:0x0010 | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU Maya 0x37B0:0x0013 | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU Maya 0x37B0:0x0015 | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU PARO 0x37B0:0x000D | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU PARO 0x37B0:0x000E | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU THORN 0x37B0:0x0019 | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU THORN 0x37B0:0x001B | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU THORN V2 0x37B0:0x0003 | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU THORN V2 0x37B0:0x0023 | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU Tachi Lite 0x37B0:0x001E | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU MayaX V2 0x373E:0x0016 | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU Atlantis OG Champion 0x37B0:0x0027 | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU Atlantis Mini 0x37B0:0x002B | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU Atlantis Mini 0x37B0:0x002A | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU MAYA M(54H20) / THORN V2(54H20) 0x37B0:0x002E | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU THORN V2(54H20) 0x37B0:0x0032 | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU DM198(54H20) / DM198_OP(54H20) / MAYA (54H20) / ORCUS 0x37B0:0x0036 | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU MAYA X(54H20) 0x37B0:0x003B | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU Maya X(LM20) / MAYA M(LM20) / Maya(LM20) / Orcus V2(LM20) 0x37B0:0x0048 | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU Mini(LM20) 0x37B0:0x0054 | 接收器 | 该行仅列 DPI | 待核对 |
| LAMZU Maya X 0x373E:0x001C | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU TACHI 0x37B0:0x0005 | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU INCA 0x37B0:0x0009 | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU Maya 0x37B0:0x0011 | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU PARO 0x37B0:0x0007 | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU THORN 0x37B0:0x0017 | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU THORN V2 0x37B0:0x0021 | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU Tachi Lite 0x37B0:0x001C | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU MayaX V2 0x37B0:0x001F | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU Atlantis OG Champion 0x37B0:0x0025 | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU Atlantis Mini 0x37B0:0x0028 | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU MAYA M(54H20) 0x37B0:0x002C | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU THORN V2(54H20) 0x37B0:0x0030 | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU THORN V2(54H20) 0x37B0:0x0040 | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU DM198(54H20) 0x37B0:0x0034 | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU DM198_OP(54H20) 0x37B0:0x0038 | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU MAYA X(54H20) 0x37B0:0x003A | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU MAYA (54H20) 0x37B0:0x003C | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU ORCUS 0x37B0:0x0042 | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU Maya X(LM20) 0x37B0:0x0044 | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU MAYA M(LM20) 0x37B0:0x0052 | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU Maya(LM20) 0x37B0:0x0050 | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU Mini(LM20) 0x37B0:0x0046 | 有线直连 | 该行仅列 DPI | 待核对 |
| LAMZU Orcus V2(LM20) 0x37B0:0x0056 | 有线直连 | 该行仅列 DPI | 待核对 |
| ATK A9 Pro Max 0x373B:0x1147 | 有线直连 | 理论实现（HECHI 电量及充电） | 待核对 |
| ATK HECHI 1K 接收器 0x373B:0x1148 | 接收器 | 理论实现（HECHI 电量及充电） | 待核对 |
| ATK HECHI 8K 接收器 0x373B:0x1196 | 接收器 | 理论实现（HECHI 电量及充电） | 待核对 |
| 万灵 HUAN 0x36A7:0xA864 | 有线直连 | 该行仅列 DPI | 待核对 |
| 万灵 HUAN / 万灵 HUAN M 0x36A7:0xA863 | 接收器 | 该行仅列 DPI | 待核对 |
| 万灵 HUAN M 0x36A7:0xA859 | 有线直连 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST MIAO 0x36A7:0xA867 | 有线直连 | 该行仅列 DPI | 待核对 |
| 万灵共享 1K 接收器 0x36A7:0xA882 | 接收器 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST MIAO 0x36A7:0xA866 | 接收器 | 该行仅列 DPI | 待核对 |
| 万灵共享 1K 接收器 0x373E:0x0065 | 接收器 | 该行仅列 DPI | 待核对 |
| 万灵 STRIDER 0x36A7:0xA873 | 有线直连 | 该行仅列 DPI | 待核对 |
| 万灵 STRIDER 0x36A7:0xA872 | 接收器 | 该行仅列 DPI | 待核对 |
| 万灵 YING 0x36A7:0xA875 | 有线直连 | 该行仅列 DPI | 待核对 |
| 万灵 YING 0x36A7:0xA874 | 接收器 | 该行仅列 DPI | 待核对 |
| 万灵 SWORD X 0x36A7:0xA879 | 有线直连 | 该行仅列 DPI | 待核对 |
| 万灵 SWORD X 0x36A7:0xA878 | 接收器 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST MINI 0x36A7:0xA886 | 有线直连 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST MINI 0x36A7:0xA885 | 接收器 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST MINI PRO 0x36A7:0xA869 | 有线直连 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST MINI PRO 0x36A7:0xA868 | 接收器 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST MAX 0x36A7:0xA881 | 有线直连 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST MAX 0x36A7:0xA880 | 接收器 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST X 0x36A7:0xA884 | 有线直连 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST X 0x36A7:0xA883 | 接收器 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST X PRO 0x36A7:0xA871 | 有线直连 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST X PRO 0x36A7:0xA870 | 接收器 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST G 0x36A7:0xA861 | 有线直连 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST G 0x36A7:0xA860 | 接收器 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST X V2 0x36A7:0xA857 | 有线直连 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST X V2 0x36A7:0xA856 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Atheris Receiver 0x1532:0x0062 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Lancehead Wireless Receiver 0x1532:0x006F | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Lancehead Wireless Wired 0x1532:0x0070 | 有线直连 | 该行仅列 DPI | 待核对 |
| Razer Mamba Wireless Receiver 0x1532:0x0072 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Mamba Wireless Wired 0x1532:0x0073 | 有线直连 | 该行仅列 DPI | 待核对 |
| Razer Pro Click Receiver 0x1532:0x0077 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Viper Ultimate Wired 0x1532:0x007A | 有线直连 | 该行仅列 DPI | 待核对 |
| Razer Viper Ultimate Wireless 0x1532:0x007B | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Death Adder V2Pro Wired 0x1532:0x007C | 有线直连 | 该行仅列 DPI | 待核对 |
| Razer Death Adder V2Pro Wireless 0x1532:0x007D | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Pro Click Wired 0x1532:0x0080 | 有线直连 | 该行仅列 DPI | 待核对 |
| Razer Basilisk XHyper Speed 0x1532:0x0083 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Basilisk Ultimate Wired 0x1532:0x0086 | 有线直连 | 该行仅列 DPI | 待核对 |
| Razer Basilisk Ultimate Receiver 0x1532:0x0088 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Naga Pro Wired 0x1532:0x008F | 有线直连 | 该行仅列 DPI | 待核对 |
| Razer Naga Pro Wireless 0x1532:0x0090 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Orochi V2Receiver 0x1532:0x0094 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Pro Click Mini Receiver 0x1532:0x009A | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Death Adder V2XHyper Speed 0x1532:0x009C | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Viper Mini SEWired 0x1532:0x009E | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Viper Mini SEWireless 0x1532:0x009F | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Viper V2Pro Wired 0x1532:0x00A5 | 有线直连 | 该行仅列 DPI | 待核对 |
| Razer Viper V2Pro Wireless 0x1532:0x00A6 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Naga V2Pro Wired 0x1532:0x00A7 | 有线直连 | 该行仅列 DPI | 待核对 |
| Razer Naga V2Pro Wireless 0x1532:0x00A8 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Basilisk V3Pro Wired 0x1532:0x00AA | 有线直连 | 该行仅列 DPI | 待核对 |
| Razer Basilisk V3Pro Wireless 0x1532:0x00AB | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Cobra Pro Wired 0x1532:0x00AF | 有线直连 | 该行仅列 DPI | 待核对 |
| Razer Cobra Pro Wireless 0x1532:0x00B0 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Naga V2Hyper Speed Receiver 0x1532:0x00B4 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Death Adder V3Pro Wired 0x1532:0x00B6 | 有线直连 | 该行仅列 DPI | 待核对 |
| Razer Death Adder V3Pro Wireless 0x1532:0x00B7 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Viper V3Hyper Speed 0x1532:0x00B8 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Basilisk V3XHyper Speed 0x1532:0x00B9 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Death Adder V4Pro Wired 0x1532:0x00BE | 有线直连 | 该行仅列 DPI | 待核对 |
| Razer Death Adder V4Pro Wireless 0x1532:0x00BF | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Viper V3Pro Wired 0x1532:0x00C0 | 有线直连 | 该行仅列 DPI | 待核对 |
| Razer Viper V3Pro Wireless 0x1532:0x00C1 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Death Adder V3Pro Wired_Alternate 0x1532:0x00C2 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Death Adder V3Pro Wireless_Alternate 0x1532:0x00C3 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Death Adder V3Hyper Speed Wired 0x1532:0x00C4 | 有线直连 | 该行仅列 DPI | 待核对 |
| Razer Death Adder V3Hyper Speed Wireless 0x1532:0x00C5 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Pro Click V2Vertical Edition Wired 0x1532:0x00C7 | 有线直连 | 该行仅列 DPI | 待核对 |
| Razer Pro Click V2Vertical Edition Wireless 0x1532:0x00C8 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Basilisk V3Pro35KWired 0x1532:0x00CC | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Basilisk V3Pro35KWireless 0x1532:0x00CD | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Pro Click V2Wired 0x1532:0x00D0 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Pro Click V2Wireless 0x1532:0x00D1 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Basilisk Mobile Wired 0x1532:0x00D3 | 有线直连 | 该行仅列 DPI | 待核对 |
| Razer Basilisk Mobile Receiver 0x1532:0x00D4 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Basilisk V3Pro35KPhantom Green Edition Wired 0x1532:0x00D6 | 有线直连 | 该行仅列 DPI | 待核对 |
| Razer Basilisk V3Pro35KPhantom Green Edition Wireless 0x1532:0x00D7 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Cobra Hyper Speed 0x1532:0x00DA | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Cobra Hyper Speed Wireless 0x1532:0x00DB | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Viper V3Pro SEWired 0x1532:0x00DE | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Viper V3Pro SEWireless 0x1532:0x00DF | 接收器 | 该行仅列 DPI | 待核对 |
| Razer DeathAdder V4 Pro Carbon Fiber Edition 0x1532:0x00EF | 有线直连 | 该行仅列 DPI | 待核对 |
| Razer DeathAdder V4 Pro Carbon Fiber Edition 0x1532:0x00F0 | 接收器 | 该行仅列 DPI | 待核对 |
| Razer Viper V4 Pro 0x1532:0x00E5 | 有线直连 | 该行仅列 DPI | 待核对 |
| Razer Viper V4 Pro 0x1532:0x00E6 | 接收器 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly F1 V2 Extreme 0x391D:0x101B | 有线直连 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly 3 Turbo 0x391D:0x1023 | 有线直连 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly 3 Turbo+ 0x391D:0x1024 | 有线直连 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly F1 V5 Turbo 0x391D:0x1025 | 有线直连 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly F1 V5 Turbo+ 0x391D:0x1026 | 有线直连 | 该行仅列 DPI | 待核对 |
| VGN Falcon 3 Extreme 0x391D:0x102E | 有线直连 | 该行仅列 DPI | 待核对 |
| VGN Falcon 3 Extreme+ 0x391D:0x1037 | 有线直连 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly 3 MINI Turbo 0x391D:0x103C | 有线直连 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly 3 MINI Turbo+ 0x391D:0x103D | 有线直连 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly F1 V2 Extreme 2.4G 0x391D:0x1A18 | 接收器 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly 3 Turbo 0x391D:0x1A20 | 接收器 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly 3 Turbo+ 0x391D:0x1A21 | 接收器 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly F1 V5 Turbo 0x391D:0x1A22 | 接收器 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly F1 V5 Turbo+ 0x391D:0x1A23 | 接收器 | 该行仅列 DPI | 待核对 |
| VGN Falcon 3 Extreme+ 2.4G 0x391D:0x1A29 | 接收器 | 该行仅列 DPI | 待核对 |
| VGN Falcon 3 Extreme 2.4G 0x391D:0x1A2A | 接收器 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly 3 MINI Turbo 0x391D:0x1A35 | 接收器 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly 3 MINI Turbo+ 0x391D:0x1A36 | 接收器 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly3 Pro Max 0x391D:0x1004 | 有线直连 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly3 Pro Max 0x391D:0x1A04 | 接收器 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly Y2 V2 Pro Max 0x391D:0x1008 | 有线直连 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly Y2 V2 Pro Max 0x391D:0x1A08 | 接收器 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly 3 Ultra 0x391D:0x1028 | 有线直连 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly 3 Ultra 0x391D:0x1A25 | 接收器 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly F1 V5 Ultra 0x391D:0x102F | 有线直连 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly F1 V5 Ultra 0x391D:0x1A2B | 接收器 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly F1 V2 Pro Max 0x391D:0x1015 | 有线直连 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly F1 V2 Pro Max 0x391D:0x1A13 | 接收器 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly 3 Master 0x391D:0x1005 | 有线直连 | 该行仅列 DPI | 待核对 |
| VGN VGN USB Receiver 0x391D:0x1A05 | 接收器 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly 3 MasterGT 0x391D:0x101D | 有线直连 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly 3 MasterGT 0x391D:0x1A1A | 接收器 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly Y2 V2 MasterGT 0x391D:0x1034 | 有线直连 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly Y2 V2 MasterGT 0x391D:0x1A2F | 接收器 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly F2 MasterGT 0x391D:0x1032 | 有线直连 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly F2 MasterGT 0x391D:0x102D | 接收器 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly F1 V2 SE+ 0x391D:0x1038 | 有线直连 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly F1 V2 SE+ 0x391D:0x1A32 | 接收器 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly F2 Turbo+ 0x391D:0x1036 | 有线直连 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly F2 Turbo+ 0x391D:0x1A31 | 接收器 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly F2 Turbo 0x391D:0x1035 | 有线直连 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly F2 Turbo 0x391D:0x1A30 | 接收器 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly 3 MINI SE+ 0x391D:0x1039 | 有线直连 | 该行仅列 DPI | 待核对 |
| VGN Dragonfly 3 MINI SE+ 0x391D:0x1A33 | 接收器 | 该行仅列 DPI | 待核对 |
| MCHOSE A5 Pro Max 0x2023:0xF019 | 有线直连 | 该行仅列 DPI | 待核对 |
| MCHOSE A5 Pro Max 0x2023:0xF013 | 接收器 | 该行仅列 DPI | 待核对 |
| MCHOSE A5 Pro Max 0x2023:0xF015 | 接收器 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC0A8 | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC513 | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC517 | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC51B | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC52B | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC52F | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC531 | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC532 | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC534 | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC537 | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC539 | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC53A | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC53D | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC53F | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC541 | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC543 | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC545 | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC547 | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC548 | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC54D | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC70A | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC70E | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC713 | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC714 | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC71B | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC71C | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC71E | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC71F | HID++ 接口 | 该行仅列 DPI | 待核对 |
| 罗技 HID++ 鼠标 0x046D:0xC54F | HID++ 接口 | 该行仅列 DPI | 待核对 |
| MCHOSE A5 V3 Pro 配对身份 3837:4035 | report4D 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE A5 V3 Ultra+ 配对身份 3837:4026 | report4D 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE A5 V3 Ultra+(3955) 配对身份 3837:4034 | report4D 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE K7 V2 Pro 配对身份 3837:403B | report4D 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE K7 V2 Pro+ 配对身份 3837:4027 | report4D 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE K7 V2 Ultra 配对身份 3837:403C | report4D 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE K7 V2 Ultra+ 配对身份 3837:4028 | report4D 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE A7 V3 配对身份 3837:4030 | report4D 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE A7 V3 Pro 配对身份 3837:4031 | report4D 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE A7 V3 Pro+ 配对身份 3837:4032 | report4D 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE A7 V3 Ultra 配对身份 3837:403D | report4D 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE A7 V3 Ultra+ 配对身份 3837:4033 | report4D 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE A7 V3-X 配对身份 3837:403A | report4D 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE S0034 配对身份 3837:403F | report4D 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE K5 V2 配对身份 3837:4040 | report4D 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE K5 Pro 配对身份 3837:4037 | report4D 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE K5 Ultra 配对身份 3837:4038 | report4D 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE R7 Ultra 配对身份 3837:4036 | report4D 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE V7 配对身份 3837:402A | report4D 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE V7 Pro 配对身份 3837:403E | report4D 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE G3 V3 配对身份 3837:4029 | report4D 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE M7 配对身份 5253:0020 | Feature11/12 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE M7 Pro 配对身份 5253:0030 | Feature11/12 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE M7 Ultra 配对身份 5253:0031 | Feature11/12 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE L7 配对身份 5253:00C0 | Feature11/12 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE L7 Pro 配对身份 5253:00B0 | Feature11/12 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE L7 Pro+ 配对身份 3837:4015 | Feature11/12 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE L7 Ultra 配对身份 5253:00B1 | Feature11/12 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE L7 Ultra+ 配对身份 3837:4016 | Feature11/12 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE A7 配对身份 5253:0070 | Feature11/12 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE A7 Pro 配对身份 5253:0010 | Feature11/12 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE A7 Ultra 配对身份 5253:0011 | Feature11/12 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE A7 V2 Pro 配对身份 3837:4018 | Feature11/12 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE A7 V2 Pro+ 配对身份 3837:4023 | Feature11/12 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE A7 V2 Ultra 配对身份 3837:4019 | Feature11/12 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE A7 V2 Ultra+ 配对身份 3837:4021 | Feature11/12 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE A7X Ultra 配对身份 3837:4011 | Feature11/12 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE A7 Ultra(RE) 配对身份 3837:4110 | Feature11/12 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE K7 Ultra 配对身份 3837:4150 | Feature11/12 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE A5 V2 Ultra 配对身份 41E4:1101 | Feature11/12 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE AX5 V2 配对身份 3837:4010 | Feature11/12 配置接口 | 该行仅列 DPI | 待核对 |
| MCHOSE AX5 V2 配对身份 5253:00A1 | Feature11/12 配置接口 | 该行仅列 DPI | 待核对 |
| VXE R1S+ 配对身份 2,63 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE V3 NK 配对身份 2,72 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 SE NK 配对身份 2,88 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK U2 SE NK 配对身份 2,92 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Y9 SE NK 配对身份 2,98 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| DRAGONFLY F1 V2 SE NK 配对身份 2,95 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 S NK 配对身份 2,117 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE X3 配对身份 2,121 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Dragonfly F1 PRO 配对身份 2,1 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Dragonfly F1 PRO 配对身份 2,2 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Dragonfly F1 PRO 配对身份 2,104 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Dragonfly F1 PRO MAX 配对身份 2,3 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Dragonfly F1 PRO MAX 配对身份 2,4 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Dragonfly F1 PRO MAX 配对身份 2,105 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Dragonfly F1 MOBA 配对身份 2,5 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Dragonfly F1 MOBA 配对身份 2,6 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Dragonfly F1 MOBA 配对身份 2,33 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Dragonfly F1 MOBA 配对身份 2,106 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Dragonfly F1 PRO MAX Elden 配对身份 2,48 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Dragonfly F1 PRO MAX JOJO 配对身份 2,52 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Dragonfly F1S 配对身份 2,19 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Dragonfly Dragonfly F1S 配对身份 2,20 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 PRO 配对身份 2,7 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 PRO 配对身份 2,8 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 PRO MAX 配对身份 2,27 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 PRO MAX 配对身份 2,28 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 PRO MAX 配对身份 2,34 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 配对身份 2,11 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 配对身份 2,12 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| MAD R 配对身份 2,49 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| MAD R + 配对身份 2,54 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 Lite 配对身份 2,89 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 S NK 配对身份 2,76 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Plus NK 配对身份 2,83 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK U2 Plus NK 配对身份 2,78 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Y9 Plus NK 配对身份 2,96 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| DRAGONFLY F1 V2 Plus NK 配对身份 2,93 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 UlTRA 配对身份 2,114 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1SE+ Silent 配对身份 2,130 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1SE 配对身份 2,29 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1SE 配对身份 2,30 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1SE+ 配对身份 2,31 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1SE+ 配对身份 2,32 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1SE+ 配对身份 2,116 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 NK 配对身份 2,84 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1E 配对身份 2,90 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 配对身份 2,99 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK U2 配对身份 2,109 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 SE NK 配对身份 2,77 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 NK 配对身份 2,57 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 NK 配对身份 2,67 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE V3 PRO NK 配对身份 2,73 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK U2 NK 配对身份 2,79 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Y9 NK 配对身份 2,97 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| DRAGONFLY F1 V2 NK 配对身份 2,94 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 X 配对身份 2,107 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE X3 Pro 配对身份 2,122 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE X3 Turbo 配对身份 2,123 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 NB 配对身份 2,120 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Pro Max 配对身份 2,85 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK U2 Pro Max 配对身份 2,100 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| DRAGONFLY F1 V2 PRO MAX 配对身份 2,110 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultra Max 2.0 配对身份 1,6 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultra Max 2.0 Tri-Mode 配对身份 1,15 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 Ultra Max 2.0 配对身份 1,12 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 Ultra Max 2.0 配对身份 1,11 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK U2 Ultra Max 2.0 配对身份 1,18 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 Ultra 配对身份 1,20 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 PRO 配对身份 2,36 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 PRO MAX 配对身份 2,37 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 PRO 配对身份 2,38 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 PRO MAX 配对身份 2,39 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Z1 PRO 配对身份 2,40 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Z1 PRO MAX 配对身份 2,41 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK U2 MINI 配对身份 2,64 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK U2 Ultra 配对身份 2,66 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Y9 PRO MAX 配对身份 2,108 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| MAD R MAJOR 配对身份 2,47 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| MAD R MAJOR+ 配对身份 2,55 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 Ultra Max 2.0 配对身份 1,87 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 Ultimate 配对身份 2,42 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 Ultra 配对身份 2,43 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK FIERCE X 配对身份 2,44 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 Ultra 配对身份 2,45 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 Ultimate 配对身份 2,46 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 EXTREME 配对身份 2,50 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Z1 Ultra 配对身份 2,53 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 LEVIATAN 配对身份 2,56 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK U2 Ultimate 配对身份 2,65 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 EXTREME 配对身份 2,70 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultra 配对身份 2,87 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 NB ULTRA 配对身份 2,115 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultimate 配对身份 1,3 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Air 配对身份 1,21 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Y9 Ultimate 配对身份 1,5 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 Ultimate 2.0 配对身份 1,8 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 Ultimate 2.0 配对身份 1,10 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Duckbill Ultra 配对身份 1,2 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Duckbill EXTREME 配对身份 1,25 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK ZERO EX 配对身份 1,4 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 EXTREME 2.0 配对身份 1,9 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 EXTREME GT 配对身份 1,22 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 EXTREME 2.0 配对身份 1,7 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 LEVIATAN 2.0 配对身份 1,13 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK U2 Mini 2.0 配对身份 1,17 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Z1 Ultimate 2.0 配对身份 1,19 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK U2 Ultimate 2.0 配对身份 1,16 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Ghost EXTREME 配对身份 1,23 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Ghost Ultimate 配对身份 1,24 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK ZERO 配对身份 1,26 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Mini 配对身份 1,27 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X EXTREME 配对身份 1,30 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 Ultra Max 2.0 配对身份 1,32 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 Ultra Max 2.0 配对身份 1,33 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultra Max 2.0 Tri-Mode 配对身份 1,37 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultra Max 2.0 配对身份 1,38 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK U2 Plus NK 配对身份 2,124 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 配对身份 2,127 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 PRO 配对身份 2,125 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 PRO MAX 配对身份 2,126 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Mini 配对身份 1,28 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK U2 Pro Max 配对身份 2,128 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| Flow 7 配对身份 2,131 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 V3 ULTIMATE 配对身份 1,85 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| MAD R 配对身份 2,133 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 S NK 配对身份 2,132 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Mini + 配对身份 1,31 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Mini + 配对身份 1,52 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK ZERO EX + 配对身份 1,47 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 Ultimate 2.0 + 配对身份 1,46 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 V3 EXTREME + 配对身份 1,58 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 V3 LEVIATAN + 配对身份 1,60 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Air + 配对身份 1,54 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultimate + 配对身份 1,53 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK U2 Mini 2.0 + 配对身份 1,51 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK U2 Ultimate 2.0 + 配对身份 1,50 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK PHANTOM ULTIMATE 配对身份 1,55 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 EXTREME 2.0 + 配对身份 1,45 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 Air + 配对身份 1,61 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Ghost Ultimate + 配对身份 1,49 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Ghost EXTREME + 配对身份 1,48 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 Ultimate 2.0 + 配对身份 1,59 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 Air + 配对身份 1,42 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 V3 ULTIMATE + 配对身份 1,62 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 NB ULTRA + 配对身份 2,129 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Y9 Ultimate + 配对身份 1,66 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Z1 Ultimate 2.0 + 配对身份 1,67 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Air + 配对身份 1,40 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultimate + 配对身份 1,39 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 V3 ULTIMATE + 配对身份 1,63 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 V3 ULTIMATE + 配对身份 1,64 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Wireless Charging 配对身份 1,71 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 Ultimate 2.0 Silent 配对身份 1,76 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultimate Silent 配对身份 1,75 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| Mouse 配对身份 2,134 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| Mouse 配对身份 2,135 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 V3 PIKACHU Edition 配对身份 1,90 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| Mouse 配对身份 1,86 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| Mouse 配对身份 1,91 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| Mouse 配对身份 1,88 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| Mouse 配对身份 1,84 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| Mouse 配对身份 1,80 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1SE+ 配对身份 2,136 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1SE+ Silent 配对身份 2,137 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 塞洛斯 V8 系列 配对身份 62,1 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 塞洛斯 V8 系列 配对身份 62,10 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 塞洛斯 V8 系列 配对身份 62,11 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 S NK 配对身份 2,9 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 SE NK 配对身份 2,2 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE V3 NK 配对身份 2,5 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE V3 NK 配对身份 2,21 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| Mouse 配对身份 2,48 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 S NK 配对身份 2,31 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 S NK 配对身份 2,44 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 SE 3.0 NK 配对身份 2,32 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 SE 3.0 NK 配对身份 2,39 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R2 SE NK 配对身份 2,19 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 NK 配对身份 2,3 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE V3 PRO NK 配对身份 2,6 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 NK 配对身份 2,8 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 SE Nearlink 配对身份 2,16 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE V3 Plus 配对身份 2,7 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Plus NK 配对身份 2,4 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Pro Max 2.0 NK 配对身份 2,1 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 V3 ULTIMATE 配对身份 2,27 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Plus NK 配对身份 2,26 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK ZERO 配对身份 1,1 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultimate 3.0 配对身份 1,2 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Mini 配对身份 1,16 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK Ghost Ultimate 配对身份 1,3 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Plus NK 配对身份 2,13 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Pro Max 2.0 NK 配对身份 2,14 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 NB ULTRA 配对身份 2,20 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| Mouse 配对身份 2,23 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultra Max 2.0 + 配对身份 1,7 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK U2 Plus NK 配对身份 2,25 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 V3 Ultra Max 配对身份 1,19 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 PRO MAX 2.0 NK 配对身份 2,17 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultimate 配对身份 1,22 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Plus 3.0 NK 配对身份 2,34 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Plus 3.0 NK 配对身份 2,40 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Pro Max 3.0 NK 配对身份 2,35 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Pro Max 3.0 NK 配对身份 2,41 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Air 配对身份 1,26 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| Mouse 配对身份 2,37 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK ZERO + 配对身份 1,8 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK ZERO + 配对身份 8,1 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultimate + 配对身份 1,9 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Ultimate + 配对身份 1,15 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Air + 配对身份 1,6 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK PHANTOM ULTIMATE 配对身份 1,10 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK PHANTOM ULTIMATE 配对身份 1,21 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 ULTIMATE 2.0+ 配对身份 1,12 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 Ultra Max 2.0 + Tri-Mode 配对身份 1,13 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 Air+ 配对身份 1,14 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 V3 ULTIMATE+ 配对身份 1,17 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK F1 V3 ULTIMATE+ 配对身份 1,18 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 Mini + 配对身份 1,4 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| Mouse 配对身份 1,23 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK ZERO PLUS GENGAR EDITION 配对身份 1,24 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| Mouse 配对身份 5,3 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| Mouse 配对身份 1,11 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| Mouse 配对身份 1,20 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE V3 PRO NK 配对身份 2,30 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 3.0 NK 配对身份 2,29 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK A9 3.0 NK 配对身份 2,38 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATK X1 SE NK 配对身份 2,33 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 SE+ 配对身份 2,22 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 SE+ 配对身份 2,42 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE V3 PRO Max 配对身份 2,15 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 NK 配对身份 2,28 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| VXE R1 NK 配对身份 2,43 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| Mouse 配对身份 2,47 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| Mouse 配对身份 2,49 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 塞洛斯 V12L 配对身份 395A:0003 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 塞洛斯 V12M 配对身份 395A:0005 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 塞洛斯 V12L-U 配对身份 395A:0007 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 塞洛斯 V12M-U 配对身份 395A:0009 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 塞洛斯 V6S 配对身份 395A:000B | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 塞洛斯 V8S 配对身份 395A:000D | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 塞洛斯 3950 协议鼠标 配对身份 2FE3:0007 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATLANTIS OG V2 配对身份 1 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| ATLANTIS MINI 配对身份 2 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| Thorn 配对身份 3 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| Maya 配对身份 4 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| Paro 配对身份 5 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| Atlantis MINI Champion 配对身份 6 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| MAYA 配对身份 7 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| Maya X 配对身份 8 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 万灵 HUAN 配对身份 36A7:A864 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 万灵 HUAN M 配对身份 36A7:A859 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST MIAO 配对身份 36A7:A867 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 万灵 STRIDER 配对身份 36A7:A873 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 万灵 YING 配对身份 36A7:A875 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 万灵 SWORD X 配对身份 36A7:A879 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST MINI 配对身份 36A7:A886 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST MINI PRO 配对身份 36A7:A869 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST MAX 配对身份 36A7:A881 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST X 配对身份 36A7:A884 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST X PRO 配对身份 36A7:A871 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST G 配对身份 36A7:A861 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 万灵 BEAST X V2 配对身份 36A7:A857 | 经相应协议接口 | 该行仅列 DPI | 待核对 |
| 塞洛斯接收器 0x395A:0x0002 | 2.4G | 该行仅列 DPI | 待核对 |
| 塞洛斯接收器 0x395A:0x0004 | 2.4G | 该行仅列 DPI | 待核对 |
| 塞洛斯充电接收底座 0x395A:0x0008 | 2.4G | 该行仅列 DPI | 待核对 |
| 塞洛斯充电接收底座 0x395A:0x000A | 2.4G | 该行仅列 DPI | 待核对 |
| 塞洛斯接收器 0x395A:0x000C | 2.4G | 该行仅列 DPI | 待核对 |
| 塞洛斯接收器 0x395A:0x000E | 2.4G | 该行仅列 DPI | 待核对 |
| Windows 蓝牙电量 | 取决于系统或描述符是否提供有效电量 | 依系统有效上报 | 未开放 |
| Windows 手柄电量 | 取决于系统或描述符是否提供有效电量 | 依系统有效上报 | 未开放 |
| 标准 HID 电量字段 | 取决于系统或描述符是否提供有效电量 | 依系统有效上报 | 未开放 |
| HeadsetControl 4.1.0 耳机电量 | 取决于系统或描述符是否提供有效电量 | 依系统有效上报 | 未开放 |
| SteelSeries Arctis Nova Elite 0x1038:0x2244 | 未启用读取 | 待研究 | 未开放 |
| Corsair Dark Core RGB Pro SE 0x1B1C:0x1B7F | 未启用读取 | 待研究 | 未开放 |
| INFI75-1.1 / RKGK890 0x05AC:0x024F | 未启用读取 | 待研究 | 未开放 |
| 狼蛛 S100 / S100Pro 2.4G（待验证） 0x1A2C:0x7FFF | 未启用读取 | 待研究 | 未开放 |
| 瓦尔基里 VK S1 系列（普通版 / 火影联名版） | 未启用读取 | 待研究 | 未开放 |
| EVO75 系列 | 未启用读取 | 待研究 | 未开放 |
| 狼蛛 SC580 系列 report5 硬件分支 | 未启用读取 | 待研究 | 未开放 |
| RX-STORM RX980 系列 | 未启用读取 | 待研究 | 未开放 |
| 八位堂 猎户座2（精确百分比） 0x2DC8:0x3106 | 未启用读取 | 待研究 | 未开放 |
| MelGeek HORUS 荷鲁斯系列 | 未启用读取 | 待研究 | 未开放 |
| 狼蛛 SC580 EVO | 未启用读取 | 待研究 | 未开放 |
| 狼蛛 V9 / X9（320F 网页驱动分支） | 未启用读取 | 待研究 | 未开放 |
| 狼蛛 MINI HE / 通用8K接收器等网页键盘剩余分支 | 未启用读取 | 待研究 | 未开放 |
| 狼蛛 L3 / L3 SE 网页驱动分支 | 未启用读取 | 待研究 | 未开放 |
| 达尔优 COOL68 系列电量能力 | 未启用读取 | 待研究 | 未开放 |
| 狼途 T6 三模鼠标 / 狼途 S98 键盘 | 未启用读取 | 待研究 | 未开放 |
| 狼蛛 F3387 / S98 三模系列 | 未启用读取 | 待研究 | 未开放 |
| 红龙 G49 系列新硬件分支 | 未启用读取 | 待研究 | 未开放 |
| EPZ N500 耳机 | 未启用读取 | 待研究 | 未开放 |
| 凌豹 M9 Pro（320F:22A0 分支） | 未启用读取 | 待研究 | 未开放 |
| NuPhy Node75 / Node 系列 U1 接收器 | 未启用读取 | 待研究 | 未开放 |
| 西伯利亚 S21GS / K02BS | 未启用读取 | 待研究 | 未开放 |
| 罗技 G321 无线耳机 0x046D:0x0B39 | 未启用读取 | 待研究 | 未开放 |
| 迈从 V9 普通版无线耳机 | 未启用读取 | 待研究 | 未开放 |
| 英菲克 ZHEN | 未启用读取 | 待研究 | 未开放 |
| 达尔优 COL98（官网 COOL98） | 未启用读取 | 待研究 | 未开放 |
| 达尔优 A98 普通版（05AC:024F 分支） | 未启用读取 | 待研究 | 未开放 |
| 雷柏普通 VT9S 185A / MT 办公系列 DPI 0x24AE:0x185A | 未启用读取 | 待研究 | 未开放 |
| LAMZU Atlantis Mini 54H20 / 共享 F517 DPI | 未启用读取 | 待研究 | 未开放 |
| 罗技板载 DPI 档位编辑 | 未启用读取 | 待研究 | 未开放 |
| 雷蛇 Mouse Dock Pro / HyperPolling DPI | 未启用读取 | 待研究 | 未开放 |
| 迈从旧分支剩余 DPI | 未启用读取 | 待研究 | 未开放 |
| VGN ByMsServe / 其余 Y2 DPI | 未启用读取 | 待研究 | 未开放 |
| 前行者 E7 V2 Max（YXT） | 未启用读取 | 待研究 | 未开放 |
| 前行者 E7 V2 / GS02 Pro（外链 freqC） | 未启用读取 | 待研究 | 未开放 |
| 前行者 G5 DPI | 未启用读取 | 待研究 | 未开放 |
| 前行者旧 X23 标准版 / 原 Pro DPI | 未启用读取 | 待研究 | 未开放 |
| 前行者贝盈非活动档改值 | 未启用读取 | 待研究 | 未开放 |
| 维咖 NUT65（原版，用户确认2.4G） 0x342D:0xE51A | 未启用读取 | 待研究 | 未开放 |
| 狼途 LT104 彩屏版 | 未启用读取 | 待研究 | 未开放 |
| 狼途 LT104 数字屏版 | 未启用读取 | 待研究 | 未开放 |
| 狼途 LT104 LOGO屏版 | 未启用读取 | 待研究 | 未开放 |
| 狼蛛 SC580 SE（DPI 读回链待确认，电量已在DB196实现） 0x248D:0x5B5F | 未启用读取 | 待研究 | 未开放 |
| Ninjutso 初代 Sora | 未启用读取 | 待研究 | 未开放 |
| Ninjutso Origin One X / Katana Superlight 无线 | 未启用读取 | 待研究 | 未开放 |
| NinjaForce 093A:EA01 | 未启用读取 | 待研究 | 未开放 |
| Ninjutso Ten / VAXEE × Ninjutso Sora 的额外版本 | 未启用读取 | 待研究 | 未开放 |
| Pulsar 3554:F40A / F517 | 未启用读取 | 待研究 | 未开放 |
| Razer BlackShark V2 Pro PlayStation 0x1532:0x1014 | 未启用读取 | 待研究 | 未开放 |
| HyperX Cloud MIX 2（2.4G） 0x03F0:0x0FAE | 未启用读取 | 待研究 | 未开放 |
| Jabra Link 380 其他变体 0x0B0E:0x24CA | 未启用读取 | 待研究 | 未开放 |
| HyperX 上游数组中的其他厂商/产品组合 | 未启用读取 | 待研究 | 未开放 |
| 西伯利亚 K06S | 未启用读取 | 待研究 | 未开放 |
| 西伯利亚 K03S 超竞版 | 未启用读取 | 待研究 | 未开放 |
| 西伯利亚 T7 新登记身份 | 未启用读取 | 待研究 | 未开放 |
| 西伯利亚 T7GT | 未启用读取 | 待研究 | 未开放 |
| 西伯利亚 S21 无线智充版 | 未启用读取 | 待研究 | 未开放 |
| 西伯利亚 W30 | 未启用读取 | 待研究 | 未开放 |
| 西伯利亚 S21GS（X HUB 新登记身份） | 未启用读取 | 待研究 | 未开放 |
| 竹林鸟 玄武 ZG2 2.0 进阶版 0x040B:0xF779 | 未启用读取 | 待研究 | 未开放 |
| 竹林鸟 朱雀 ZG3 | 未启用读取 | 待研究 | 未开放 |
| 腹灵 CMK99 NXP / QN9080 接收器 0x1FC9:0xE8D8 | 未启用读取 | 待研究 | 未开放 |
| 维咖 NUT108 / 接收器 0x342D:0xE554 | 未启用读取 | 待研究 | 未开放 |
| 狼蛛 F87 Pro / FEFE接收器候选 0x0C45:0xFEFE | 未启用读取 | 待研究 | 未开放 |
| RK R87s / FA70接收器候选 0x25A7:0xFA70 | 未启用读取 | 待研究 | 未开放 |
| iKF V11 MAX / Report02接收器 0x0D8C:0x0334 | 未启用读取 | 待研究 | 未开放 |
| 漫步者 W820NB经典版 / 蓝牙 | 未启用读取 | 待研究 | 未开放 |
| 雷蛇毒蝰 V2 Pro / 接收器DPI诊断 0x1532:0x00A6 | 未启用读取 | 待研究 | 未开放 |
| 前行者 VX01 耳机 / FD32布局（原反馈误写V10） 0x0D8C:0x0334 | 未启用读取 | 待研究 | 未开放 |
| 蝙蝠骑士 BK75 / 反馈接口待核对 | 未启用读取 | 待研究 | 未开放 |
