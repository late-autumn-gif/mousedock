# Project Workflow

- 源码从用户指定的 `https://github.com/late-autumn-gif/mousedock` 获取，
  对应只读上游 `upstream`；本地 SDK、打包规则和提示词优先保留。
- 每轮修改完成后检查、更新日志并提交到用户的仓库
  `https://github.com/JEFF-dev-ui/mousedock`（`origin`）。
  推送前确认目标仓库，使用正常快进推送并保留远程改动，不向上游仓库推送。
- 后续上传说明统一使用中文，包括 Git 提交标题与正文、PR 标题与正文、
  发布说明及更新日志；代码标识符、命令和专有名称可保留原文。
- 上传说明只描述具体改动及其效果，不写验证结果、测试数量、执行过程或排查经过。
  必要检查仍照常执行，验证记录保留在本地；改动描述不得夸大已实现的范围。
- Upload source and project documentation only. Keep personal settings, hardware
  captures, diagnostic output, SDKs, downloaded drivers, and build output local.
- Never infer battery percentages or charging flags from unverified device bytes.
- Keep future update notes in `CHANGELOG.md`; do not edit `README.md` unless the
  user explicitly requests it.
- Whenever a new executable is built and verified, replace both the executable
  at the user's current application location and `D:\桌面\MouseDock.exe`.
  Identify the current application path before replacing it. If MouseDock is
  running, close it gracefully, replace the files, and restart the updated app.
  Preserve user settings and verify the installed files match the tested build.

## Source and Packaging

- Use this project root as the Git working directory. Fetch and preserve remote
  changes before editing or pushing; do not create another upload copy under
  `artifacts/`.
- 保留现有打包约定；同步上游源码时按其实际依赖补充项目配置，不擅自恢复其它历史实现。
- For a release build, run `.\build.ps1 -Configuration Release -Runtime win-x64`.
  The current script produces `app\MouseDock.exe` as a compressed, self-contained
  single file, including native libraries and content for extraction.
- 2026-11-05 按用户要求接入上游 `main` 的 v1.4.3 源码。
  后续功能按用户要求开发，不继续 IN9 Pro 适配，除非用户明确要求。
- 2026-11-06 同步上游 `84b40b8`，上游已将版本重新编号为 v1.1.1；
  `--tray` 自启时显示保存的电量面板，仅跳过主动启动的设置窗口。
- 运行适用的 `tests/MouseDock.Checks`、`tests/MouseDock.ProtocolTests` 和
  `tests/feedback-report-regression.ps1`，验证记录留在本地，
  并明确区分离线检查与设备实测。
- Source synchronization or workspace cleanup alone does not require packaging
  a new release. Keep the running application and its settings intact.
