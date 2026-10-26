# 构建

## 环境

| 项 | 版本 |
|---|---|
| Windows | 10 1809+ / 11 |
| .NET SDK | **10.0.200+**（`dotnet --list-sdks` 确认） |
| 依赖 | 无。不引用任何 NuGet 包 |

## 一条命令

```powershell
./build.ps1
```

等价于：

```powershell
dotnet publish src/MouseDock/MouseDock.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=none -p:EnableCompressionInSingleFile=true `
  -o dist
```

产物：`dist\MouseDock.exe`（单文件、自包含，目标机无需安装 .NET）。

## 校验

```powershell
Get-FileHash dist\MouseDock.exe -Algorithm SHA256
```

把结果贴到 Release 说明里，用户下载后可自行比对。

## 可复现性说明

本程序**不混淆、不加壳、不裁剪元数据**，IL 可被 dnSpy / ILSpy 直接读取。
相同 SDK 版本下重复构建，`dist\MouseDock.exe` 的 IL 部分应当一致；单文件打包的时间戳会导致整体哈希略有差异，
因此校验以 **Release 页公示的哈希** 为准。

## 目录

```
src/MouseDock/            应用
tools/                    开发期工具（pcap 解析等，不参与构建）
docs/                     文档
.github/workflows/        CI
```
