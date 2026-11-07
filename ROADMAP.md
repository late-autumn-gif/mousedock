# 路线图

## 当前

- 版本 1.1.2，单文件自包含，`D:\mousedock\MouseDock.exe`
- 离线断言 16093 条全过
- 界面/运行时检查 59/64 通过、5 项跳过、退出码 0
- 本地 `main` 领先 `origin/main` 20+ 提交，未推送

## 待办，按优先级

1. **推送**：本地提交需要推到远程，否则群里发的包和仓库对不上。属于对外变更，等明确授权。
2. **RK R98Pro**：`D:\RK\RK_Keyboard_Software_Setup_V4.6_20260816.exe` 是 Inno Setup，用 `D:\OMNIDEV\Tools\Utilities\7zip\extra\x64\7za.exe` 解包，找 HID 命令。
3. **前行者 X23 SE**：`common_hid_rs.exe` 已确认是 native x64（导入 hidapi），下一步用 REA 反编译找 `AA/F0 30` 帧。
4. **ATK ZERO / V75X**：`ATK HUB` 的 asar 需要全量解包后按 `1155`/`124F`/`FF05`/`0x92` 扫，不能只看主进程索引。
5. **迈从 A7 V3 / G7**：协议在 `MHub-installed\resources\app.asar`，按 `0x11`/`249`/`0x4D`/`4018`/`4021` 扫。
6. **英菲克 5B2F**：官方表外，需要该型号对应的官方驱动或设备返回的 dev_id，现有资料给不出。

## 不做

- 不重写扫描层。当前 `HidDiscovery` 1.5k 行、路由表与读取器表已拆开，继续拆文件收益低于风险。
- 不引入 DI 容器、EF、MediatR。单机桌面应用，2.5 万行，核心复杂度在硬件差异。
- 不为未验证的帧猜协议。读不到就显示 `—`。

## 发布节奏

- 每次改动：两套测试 → `--detect` 复验 → 更新 CHANGELOG → 提交
- 新设备：先写协议断言，再接读取器，最后补 `docs/` 证据等级
- 对外发布：只发布与仓库 commit 对应的构建，避免手工包与源码脱节
