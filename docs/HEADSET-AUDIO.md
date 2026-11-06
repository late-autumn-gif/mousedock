# 耳机：名称、音量与麦克风

版本保持 v1.1.1。耳机放在独立音频控制页，不再作为自动电量面板里的电量设备。

## 打开与使用

从托盘或面板右键选择“耳机控制…”，也可在设置的“耳机”分类点击“打开耳机控制”。

1. 选择播放设备。名称原样来自 Windows，不通过 VID/PID 替换品牌或型号。
2. 拖动音量条调节 0–100%。作用于所选播放设备，不切换系统默认设备。
3. 选择对应麦克风，点击“麦克风静音”或“恢复麦克风”。

部分 USB 耳机会被 Windows 命名为“扬声器”，可勾选“显示其他音频输出”后选择。显示这些输出是方便用户选择，不代表程序确认它们全部属于耳机。

麦克风禁用采用 Windows 音频端点的静音开关，属于可恢复的系统静音。它不修改驱动、系统设备启用状态或隐私权限。关闭 MouseDock 不会自动恢复用户设置的系统静音。

## 设备与状态处理

- 只读取音频端点的名称、设备属性、音量和静音状态，不打开录音流，不读取或上传声音。
- 不查询耳机电量；自动蓝牙扫描跳过耳机的 GATT 电量/HFP 私有读取，自动电量列表排除耳机。
- 播放与录音操作使用各自的完整 Windows 端点 ID，操作前重新确认方向及在线状态，完成后读取实际值。
- 自动关联麦克风要求同一有效 Container ID、唯一录音端点，且 Windows 明确将其中一个端点描述为 Headset。普通声卡的耳机插孔不会仅凭同一声卡就关联电脑内置麦克风。
- 无法确认时需要用户选择；用户选择按端点 ID 记住。已选麦克风断开后不回退到其他麦克风；已选耳机断开后不会自动切换到另一副。
- 耳机页可见时每两秒刷新状态；切换设备和刷新不触发音量/静音写入。关闭页面停止轮询与待执行音量调节。
- 快速拖动保留最后一次音量选择；旧刷新结果不能覆盖刚确认的控制结果。读取失败时禁用旧控件，避免显示操作成功。
- 设置只保存播放设备及麦克风映射，不保存并回写音量或静音值。启动程序不会替用户恢复、静音或调节任何设备。

## 验证

- 5830 条离线协议/集成断言通过。
- 17 项耳机界面与控制检查通过：使用模拟后端覆盖目标隔离、静音/恢复、失败提示、拔插、快速音量操作、刷新竞态和深浅主题布局，没有执行系统音频写入。
- 45 项设备管理检查通过。
- 真实 Windows 设备仅做名称、端点方向、音量及静音状态的只读检查；没有为测试更改用户当前音量或麦克风状态。

## 实现依据与依赖

使用固定版本 NAudio.Wasapi / NAudio.Core 2.2.1 调用 Windows Core Audio。许可及 CoreAudio 包装代码归属见 [第三方声明](../THIRD-PARTY-NOTICES.md)。

- [Windows 音量标量接口](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nf-endpointvolume-iaudioendpointvolume-setmastervolumelevelscalar)
- [Windows 麦克风／音频端点静音接口](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nf-endpointvolume-iaudioendpointvolume-setmute)
- [Windows 音频端点 Container ID](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/audio-endpoint-container-id)
- [NAudio 2.2.1 MMDevice](https://github.com/naudio/NAudio/blob/v2.2.1/NAudio.Wasapi/CoreAudioApi/MMDevice.cs)
