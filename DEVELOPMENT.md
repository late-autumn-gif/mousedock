# 构建与发布

## 环境

- .NET SDK 10.0.403，装在 `C:\Program Files\dotnet\dotnet-sdk`（非默认位置）
- 目标框架 `net10.0-windows10.0.26100.0`，WPF + WinForms
- 每次构建前设置：

```powershell
$env:DOTNET_ROOT='C:\Program Files\dotnet\dotnet-sdk'
$env:PATH="C:\Program Files\dotnet\dotnet-sdk;$env:PATH"
& "C:\Program Files\dotnet\dotnet-sdk\dotnet.exe" ...
```

直接调 `dotnet` 会落到没有 SDK 的 `C:\Program Files\dotnet\dotnet.exe`，报找不到 SDK。

## 编译

```powershell
$env:DOTNET_ROOT='C:\Program Files\dotnet\dotnet-sdk'
& "C:\Program Files\dotnet\dotnet-sdk\dotnet.exe" build src\MouseDock\MouseDock.csproj -c Release
```

产物在 `src\MouseDock\bin\Release\net10.0-windows10.0.26100.0\`。

## 发布单文件

```powershell
& "C:\Program Files\dotnet\dotnet-sdk\dotnet.exe" publish src\MouseDock\MouseDock.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:IncludeAllContentForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None -p:DebugSymbols=false `
  -o app
```

`app\MouseDock.exe` 约 82 MB，运行时与图片资源都打在里面，拷一个文件就能跑。

## 部署

部署目标：`D:\mousedock\MouseDock.exe`。

1. 结束常驻进程：`Get-Process MouseDock | Stop-Process -Force`
2. 轮询等文件释放（约 0.4 秒一次，最多 12 秒）
3. 备份旧文件为 `MouseDock-1.1.2-before-<时间>.exe`
4. 复制新 exe
5. 启动，确认 `Get-Process MouseDock` 有进程

不复制就直接覆盖会报「文件正由另一进程使用」。

## 发布前检查

按 [TESTING.md](TESTING.md) 顺序：

1. `MouseDock.ProtocolTests` 16093 条全过
2. `MouseDock.Checks` 0 失败（Windows 11 上 5 项 SKIP 属正常）
3. `--detect` 设备名称/类型/通道正确
4. 替换后常驻实例能启动、托盘正常

## 打包

```powershell
Compress-Archive -Path app\MouseDock.exe -DestinationPath D:\mousedock\MouseDock-1.1.2-<标签>-<日期>.zip -CompressionLevel Optimal
```

## 诊断命令

| 命令 | 用途 |
|---|---|
| `MouseDock.exe --detect` | 枚举并打印识别结果到 `%APPDATA%\MouseDock\detect.txt` |
| `MouseDock.exe --probe` | 接口与协议探测，写 `probe.txt` |
| `MouseDock.exe --battery` | 单次电量读取 |
| `MouseDock.exe --replay-report <zip>` | 回放用户报告 |
| `MouseDock.exe --shot` | 截图 |

这些命令绕过单实例互斥（`App.IsDiagnosticCommand`），但要先停常驻实例，否则厂商 Feature 通道被占用。
