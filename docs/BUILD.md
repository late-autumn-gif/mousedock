# 构建与重新开发

旧业务实现已清空。当前保留的是项目与打包配置，尚无应用入口，不能直接生成可运行的新版本。

## 保留内容

- `.dotnet/`：本机 .NET 10 SDK 与框架包，不上传 Git。
- `src/MouseDock/MouseDock.csproj`：Windows .NET 10、WPF、WinForms 配置。
- `src/MouseDock/app.manifest`、`Assets/app.ico`：应用清单与打包图标。
- `build.ps1`、`install.ps1`、`MouseDock.cmd`、`tools/bootstrap.ps1`：构建、安装和启动入口。
- `scripts/sign.ps1`、`docs/SIGNING.md`：签名工具与说明。
- `.github/workflows/`：构建与发布流程。
- `AGENTS.md`：后续开发、上传说明和打包约定。

项目没有额外的第三方 NuGet 包依赖。新代码放入 `src/MouseDock/`，需提供 WPF `App.xaml` 或其它有效应用入口。

## 恢复依赖

```powershell
.\.dotnet\dotnet.exe restore .\src\MouseDock\MouseDock.csproj
```

## 打包

添加新的应用代码后运行：

```powershell
.\build.ps1 -Configuration Release -Runtime win-x64
```

产物为 `app\MouseDock.exe`：Windows x64、压缩、自包含单文件，原生库和内容随包解压。可用 `scripts/sign.ps1` 为产物签名。

空项目的 CI 仍恢复依赖，跳过编译和产物上传。发布流程需要存在新的应用源码。

旧版本和文档可从 Git 历史查阅；根目录 README 暂保留历史内容，不代表重建后的功能清单。
