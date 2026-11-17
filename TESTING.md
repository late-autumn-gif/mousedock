# 测试

2026-11-17 登录启动与界面恢复：Release 编译和 1.2.2 自包含 win-x64 发布通过。桌面回归 166/166，离线协议与集成断言 39,416 条，旧偏好 10 项及 K87 13 项通过。发布 EXE 使用独立配置执行 `--tray`，在 150% DPI 恢复迷你窗坐标，重复静默实例退出且未打开设置，关闭迷你窗后保存样式和收起状态，再次启动仍驻留托盘。实机启动注册值保持原样。未执行实际注销、整机重启、混合 DPI 拖移或启动性能量化。

2026-11-16 桌宠待机：桌面回归 164/164、离线协议 39,410 条通过。模拟时钟覆盖随机调度、静息、唤醒与落地，四角色 WPF 窗口通过图集、充电、动作和减少动态效果检查。单文件 EXE 通过启动截图和便携设置保存验证。未做长时间资源测量、跨 DPI 实鼠拖拽和高刷新率组合验收。

2026-11-16 单文件便携测试包：自包含 EXE 在无标记文件的独立目录运行 --shot，产出 18 张截图；旧设置迁移结果写入 EXE 旁 data，MouseDock 登录自启注册值未变化。解包后的托盘图标与原资源哈希一致，内嵌许可与外置许可一致。普通构建 Light UI 专项 10/10 通过。

2026-11-16 字母标识图标：发布的 EXE 提取图标已核验三色像素，ICO 九种尺寸可解码，发布资源与源码一致。Tray 专项 12/12 通过，0 跳过。

2026-11-16 卡片密度移除：Light UI 专项 10/10 通过，0 跳过，覆盖设置导航和卡片密度入口移除。

2026-11-16 托盘设备勾选与显示方式：Light UI 专项 10/10 通过，0 跳过，覆盖直接编辑、其他设备选中状态保留、设置落盘和恢复跟随。

2026-11-16 最终集成包：Release win-x64 自包含发布通过。全量离线协议与集成 26603 条、旧偏好 10 项、K87 13 项通过。桌面回归 161/162，0 跳过。分段电量环可区分未知和实测 0%，已通过像素回归。任务栏透明合成检查仍失败，与桌宠洁净构建记录一致。

2026-11-15 角色资源元数据更新：三张 PNG 各增加 58 字节资源修订标记。3/3 原始 PNG 块保持一致且 CRC 有效，3/3 WPF BitmapDecoder 解码成功，CopyPixels 的 SHA-256 与修改前一致。`dotnet build src/MouseDock/MouseDock.csproj -c Release --no-restore` 通过（0 警告、0 错误）。本轮无功能代码改动，未运行硬件测试或完整交互回归；现有测试包与已安装程序未替换。

2026-11-15 VT3 MAX 原识别链路/Feature08 实机反馈：`--verify-rapoo-ahub` 657 项、全量 27367 条通过，另偏好 10 项、K87 13 项通过。新增真实 ACK-first 失败帧、两种布局的 ACK/busy/字段偏移、配置名脱敏及完整回放；撤回常驻会话，验证现有查询门禁下的单轮事务、离线/无通知/初始化失败拒绝。新报告停于首个配置读取，未捕获电量帧，不能用离线初始化成功宣布电量已恢复。

2026-11-15 VT3 MAX 官方只读连接流程：`--verify-rapoo-ahub` 610 项、全量协议/集成 27320 条、旧偏好 10 项、K87 13 项通过。新增完整初始化、逐字节白名单、通知消费和完整事务回放拒绝测试；均无硬件 I/O，原生后台会话、重连/休眠和性能待实机验证，见 [说明](docs/RAPOO-AHUB-SESSION.md)。

2026-11-15 VT3 MAX 标准当前输入报告实验：`--verify-reported-trio` 586 项、全量协议/集成 26710 条，旧偏好 10 项、K87 13 项通过。覆盖通知优先、无通知补偿、第二份无效/缺失、RF 离线、包长/编号/范围错误、禁止无效通知和未清队列触发补偿，以及新事务回放拒绝路径。均为离线；真实报告仍三轮未知，目标固件 GetInputReport 支持待验证，见 [说明](docs/VT3MAX-INPUT-SNAPSHOT.md)。

2026-11-15 Xbox 无线适配器：`--verify-xbox-wireless` 80 项、全量协议 26693 条，另 10 项旧偏好和 13 项 K87 检查通过；`MouseDock.Checks --xbox-ui <PNG>` 覆盖系统读数经完整刷新/选择到 WPF 卡片及七种任务栏样式、缓存转换。均为离线，目标无线适配器待实机验证，见 [记录](docs/XBOX-WIRELESS.md)。

