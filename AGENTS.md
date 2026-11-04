# Project Workflow

- After each completed round of code changes, run the relevant checks, update
  `CHANGELOG.md`, and commit and push the reviewed changes to the user's repository:
  `https://github.com/JEFF-dev-ui/mousedock`.
- The user has authorized these routine uploads. Use normal fast-forward pushes
  and preserve remote changes.
- Include a concrete list of changes and verification results in the commit
  description. Distinguish verified fixes from unresolved device limitations.
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
- Follow the agreements in the project's "识别项目类型" chat. Do not resume
  IN9 Pro adaptation unless the user explicitly requests it.
- For a release build, run `.\build.ps1 -Configuration Release -Runtime win-x64`.
  The current script produces `app\MouseDock.exe` as a compressed, self-contained
  single file, including native libraries and content for extraction.
- Run the relevant checks in `tests/MouseDock.Checks`,
  `tests/MouseDock.ProtocolTests`, and `tests/feedback-report-regression.ps1`.
  Record actual results and keep device verification limits explicit.
- Source synchronization or workspace cleanup alone does not require packaging
  a new release. Keep the running application and its settings intact.
