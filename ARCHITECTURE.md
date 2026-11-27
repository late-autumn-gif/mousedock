# 架构

## 总体

单体 WPF 应用，单进程常驻，托盘 + 六种显示方式。没有后端、没有数据库，普通配置在 `%APPDATA%\MouseDock`；1.2.1 便携测试标记或 `MOUSEDOCK_DATA_DIR` 可隔离配置及实例，测试模式不改开机启动、不访问更新源。核心复杂度在 HID 枚举与厂商协议，不在界面。

1.2.1 设置用六分类控制既有命名控件的可见性，保留事件和保存路径。`TraySurfacePicker` 以原生 WinForms 按钮实现六模式互斥快选，仍由 `App.SetSurface` 调度既有窗口。`SettingsStore.TrySave` 回报真实写入结果，设置底栏通过 `App.SettingsSaveCompleted` 显示成功或失败。卡片详情绑定原 DeviceInfo，不创建额外采集任务。硬件发现、去重和协议模块未变更。

```
App.xaml.cs
 ├─ 单实例互斥 / 诊断命令分发
 ├─ 托盘与菜单
 └─ BatteryService（每型号 daemon worker，30/60 秒轮询）
       └─ AutoDetectSource（0.5 秒 tick 的自动识别）
             └─ HidDiscovery（枚举 + 归并 + 读取）
                   ├─ VendorReaderTable（通用读取器表）
                   ├─ HidDiscovery.*.cs（各品牌专用读取器）
                   └─ VendorProtocol.*.cs（协议解析）
       └─ DeviceInfo（UI 绑定）
Views/  DockWindow / MiniWindow / IconWindow / SettingsWindow / ReportDevicePicker
Controls/ DeviceBatteryBadge / DeviceBatteryRing
Services/FeedbackService（报告导出）
Services/UpdateService（自更新）
```

## 桌宠动作

`DesktopPetWindow` 的自由形态通过 `CoopanionMotion` 在进程内执行 Coopanion v0.2.23 原版 `body.js` 和 `rig.js`。宿主将 WPF 指针事件送入原生动作状态机，并按其输出绘制角色图集。自主活动区域与固定电量框相隔 2 个物理像素。手动拎起和落下期间，由鼠标穿透的透明窗口绘制角色，脱离原活动区域的高度限制，落地后回到原区域。宿主设置动作间歇，在空闲时通过引擎追加手势，并关闭声音输出。减少动态效果时显示静态形象。

键鼠联动形态继续使用 `BongoPetScene` 和现有键鼠输入通道。设备读数与状态由电量服务提供。

自由桌宠的行走、小跑、起跳、被拎起及手势可使用 `LayeredPetRig`。四个角色各自包含透明部件图集及在 320 像素坐标中的关节。宿主从原版 DeepSeek 形象读取颈、双臂、双腿、腰、衣摆和长发的变形输出，WPF 按注册点绘制。已用于坐地、睡眠和重摔回神的图帧继续用于这些阶段。

部件图集由内置 imagegen 编辑原图的第一格站姿。提示要求保留脸、发饰、比例、服装和朝向，按九宫格拆出头与颈、无手脚的衣身、后发或尾巴、左右手臂、配件与左右腿，补绘藏在衣身下的肩与大腿，并用透明像素隔开各件。心月狐的复绘额外要求衣身在腿上方结束、保留手扇和去除轮廓外的底色。WPF 只读取标定的矩形，图片及绑定可在 `Assets/Characters/RigParts` 与 `Controls/LayeredPetRig.cs` 核对。

## 识别链路

VT3 MAX `24AE:1417/0105` 沿用原 HidDiscovery → InstanceScope → ExtraDeviceRoutes → BatteryService 链路。`RapooAhubReader` 在原 ReferenceQueryGate 中按次刷新打开、校验及释放接口，使用 `RapooAhubProtocol` 执行只读初始化并读取新报告 07。Feature08 兼容实机 ACK-first 布局；没有独立常驻会话或跨轮电量缓存。该方案待实机验证，详见 [限制](docs/RAPOO-AHUB-SESSION.md)。

