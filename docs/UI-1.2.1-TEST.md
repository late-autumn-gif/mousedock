# MouseDock 1.2.1「轻驻」测试版

## 测试版修正 2：淡色按钮、设置拖动与独立 DPI

2026-11-14：卡片操作改为无需图标字体的线条图标，使用自有模板，去掉系统蓝色高亮。悬停和置顶状态使用现有低透明度 TrackBrush，键盘焦点仅细描边；实际桌面检查过置顶启用状态。

设置标题栏增加透明命中背景并改用鼠标按下预览事件，关闭按钮仍独立处理。合成设置窗口用 `--settings-drag-fixture` 暴露供鼠标验收，本机记录从 900,386 移到 1000,446，日志保留在 `D:\mousedock\测试\1.2.1-fix2-native\native-settings-drag.txt`。

鼠标卡片 DPI 改为全宽独立区域；点击打开窗口而非行内小数字/小菜单。数值编辑适配设备沿用 App.ShowDpi 和 DpiWindow；已有旧协议档位使用独立窗口，调用原 HidDiscovery.ReadOrChangeMouseDpi，操作前复核安全模式、设备在线、当前物理对象和档位状态。未适配数值写入的设备不提供任意 DPI 数字编辑。未知读数保留 —，只读、未知、离线或安全模式给出限制说明。实际合成设备已点击验证弹窗，无真实硬件写入测试。

回归：`1.2.1-fix2-checks-desktop.txt` 为真实桌面 153/155、0 跳过，仅两项原有角色/照片字幕失败；DPI 检查已按独立区域与弹窗工作流更新，仍验证只读及未知状态。`1.2.1-fix2-light-checks.txt` 的五项轻驻检查全部通过，包括图标、自定义焦点模板及原始报文隔离。初次沙箱执行为 145/155，多出的失败涉及任务栏不可见及诊断输出权限，随后在真实桌面重新执行以排除环境影响。依赖使用项目已有缓存和 `--no-restore`，没有安装系统组件。

版本仍为 1.2.1，本地单独打包 fix2，保留之前的交付用于回滚；未推送远程。协议模块没有更改，本轮未重复运行协议套件。跨 Windows/DPI、多屏及所有型号的真实 DPI 修改仍待逐项验证。

## 测试版修正 1：卡片拖动与诊断文本

2026-11-14 用户反馈卡片无法拖动，悬停提示出现 MHUB/connectStatus/RX 报文。修正后 Root 接收非控件区域的鼠标按下，通过 `DragMove()` 立即进入 Windows 移动循环。以窗口实际位移判定拖动，拖动后保存位置；未越过系统阈值的单击才展开详情。置顶、设置、收起、DPI 和其他交互控件不会被拖动抢占。

卡片 tooltip 改为名称，详情不绑定 Note；原始诊断字段不删除，不改变设备读取、未知/过期状态或诊断报告。

新增回归覆盖标准/紧凑/极简密度中的点击、拖动阈值、拖动不展开、按钮不被拖动拦截，以及 RX 内容不进入 tooltip/详情、原 Note 仍被保留。

原生 GUI 夹具命令：`dotnet run --project tests/MouseDock.Checks -c Release -- --card-drag-fixture`，需设置项目内的 `MOUSEDOCK_DATA_DIR`。夹具仅暴露自身测试窗口供桌面工具操作，使用合成设备，不访问硬件。本机实际鼠标拖动将位置从 240,160 移到 340,220，日志位于 `测试/1.2.1-fix-fixture-data/native-drag.txt`；之后实际单击展开详情，悬停仅显示 Mouse fixture，不出现合成 RX 文本。该原生交互验证覆盖标准密度；三种密度的状态行为另由自动检查覆盖。

修正 1 全量界面回归：153/155、0 跳过，新增手势检查通过；仅剩初版已有的两项角色/照片字幕几何失败，无新增失败。本轮未修改协议代码，未重复执行离线协议套件。

修正 1 的本地日志：`D:\mousedock\测试\1.2.1-drag-fix-checks-final.txt`。初版结果、历史限制和基线失败说明保留在下文。

日期：2026-11-14。基线：GitHub `main` / `a0d8d16af64c4022c2564d215714183a9c5ed0a8`，版本 1.2.0。工作分支：`ui-light-1.2.1`。仅本地开发、提交和打包，未 push 或发布 Release。

## 本次范围

用户要求以当前 GitHub 1.2.0 为基础，将已讨论的轻量常驻设计实施为 1.2.1 测试版。原本的本地 UI 分支源码已经不在目录中，因此本轮重新克隆 GitHub 源码；不是从此前本地宽屏 UI 分支继续。原有 `D:\mousedock\应用\MouseDock.exe` 保留。

