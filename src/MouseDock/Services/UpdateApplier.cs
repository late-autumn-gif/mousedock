using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MouseDock.Services;

/// <summary>
/// 更新启动器。
///
/// 自己不替换任何文件——Windows 不允许进程移动正在运行的 exe 所在目录。
/// 这里只做一件事：把「等旧进程退出、关闭全部 MouseDock、删旧文件、删旧入口、
/// 装新版本、重建 Run 项与桌面快捷方式、拉起新版」这串动作用 PowerShell 脚本
/// 写成独立进程，启动后本进程立刻退出，后面全部由那个进程完成。
/// </summary>
internal static class UpdateApplier
{
    /// <summary>
    /// 更新流程里可以上报的一个进度点。
    /// 主进程只能走到「交给独立进程」这一步，再往后进程就退出了，
    /// 所以 100% 只表示交接完成，真正的收尾由那个线程自己写进 apply.log。
    /// </summary>
    internal readonly struct Step
    {
        internal Step(string message, int percent) { Message = message; Percent = percent; }
        internal string Message { get; }
        internal int Percent { get; }
    }

    /// <summary>
    /// 更新启动器看到这个参数就进入更新模式，不启动主界面。
    /// </summary>
    public const string ApplySwitch = "--apply-update";

    /// <summary>主程序重新拉起时带这个参数，用来收掉上次更新留下的暂存目录。</summary>
    public const string CleanupSwitch = "--cleanup-update";

    private const string RunKey = @"HKCU:\Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "MouseDock";

    internal static bool TryGetApplyTarget(string[] args, out string packagePath)
    {
        packagePath = null;
        int index = Array.FindIndex(args, a => string.Equals(a, ApplySwitch, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length) return false;
        packagePath = args[index + 1];
        return !string.IsNullOrWhiteSpace(packagePath);
    }

    /// <summary>
    /// 启动独立更新进程。返回非空字符串表示启动失败（旧版本此时还没有被碰过）。
    /// </summary>
    internal static string Apply(string packagePath, Action<Step> progress = null)
    {
        string exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            return "拿不到当前进程路径";
        if (!File.Exists(packagePath))
            return "更新包不存在：" + packagePath;

        string installDir = Path.GetDirectoryName(exePath) ?? "";
        string work = Path.Combine(Path.GetTempPath(), "MouseDock-update-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);

            progress?.Invoke(new Step("准备更新目录…", 72));
            string scriptPath = Path.Combine(work, "apply.ps1");
            string logPath = Path.Combine(work, "apply.log");
            File.WriteAllText(scriptPath, BuildScript(), new UTF8Encoding(true));

            progress?.Invoke(new Step("复制更新包…", 82));
            string local = Path.Combine(work, Path.GetFileName(packagePath));
            if (!string.Equals(Path.GetFullPath(local), Path.GetFullPath(packagePath),
                    StringComparison.OrdinalIgnoreCase))
                File.Copy(packagePath, local, true);

            progress?.Invoke(new Step("校验更新包…", 92));
            string stage = Path.Combine(work, "x");
            Directory.CreateDirectory(stage);
            string newExe = UpdateService.PreparePackage(local, stage);
            if (string.IsNullOrEmpty(newExe) || !File.Exists(newExe))
                return "更新包里没有找到 MouseDock.exe";

            progress?.Invoke(new Step("启动更新进程，程序即将重启…", 100));
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell", "v1.0", "powershell.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetTempPath()
            };
            foreach (string arg in new[]
                     {
                         "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                         "-File", scriptPath,
                         "-ParentPid", Environment.ProcessId.ToString(),
                         "-Package", local,
                         "-TargetExe", exePath,
                         "-InstallDir", installDir,
                         "-LogPath", logPath
                     })
                start.ArgumentList.Add(arg);

            using var updater = Process.Start(start);
            if (updater == null) return "无法启动更新进程";
            return null;
        }
        catch (Exception ex)
        {
            try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { }
            return "更新启动失败：" + ex.Message;
        }
    }

