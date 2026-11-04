# 英菲克（Inphic）电量协议

2026-11-04。协议从网页驱动本地桥里逆出来，还没插 IN9 Pro 真机验过。

实现：`src/MouseDock/Services/Detection/VendorProtocol.Inphic.cs`
接线：`HidDiscovery.cs` 对 `1D57:FA60` 按归并后的接收器读取一次。IN9 Pro 蓝牙回退路径位于 `BluetoothDiscovery.cs`。
真机验证：`dotnet run --project tests/MouseDock.ProtocolTests -c Release -- --inspect-inphic-battery`

## 一、网页驱动是哪一个

官网那个「驱动下载」页面上，真正算网页驱动的只有一条：

- 页面地址 `http://hub.inphic.cn/`（Vue SPA，标题 HUB WEB）
- 它自己不开 WebHID，靠本地桥 `ws://localhost:10085` 发 hex 帧
- 装桥的包就是页面按钮指向的 `http://hub.inphic.cn/downloads/Inphic_SetUp_V1.01.exe`
  （4,983,338 字节，Inno Setup 打包，装出 `Inphic.exe` 4,309,504 字节 + `hidapi.dll`
  + `register_protocol.bat`，注册 `myapp://` 协议）

`D:\英菲克` 里另外那几个是桌面端，不是网页驱动：

| 目录/文件 | 是什么 | 跟电量有没有关系 |
| --- | --- | --- |
| `INPHIC HUB\INPHIC.exe` | DuiLib 桌面 HUB，导入 hidapi，模型表里有 `IN9 PRO` / `IN9 PRO V2` / `IN9 Ultra` | 有关系，它自己读 HID，走的是本文这套 |
| `Inphic Mouse\Inphic Mouse.exe` | IN9/A9 的 MFC 老驱动，`config.xml` 里 `VID_248A&PID_5C2E/5C2F` | 老协议，只覆盖有线 IN9 |
| `INPHIC\INPHIC.exe` | 更老的 DuiLib 版，同 HUB | 同 HUB |
| `INPHIC 英云\INPHIC 英云.exe` | Go + astilectron 的「英云」2.0.15，`smurfs/driver` 那套 Dongle 协议 | 另一套固件，先不管 |

## 二、本地桥做了什么（逆向结论）

桥在 `hid_read` 之后只做一件事：把收到的报告前 5 个字节拼成 hex 串推给页面。

```
ws 帧 = "8x" + "01" + %02x%02x%02x%02x%02x
```

五套格式串（`Inphic.exe` 里 0x6068bc / 0x606b60 / 0x606b7c / 0x606b98 / 0x606bb4）：

```
8001%02x%02x%02x%02x%02x     8101...     8201...     8301...     8501...
```

对应函数 `FUN_0040e3d0`（8201）、`FUN_0040e750`（8301）、`FUN_0040f9e0`（8501）等，
每个函数只管一条通道，读法完全一样：`hid_read(h, buf, 100, ...)`，取 `buf[0..4]`。

页面侧（`index-BcHQx9B6.js`，偏移 ~1384315）把这 7 字节拆成：

| 下标 | 含义 |
| --- | --- |
| 0 | 通道 `0x80`..`0x85`（`0x80` 有线、`0x81` 2.4G、`0x82`/`0x83` 高速/8K、`0x84` 蓝牙、`0x85` 有线 8K） |
| 1 | `0x01` 状态帧；`0x00` 是掉线事件（3 字节帧） |
| 2 | 报告 ID 位（无编号报告时这一位就是机型号） |
| 3 | 机型号（`149` = IN9 Pro） |
| 4 | 电量类子命令 `0x40` / `0x41` |
| 5 | 状态：`1` 按百分比、`2` 满电 100%、`3` 充电中 |
| 6 | 电量 `0..100` |

所以原始 HID 输入报告就是：

```
[report id] [机型号] [0x40|0x41] [状态] [电量]
```

没有 report id 的设备少头一位：

```
[机型号] [0x40|0x41] [状态] [电量]
```

## 三、取数顺序

对 `1D57:FA60`，打开同一接收器的 `000A/0000` 输入集合，再对 `000B/0000` Feature 集合发送初始化帧 `0C 0A 01 FE 01 FE 00 00 00 00`，随后从输入集合等电量上报。官方 `hidapi.dll` 会按设备声明的 Feature 长度补零，报告中的 `MI_02&COL04` 是 262 字节，`MI_02&COL03` 提供 5 字节输入。

IN9 Pro 在系统和标准 BLE 电池服务均无读数时，订阅服务 `6e3bb679-4372-40c8-9eaa-4509df260cd8` 下的 `FEE4` 通知，解析前四字节 `[95] [40|41] [状态] [电量]`。安全模式及停用识别时跳过这项订阅。

旧版通用英菲克读取器沿用三级流程：

1. **被动听 1.5 秒**（250ms 一片）。官方页面就是这么拿电量的，桥只转发。
2. **主动唤醒一次**。桥在 `hid_open_path` 之后对 `usage 0x0B` 的集合发过一帧 10 字节 feature：

   ```
   0C 0A 01 FE 01 FE 00 00 00 00
   ```

   出自 `FUN_00409820`：`local_6c = 0xFE010A0C`、`local_68 = 0xFE01`，
   紧接着 `hid_send_feature_report(h, buf, 10)`，再 `Sleep(200)`。
   这是官方全程唯一一次主动写设备，当初始化/唤醒用。
   鼠标的厂商集合上发同一帧没实证，所以放在被动失败之后，发完再听 800ms。
3. **SC525 兜底**：`0E 20` + 14 个 00，一问一答，600ms 超时。

页面侧另有两条负向证据，说明 0x40/0x41 不是查询：页面 `sendMessage` 只有 4 个调用点，
全在「恢复默认设置」；没有 getBattery，没有定时器。

## 四、设备身份

| VID | PID | 说明 |
| --- | --- | --- |
| `0x1D57` | `0xFA60`、`0xFA65` | 网页驱动桥认的 2.4G 接收器（`Inphic.exe` 里硬编码的 `VID_1D57` / `PID_FA60` / `PID_FA65`） |
| `0x248A` | `0x5C2E`、`0x5D2E`、`0x5E2E` | 有线本体（`Inphic Mouse\config.xml`） |
| `0x248A` / `0x249A` | `0x5C2F` | 2.4G 接收器 |
| `0x32C2` | `0x0027` | AULA/INPHIC SC525，已有实现，走 `0x0E 0x20` 那条 |

`0x248A:0x5C2F` 与 AJAZZ AJ159 系列撞号，所以判定要带产品名：名字含 `AJ159` 让给黑爵，
名字为空照旧放行（接收器常只报 `Wireless Receiver`）。

## 五、兜底

听 1.5 秒没有状态帧，再试 SC525 那条一问一答（`0E 20` + 14 个 00 → 32 字节输入，
`raw[8] & 0x7F` 电量、`raw[8] & 0x80` 充电、`raw[9] & 0x80 == 0` 休眠）。
这条没在英菲克真机上验过，只是同主控换标的推测。