1. `HidDiscovery.Enumerate` 用 SetupAPI + hid.dll 枚举所有 HID 顶层集合，按 `InstanceScope` 把同一物理设备的多个接口归并成 `Bucket`。
2. 每个集合先判类型（`TryDeviceKind` / `RefineKind`），并累计到 `Bucket.Counts`。
3. 路由表 `DeviceRoutes.Find(vid,pid,product)` 给出已知设备的类型与通道；表里没有的才按集合计数兜底。这是 2026-11-08 修掉「鼠标接收器被标成键盘」的地方。
4. `QueryVendorCollections` 与各品牌 `Query*` 方法读取电量；每个读取器自己判断认不认这台设备。
5. `Bucket.ToDevice()` 生成 `DetectedDevice`，`HandledBy` 记录最终接管它的读取器，`device.txt` 会写出这条处理链。
6. `AutoDetectSource` 转成 `DeviceInfo` 给界面，`BatteryService` 负责轮询与快照。

## 关键约定

- **允许查询与路由是两件事**：`DeviceRoutes.IsKnown` 决定能不能发厂商命令，`DeviceRoutes.Find` 决定类型与通道。两者集合不同，不要合并。
- **读不到就写 `—`**：`Level = -1` 表示未知，不拿缓存冒充新读数，不拿 0 当 100%。
- **两次同型有效帧**：多数品牌读取器要求连续两次一致回应才落库，单次回包只进诊断。
- **安全模式不发查询**：`safeMode` 下只读已有缓存。
- **实例隔离**：同一型号两台设备靠 `InstanceScope` 的物理键区分，不按 VID:PID 合并。
- **写操作只限厂商查询帧**：不改 DPI、灯效、回报率。

## 目录

| 路径 | 职责 |
|---|---|
| `Services/Detection/HidDiscovery.cs` | 扫描主循环、Bucket 归并、ToDevice |
| `Services/Detection/HidDiscovery.*.cs` | 各品牌 `Query*` 读取器 |
| `Services/Detection/DeviceRoutes.cs` | 路由表、`IsKnown`、`HandledBy` 枚举 |
| `Services/Detection/VendorReaderTable.cs` | 通用读取器表（Razer/ROG/SteelSeries/…） |
| `Services/Detection/VendorProtocol*.cs` | 协议帧构造与解析，按品牌拆分 |
| `Services/Detection/ReferenceProfiles.cs` | 1.0.2 参考设备移植 |
| `Services/Detection/BatteryReplay.cs` | 用户报告回放 |
| `Services/FeedbackService.cs` | 错误报告 zip（device/probe/hid-devices 等） |
| `Services/UpdateService*.cs` | 自更新与替换脚本 |
| `docs/` | 设备协议、支持清单、复核矩阵 |

## 测试

- `tests/MouseDock.ProtocolTests`：16093 条离线断言，覆盖帧解析、拒绝路径、路由、ToDevice。
- `tests/MouseDock.Checks`：64 项界面/运行时检查，需要桌面会话。

详见 [TESTING.md](TESTING.md)，已知限制见 [KNOWN_ISSUES.md](KNOWN_ISSUES.md)。

## 扩展一个新品牌

1. 在 `VendorProtocol.<Brand>.cs` 写帧构造与解析，加断言。
2. 在 `DeviceRoutes.IsKnown` 加号段（决定能否查询），在 `DeviceRoutes.Find` 加类型与通道。
3. 在 `HidDiscovery.<Brand>.cs` 写 `Query*` 读取器，设置 `HandledBy`。
4. 在 `HidDiscovery.TryCollect` 或 `VendorReaderTable` 接入调度。
5. 补 `docs/` 协议说明与 `docs/VERIFICATION-*.md` 的证据等级。
