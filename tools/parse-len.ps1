param(
    [Parameter(Mandatory=$true)][string]$Pcap,
    [string]$Lens = "33,64,65",
    [int]$Limit = 60
)

$want = @{}
foreach ($x in $Lens.Split(',')) { $want[[int]$x.Trim()] = $true }

$bytes = [System.IO.File]::ReadAllBytes($Pcap)
$pos = 24
$hits = New-Object System.Collections.ArrayList
$devAgg = @{}

while ($pos + 16 -le $bytes.Length) {
    $tsSec = [BitConverter]::ToUInt32($bytes, $pos)
    $tsUsec = [BitConverter]::ToUInt32($bytes, $pos + 4)
    $inclLen = [BitConverter]::ToInt32($bytes, $pos + 8)
    $pos += 16
    if ($inclLen -le 0 -or $pos + $inclLen -gt $bytes.Length) { break }

    if ($inclLen -ge 27) {
        $hdrLen = [BitConverter]::ToUInt16($bytes, $pos)
        if ($hdrLen -ge 20 -and $hdrLen -lt 128 -and $hdrLen -lt $inclLen) {
            $dev = [BitConverter]::ToUInt16($bytes, $pos + 19)
            $ep = $bytes[$pos + 21]
            $tr = $bytes[$pos + 22]
            $dataLen = [BitConverter]::ToInt32($bytes, $pos + 23)
            $dataStart = $pos + $hdrLen
            $avail = $inclLen - $hdrLen

            if (-not $devAgg.ContainsKey($dev)) { $devAgg[$dev] = @{ N = 0; MaxLen = 0 } }
            $devAgg[$dev].N++
            if ($dataLen -gt $devAgg[$dev].MaxLen) { $devAgg[$dev].MaxLen = $dataLen }

            if ($want.ContainsKey($dataLen) -and $dataLen -le $avail) {
                $p = New-Object byte[] $dataLen
                [Array]::Copy($bytes, $dataStart, $p, 0, $dataLen)
                $sb = New-Object System.Text.StringBuilder
                $n = [Math]::Min(40, $dataLen)
                for ($i = 0; $i -lt $n; $i++) { [void]$sb.Append($p[$i].ToString("X2")); [void]$sb.Append(" ") }
                [void]$hits.Add([pscustomobject]@{
                    T   = [DateTimeOffset]::FromUnixTimeSeconds([int64]$tsSec).AddMilliseconds($tsUsec/1000).ToString("HH:mm:ss.fff")
                    Dev = $dev
                    Dir = $(if (($ep -band 0x80) -ne 0) { "IN" } else { "OUT" })
                    Ep  = ("0x{0:X2}" -f $ep)
                    Tr  = $tr
                    Len = $dataLen
                    Data = $sb.ToString().Trim()
                })
            }
        }
    }
    $pos += $inclLen
}

Write-Output "=== devices ==="
$devAgg.GetEnumerator() | Sort-Object Key | ForEach-Object {
    "dev={0}  packets={1}  maxLen={2}" -f $_.Key, $_.Value.N, $_.Value.MaxLen
}

Write-Output ""
Write-Output ("=== packets with len in [{0}] : {1} ===" -f $Lens, $hits.Count)
$hits | Select-Object -First $Limit | Format-Table -AutoSize
