# Xbox 无线适配器电量：离线适配记录

日期：2026-11-15。状态：**系统读取、档位显示及诊断已实现，离线测试通过，待目标硬件验证**。

## 报告证据与问题

用户明确通过 Xbox 无线适配器连接。提供的 1.2.0 报告为 `045E:0B00`、`Controller (Xbox One For Windows)`，设备在线；三轮均只有一个 Page 1 / Usage 5 的 HID 集合，Input 16、Output 0、Feature 0，没有电量查询或有效百分比，连接方式却显示为有线。

报告没有 Windows.Gaming.Input（WGI）或 XInput 电池样本，不能由此推断手柄电量、充电状态或系统接口一定可用。原实现已有 WGI 读取，但会丢弃无百分比的系统结果；无法与 HID 精确关联的系统身份还会被同 VID/PID 的 HID 设备抑制。这些是源码确认的问题，尚不能宣称已复现目标机器的全部故障。

## 实现与身份边界

- 保留 WGI `RawGameController.TryGetBatteryReport` 路径，精确路径绑定成功时才向同一物理 HID 设备补充电量及无线元数据；无线状态不再依赖百分比有效。
- 不透明的 WGI `NonRoamableId` 使用独立稳定身份。存在同 VID/PID 的未关联 HID 设备时，名称加「· 系统」，保留两条记录；禁止按型号、名称、唯一数量或 XInput 玩家编号强行合并。多个同型号手柄仍按各自身份隔离。
- 系统相对读数保存在 `BatteryBand`，显示「档位」及原始比例，例如 `70/100`；百分比字段保持未知。不据此发明 70% 或 2/5 档。现有精确容量报告保持原百分比路径；缺少设计容量和功率信息的常见 0/10/40/70/100 比例，以及小容量刻度，保守视为相对读数。这是保守分类规则，并非对所有驱动精度的保证。
- 新相对读数清除同设备旧百分比缓存；后续无效读数清除档位，不复活旧百分比。普通精确电量设备原有过期缓存策略保持不变。环形填充仍不代表相对比例。
- WGI 每轮最多 16 个系统手柄，等待上限 2.5 秒，单个未完成任务隔离；超时结果不修改已经结束的报告或本轮 UI。

## 诊断与回放

新报告在 HID 采样之后记录 WGI 标量电池字段、无线标志、关联方式及查询结果。单设备限域在系统电池查询之前生效；单 HID 报告不会顺带查询身份未证明的系统对象。安全模式及取消识别策略继续生效，未关联同型号对象不能绕过原生实例的识别排除。

**全部设备报告**另调用系统 `XInputGetBatteryInformation`，最多四个玩家槽位，等待上限 1 秒，单任务隔离。仅保存电池类型、档位、槽位和错误码，不读取按键、摇杆或振动。槽位不构成物理身份，数据仅作诊断，不赋给任何界面设备。碱性/镍氢电池有效档位记作耗尽/低/中/高；FULL 不当成精确 100% 或正在充电/已充满。

v2 JSON 增加可选 `BatteryBand` 及两类系统查询记录，旧报告继续兼容。WGI 原始 ID、HID 路径、序列号、蓝牙地址不写入新增证据；只输出报告内别名。回放严格校验查询成功、字段范围及关联方式。原用户 ZIP 没有系统证据，重放仍应保持未知。

## 验收与限制

- `MouseDock.ProtocolTests --verify-xbox-wireless`：80 项通过；覆盖相对/精确容量、异常状态和容量、实例隔离、单设备限域、安全/取消识别、脱敏回放，以及 XInput 类型/档位/槽位组合。无硬件 I/O。
- 全量协议：26693 条断言通过，另有 10 项旧偏好及 13 项 K87 检查通过。编译有既有测试警告。
- `MouseDock.Checks --xbox-ui <PNG>`：模拟系统报告经合并、AutoDetectSource、BatteryService.PollAsync、设备选择到正常 WPF 卡片通过；七种任务栏样式显示档位；精确 → 相对 → 未知转换不会复活缓存。PNG 经人工可视检查。
- 相关六项界面/路由回归通过：实时数字、未知电量环、充电合并、旧精确电量缓存恢复、厂商安全门禁及报告描述符约束。
- 缺少目标 Xbox 手柄和无线适配器，未验证该机器的 WGI 枚举、驱动返回值、电池型号、休眠恢复或多手柄实机行为。其他系统手柄也会使用本次修正的公共系统路径；既有厂商专用读取器不被取代。

用户下一步：退出旧版，启动独立便携测试版，唤醒通过无线适配器连接的手柄；若出现「· 系统」记录，勾选该记录。无读数时导出**全部设备**报告，保留同一时刻 Windows/Xbox 官方电量显示作对照。原单 HID 报告无法覆盖未关联的系统对象或 XInput 槽位。

## 官方依据

- [RawGameController.TryGetBatteryReport](https://learn.microsoft.com/en-us/uwp/api/windows.gaming.input.rawgamecontroller.trygetbatteryreport?view=winrt-26100)
- [BatteryReport 可空容量字段](https://learn.microsoft.com/en-us/uwp/api/windows.devices.power.batteryreport?view=winrt-26100)
- [NonRoamableId 的身份语义](https://learn.microsoft.com/en-us/uwp/api/windows.gaming.input.rawgamecontroller.nonroamableid?view=winrt-26100)
- [XINPUT_BATTERY_INFORMATION 的电池类型和档位](https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_battery_information)
- [XInputGetBatteryInformation](https://learn.microsoft.com/en-us/windows/win32/api/xinput/nf-xinput-xinputgetbatteryinformation)
