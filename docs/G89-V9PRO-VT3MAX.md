# G89 / V9 Pro / VT3 MAX 离线适配（2026-11-15）

当前状态：G89 新实机报告三轮共六次返回 100%，包长与校验复核通过，正常卡片回放显示 100%、未充电；用户仍反馈不显示或其他异常，具体界面现象尚未复现，电量准确性仍缺少官方读数对照。V9 Pro 新报告证明仅监听三轮均无通知；已改为固定官方电量查询，待重新实机验证。VT3 MAX 保持上一轮离线验证等级。原 1.1.8 零查询报告不能作为电量准确性证据。

用户随后确认两台设备名称可见但电量为 —，并提供已选择显示的设置截图。当前本机仅有主动查询修正前的两份报告，尚无最新失败帧证据。补充验证已将旧报告的 G89 电量送经 AutoDetectSource、BatteryService.PollAsync、默认设备选择、VisibleDevices、正常 WPF 卡片，仍能显示 100%；V9 无读数时设备不隐藏、显示 —。专项 1 项通过，仅改变测试，没有新的硬件修复结论。下一步应从实际失败的当前程序导出“全部设备”报告，核对 BuildId、SafeMode、实际查询与失败阶段，避免继续基于旧报告猜测。

## G89

用户所述 G89 的接收器为 `41E4:2001`、通用产品名 `MCHOSE 2.4G Wireless`，USB revision `3000`。`MI_01/COL_01` 为 `FF02/0002`、20 字节 Input/Output，报告 `13`（十六进制，即十进制 19）。当前 MchoseBy 配置已接受这组候选边界，继续复用既有实例读取器，不增加按公共产品名全局命名 G89 的规则。

只读电量请求为 `13 4A 01`，20 字节，末字节 `5E`；两次新响应后采用第二次百分比。响应须为单包、序号 0、有效载荷长 2、尾字节为前 19 字节和的低 8 位，填充为零。回显、越界、校验错误、错误包长、截短、第二次缺失均拒绝。

官方 `getBatteryParser` 使用 little-endian 位字段：状态字节低四位为 `fullStatus`，高四位为 `chargeStatus`；官方 `generateBit` 明确在 little-endian 下从低位开始取。新报告状态 `01` 是满电位，不是充电位；用户也确认两台未插充电线。当前准确区分充电/未充电，不用满电位强制改写百分比。新增响应尾校验与真实回包到 WPF 卡片的绑定检查；当前正常卡片显示 100%，不据此宣称复现或解决用户的所有显示问题。

## VT3 MAX

最新方案已切换为 [官方只读初始化与持续监听](RAPOO-AHUB-SESSION.md)，取代以下两轮短时监听/标准当前报告实验。仍待目标硬件验证。

2026-11-15 后续：新实机报告三轮均无报告 07 通知，官方 A HUB 可显示电量（用户确认）。已增加标准当前输入报告的实验补偿，尚未实机验证；原被动路径作为首选保留。最新实现、测试和限制见 [VT3 MAX 当前输入报告实验](VT3MAX-INPUT-SNAPSHOT.md)。下面的被动实现描述为前一轮方案。

官方 AHUB 的 `index-B1AV7SwT.js`，SHA256 `2BCD9C1DA4E57C9600FA29B47153076CD086412816F0CAC0E0D2862573CD4225`：

- VT3 MAX manifest 指定 `24AE:4617/1417`、配置通道 `FF00/0002`、驱动 `VT_nrf54L`；`usage14` 明确属于升级通道。
- `Kx.handleReport` 接收报告 7，`Sx/hm` 解析 payload[6] 状态、[7] 百分比、[17] 高位 RF 在线标志。Windows 报告包含 ID，因此对应 [7]、[8]、[18]。
- `Ui/An`：状态 1/2/3 有效，2/3 表示充电。本实现不使用“有线即充电”的 UI 推断。

新增独立 `RapooVt3Max` 路由；仅启用报告实际提供的无线 `1417`、revision `0105`、MI_01、19 字节报告 07。打开后再次核对身份、产品、集合与唯一报告长度。使用只读句柄，清理旧队列后在两个各 1.5 秒窗口中等待两份新通知；无线离线、无通知或第二份失效时保留未知。事务总预算 4 秒，外部等待上限 4.5 秒。不发送旧版 `A5 AA`，不读取升级通道，不据此扩大到未采样的 `4617` 有线描述符。

