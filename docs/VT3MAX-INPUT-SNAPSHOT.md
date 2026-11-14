# VT3 MAX 当前输入报告实验（2026-11-15）

后续实现已切换为 [官方只读初始化与持续监听](RAPOO-AHUB-SESSION.md)，当前生产路由不再调用本文的 GetInputReport 补偿。本文保留上一轮实验及回放的历史边界，不作为当前读取流程说明。

状态：已实现并离线验证；待实机验证，尚未解决用户设备的无电量反馈。

用户提供的 1.2.1 报告中，`24AE:1417`、revision `0105`、MI_01/COL_09、FF00/0002、19 字节 IN/OUT 报告 07 均匹配。接口打开、清队列成功；三轮均在约 1.5 秒后等待输入超时，没有任何电量帧。用户确认同一设备的官方 A HUB 能显示电量。这不能证明通知触发条件或其他软件争用原因。

官方网页 https://hub.rapoo.cn/ 当前 `index-exv-sdE5.js`（SHA256 `F8E9B18C69EB294730FBBD6789FB04C542D1E5CEC6A0414F4067A173CD327984`）中的 Kk.handleReport → Hk/py 延续报告 07 的状态、百分比和 RF 字段。连接初始化还会读取配置和 DPI，部分配置初始化存在创建默认配置行为，故本次不复制整个初始化流程，也不假定它是电量查询。

## 候选方案

[Microsoft HidD_GetInputReport](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_getinputreport) 用于读取集合当前状态。它不是已证实的雷柏官方读取流程，目标固件是否实现仍未知。本次只对已打开并重新核验的精确 VT3 MAX 只读通道请求当前报告 07，不写 Output/Feature，不访问升级通道。

优先使用原双通知路径。只有新通知等待超时才尝试两次标准当前输入报告，间隔 50ms，第二次作为最终结果；两次都需完整 19 字节、编号 07、合法连接/状态/百分比及 RF 在线字段。无效通知、未清空队列不触发补偿。失败保持 —，不使用此前一次成功帧或已超时工作线程的晚到结果。

原生调用继承 ReferenceQueryGate 的实例互斥、4.5 秒外部等待与最多四个未完成工作线程限制；同步原生调用不能被强制取消，超时后由其工作线程保留资源直至结束，后续刷新不会重复打开同实例。内部事务预算仍为 4 秒。

诊断记录 `vt3-get-input-result` 的 Win32 错误和报告编号；输入仅保存 ID、连接、电量状态、百分比和 RF 字段。回放要求同一接收器的精确元数据、成功最终状态、两份有效快照及完整开始/结束标志，拒绝输出/Feature 混入。捕获结果仍不证明电池准确性或接收器固件无缓存。

## 验证和后续

- `dotnet run --no-restore --project tests/MouseDock.ProtocolTests -c Release -- --verify-reported-trio`：586 项通过。
- `dotnet run --no-build --no-restore --project tests/MouseDock.ProtocolTests -c Release`：26710 条离线协议/集成断言、10 项偏好、13 项 K87 检查通过；保留既有编译警告。
- 原实机 ZIP 三轮回放均 Failed/unavailable，未从无帧报告产生电量。

没有目标硬件，不宣称标准当前输入接口实机成功，也没有进行全量桌面或性能回归。应用的显示模型、布局、其他设备协议不变。源码仅本地提交，不推送。用户应退出旧 MouseDock，用隔离测试包重采雷柏报告；测试时先退出官方驱动以排除通道争用。如标准读取失败，下一步需要官方实际读取事务证据，不能继续猜厂商命令。