- 设置：760×620 DIP 起，六分类导航；工作区不足时压缩到可见区域。原有命名设置控件、绑定及事件保留。显示方式按当前模式展开原配置；自定义主题、背景、角色、照片、任务栏和右侧参数保留。
- 卡片：标准 316 DIP、紧凑 288 DIP；名称、通道、电量、状态和 DPI 入口；点击或键盘 Enter/Space 展开详情，Esc 收起。顶部文字操作置顶、设置、收起，避免依赖缺失字体的图标。
- 托盘：原生 2×3 六模式快选，当前项勾选；置顶和穿透状态每次打开读取真实配置。保留设备管理、DPI、耳机、更新及退出。
- 主题：新增 resident「轻驻薄荷」，深浅色实色面层；新配置默认采用，已有配置不强制换色。下拉菜单跟随主题。
- 电量：未知 —；过期明确标注「已过期 · 上次读数待刷新」，继续沿用现有 ValueOpacity 与充电动效门禁。未修改设备发现、身份隔离、电量采集或厂商协议。
- 保存：沿用 SettingsStore，新增 TrySave 返回真实结果，成功与失败的反馈都经过文件系统测试。

## 交付与运行

测试压缩包直接放在 `D:\mousedock` 根目录。解压整个目录后运行「启动1.2.1测试版.cmd」；也可以双击 MouseDock.exe。必须保留同目录运行时文件及 `test-portable.flag`，不能只拿走 exe。

测试包为 Windows x64 自包含目录，避免单文件本机库解包，不要求安装 .NET。标记启用程序旁 `data` 配置、独立实例锁，禁用开机启动写入和联网更新。启动脚本也将 TEMP/TMP 指向程序旁 temp。测试版可以与正式版配置隔离，但同时读取同一硬件仍可能竞争 HID 通道，测设备电量前请先从旧版托盘退出原程序。

不携带用户配置、截图、抓包或原始诊断数据。不使用演示数据替代真实采集。包内初始设置只指定卡片模式、新主题、不开机启动及不检查更新。

## 实际验证

本轮工具：本地 SDK `D:\mousedock\tools\dotnet\dotnet.exe`，10.0.404；NuGet、CLI home、TEMP 均设在 D:\mousedock 内。

```powershell
$env:DOTNET_ROOT='D:\mousedock\tools\dotnet'
$env:DOTNET_CLI_HOME='D:\mousedock\cache\dotnet-home'
$env:NUGET_PACKAGES='D:\mousedock\cache\nuget'
$env:TEMP='D:\mousedock\cache\temp'; $env:TMP=$env:TEMP
$env:MOUSEDOCK_DATA_DIR='D:\mousedock\测试\1.2.1-check-data'
& "$env:DOTNET_ROOT\dotnet.exe" run --project tests\MouseDock.ProtocolTests -c Release
& "$env:DOTNET_ROOT\dotnet.exe" run --project tests\MouseDock.Checks -c Release
& "$env:DOTNET_ROOT\dotnet.exe" publish src\MouseDock\MouseDock.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o 'D:\mousedock\测试\MouseDock-1.2.1-test-win-x64'
```

| 验证 | 实际结果 |
| --- | --- |
| 修改前协议基线 | 25270 条通过 |
| 修改前界面基线 | 148/150，0 跳过 |
| 产品构建 | 成功，0 警告、0 错误 |
| 修改后协议 | 25270 条通过，无硬件 I/O |
| 修改后界面 | 152/154，0 跳过；新增四项通过，无新增失败 |
| Windows 实际交互 | 六分类中的显示/设备导航、卡片模式入口、主题下拉密度选择、真实保存时间已检查 |
| WPF 截图 | 设置六页、卡片三密度、深浅色及其他显示窗口已生成并抽查 |
| 硬件数据 | 截图使用自动检测的本机设备数据；不据此宣称每个型号或全部协议实机验证成功 |

UI 两项未解决的基线失败：

1. `Character device captions stay below artwork with live independent values`。
2. `Device photos retain independent artwork and captions on every surface`。

两者在改动前的 GitHub 基线均复现，本轮没有修改该图像布局。不能将 152/154 表述为完整通过。两处其他旧测试已按新的设置分类切页后检查原来的主题/设备/显示控件，未删除原断言。

日志与截图保存在 `D:\mousedock\测试\1.2.1-*`，均留本地，不进入源码提交或分发包。完整硬件兼容性继续以既有支持矩阵和型号证据为准。

## 未验收与风险

- 非本机 Windows 版本、多显示器、所有 DPI 档位、完整键盘与原生托盘快选焦点流程、所有样式和壁纸组合仍需扩展验收。
- 所有设备 DPI 写入、麦克风静音、音量和厂商专用功能未逐项用实机操作；保留当前 GitHub 路径，不据编译结果宣称硬件验证。
- 原生托盘菜单通过现有窗口/句柄检查和新增派发测试；最终真实任务栏弹出定位及穿透恢复仍需要用户实机重点验收。
- 未做长时 CPU/内存对比或长时硬件稳定性测试；没有以「轻量」名义减少采集验证或声称性能提升。

## 后续优先级

先在日常桌面确认卡片尺寸、托盘切换、穿透解除和设置保存；再修复既有照片/角色字幕几何问题、补足跨 DPI 与多屏验收。本轮不涉及远程发布。
