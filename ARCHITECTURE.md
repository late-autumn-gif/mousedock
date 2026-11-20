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

## 桌宠动作与输入跟随

`DesktopPetWindow` 根据电量与指针状态选择姿势。刻晴使用独立内嵌的九姿势图集和卡片头像。战技和爆发在右键菜单手动触发，分别以 50 毫秒一帧播放；动画结束后重新读取当前界面状态以选择姿势。隐藏、拖动或启用减少动态效果时停止绘制战斗帧。设备读数和状态继续来自电量服务。

### 场景分层

v1.3.0 的桌前场景由 `Controls/DesktopPetDeskScene.cs` 实现：内部使用 300×236 设计坐标，按 0.72 输出约 216×170 DIP，接近常见键鼠同步小猫桌宠的占用；图层顺序固定，逐帧只重绘 `DrawingContext`，不重建可视化树：

1. 背景层 `_back`：角色光晕与桌垫落影。
2. 角色层：普通角色是窗口持有的九姿势 PNG `Image`（按图集内容归一化，刻晴 ×1.12，其余 ×1）；德蕾琪娜使用分层矢量控件 `DracenaPetRig`（后发、编发、躯干、头部、眼睛、刘海、发饰分层），由 `SetCharacterVisual` 在同一图层位置替换。
3. 前景层 `_front`：霓虹发光桌垫与描边线缆、小玩偶、5 行键帽键盘、鼠标、双手、前沿文本条与浮起键帽，全部矢量绘制。
4. `_sparks`：六花与刻晴在充电、满电时的星星点缀。

配色在 `ApplyPalette` 时从角色的 `Surface/Outline/Accent/Hand/Sleeve` 派生并 `Freeze`，所有角色共用一套场景，不各自复制资源（德蕾琪娜的金色霓虹来自她的强调色）。键位几何由 `PetKeyboardLayout` 的行、偏移和宽度计算，`KeyBounds`、`KeyboardBounds`、`MouseBounds`、`MatBounds`、`DisplayBounds` 同时供绘制、手部定位和回归检查复用。

### 输入链路

```
DesktopPetInputHook（专用后台线程 + 消息泵）
  ├─ WH_KEYBOARD_LL → PetInputCue（vkCode、scanCode、标签、时间戳）
  └─ WH_MOUSE_LL    → PetInputCue（左 / 右 / 中键的按下与松开）
        └─ ConcurrentQueue（上限 256，满时丢最旧）
              └─ UI 线程 33ms DispatcherTimer → Drain(64 槽缓冲) → DesktopPetInputState
                    ├─ trail：最近 8 个按键，1.5 秒后逐条过期
                    ├─ LastKey / LastMouse：1.6 秒后过期；HeldButton 记录按住，松开丢失时 30s 自愈
                    └─ DesktopPetDeskScene.Render(now, motion, state, showLabels, mirrorMouse)
                          ├─ 双手：指数平滑到目标键位，按键后 180ms 保持下压
                          ├─ 键帽：420ms 高亮，标签约 700ms 浮起
                          ├─ 前沿文本条：按宽度整标签截断的最近按键（最多 8 个），520ms 光标闪烁
                          └─ 鼠标：左右半侧按住/点击高亮，手部停留到松开后再延后 700ms
```

