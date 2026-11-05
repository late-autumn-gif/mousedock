#requires -Version 5.1
<#
    apply-capture.ps1 —— 把 HidProbe 抓到的应答直接变成代码里的索引。

    用法:
        .\apply-capture.ps1 -CaptureDir "C:\Users\xxx\Desktop\HidProbe-20261105-013000"
        .\apply-capture.ps1 -CaptureDir ... -Apply        # 真的改代码并重编

    不带 -Apply 只出分析报告，不动源码。
    带了 -Apply 会改 VendorProtocol.KeyboardBattery.cs 里的
    RainyBatteryIndex / RainyChargingIndex，然后 build + 跑测试。

    判定依据:
        这套键盘的应答有固定锚点 20 00 1A 06 落在 [1][3][4]。
        电量必然是个 1..100 的字节，充电位是 0..2。
        在 [5..24] 里找同时满足「所有应答都 <=100」「至少一条非零」的字节，
        唯一命中的就是电量位；多于一个命中就按「靠近锚点」排序，并在报告里列全。
#>
param(
    [Parameter(Mandatory = $true)][string]$CaptureDir,
    [string]$RepoRoot = "D:\mousedock\repo",
    [switch]$Apply
)

$ErrorActionPreference = "Stop"

function Read-Events([string]$dir) {
    $file = Join-Path $dir "events.jsonl"
    if (-not (Test-Path $file)) { throw "找不到 $file。这是 HidProbe 的输出目录吗？" }
    $list = New-Object System.Collections.ArrayList
    foreach ($line in Get-Content $file -Encoding UTF8) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { [void]$list.Add(($line | ConvertFrom-Json)) } catch { }
    }
    return $list
}

function ConvertFrom-Hex([string]$hex) {
    if ([string]::IsNullOrEmpty($hex)) { return ,@() }
    $n = [int]($hex.Length / 2)
    $bytes = New-Object 'int[]' $n
    for ($i = 0; $i -lt $n; $i++) {
        $bytes[$i] = [Convert]::ToInt32($hex.Substring($i * 2, 2), 16)
    }
    return ,$bytes
}

function Format-Hex($bytes, [int]$max = 24) {
    $n = [Math]::Min($bytes.Count, $max)
    $parts = @()
    for ($i = 0; $i -lt $n; $i++) { $parts += ('{0:X2}' -f $bytes[$i]) }
    $text = $parts -join ' '
    if ($bytes.Count -gt $max) { $text += ' …' }
    return $text
}

Write-Host "读取抓包: $CaptureDir"
$events = Read-Events $CaptureDir
Write-Host ("事件 {0} 条" -f $events.Count)

# ── 1. 配对 SEND 与它之后的 IN ────────────────────────────────────────────
$pairs = New-Object System.Collections.ArrayList
for ($i = 0; $i -lt $events.Count; $i++) {
    $send = $events[$i]
    if ($send.kind -ne 'SEND') { continue }

    $reply = $null
    for ($j = $i + 1; $j -lt $events.Count; $j++) {
        $next = $events[$j]
        if ($next.kind -eq 'SEND') { break }
        if (($next.t - $send.t) -gt 1500) { break }
        if ($next.kind -eq 'IN' -and $next.col -eq $send.col) { $reply = $next; break }
    }
    if ($null -eq $reply) { continue }

    [void]$pairs.Add([pscustomobject]@{
        Vid      = $send.vid
        Pid      = $send.pid
        Page     = $send.up
        Col      = $send.col
        Name     = $send.note
        Latency  = $reply.t - $send.t
        Request  = (ConvertFrom-Hex $send.hex)
        Response = (ConvertFrom-Hex $reply.hex)
    })
}

Write-Host ("拿到 {0} 组请求/应答" -f $pairs.Count)

# ── 2. 只留 Rainy 75 家族（320F:5055 / 320F:5088）────────────────────────
$rainy = @($pairs | Where-Object { $_.Vid -eq 0x320F -and ($_.Pid -eq 0x5055 -or $_.Pid -eq 0x5088) })
Write-Host ("其中 Rainy 75 家族 {0} 组" -f $rainy.Count)

