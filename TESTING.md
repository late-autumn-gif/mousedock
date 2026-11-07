# 测试

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

- 64 项，需要 Windows 桌面会话与 STA 线程
- 当前基线：`59/64 checks passed, 5 skipped`，退出码 0
- 5 项 SKIP 的原因见 [KNOWN_ISSUES.md](KNOWN_ISSUES.md)，不计入通过、也不算失败

失败判定：任意一项抛出非 `SkippedException` 的异常即 `FAIL`，退出码 1。SKIP 只能由 `SkipOnXamlTaskbar` 或显式抛 `SkippedException` 产生，不允许把失败写成跳过。

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
