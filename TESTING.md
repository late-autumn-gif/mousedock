# 测试

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
