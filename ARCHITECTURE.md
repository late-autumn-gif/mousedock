# 架构

## 总体

单体 WPF 应用，单进程常驻，托盘 + 四种界面形态。没有后端、没有数据库，配置在 `%APPDATA%\MouseDock`。核心复杂度在 HID 枚举与厂商协议，不在界面。

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

## 识别链路

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
