param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet = "D:\AgentTools\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

Write-Host "[MouseDock] build $Configuration ..." -ForegroundColor Cyan
& $dotnet build "$root\src\MouseDock\MouseDock.csproj" -c $Configuration -v m --nologo
if ($LASTEXITCODE -ne 0) { throw "build failed" }

Write-Host "[MouseDock] publish -> dist ..." -ForegroundColor Cyan
& $dotnet publish "$root\src\MouseDock\MouseDock.csproj" -c $Configuration -r $Runtime `
    --self-contained false -o "$root\dist" -v m --nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

Write-Host "[MouseDock] done: $root\dist\MouseDock.exe" -ForegroundColor Green
Write-Host "  自检截图:  $root\dist\MouseDock.exe --shot" -ForegroundColor DarkGray
