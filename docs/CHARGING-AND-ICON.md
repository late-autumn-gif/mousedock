# 1.3.4：充电动画与新图标

应用图标已采用用户在本次会话确认的版本：青绿色圆角底，横向白色电池，三格薄荷绿色电芯。

- 生成工具：内置 `image_gen.imagegen`，生成后经用户确认。
- 设计提示词摘要（非原始提示词逐字记录）：简洁现代的桌面外设电量工具图标；水平电池轮廓与三格薄荷绿电芯，青绿色圆角方块，透明外部背景；避免鼠标、竖直长条、U 形或容易引起歧义的人体形态。
- `Assets/app-icon-source.png` 保留用户确认的生成图；ICO/不同尺寸 PNG 由现有图标脚本及图像格式转换生成。
- `app-battery-v2.ico` 与主 ICO 内容相同，供桌面快捷方式使用新路径刷新图标缓存。

充电动画绑定 `DeviceInfo.IsChargingActive`：设备已连接、最近数据未过期，且读取到充电标志。百分比上涨、满电、USB 连接均不能单独证明正在充电。缓存值可继续显示，但会标记待刷新并停止动画。轮询间隔沿用应用设置，不保证每毫秒更新。

卡片、迷你面板、悬浮设备列表使用三格循环动画；隐藏、卸载、断开或过期时停止。托盘使用现有计时器驱动充电动画，逐帧释放旧 Icon 和原生 HICON。

`tests/MouseDock.ProtocolTests` 包含协议和 UI 属性断言，`--verify-charging-ui <目录>` 验证动画启停、不同帧和托盘句柄释放。预览使用模拟状态，不能当作实际设备正在充电的证据。本机 SC580 的两次 Identify 实测返回未充电；插线、拔线的实际充电状态切换仍待硬件验证。

启动实测还暴露了旧 HID I/O 的 AccessViolation：托管数组和临时 OVERLAPPED 在请求尚未完成时已离开 P/Invoke 的有效期，超时后没有等待取消完成。共享读写函数现为每次请求保留非托管缓冲区和 OVERLAPPED，超时调用 CancelIoEx，并等待 GetOverlappedResult 完成后释放。依据：[CancelIoEx](https://learn.microsoft.com/en-us/windows/win32/api/ioapiset/nf-ioapiset-cancelioex)、[GetOverlappedResult](https://learn.microsoft.com/en-us/windows/win32/api/ioapiset/nf-ioapiset-getoverlappedresult)。`--verify-native-io` 使用实际 Windows 异步管道验证连续超时取消、下次读取、写入和错误句柄，不访问外设。

读取缓冲区改用当前 HID 集合的 InputReportByteLength，替代固定128字节。实机生产查询已取得 M137、77%、未充电；随后休眠返回相同缓存值并标记 stale。`--inspect-sc580-battery` 可在本机执行这条实际读取路径。

罗技 G700/G700s 虽然可以拆换电池，也支持 USB 充电，已从禁用充电显示的集合中排除。依据：[G700 官方指南](https://www.logitech.com/assets/32643/2/g700620-002446003403qsg-amr.pdf)、[G700s 官方指南](https://www.logitech.com/assets/47990/2/g700s-rechargeable-gaming-mouse-quick-start-guide.pdf)。
