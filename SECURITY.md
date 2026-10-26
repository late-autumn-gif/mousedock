# 安全策略

## 报告问题

发现安全问题请走 GitHub 的 **Private vulnerability reporting**（仓库 → Security → Report a vulnerability），不要开公开 issue。

## 设计约束

MouseDock 对系统的影响面被刻意限制在以下几点，任何 PR 都不得突破：

1. **不联网**：不得引入任何网络调用（含自动更新、遥测、崩溃上报）
2. **不提权**：不请求管理员权限，不写 `HKLM`，不注册服务、不装驱动
3. **不注入**：不对其它进程做 DLL 注入 / API Hook / 内存读写
4. **不持久化到敏感位置**：只写 `%APPDATA%\MouseDock` 与可选的 `HKCU\...\Run` 启动项
5. **HID 只按协议读写**：除「厂商协议读取电量」外，不对设备写入任何数据；该功能可在设置里用**安全模式**关闭

## 供应链

- 无 NuGet 第三方依赖（仅 .NET BCL 与 Windows SDK 投影）
- 构建可复现：见 [BUILD.md](BUILD.md)
- 发布产物附带 SHA-256；签名流程见 [SIGNING.md](SIGNING.md)
