param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet = Join-Path $root ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnetCommand) { throw "未找到 .NET 10 SDK，请先运行 MouseDock.cmd 或安装 SDK。" }
    $dotnet = $dotnetCommand.Source
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

Write-Host "[MouseDock] build $Configuration ..." -ForegroundColor Cyan
& $dotnet build "$root\src\MouseDock\MouseDock.csproj" -c $Configuration -v m --nologo
if ($LASTEXITCODE -ne 0) { throw "build failed" }

Write-Host "[MouseDock] publish -> app（单文件、自包含）..." -ForegroundColor Cyan
& $dotnet publish "$root\src\MouseDock\MouseDock.csproj" -c $Configuration -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -p:DebugSymbols=false `
    -o "$root\app" -v m --nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

Write-Host "[MouseDock] done: $root\app\MouseDock.exe" -ForegroundColor Green
Write-Host "  自检截图:  $root\app\MouseDock.exe --shot" -ForegroundColor DarkGray
