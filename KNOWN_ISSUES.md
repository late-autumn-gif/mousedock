# 已知问题

记录当前不修或修不了、但需要明确告知的问题。按影响排序。

## 测试环境跳过项（5 项）

`tests/MouseDock.Checks` 在 Windows 11 上会跳过 5 项，输出 `SKIP`，不计入通过：

| 检查 | 原因 |
|---|---|
| Embedded taskbar uses a transparent native child | Windows 11 任务栏是 XAML 宿主，不接受原生子窗口 `SetParent` 嵌入 |
| Embedded taskbar retains native alpha composition | 同上 |
| Embedded taskbar recovers after its native host is destroyed | 同上 |
| Embedded taskbar recovers after a cross-process host exits | 同上 |
| Update waits for the old process before replacement and restart | 夹具用 `.cmd` 占位，而 `CreateApplyStartInfo` 要求被替换文件是名为 `MouseDock.exe` 的 PE；需要真实 PE 才能验证重启 |

任务栏四项在 Windows 10 / 旧版任务栏环境仍会执行。更新一项目前只有代码审查覆盖，没有自动验证。

## 设备侧

| 设备 | 状态 | 说明 |
|---|---|---|
| 英菲克 248A:5B2F | 官方表外 | 不在 IN9/A9 的 `5C2E/5D2E/5E2E/5C2F` 清单里；官方 IN9 驱动与 INPHIC HUB 二进制都没有可读设备表条目 |
| 前行者 X23 SE | 协议未取到 | 官方桌面包已解，HID 帧在本地模块，静态未定位 |
| ATK ZERO / V75X | 待实机 | asar 索引到的主进程是 BYKB 灯效线，ZERO 的 FF05 命令未出现在已索引文件 |
| 迈从 A7 V3 / G7 | 待实机 | 官网网页驱动不含 11/F9 与现代 4D 帧，协议在 MHub 桌面端 |
| 雷柏 VT7 | 既有审计结论 | 官方 1.0.0 驱动为被动 report 7，`payload[6]` 状态、`payload[7]` 电量；未在本轮重复拆包 |
| WOB Rainy 75 | 部分验证 | VIA 定义确认 `320F:5088`；桌面工具是 `usb_ota_tool` 固件工具，不含电量协议 |
| RK R98Pro | 待拆包 | `RK_Keyboard_Software_Setup_V4.6` 是 Inno Setup，已下载未解 |
| 维咖 WEIKAV | 无官方桌面程序 | 官网只提供 VIA/网页配置 |

## 运行时

- 常驻实例占用 Feature 通道时，`--detect` 可能读不到电量；先停常驻进程再跑。
- 迈从 A7 V2 的 Feature 通道第一次 `GetFeature` 恒回缓存帧，已改为每次重试重发查询。
- 任务栏嵌入、WPF 反射相关测试对构建环境敏感，`MouseDock.Checks` 的 5 项跳过以上文为准。