if ($rainy.Count -eq 0) {
    Write-Host ""
    Write-Host "没有 320F:5088 / 320F:5055 的应答。可能的原因:" -ForegroundColor Yellow
    Write-Host "  - 抓包时键盘没插，或者接收器没连上"
    Write-Host "  - 官方驱动独占着设备（打开报 err=32），先把驱动退了重抓"
    Write-Host "  - 这条键盘的电量不走 FF1C/0092，需要看 devices.txt 里它到底有哪些厂商集合"
    Write-Host ""
    Write-Host "把 devices.txt 和 timeline.txt 发回来，我按实际集合改候选帧。"
    exit 2
}

# ── 3. 找电量字节 ────────────────────────────────────────────────────────
$anchorPairs = @($rainy | Where-Object {
    $_.Response.Count -ge 10 -and $_.Response[1] -eq 0x20 -and
    $_.Response[3] -eq 0x1A -and $_.Response[4] -eq 0x06
})

Write-Host ("带 20 00 1A 06 锚点的应答 {0} 条" -f $anchorPairs.Count)

$scored = New-Object System.Collections.ArrayList
if ($anchorPairs.Count -gt 0) {
    $maxLen = ($anchorPairs | ForEach-Object { $_.Response.Count } | Measure-Object -Maximum).Maximum
    for ($idx = 5; $idx -lt [Math]::Min($maxLen, 25); $idx++) {
        $values = @()
        $valid = $true
        foreach ($p in $anchorPairs) {
            if ($idx -ge $p.Response.Count) { $valid = $false; break }
            $v = $p.Response[$idx]
            if ($v -gt 100) { $valid = $false; break }
            $values += $v
        }
        if (-not $valid) { continue }
        if (($values | Measure-Object -Maximum).Maximum -le 0) { continue }

        [void]$scored.Add([pscustomobject]@{
            Index  = $idx
            Values = ($values -join ',')
            Min    = ($values | Measure-Object -Minimum).Minimum
            Max    = ($values | Measure-Object -Maximum).Maximum
            Spread = ($values | Measure-Object -Maximum).Maximum - ($values | Measure-Object -Minimum).Minimum
        })
    }
}

$batteryIndex = -1
$chargingIndex = -1
if ($scored.Count -gt 0) {
    # 电量是这组里数值最大的那个可信字节；充电位只会是 0/1/2。
    # 先按最大值降序，同值再按跨度升序，最后按位置靠前。
    $best = $scored | Sort-Object @{ Expression = { $_.Max }; Descending = $true },
                                 @{ Expression = { $_.Spread }; Descending = $false },
                                 @{ Expression = { $_.Index }; Descending = $false } |
            Select-Object -First 1
    $batteryIndex = $best.Index

    # 充电位：0..2，且至少有一帧出现过 1（全 0 的位置只是填充）。
    for ($idx = 5; $idx -lt 25; $idx++) {
        if ($idx -eq $batteryIndex) { continue }
        $ok = $true
        $hasOne = $false
        foreach ($p in $anchorPairs) {
            if ($idx -ge $p.Response.Count) { $ok = $false; break }
            $v = $p.Response[$idx]
            if ($v -gt 2) { $ok = $false; break }
            if ($v -eq 1) { $hasOne = $true }
        }
        if ($ok -and $hasOne) { $chargingIndex = $idx; break }
    }

    # 没有 1 就退一步，取第一个全 0/1 的位置。
    if ($chargingIndex -lt 0) {
        for ($idx = 5; $idx -lt 25; $idx++) {
            if ($idx -eq $batteryIndex) { continue }
            $ok = $true
            foreach ($p in $anchorPairs) {
                if ($idx -ge $p.Response.Count) { $ok = $false; break }
                if ($p.Response[$idx] -gt 2) { $ok = $false; break }
            }
            if ($ok) { $chargingIndex = $idx; break }
        }
    }
}