    /// <summary>收掉上一次更新留下的暂存目录。</summary>
    internal static void CleanupPrevious(string[] args)
    {
        int index = Array.FindIndex(args, a => string.Equals(a, CleanupSwitch, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length) return;
        string stale = args[index + 1];
        if (string.IsNullOrWhiteSpace(stale) || !Directory.Exists(stale)) return;
        if (!Path.GetFileName(stale).StartsWith("MouseDock-update-", StringComparison.Ordinal)) return;
        try { Directory.Delete(stale, true); } catch { }
    }

    /// <summary>
    /// 独立更新进程的脚本。全部步骤都在这里，主程序退出后由它接手。
    /// </summary>
    private static string BuildScript() => """
        param(
            [Parameter(Mandatory=$true)][int]$ParentPid,
            [Parameter(Mandatory=$true)][string]$Package,
            [Parameter(Mandatory=$true)][string]$TargetExe,
            [Parameter(Mandatory=$true)][string]$InstallDir,
            [Parameter(Mandatory=$true)][string]$LogPath
        )
        $ErrorActionPreference = 'Stop'
        function Note([string]$m) {
            try { Add-Content -LiteralPath $LogPath -Value ((Get-Date -Format 'HH:mm:ss') + ' ' + $m) -Encoding UTF8 } catch { }
        }
        $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
        $runName = 'MouseDock'
        $finished = $false
        try {
            Note "updater started pid=$PID parent=$ParentPid"

            # 1. 等主程序退出
            $parent = Get-Process -Id $ParentPid -ErrorAction SilentlyContinue
            if ($null -ne $parent) {
                try { [void]$parent.WaitForExit(60000) } finally { $parent.Dispose() }
            }
            Note 'parent exited'

            # 2. 彻底关闭所有 MouseDock 进程
            for ($i = 0; $i -lt 20; $i++) {
                $left = @(Get-Process -Name 'MouseDock' -ErrorAction SilentlyContinue)
                if ($left.Count -eq 0) { break }
                foreach ($p in $left) { try { $p.Kill(); $p.WaitForExit(5000) } catch { } finally { $p.Dispose() } }
                Start-Sleep -Milliseconds 400
            }
            $still = @(Get-Process -Name 'MouseDock' -ErrorAction SilentlyContinue)
            if ($still.Count -gt 0) { throw ("仍有 $($still.Count) 个 MouseDock 进程未退出") }
            Note 'all MouseDock processes closed'

            # 3. 删除旧启动入口：注册表项 + 桌面快捷方式
            try { Remove-ItemProperty -Path $runKey -Name $runName -ErrorAction SilentlyContinue } catch { }
            $desktop = [Environment]::GetFolderPath('Desktop')
            if ($desktop) {
                Get-ChildItem -LiteralPath $desktop -Filter 'MouseDock*.lnk' -ErrorAction SilentlyContinue |
                    ForEach-Object { try { [IO.File]::Delete($_.FullName) } catch { } }
            }
            Note 'old launch entries removed'

            # 4. 删除旧文件：安装目录里程序同族文件全部清掉
            if (Test-Path -LiteralPath $InstallDir) {
                $program = @(Get-ChildItem -LiteralPath $InstallDir -File -Recurse -ErrorAction SilentlyContinue |
                    Where-Object { $_.Extension -in @('.exe','.dll','.json','.pdb','.deps','.runtimeconfig','.xml','.config') })
                foreach ($f in $program) { try { [IO.File]::Delete($f.FullName) } catch { Note ("skip " + $f.FullName) } }
                Note ("old files removed: " + $program.Count)
            } else {
                [IO.Directory]::CreateDirectory($InstallDir) | Out-Null
            }

            # 5. 解压新版本
            $stage = Join-Path $env:TEMP ('MouseDock-stage-' + [guid]::NewGuid().ToString('N'))
            [IO.Directory]::CreateDirectory($stage) | Out-Null
            Expand-Archive -LiteralPath $Package -DestinationPath $stage -Force
            $newExe = @(Get-ChildItem -LiteralPath $stage -Filter 'MouseDock.exe' -File -Recurse -ErrorAction SilentlyContinue) | Select-Object -First 1
            if ($null -eq $newExe) { throw '更新包里没有 MouseDock.exe' }
            Note ("package extracted: " + $newExe.FullName)

            # 6. 铺新文件
            $root = $newExe.Directory.FullName
            $rootPrefix = $root.TrimEnd('\') + '\'
            foreach ($f in @(Get-ChildItem -LiteralPath $root -File -Recurse)) {
                $relative = $f.FullName.Substring($rootPrefix.Length)
                $dest = [IO.Path]::Combine($InstallDir, $relative)
                $destDir = [IO.Path]::GetDirectoryName($dest)
                if ($destDir) { [IO.Directory]::CreateDirectory($destDir) | Out-Null }
                Copy-Item -LiteralPath $f.FullName -Destination $dest -Force
            }
            Note 'new files copied'

            # 7. 重建自启动项
            try {
                if (!(Test-Path -LiteralPath $runKey)) { New-Item -Path $runKey -Force | Out-Null }
                New-ItemProperty -Path $runKey -Name $runName -Value ('"' + $TargetExe + '" --tray') `
                    -PropertyType String -Force | Out-Null
                Note 'run key rebuilt'
            } catch { Note ('run key failed: ' + $_.Exception.Message) }

            # 8. 重建桌面快捷方式
            try {
                if ($desktop) {
                    $lnk = Join-Path $desktop 'MouseDock.lnk'
                    $wsh = New-Object -ComObject WScript.Shell
                    $sc = $wsh.CreateShortcut($lnk)
                    $sc.TargetPath = $TargetExe
                    $sc.WorkingDirectory = $InstallDir
                    $sc.Description = '外设电量，一起看见'
                    $sc.IconLocation = $TargetExe + ',0'
                    $sc.Save()
                    Note 'desktop shortcut rebuilt'
                }
            } catch { Note ('shortcut failed: ' + $_.Exception.Message) }

            # 9. 暂存目录收掉，再拉起新版
            try { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue } catch { }
            if (Test-Path -LiteralPath $TargetExe) {
                Start-Process -FilePath $TargetExe -WorkingDirectory $InstallDir | Out-Null
                Note 'new version started'
            } else {
                throw '新版本 exe 不在预期位置'
            }
            $finished = $true
        } catch {
            Note ('FAILED: ' + ($_ | Out-String))
            # 失败时尽力把旧主程序拉起来，至少不让人完全没程序可用。
            try {
                if (Test-Path -LiteralPath $TargetExe) {
                    Start-Process -FilePath $TargetExe -WorkingDirectory $InstallDir | Out-Null
                }
            } catch { }
        } finally {
            Note ("updater finished success=$finished")
        }
        exit ($(if ($finished) { 0 } else { 1 }))
        """;
}