钩子线程与 WPF 线程分离：键盘回调只读 `KBDLLHOOKSTRUCT` 的 `vkCode`/`scanCode`，忽略修饰键并对同一键做 30ms 节流；鼠标回调识别左右中键的按下与松开。两者都只入队后立即调用 `CallNextHookEx`，不等待 UI 线程。窗口在 `DrainInput` 阶段按「镜像鼠标点击」开关丢弃非键盘 cue，关闭该开关时鼠标事件不会进入状态模型。`DesktopPetInputState` 只持有最近一次按键/鼠标事件、按住状态、连击数和文本条字符序列，供场景逐帧读取；窗口层面的 `TypingPulse` 只取最近按键的时间参与抬升，不读取按键内容。可显示标签由 `PetKeyboardLayout.LabelOf` 生成：字母和数字直接给出字符，编辑、导航和功能键给出固定名称，其余键使用前台窗口的 `ToUnicodeEx`，失败再退回 `MapVirtualKey` 或 `0x` 前缀。输入优先级由 `DesktopPetWindow` 统一裁决：键鼠活跃（`InputActive`）时把行为调度标记为 `blocked`，立即结束随机动作；低电与充电优先于空闲随机动作；普通角色的表情仍受 PNG 帧限制。`DracenaPetRig` 把这一优先级映射为 Focus / Sleepy / Happy / Yawn / Smile，并在输入停止后自然回到微笑。

### 线程与生命周期

- 低级钩子只能在带消息泵的线程安装。`DesktopPetInputHook` 自建名为「MouseDock 桌宠输入钩子」的后台线程，`SetWindowsHookEx`、消息循环与 `UnhookWindowsHookEx` 都在该线程执行；回调委托保存在 `_keyboardProc` / `_mouseProc` 字段中，避免被 GC 回收造成崩溃。`Start()` 等待就绪事件最多 2 秒，`Stop()` 的 `Join` 上限同为 2 秒，失败只记录 `LastError` 并保持降级状态，不向调用方抛异常。
- 一次 `Start()` 同时安装键盘与鼠标两个钩子；关闭「镜像鼠标点击」时鼠标钩子仍会安装，但 `DesktopPetWindow.DrainInput` 会在界面线程丢弃非键盘 cue，`DesktopPetDeskScene` 也不呈现点击反馈。需要完全停止两个钩子时关闭「镜像键盘输入」。
- 启动条件是「镜像键盘输入」开启、窗口 `IsVisible`、`Application.Current` 存在，且环境变量 `MOUSEDOCK_PET_INPUT` 不等于 `off`；`SyncInputCapture` 幂等，重复调用不会重复安装。
- 隐藏窗口、切换显示方式或关闭监听开关时调用 `Stop()`：卸载钩子、结束消息泵并清空 `DesktopPetInputState`；`DesktopPetWindow.OnClosed` 再 `Dispose()` 兜底。
- 鼠标进入桌宠窗口时设置 `SuppressMouse`，避免把用户点击桌宠自身当作被镜像的点击。
- 帧计时器只在 `Motion` 或跟随生效时运行。减少动态效果（`Motion == false`）下仍保留键帽高亮、前沿文本条和鼠标按键高亮，但双手直接落位，不播放平滑过渡、呼吸与随机动作。

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

- `tests/MouseDock.ProtocolTests`：39,445 条离线断言，覆盖帧解析、拒绝路径、路由、ToDevice。
- `tests/MouseDock.Checks`：173 项 Windows 本地界面/运行时检查，需要桌面会话；2026-11-20 键盘桌宠与德蕾琪娜回归为 172 通过、0 skipped，1 项 Taskbar 透明像素检查在改动前的 HEAD 上同样失败。`--filter 键盘桌宠` 只跑 6 项桌宠专项；`--pet-input-live` 用 `keybd_event` 合成 F24 验证系统输入到钩子的链路，属于合成输入，不等于真人敲击。

详见 [TESTING.md](TESTING.md)，已知限制见 [KNOWN_ISSUES.md](KNOWN_ISSUES.md)。

## 扩展一个新品牌

1. 在 `VendorProtocol.<Brand>.cs` 写帧构造与解析，加断言。
2. 在 `DeviceRoutes.IsKnown` 加号段（决定能否查询），在 `DeviceRoutes.Find` 加类型与通道。
3. 在 `HidDiscovery.<Brand>.cs` 写 `Query*` 读取器，设置 `HandledBy`。
4. 在 `HidDiscovery.TryCollect` 或 `VendorReaderTable` 接入调度。
5. 补 `docs/` 协议说明与 `docs/VERIFICATION-*.md` 的证据等级。