# ── 4. 出报告 ────────────────────────────────────────────────────────────
$report = New-Object System.Text.StringBuilder
[void]$report.AppendLine("# Rainy 75 抓包分析")
[void]$report.AppendLine("")
[void]$report.AppendLine("抓包目录: $CaptureDir")
[void]$report.AppendLine("生成时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
[void]$report.AppendLine("")
[void]$report.AppendLine("## 请求 / 应答")
[void]$report.AppendLine("")
foreach ($p in $rainy) {
    [void]$report.AppendLine(("- {0:X4}:{1:X4}  col#{2}  up={3:X4}  {4}ms  {5}" -f `
        $p.Vid, $p.Pid, $p.Col, $p.Page, $p.Latency, $p.Name))
    [void]$report.AppendLine("  - 发: " + (Format-Hex $p.Request 32))
    [void]$report.AppendLine("  - 收: " + (Format-Hex $p.Response 32))
    [void]$report.AppendLine("")
}

[void]$report.AppendLine("## 疑似电量字节")
[void]$report.AppendLine("")
if ($scored.Count -eq 0) {
    [void]$report.AppendLine("没有找到同时满足条件的字节。把上面这段应答发回来人工看。")
} else {
    [void]$report.AppendLine("| 下标 | 取值 | 跨度 |")
    [void]$report.AppendLine("|---|---|---|")
    foreach ($s in ($scored | Sort-Object Index)) {
        [void]$report.AppendLine(("| [{0}] | {1} | {2} |" -f $s.Index, $s.Values, $s.Spread))
    }
    [void]$report.AppendLine("")
    [void]$report.AppendLine(("选中: 电量 = [{0}]，充电位 = [{1}]" -f $batteryIndex, $chargingIndex))
}

$reportPath = Join-Path $CaptureDir "capture-analysis.md"
$report.ToString() | Set-Content -Path $reportPath -Encoding UTF8
Write-Host "分析报告: $reportPath"

# ── 5. 改代码 ────────────────────────────────────────────────────────────
if ($batteryIndex -lt 0) {
    Write-Host ""
    Write-Host "没推断出电量位，不动源码。把 capture-analysis.md 发回来。" -ForegroundColor Yellow
    exit 3
}

$sourceFile = Join-Path $RepoRoot "src\MouseDock\Services\Detection\VendorProtocol.KeyboardBattery.cs"
if (-not (Test-Path $sourceFile)) { throw "找不到 $sourceFile" }

if (-not $Apply) {
    Write-Host ""
    Write-Host ("预览: 会把 RainyBatteryIndex 改成 {0}，RainyChargingIndex 改成 {1}" -f $batteryIndex, $chargingIndex)
    Write-Host "加 -Apply 就真的改并重编。"
    exit 0
}

$text = Get-Content $sourceFile -Raw -Encoding UTF8
$before = $text
if (-not [regex]::IsMatch($before, 'RainyBatteryIndex = \d+')) {
    Write-Host "源码里没有 RainyBatteryIndex 常量，检查文件版本。" -ForegroundColor Yellow
    exit 4
}
$text = [regex]::Replace($text, 'RainyBatteryIndex = \d+', "RainyBatteryIndex = $batteryIndex")
if ($chargingIndex -ge 0) {
    $text = [regex]::Replace($text, 'RainyChargingIndex = \d+', "RainyChargingIndex = $chargingIndex")
}
# 保持无 BOM，免得整文件在 git 里显示成全量变更。
[System.IO.File]::WriteAllText($sourceFile, $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("已写入 {0}: 电量 [{1}] 充电 [{2}]" -f $sourceFile, $batteryIndex, $chargingIndex)

Write-Host ""
Write-Host "重编并跑测试……"
& (Join-Path $RepoRoot ".dotnet\dotnet.exe") build (Join-Path $RepoRoot "src\MouseDock\MouseDock.csproj") -c Release -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "编译失败" }
& (Join-Path $RepoRoot ".dotnet\dotnet.exe") run --project (Join-Path $RepoRoot "tests\MouseDock.ProtocolTests") -c Release
if ($LASTEXITCODE -ne 0) { throw "协议测试失败，回滚索引再试" }

Write-Host ""
Write-Host "完成。新版 exe 在 src\MouseDock\bin\Release\net10.0-windows10.0.26100.0\win-x64\MouseDock.exe"
Write-Host "拿去替换对方的 MouseDock，插上键盘看电量。"
