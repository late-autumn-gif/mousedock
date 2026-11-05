# 参与贡献

项目正在重新开发，旧业务实现和测试已移除。请先阅读 [AGENTS.md](AGENTS.md) 中的项目约定。

## 开发基础

- 目标框架为 `net10.0-windows10.0.26100.0`，保留 WPF 与 WinForms 配置。
- 新应用代码放入 `src/MouseDock/`，从新的应用入口开始。
- 供 Windows PowerShell 调用的中文 `.ps1` 文件使用 UTF-8 BOM 编码。
- 不自动恢复旧实现，不把历史文档中的功能当作当前需求。
- 为新实现建立相应检查；个人设置、硬件记录和构建产物不提交。

## 打包

应用入口完成后运行：

```powershell
.\build.ps1 -Configuration Release -Runtime win-x64
```

产物为 `app/MouseDock.exe`。详细配置见 [构建说明](docs/BUILD.md)，签名方式见 [签名说明](docs/SIGNING.md)。

## 提交与 PR

- 一次改动聚焦一个明确目标。
- 标题和正文使用中文，只描述具体改动及其效果。
- 必要检查照常执行，验证记录保留在本地，不写入上传说明。
