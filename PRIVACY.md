# 隐私说明

MouseDock 是一个**纯本地**程序。

## 不收集任何数据

- 唯一的网络请求是检查更新：向 `api.github.com` 查询最新 Release 版本号，可在设置里关闭
- 不发送任何本机信息（无设备 ID、无系统信息、无崩溃上报、无遥测）
- 不读取浏览器历史、剪贴板、文档、聊天记录
- 不采集设备序列号上传；设备 VID/PID 只在内存中用于识别型号

## 本地会写什么

| 位置 | 内容 | 用途 |
|---|---|---|
| `%APPDATA%\MouseDock\settings.json` | 主题、刷新间隔、壁纸路径、勾选的设备 | 记住你的设置 |
| `%HKCU%\Software\Microsoft\Windows\CurrentVersion\Run` | 仅在勾选「开机自启」时写入一条启动项 | 开机启动 |
| `%TEMP%\mousedock-bt.log` | 仅在 `--detect` 诊断模式下生成 | 排查蓝牙识别问题，可随时删除 |

删除 `%APPDATA%\MouseDock` 即可完全清除配置。

## 权限

程序以**普通用户权限**运行，不请求管理员。读写 HID 设备走的是 Windows 公开 API（`CreateFile` / `HidD_*` / `SetupAPI`），与厂商驱动的访问方式一致。
