param(
    [switch]$KeepNexus
)

# 输入卡死急救：全屏游戏/反作弊把输入吃掉时，从键盘跑这一个脚本即可恢复
# 用法：右键“使用 PowerShell 运行”，或在任意终端执行：
#   powershell -ExecutionPolicy Bypass -File D:\MouseDock\tools\rescue-input.ps1

$ErrorActionPreference = "SilentlyContinue"
Write-Host "=== 输入急救 ===" -ForegroundColor Cyan

# 1) 结束常见的全屏游戏 / 启动器（WMI 终止对反作弊保护的进程也有效）
$patterns = 'ZenlessZoneZero', 'launcher', 'ACLOS', 'aclos-launcher', 'WeGame', 'steamwebhelper'
$killed = @()
Get-CimInstance Win32_Process |
    Where-Object { $_.Name -and ($patterns | Where-Object { $_.Name -like "*$_*" }) } |
    ForEach-Object {
        $r = Invoke-CimMethod -InputObject $_ -MethodName Terminate
        if ($r.ReturnValue -eq 0) { $killed += "$($_.Name)($($_.ProcessId))" }
    }
if ($killed.Count -gt 0) { Write-Host ("已结束: " + ($killed -join ', ')) -ForegroundColor Yellow }
else { Write-Host "没有需要结束的游戏进程" -ForegroundColor DarkGray }

# 2) 重启资源管理器（任务栏/悬停/点击路由）
Write-Host "重启资源管理器…" -ForegroundColor Cyan
Stop-Process -Name explorer -Force
Start-Sleep -Seconds 3
if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }
Start-Sleep -Seconds 2

# 3) 可选：重启 Nexus 停靠栏
if (-not $KeepNexus) {
    $nexus = "D:\Nexus\Winstep\Nexus.exe"
    if (Test-Path $nexus) {
        Get-Process Nexus | Stop-Process -Force
        Start-Sleep -Seconds 2
        Start-Process $nexus -WorkingDirectory (Split-Path $nexus -Parent)
        Write-Host "Nexus 已重启" -ForegroundColor Cyan
    }
}

# 4) 结论
Start-Sleep -Seconds 2
$tray = Get-Process explorer -ErrorAction SilentlyContinue
Write-Host ""
Write-Host ("explorer: " + ($(if ($tray) { "OK (PID $($tray.Id -join ','))" } else { "未启动" }))) -ForegroundColor Green
Write-Host "如果点击仍然无反应，按 Ctrl+Alt+Del → 右下角电源 → 重启" -ForegroundColor Yellow
