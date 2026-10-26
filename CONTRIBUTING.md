# 参与贡献

## 提 Issue

请附上：

1. `设置 → 数据源 → 查看探测报告` 导出的文本（含设备树与失败原因）
2. 设备型号 + 连接方式（2.4G / 蓝牙 / 有线）
3. 系统版本

## 新增一款设备的电量支持

多数情况只需要一行：

```csharp
// Services/Detection/Detection.cs → VendorMap.KnownReceivers
["089D:062F"] = ("狼蛛 SC580", DeviceKind.Mouse, "aula-sc580"),
```

若对方命令号不同，需要抓包还原，流程见 [PROTOCOL.md](PROTOCOL.md)：

```powershell
# 1. 装 USBPcap（需要管理员，且重启一次让过滤驱动挂到根集线器）
# 2. 抓 25 秒，期间启动厂商驱动
USBPcapCMD.exe -d \\.\USBPcap1 -o cap.pcap -A -s 65535
# 3. 解包看命令帧
tools/parse-len.ps1 -Pcap cap.pcap -Lens 32,64
```

## 代码约定

- 目标框架 `net10.0-windows10.0.26100.0`，**不引入第三方 NuGet**
- UTF-8 **带 BOM** 保存 `.ps1`（否则 Windows PowerShell 会误读中文）
- 任何设备读写都必须 `try/catch` 兜底，失败只能降级为 `—`，不能抛到 UI
- 不新增网络调用、不提权、不注入其它进程（见 [SECURITY.md](SECURITY.md)）

## 提交前自检

```powershell
./build.ps1
./dist/MouseDock.exe --detect        # 控制台输出真实设备列表
```

## PR

- 一个 PR 只做一件事
- 涉及识别逻辑的改动，请在描述里贴 `--detect` 的前后对比
