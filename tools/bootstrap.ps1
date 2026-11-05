param(
    [switch]$Rebuild,
    [switch]$NoShortcut,
    [switch]$NoLaunch,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root  = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$proj  = Join-Path $root "src\MouseDock\MouseDock.csproj"
$exe   = Join-Path $root "app\MouseDock.exe"
$dist  = Join-Path $root "app"
$local = Join-Path $root ".dotnet"
$sources = @(Get-ChildItem (Join-Path $root "src\MouseDock") -Recurse -File |
    Where-Object { $_.Extension -in ".cs", ".xaml" -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
if ($sources.Count -eq 0) { throw "旧代码已清空，请先添加新的应用入口后再启动。" }

function Write-Step($n, $text) {
    Write-Host ("[" + $n + "] " + $text) -ForegroundColor Cyan
}

function Find-Dotnet {
    $cands = @(
        (Join-Path $local "dotnet.exe"),
        "D:\AgentTools\dotnet\dotnet.exe"
    )
    foreach ($c in $cands) { if (Test-Path $c) { return $c } }
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

function Test-Sdk10($exePath) {
    try {
        $out = & $exePath --list-sdks 2>$null
        foreach ($line in $out) { if ("$line" -match '^10\.') { return $true } }
    } catch { }
    return $false
}

function Install-Dotnet {
    Write-Host "[.] 没找到 .NET 10 SDK，装到仓库本地 .dotnet\（不写注册表、不碰 C 盘）" -ForegroundColor Yellow
    $installer = Join-Path $root ".dotnet-install.ps1"
    if (-not (Test-Path $installer)) {
        Write-Host "    下载 dotnet-install.ps1 ..."
        Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installer -UseBasicParsing
    }
    & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass `
        -File $installer -Channel 10.0 -InstallDir $local -NoPath
    $new = Join-Path $local "dotnet.exe"
    if (-not (Test-Path $new)) { throw "SDK 安装失败：$new 不存在" }
    return $new
}

function Test-NeedsBuild {
    if ($Rebuild) { return $true }
    if (-not (Test-Path $exe)) { return $true }
    $exeTime = (Get-Item $exe).LastWriteTimeUtc
    $srcDir  = Join-Path $root "src\MouseDock"
    $newer = Get-ChildItem $srcDir -Recurse -File -Include *.cs, *.xaml, *.csproj |
             Where-Object { $_.LastWriteTimeUtc -gt $exeTime }
    return [bool]$newer
}

function New-Shortcut($lnkPath) {
    $shell = New-Object -ComObject WScript.Shell
    $sc = $shell.CreateShortcut($lnkPath)
    $sc.TargetPath        = $exe
    $sc.WorkingDirectory  = (Split-Path $exe -Parent)
    $sc.IconLocation      = ($exe + ",0")
    $sc.Description       = "MouseDock - 外设电量面板"
    $sc.WindowStyle       = 1
    $sc.Save()
}

Write-Host ""
Write-Host "MouseDock 一键启动" -ForegroundColor Green
Write-Host ("仓库: " + $root) -ForegroundColor DarkGray

$dotnet = Find-Dotnet
if (-not $dotnet -or -not (Test-Sdk10 $dotnet)) {
    $dotnet = Install-Dotnet
}

if (Test-NeedsBuild) {
    Write-Step "1/3" "编译并打成单文件 exe（第一次要下运行时，几分钟）"
    & $dotnet publish $proj -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None -p:DebugSymbols=false `
        -o $dist -v m --nologo
    if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE（多半是还原 NuGet 包时网络不通）" }
    if (-not (Test-Path $exe)) { throw "编译结束了但没找到 $exe" }
} else {
    Write-Step "1/3" "dist 已是最新，跳过编译（要强制重编加 -Rebuild）"
}

if ($NoShortcut) {
    Write-Step "2/3" "跳过快捷方式"
} else {
    Write-Step "2/3" "写入桌面 / 开始菜单快捷方式"
    New-Shortcut (Join-Path ([Environment]::GetFolderPath("Desktop")) "MouseDock.lnk")
    $startMenu = Join-Path ([Environment]::GetFolderPath("StartMenu")) "Programs"
    New-Item -ItemType Directory -Force -Path $startMenu | Out-Null
    New-Shortcut (Join-Path $startMenu "MouseDock.lnk")
}

if ($NoLaunch) {
    Write-Step "3/3" "跳过启动（-NoLaunch）"
} else {
    Write-Step "3/3" "启动"
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe -Parent)
}

Write-Host ""
Write-Host "好了。" -ForegroundColor Green
Write-Host ("  程序  : " + $exe)
Write-Host  "  桌面  : MouseDock.lnk"

