# 合作者 PR 归档与架构映射

记录 `late-autumn-gif/mousedock` 上 9 个合作者 PR（#2 – #10）的内容、原始代码位置，以及在当前 `DeviceRoutes` 架构下的落点。

原始 diff 与元数据存在 `D:\OMNIDEV\Reports\mousedock-prs\`（每个 PR 三个文件：`.json` 元数据、`.diff` 完整补丁、`.files.txt` 文件清单）。

## PR 一览

| PR | 标题 | 状态 | 提交 | 与当前架构的关系 |
| --- | --- | --- | --- | --- |
| #2 | Add taskbar battery rings and fix device display, polling, and window | MERGED | 已进 main 历史 | 任务栏圆环的初版，现行 `DeviceBatteryRing` 是其演进而来的 |
| #3 | 新增屏幕右侧电量显示与键盘适配，修复连接切换后的旧电量残留 | CLOSED（不可 reopen） | head 已不在分支上 | `MchoseKeyboardProtocol` 首次落地；右侧窗口 `ScreenEdgeWindow` 已存在 |
| #4 | 原生嵌入任务栏电量显示并更新至 v1.3.9 | MERGED | 已进 main 历史 | 任务栏原生嵌入的基线 |
| #5 | fix: 修复迈从 A7 V2 Ultra+ 电量读取 | CLOSED（不可 reopen） | head 已不在分支上 | 迈从 A7 V2 Ultra+ 支持已在 `VendorProtocol.Mchose` |
| #6 | 修复开机自启不直接显示电量bug | CLOSED（不可 reopen） | head 已不在分支上 | 被 #7 取代；`K87DiscoveryRegression` 由此引入 |
| #7 | 修复了开机自启电量不会自动显示的bug | MERGED | `eb56840` 一带 | 任务栏就绪回退（`TaskbarAnchor.TryGet` 失败时 `Opacity=0`，500ms 重试） |
| #8 | 新增 ROG 游侠 RX2 电量读取并修复狼蛛 SC580 接收器识别 | CLOSED（可 reopen，CONFLICTING） | 分支 `codex/sc580-m137-feature-battery` | ROG Omni 与 M137 的来源；我们另做了 `RogScope` |
| #9 | 扩展外设电量适配并完善任务栏显示与主题配色 | CLOSED（可 reopen，CONFLICTING） | 分支 `codex/taskbar-m9pro-rainy75` | M9 Pro、K87 读取器、任务栏样式与配色 |
| #10 | 让任务栏和屏幕右侧支持悬浮图标样式 | CLOSED（archived，只读） | `a72cb85` | 已快进合入 main |

## 文件级映射

合作者 PR 一共引用 109 个不同的文件。其中 95 个在当前仓库里存在；14 个不存在，按性质分三类。

### 一、被等价实现取代（架构重构的直接结果）

| PR 里的文件 | 现位置 | 取代原因 |
| --- | --- | --- |
| `RecognitionPolicy.cs` | `DeviceRoutes.cs` | `34e922b` 把「能查吗」与「怎么查」合成一张路由表，`CanQuery` 换成 `IsKnown`，`Find` 给出 Reader |
| `HidDiscovery.RogOmniKeyboard.cs` | `HidDiscovery.RogScope.cs` | 合并成单一 ROG Omni 路由，并多认 `0x1C25` 夜魔 X |
| `RogOmniKeyboardProtocol.cs` | `VendorProtocol.RogScope.cs` | 同上；配对表要求「恰好一台已收录键盘」，两次一致才采用 |
| `VendorProtocol.RogOmniKeyboard.cs` | `VendorProtocol.RogScope.cs` | 同上 |
| `VendorProtocol.F75.cs` | `VendorProtocol.VgnF1F75.cs` | VGN 蜻蜓与狼蛛 F75 合并到一个文件，共用 `HidLink` |
| `VendorProtocol.VgnF1.cs` | `VendorProtocol.VgnF1F75.cs` | 同上 |

### 二、协议同源但只做身份诊断（未移植）

| PR 里的文件 | 说明 |
| --- | --- |
| `ReceiverIdentityProtocol.cs` | `248A:5B2F`，`FF00/0001` Feature 33。帧是 `12 01 20 00 seq`——与 `M137FeatureProtocol` 完全同一套 GetInfo，区别是它走 SetFeature + 两次 GetFeature 取身份串，**不输出电量**。电量侧我们已有 M137 主动查询。 |
| `VendorProtocol.ReceiverIdentity.cs` | 上述协议的句柄绑定层，同样只服务诊断 |

这两者当初是「确认 248A:5B2F 到底是什么设备」的一次性探针，不是长期功能。

### 三、测试文件（未移植）

| PR 里的文件 | 覆盖的内容 | 现状 |
| --- | --- | --- |
| `RogOmniKeyboardRegression.cs` | ROG Omni 配对表与电量帧 | 现由 `RogScopeRegression` 覆盖，且多测 1C25 |
| `ReceiverIdentityRegression.cs` | 248A:5B2F 身份诊断 | 对应功能未移植，测试无从谈起 |
| `M9ProRegression.cs` | M9 Pro 被动 Input9 | **已移植**（`a932696`），49 条断言 |
| `TaskbarStyleChecks.cs` | 任务栏样式切换 | 未移植，样式逻辑现由 `TaskbarBatteryStrip` + `SharedDisplayStyleChecks` 覆盖 |
| `TaskbarThemeChecks.cs` | 任务栏跟随主题配色 | 未移植 |
| `StartupRecoveryChecks.cs` | 开机自启后的任务栏恢复 | 未移植，正是 `KNOWN_ISSUES.md` 里自启动那条待验项的测试化版本 |
| `TaskbarRegressionChecks.cs` | 任务栏窗口整体回归 | 未移植 |

## 已合入的内容

| 来源 | 提交 | 内容 |
| --- | --- | --- |
| PR #7 的 K87 可达性 | `de19a83` | `IsKnown` 放行 `MchoseKeyboardProtocol.IsSupportedDevice`，断言按 PR 原话改回「必须可达」 |
| PR #9 的 K87 读取器 | `4488c4a` | `VendorProtocol.K87` + `HidDiscovery.K87` + `MchoseKeyboardProtocol.Read(ILink)`，按物理实例开句柄 |
| PR #9 的 M9 Pro | `ff2814a` | `M9ProProtocol` + `HidDiscovery.M9Pro` + `VendorProtocol.M9Pro`，被动 Input9 |
| PR #9 的 M9 Pro 回归 | `a932696` | `M9ProRegression`（49 条）；同时补了 `TryDeviceKind` 的 FF05 分支和两个标志位的 M9Pro 分支 |
| PR #10 全部 | `a72cb85` | 六种悬浮图标样式、屏幕右侧竖向布局、样式分栏保存、`ReferenceBatteryIcon` |

## 关于 PR 状态

- **#2 / #4 / #7** 当初就合并了，代码在 main 历史里。
- **#3 / #5 / #6** 无法 reopen：head 都指向 `JEFF-dev-ui:main`，分支已前进到 `a90e81e2`，PR 记录的提交不在分支顶端，GitHub 拒绝恢复。
- **#8 / #9** 能 reopen，但基线停在 2026-11-04 的 `ca1b5689`，与现在的 `DeviceRoutes` 冲突。
- **#10** 被 GitHub 锁成归档态，reopen、merge、评论全部返回 `archived`。只能绕过 PR 直接快进合并。

页面状态无法统一改成 Merged。代码内容的落点以上面的表为准。
