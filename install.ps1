param(
    [switch]$NoShortcut,
    [switch]$AutoStart
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root "dist\MouseDock.exe"

if (-not (Test-Path $exe)) {
    Write-Host "[1/4] build + publish ..." -ForegroundColor Cyan
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "build.ps1")
    if (-not (Test-Path $exe)) { throw "build failed: $exe not found" }
} else {
    Write-Host "[1/4] dist\MouseDock.exe exists, skip build" -ForegroundColor DarkGray
}

$desktopLnk = Join-Path ([Environment]::GetFolderPath("Desktop")) "MouseDock.lnk"

if (-not $NoShortcut) {
    Write-Host "[2/4] desktop shortcut ..." -ForegroundColor Cyan
    $shell = New-Object -ComObject WScript.Shell
    $sc = $shell.CreateShortcut($desktopLnk)
    $sc.TargetPath = $exe
    $sc.WorkingDirectory = (Split-Path $exe -Parent)
    $sc.IconLocation = ($exe + ",0")
    $sc.Description = "MouseDock - battery widget"
    $sc.WindowStyle = 1
    $sc.Save()

    Write-Host "[3/4] start menu shortcut ..." -ForegroundColor Cyan
    $startMenu = Join-Path ([Environment]::GetFolderPath("StartMenu")) "Programs"
    New-Item -ItemType Directory -Force -Path $startMenu | Out-Null
    $sc2 = $shell.CreateShortcut((Join-Path $startMenu "MouseDock.lnk"))
    $sc2.TargetPath = $exe
    $sc2.WorkingDirectory = (Split-Path $exe -Parent)
    $sc2.IconLocation = ($exe + ",0")
    $sc2.Description = "MouseDock - battery widget"
    $sc2.Save()
} else {
    Write-Host "[2/4][3/4] skip shortcuts" -ForegroundColor DarkGray
}

if ($AutoStart) {
    Write-Host "[4/4] autostart ..." -ForegroundColor Cyan
    $run = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    Set-ItemProperty -Path $run -Name "MouseDock" -Value ('"' + $exe + '" --tray')
} else {
    Write-Host "[4/4] autostart not enabled (use -AutoStart)" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "done:" -ForegroundColor Green
Write-Host ("  exe       : " + $exe)
Write-Host ("  desktop   : " + $desktopLnk)
Write-Host ("  detect    : " + $exe + " --detect")
Write-Host ("  capture   : " + $exe + " --capture24g 10")
Write-Host ("  background: " + $exe + " --tray")
