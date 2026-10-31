# 鼠标电量的读法：通道、交互、编码

这份是给后来人看的。看完应该能自己给一只新鼠标补读取器。

## 一、物理通道只有三种

**feature report**。`HidD_GetFeature` / `HidD_SetFeature`，一问一答，同步，不占输入队列。标准 HID 的电量用法页走这条。罗技 C54F 也走这条。

**output report 写、input report 读**。`WriteFile` 发出去，`ReadFile` 等回来。罗技 C53F（G304 接收器）的 feature 长度是 0，只能走这条；Razer、ROG、SteelSeries 的厂商集合也多是这条。等回复必须限时，否则一次探测能把界面拖死。

**纯被动 input**。不发帧，只听设备自己推。AULA SC900（`0a` 报告）、雷柏 VT2 MAX（`07` 报告）属于这种。代价是拿不到「现在立刻」的值，只能等下一次推送。

蓝牙鼠标不在这三条里，走 GATT `0x180F` / `0x2A19`。

## 二、交互有五种模型

**一问一答**。发一帧，回一帧。占大多数。

**先发现再读**。HID++ 2.0 就是：先 `getFeature` 拿 feature 索引，再用索引发 `getStatus`。两步之间索引会变，所以每次都得先发现。`LogitechHidpp.FeatureIndex`。

**要先登录**。EXX Alpha 7 先发 cmd 0x00 带固定 token `EF09B08AEEC83D7C`，之后 0x1E 才给设备信息。鉴权算法没还原。

**要申请会话**。Pulsar 用 cmd 3 申请/释放在线会话，cmd 4 才读电量。不申请，设备不回。

**两步枚举**。RIDLAG Owl 和 VGN KC2 都是 `B5 03` 列出配对设备、`B3 06` 读电量。接收器上挂了不止一只设备时必须先枚举，否则读到的可能是别人的。

## 三、电量编码有四种

**直接百分比**。`b[n]` 就是 1..100。最常见。

**电压折算**。给 mV，查表插值。Finalmouse cmd=5 给 2500–4600mV，按 7 个点插值；罗技 0x1000 理论上给电压，但 G304 实际返回的 `reply[0]` 直接就是百分比（90），所以先试百分比、不合法再当电压。这个坑踩过。

**台阶**。SteelSeries 只给 21 级，`(step-1)*5`。不是线性百分比，别当 0..100 用。

**状态位混在电量里**。AULA SC525 的 `raw[8]` 低 7 位是电量、最高位是充电；雷柏 `b[7]` 是状态枚举（0 休眠、1 在线、2 充电）。这种必须先掩码再判范围。

## 四、充电状态怎么判

各家的判法不统一：

罗技 HID++ 看 `state` 字节，落在 1/2/4 算充电。官方实现是 `value = reply[0]`、`state = reply[2]`，电量是电量、状态是状态，两码事。

换电池的型号（G304/G305、G602/G603、办公 Nano 鼠）不存在充电状态。`AutoDetectSource.cs` 里用 `IsReplaceableBattery` 强制 `Charging = false`，免得把「电量低」画成「充电中」。

充电时电量照实显示。凑 100% 是错的——那会让用户以为电池满的。

## 五、校验和

有校验的必须先算对，否则设备直接丢弃：

COMPX 整帧 `Σ%256 == 0x55`（17 字节）。Razer Carbon `b[89] = XOR(b[3:89])`（91 字节）。SCYROX `b[63] = sum(b[:63])`。WAIZOWL `b[63] = (0xA1 - sum(b[:63]))`。

无校验的也别假设「回包一定对」——HID 输入队列会积压，上一轮的残包会被当成这一轮的响应。`LogitechHidpp.Await` 按 report id / 设备索引 / feature 索引三重过滤，就是为了这个。

## 六、已实现的读取器

| 品牌 | 协议 | 入口 | 通道 |
|---|---|---|---|
| 罗技 | HID++ 2.0：0x1004 function 1 / 0x1001 / 0x1000 + 名字 0x0005 | `LogitechHidpp.Probe` | feature 或 output |
| AULA | 32/33 字节 Identify 应答 | `VendorProtocol.Session` | output |
| AULA SC900 | 被动 `0a` 报告 | `TryReadStatus` | 被动 |
| MCHOSE | FF01 `11 F9` + 19×FF | `TryReadMchose` | feature |
| VGN / Neon75 | 0xFFFF 厂商集合只读状态 | `TryReadStatus` | feature |
| Razer | feature 0x5B 91 字节 + XOR + tx 轮转 | `TryReadRazer` | feature |
| ROG | 64 字节 `03 12 07` 两种头 | `TryReadRog` | output |
| SteelSeries | 64 字节 `D2`，21 级台阶 | `TryReadSteelSeries` | output |
| Finalmouse | tx/rx 双通道，cmd 38/5 | `TryReadFinalmouse` | output |
| ATK / Lamzu / G-Wolves | COMPX 17 字节，Σ==0x55 | `TryReadCompx` | output |

## 七、还没搬的

EXX（要 token）、Pulsar（要会话）、RIDLAG / VGN KC2（两步枚举）、RAWM（分片 JSON）、SCYROX、WAIZOWL、AUSDOM、AM Infinity、ZYNOO、AKKO、LunaFury / AttackShark / WLMouse（Feature83）、狼蛛 SC525、达尔优 A980 Pro、雷柏 VT2 MAX / 0x14A5。

没搬不是忘了，是每条都要单独验：握手时序错一步就读不到，校验算错一位设备就不回。硬塞进通用分支会互相抢报告队列——`Paths(vid, pid)` 每次重新打开设备，并发探测会打架。