2026-11-15 SCYROX V6/V8 旧代接收器：`--verify-scyrox-legacy` 268 项、全量协议 26613 条、模拟完整显示链路及相关界面/路由 5 项通过。旧报告仍零查询，不能标为实机通过。见 [验证范围](docs/SCYROX-V6-V8-LEGACY.md)。

2026-11-15 迈从完整服务链路补充：`MouseDock.Checks --mchose-report-ui <用户G89 ZIP> <本地PNG>` 专项 1 项通过，覆盖 AutoDetectSource → BatteryService.PollAsync → 设备选择与可见列表 → WPF 卡片；G89 旧报告显示 100%，V9 无读数显示 —。仅改测试，未重新运行全量协议；最新两台均 — 的用户问题仍需新报告定位。

2026-11-15 迈从实机反馈修正：`--verify-mchose-feedback` 新增 506 项、全量协议 26345 条通过；相关界面/路由 5/5，加真实 G89 ZIP 到 WPF 卡片 1 项通过。G89 三轮重放均 100%、未充电；V9 原被动报告三轮仍无电量，官方固定查询需要下一轮实机验证。此前数量为历史记录。

2026-11-15 三设备离线适配：专项 `--verify-reported-trio` 569 项、全量协议 25839 条、相关界面/路由 5/5 通过。详见 [验证范围与硬件限制](docs/G89-V9PRO-VT3MAX.md)，未宣称三台目标设备实机通过。

## 1.2.1 轻驻本地验证（2026-11-14）

本轮协议断言 25270 条通过。界面检查 152/154，0 跳过；两项失败为改动前基线已有的角色字幕和照片几何重叠（基线 148/150）。新增四项检查覆盖六分类导航/密度、真实保存成功与失败、托盘六模式派发和卡片未知/过期状态。完整限制与命令见 [1.2.1 测试说明](docs/UI-1.2.1-TEST.md)。历史章节的旧数量不作为本轮结果。

两套测试，互相独立，都有明确的通过基线。

## 离线协议断言

```powershell
$env:DOTNET_ROOT='C:\Program Files\dotnet\dotnet-sdk'
& "C:\Program Files\dotnet\dotnet-sdk\dotnet.exe" run --project tests\MouseDock.ProtocolTests -c Release
```

- 16093 条断言，不碰硬件
- 覆盖：帧解析、身份校验、拒绝路径、安全模式、实例隔离、ToDevice 路由、各品牌协议
- 失败即抛异常，进程退出码非 0；输出 `PASS 16093 offline protocol/integration assertions`

新增断言放在 `tests/MouseDock.ProtocolTests/*Regression.cs`，优先用合成 `HidDiscovery.Bucket` 构造，不依赖真实设备。

## 界面 / 运行时检查

```powershell
& "C:\Program Files\dotnet\dotnet-sdk\dotnet.exe" run --project tests\MouseDock.Checks -c Release
```

- 119 项，需要交互式 Windows 桌面会话与 STA 线程。
- Windows 11 Build 26300 基线为 `119/119 checks passed, 0 skipped`，退出码 0。
- 任务栏检查使用真实任务栏与本机窗口，包含屏幕像素透明合成和两种宿主退出路径。
- 更新检查用隔离目录内的旧版、新版 PE 执行真实更新脚本，验证先等待旧进程退出，再替换并启动新版。测试关闭系统集成。

任意一项失败会输出 `FAIL` 并使进程以退出码 1 结束。环境限制导致的 `SKIP` 单独计数。

### 桌宠回归

```powershell
& "$env:DOTNET_ROOT\dotnet.exe" run --project tests\MouseDock.Checks -c Release -- --filter "Desktop pet"
```

检查原有三个角色和刻晴的图集、电量跟随和满电状态。刻晴用菜单触发战技和爆发，验证首尾帧、结束后恢复充电姿势及减少动态效果。测试使用模拟电量设备。

## 真机诊断

```powershell
D:\mousedock\MouseDock.exe --detect
```

结果写入 `%APPDATA%\MouseDock\detect.txt`。跑之前停掉常驻实例，否则 Feature 通道被占用，电量会显示未知。

2026-11-08 基线：ROG 游侠2 RX 无线版 52%、迈从 A7 V2 Pro 83%、竹林鸟 ZG2 未知。

## 发布验证顺序

1. `ProtocolTests` 全过
2. `Checks` 0 失败
3. `--detect` 三台设备名称/类型/通道正确
4. 替换 `D:\mousedock\MouseDock.exe` 后重启常驻实例，确认单实例与托盘正常