## V9 Pro

官方 MHUB `main-DCRwBIvm.js`，SHA256 `405102805E2ADEB93073EE2186F6D0193CA1317A6B4657D636E4E611BD25A446`：`blkBattery` 和 `blkNotifications` 支持报告 `55`、命令 `65` 的电量通知；`parseBatteryResponse` 读取百分比和状态，26 表示休眠。

用户报告的 `291D:385D` + 精确产品名 `MCHOSE V9 PRO`、revision `0012`、MI_00/COL_05、`FF90/0001`、64 字节集合为 `IN[55] OUT[]`，但 OutputReportByteLength 仍为 64。上一版仅监听三轮均没有输入，不能假设设备会主动上报。

官方 `createReadBatteryCommand$2` 明确使用 report ID `55`，63 字节 payload 以 `65 01` 开头；V9 Pro `10525:14429` 的产品配置选择 BLK 且无报告编号覆盖。现在对上述精确布局发送此固定电量查询，逐字节限制整个 64 字节请求；禁止其他命令、报告编号或非零附加字段，不改走 Feature、升级或其他集合。每次先清队列，需两次新应答；回显、失败写入、超时、休眠和异常字段拒绝。原先完整输出映射路径保留原校验。

`OUT[]` 来自 value/button 能力项枚举，并不是原始 Report Descriptor 的完整证明。[Microsoft HIDP_CAPS](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/ns-hidpi-_hidp_caps) 分别定义报告最大字节数与能力项数量。本次例外依据精确实机元数据及官方已知只读请求，不推断任意未枚举报告都可写；Windows 是否接受本设备上的该查询仍待新报告确认。

继续沿用既有 V9 电量可信度策略：仅状态 2 采用百分比，外接电源/充电时抑制可能冻结的百分比；休眠、异常状态及无通知保持未知。官方支持通知不等于所有固件会定期发送通知，实际频率仍需新报告确认。

## 诊断、验证与边界

V9 新查询路径和原被动路径仅保留电量解析字段，省略其他未知字段。回放按同一接收器、精确集合、版本、报告编号、两份完整事务及成功状态复核；G89 与 V9 当前查询布局须有两次正确请求/响应，保留旧被动报告的兼容回放。额外记录读取器最终失败原因，避免只有 Failed 而没有失败阶段。此处回放不泛化到其他 MchoseBy 或 V9 固件。`captured` 仍不等同于真实电池准确性。

实际执行：

```powershell
dotnet run --no-restore --project tests/MouseDock.ProtocolTests -c Release -- --verify-reported-trio
dotnet run --no-restore --project tests/MouseDock.ProtocolTests -c Release -- --verify-mchose-feedback
dotnet run --no-build --no-restore --project tests/MouseDock.ProtocolTests -c Release
dotnet build tests/MouseDock.Checks -c Release --no-restore
dotnet run --no-build --no-restore --project tests/MouseDock.Checks -c Release -- --filter <检查名>
```

本轮新增反馈专项 506 项、全量协议/集成 26345 条通过；另有 10 项偏好和 13 项 K87 检查通过。相关界面/路由 5/5，加真实 G89 报告到 WPF 卡片 1 项通过（实际命令：`MouseDock.Checks --mchose-report-ui <用户ZIP> <本地PNG>`）。编译通过，保留既有测试警告。G89 新报告三轮重放均为 100%、Charging=false；V9 新报告三轮仍 Failed/unavailable，不能用模拟成功取代实机结果。没有执行全量桌面套件，历史角色/照片字幕几何问题不在本次修改范围。

下一步：退出旧程序，运行新本地隔离测试包复采 V9；若 WriteFile 被设备拒绝，保留准确失败原因，补官方驱动 USB 通信证据，不猜其他通道。G89 需当前实际显示模式及可见界面证据、官方驱动对照；已有回包不支持把百分比改成另一个数字。仅本地提交源码和文档，报告、图片证据与构建产物留在仓库外，不推送远程。
