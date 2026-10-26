param([int]$Seconds = 6, [string]$OutDir = "D:\MouseDock\usbcapture")

$ErrorActionPreference = "SilentlyContinue"
$cmd = "C:\Program Files\USBPcap\USBPcapCMD.exe"
$driver = "D:\SC580\SC580\CBMouse.exe"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$log = Join-Path $OutDir "capture.log"
"start $(Get-Date -Format o)" | Set-Content $log

foreach ($hub in 1..6) {
    $dev = "\\.\USBPcap$hub"
    Get-Process CBMouse | Stop-Process -Force
    Start-Sleep -Milliseconds 400

    $out = Join-Path $OutDir ("hub{0}.pcap" -f $hub)
    Remove-Item $out -ErrorAction SilentlyContinue

    $err = Join-Path $OutDir ("hub{0}.err.txt" -f $hub)
    $cap = Start-Process -FilePath $cmd -ArgumentList @("-d", $dev, "-o", $out, "-A") -PassThru -WindowStyle Hidden -RedirectStandardError $err
    Start-Sleep -Milliseconds 900

    if ($cap.HasExited) {
        "hub$hub 打不开(err=$($cap.ExitCode)): $((Get-Content $err -Raw) -replace "`r?`n"," ")" | Add-Content $log
        continue
    }

    Start-Process -FilePath $driver -WorkingDirectory (Split-Path $driver -Parent) | Out-Null
    Start-Sleep -Seconds $Seconds
    Get-Process CBMouse | Stop-Process -Force
    Start-Sleep -Milliseconds 300
    try { Stop-Process -Id $cap.Id -Force } catch {}

    $size = (Get-Item $out -ErrorAction SilentlyContinue).Length
    "hub$hub -> $size bytes" | Add-Content $log
    if ($size -gt 1000) { break }
}

Get-Process CBMouse | Stop-Process -Force
"done" | Add-Content $log
Get-Content $log