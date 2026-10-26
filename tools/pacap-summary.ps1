param(
    [Parameter(Mandatory=$true)][string]$Pcap,
    [int]$Dev = -1,
    [int]$Limit = 40
)

$bytes = [System.IO.File]::ReadAllBytes($Pcap)
$pos = 24
$rows = New-Object System.Collections.ArrayList
$agg = @{}

while ($pos + 16 -le $bytes.Length) {
    $tsSec = [BitConverter]::ToUInt32($bytes, $pos)
    $inclLen = [BitConverter]::ToInt32($bytes, $pos + 8)
    $pos += 16
    if ($inclLen -le 0 -or $pos + $inclLen -gt $bytes.Length) { break }

    if ($inclLen -ge 27) {
        $hdrLen = [BitConverter]::ToUInt16($bytes, $pos)
        if ($hdrLen -ge 20 -and $hdrLen -lt 128 -and $hdrLen -lt $inclLen) {
            $dev = [BitConverter]::ToUInt16($bytes, $pos + 19)
            $ep = $bytes[$pos + 21]
            $transfer = $bytes[$pos + 22]
            $dataLen = [BitConverter]::ToInt32($bytes, $pos + 23)
            $dataStart = $pos + $hdrLen
            $avail = $inclLen - $hdrLen

            $key = ("dev={0} ep=0x{1:X2} tr={2} len={3}" -f $dev, $ep, $transfer, $dataLen)
            if ($agg.ContainsKey($key)) { $agg[$key]++ } else { $agg[$key] = 1 }

            if (($Dev -lt 0 -or $dev -eq $Dev) -and $dataLen -ge 8 -and $dataLen -le $avail) {
                $p = New-Object byte[] $dataLen
                [Array]::Copy($bytes, $dataStart, $p, 0, $dataLen)
                $sb = New-Object System.Text.StringBuilder
                $n = [Math]::Min(32, $dataLen)
                for ($i = 0; $i -lt $n; $i++) { [void]$sb.Append($p[$i].ToString("X2")); [void]$sb.Append(" ") }
                [void]$rows.Add([pscustomobject]@{
                    T    = [DateTimeOffset]::FromUnixTimeSeconds([int64]$tsSec).ToString("HH:mm:ss")
                    Dev  = $dev
                    Dir  = $(if (($ep -band 0x80) -ne 0) { "IN" } else { "OUT" })
                    Ep   = ("0x{0:X2}" -f $ep)
                    Tr   = $transfer
                    Len  = $dataLen
                    Data = $sb.ToString().Trim()
                })
            }
        }
    }
    $pos += $inclLen
}

Write-Output "=== packet shape summary (top 25) ==="
$agg.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 25 |
    ForEach-Object { "{0,-36} x{1}" -f $_.Key, $_.Value }

Write-Output ""
Write-Output ("=== data packets (dev filter = {0}, first {1}) ===" -f $Dev, $Limit)
$rows | Select-Object -First $Limit | Format-Table -AutoSize
