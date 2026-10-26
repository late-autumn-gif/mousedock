param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Pfx,
    [Parameter(Mandatory = $true)][string]$Password,
    [string]$Timestamp = "http://timestamp.digicert.com"
)

# 代码签名模板：接自己的 PFX 证书即可。
# 申请免费开源签名的路径见 docs/SIGNING.md。
$ErrorActionPreference = "Stop"

$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match 'x64' } |
            Select-Object -Last 1 -ExpandProperty FullName

if (-not $signtool) {
    $signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue)?.Source
}

if (-not $signtool) {
    Write-Error "找不到 signtool.exe，请安装 Windows SDK"
    exit 1
}

Write-Host "使用 $signtool"

& $signtool sign /fd SHA256 /td SHA256 /tr $Timestamp /f $Pfx /p $Password `
    /d "MouseDock" /du "https://github.com/late-autumn-gif/MouseDock" $Exe

if ($LASTEXITCODE -ne 0) { Write-Error "签名失败"; exit $LASTEXITCODE }

$s = Get-AuthenticodeSignature $Exe
Write-Host ("状态: {0}" -f $s.Status)
if ($s.SignerCertificate) { Write-Host ("签名者: {0}" -f $s.SignerCertificate.Subject) }

Get-FileHash $Exe -Algorithm SHA256 | Format-List
