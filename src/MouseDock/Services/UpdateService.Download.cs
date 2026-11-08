using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
namespace MouseDock.Services;
internal static partial class UpdateService
{
    // Public prefix documented at https://gh-proxy.com/docs/quick-start.
    // Mirrors receive only a public release URL, never credentials or local data.
    internal static IEnumerable<string> DownloadUrls(UpdateInfo info)
    {
        yield return info.AssetUrl;
        if (System.Text.RegularExpressions.Regex.IsMatch(info.Sha256??"",@"^[a-fA-F0-9]{64}$"))
            yield return "https://gh-proxy.org/"+info.AssetUrl;
    }

    internal static async Task<bool> DownloadPackageAsync(UpdateInfo info,string destination,IProgress<int> progress,
        IProgress<string> status,CancellationToken ct,HttpClient client=null)
    {
        if(info==null || !Uri.TryCreate(info.AssetUrl,UriKind.Absolute,out var uri) || uri.Scheme!="https" ||
            uri.Host!="github.com" || !uri.AbsolutePath.StartsWith($"/{Owner}/{Repo}/releases/download/",StringComparison.OrdinalIgnoreCase) ||
            info.AssetSize<=0)return false;
        using var total=CancellationTokenSource.CreateLinkedTokenSource(ct);total.CancelAfter(TimeSpan.FromMinutes(15));
        int attempt=0;
        foreach(string url in DownloadUrls(info))
        {
            if(total.IsCancellationRequested)return false;
            status?.Report(attempt++==0?"正在通过GitHub直连下载…":"直连失败，正在通过GH-Proxy镜像下载…");progress?.Report(0);
            try
            {
                using var headers=CancellationTokenSource.CreateLinkedTokenSource(total.Token);headers.CancelAfter(TimeSpan.FromSeconds(20));
                using var response=await (client??_http).GetAsync(url,HttpCompletionOption.ResponseHeadersRead,headers.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();headers.CancelAfter(Timeout.InfiniteTimeSpan);
                using(var source=await response.Content.ReadAsStreamAsync(total.Token).ConfigureAwait(false))
                using(var target=new FileStream(destination,FileMode.Create,FileAccess.Write,FileShare.None,81920,true))
                {
                    long done=0;var buffer=new byte[81920];
                    while(true)
                    {
                        using var idle=CancellationTokenSource.CreateLinkedTokenSource(total.Token);idle.CancelAfter(TimeSpan.FromSeconds(30));
                        int n=await source.ReadAsync(buffer.AsMemory(),idle.Token).ConfigureAwait(false);if(n==0)break;
                        done+=n;if(done>info.AssetSize)throw new InvalidDataException("Download exceeds release asset size");
                        await target.WriteAsync(buffer.AsMemory(0,n),total.Token).ConfigureAwait(false);
                        progress?.Report((int)(done*90/info.AssetSize));
                    }
                    if(done!=info.AssetSize)throw new InvalidDataException("Incomplete release asset");
                }
                status?.Report("正在校验安装包…");
                if(!string.IsNullOrEmpty(info.Sha256))
                {
                    using var stream=File.OpenRead(destination);
                    string hash=Convert.ToHexString(await SHA256.HashDataAsync(stream,total.Token).ConfigureAwait(false));
                    if(!hash.Equals(info.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Release SHA256 mismatch");
                }
                return true;
            }
            catch { } // Keep failed downloads for diagnostics, per user preference.
        }
        status?.Report("直连与可用镜像下载失败，未修改旧版本。");return false;
    }

    internal static string PreparePackage(string downloaded,string extract)
    {
        if(downloaded.EndsWith(".zip",StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(downloaded,extract);
            var found=Directory.GetFiles(extract,"MouseDock.exe",SearchOption.AllDirectories);
            if(found.Length!=1)throw new InvalidDataException("Package must have exactly one MouseDock.exe");
            return found[0];
        }
        if(downloaded.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(extract);string exe=Path.Combine(extract,"MouseDock.exe");File.Copy(downloaded,exe);return exe;
        }
        throw new InvalidDataException("Unsupported update package");
    }

    public static async Task<bool> DownloadAndApplyAsync(UpdateInfo info,IProgress<int> progress=null,
        CancellationToken ct=default,IProgress<string> status=null)
    {
        string work=null;
        try
        {
            if(info==null || string.IsNullOrWhiteSpace(info.AssetName) || Path.GetFileName(info.AssetName)!=info.AssetName)return false;
            work=Path.Combine(Path.GetTempPath(),"MouseDock-update-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
            string downloaded=Path.Combine(work,info.AssetName);
            if(!await DownloadPackageAsync(info,downloaded,progress,status,ct).ConfigureAwait(false))return false;

            // 下载完之后交给同一个独立进程去替换。在线路径和本地包路径必须是同一条路，
            // 否则「删旧文件、重建入口」这两件事只在本地包路径生效，用户走自动更新就享受不到。
            status?.Report("准备独立更新进程…");
            string failure=UpdateApplier.Apply(downloaded, m=>status?.Report(m));
            if(failure!=null){status?.Report(failure);return false;}
            work=null; // The detached updater owns its own staging directory.
            return true;
        }
        catch{return false;}
        // Keep staging on success or failure; temporary cleanup was explicitly cancelled.
    }

    internal static ProcessStartInfo CreateApplyStartInfo(string work,string newExe,string currentExe,int processId,bool restart=true)
    {
        work=Path.GetFullPath(work);newExe=Path.GetFullPath(newExe);currentExe=Path.GetFullPath(currentExe);
        string temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!work.StartsWith(temp,StringComparison.OrdinalIgnoreCase) ||
            !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(work),@"^MouseDock-update-[a-f0-9]{32}$") ||
            !newExe.StartsWith(work+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) ||
            currentExe.StartsWith(work+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid staging paths");
        File.WriteAllText(Path.Combine(work,"apply.json"),System.Text.Json.JsonSerializer.Serialize(new
        {WorkRoot=work,PackageRoot=Path.GetDirectoryName(newExe),NewExe=newExe,CurrentExe=currentExe,ProcessId=processId,Restart=restart}));
        const string script="""
            $ErrorActionPreference = 'Stop'
            $touched = [Collections.Generic.List[object]]::new()
            $rollbackOk = $true
            try {
                $job = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'apply.json') -Encoding UTF8 -Raw | ConvertFrom-Json
                $work = [IO.Path]::GetFullPath($PSScriptRoot)
                $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
                if ($work -ne [IO.Path]::GetFullPath($job.WorkRoot) -or !$work.StartsWith($tempPrefix,[StringComparison]::OrdinalIgnoreCase) -or
                    [IO.Path]::GetFileName($work) -notmatch '^MouseDock-update-[a-f0-9]{32}$') { throw 'Invalid staging root' }
                $workPrefix = $work.TrimEnd('\') + '\'
                $source = [IO.Path]::GetFullPath($job.PackageRoot)
                $target = [IO.Path]::GetFullPath($job.CurrentExe)
                $install = [IO.Path]::GetDirectoryName($target)
                $installPrefix = $install.TrimEnd('\') + '\'
                if (!$source.StartsWith($workPrefix,[StringComparison]::OrdinalIgnoreCase) -or
                    $target.StartsWith($workPrefix,[StringComparison]::OrdinalIgnoreCase) -or
                    [IO.Path]::GetFullPath($job.NewExe) -ne (Join-Path $source 'MouseDock.exe') -or
                    [IO.Path]::GetFileName($target) -ine 'MouseDock.exe') { throw 'Invalid update paths' }
                $sourcePrefix = $source.TrimEnd('\') + '\'
                if (!(Test-Path -LiteralPath $job.NewExe -PathType Leaf)) { throw 'Missing program' }
                $old = Get-Process -Id $job.ProcessId -ErrorAction SilentlyContinue
                if ($null -ne $old -and $job.ProcessId -gt 0) {
                    try { if (!$old.WaitForExit(60000)) { throw 'Application did not exit' } } finally { $old.Dispose() }
                }
                $files = @(Get-ChildItem -LiteralPath $source -File -Recurse | Sort-Object @{Expression={ $_.Name -ieq 'MouseDock.exe' }},FullName)
                foreach ($file in $files) {
                    $relative = $file.FullName.Substring($sourcePrefix.Length)
                    $dest = [IO.Path]::GetFullPath((Join-Path $install $relative))
                    if (!$dest.StartsWith($installPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Destination outside install folder' }
                    $ancestor = $dest
                    while ($ancestor -and $ancestor -ne $install) {
                        $existing = Get-Item -LiteralPath $ancestor -Force -ErrorAction SilentlyContinue
                        if ($null -ne $existing -and ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Destination reparse point' }
                        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
                    }
                    if (Test-Path -LiteralPath $dest -PathType Container) { throw 'Destination file is a directory' }
                    $existed = Test-Path -LiteralPath $dest -PathType Leaf
                    $backup = Join-Path (Join-Path $work 'backup') $relative
                    if ($existed) {
                        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup)) | Out-Null
                        Copy-Item -LiteralPath $dest -Destination $backup -Force
                    }
                    $touched.Add([pscustomobject]@{Path=$dest;Backup=$backup;Existed=$existed})
                    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($dest)) | Out-Null
                    Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
                }
                # 清理旧版本遗留：更新包里没有的程序文件从安装目录删掉。
                $keep = @{}
                foreach ($file in $files) { $keep[$file.FullName.Substring($sourcePrefix.Length)] = $true }
                $stale = @(Get-ChildItem -LiteralPath $install -File -Recurse -ErrorAction SilentlyContinue)
                foreach ($old in $stale) {
                    $relative = $old.FullName.Substring($installPrefix.Length)
                    if ($keep.ContainsKey($relative)) { continue }
                    # 只删主程序同族的文件，用户自己放的东西一律不碰。
                    if ($old.Extension -notin @('.exe','.dll','.json','.pdb','.deps','.runtimeconfig')) { continue }
                    try { [IO.File]::Delete($old.FullName) } catch { }
                }
                # 重建自启动项：先删后写。路径变了就不会留下指向旧 exe 的入口。
                try {
                    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
                    Remove-ItemProperty -Path $runKey -Name 'MouseDock' -ErrorAction SilentlyContinue
                    New-ItemProperty -Path $runKey -Name 'MouseDock' -Value ('"' + $target + '" --tray') `
                        -PropertyType String -Force | Out-Null
                } catch { }
                # 重建桌面快捷方式：先清掉所有旧的和重复的，再建一个指向当前 exe。
                try {
                    $desktop = [Environment]::GetFolderPath('Desktop')
                    if ($desktop) {
                        Get-ChildItem -LiteralPath $desktop -Filter 'MouseDock*.lnk' -ErrorAction SilentlyContinue |
                            ForEach-Object { try { [IO.File]::Delete($_.FullName) } catch { } }
                        $lnk = Join-Path $desktop 'MouseDock.lnk'
                        $wsh = New-Object -ComObject WScript.Shell
                        $sc = $wsh.CreateShortcut($lnk)
                        $sc.TargetPath = $target
                        $sc.WorkingDirectory = $install
                        $sc.Description = '外设电量，一起看见'
                        $sc.IconLocation = $target + ',0'
                        $sc.Save()
                    }
                } catch { }
                if ($job.Restart) {
                    $start = New-Object Diagnostics.ProcessStartInfo
                    $start.FileName = $target; $start.WorkingDirectory = $install
                    $start.UseShellExecute = $true; $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
                    $replacement = [Diagnostics.Process]::Start($start)
                    if ($null -eq $replacement) { throw 'Restart failed' }; $replacement.Dispose()
                }
                # Keep download, extracted package and backups; cleanup was cancelled.
                exit 0
            } catch {
                $failure = $_ | Out-String
                for ($i=$touched.Count-1; $i -ge 0; $i--) {
                    $item = $touched[$i]
                    try {
                        if ($item.Existed) { Copy-Item -LiteralPath $item.Backup -Destination $item.Path -Force }
                        elseif (Test-Path -LiteralPath $item.Path -PathType Leaf) { [IO.File]::Delete($item.Path) }
                    } catch { $rollbackOk=$false; $failure += $_ | Out-String }
                }
                $failure | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'apply.log') -Encoding UTF8
                if ($job.Restart) {
                    try {
                        Add-Type -AssemblyName PresentationFramework
                        [Windows.MessageBox]::Show("更新失败。已尝试恢复旧文件，请保留以下目录以便排查：`n$PSScriptRoot",'MouseDock 更新') | Out-Null
                        if ($rollbackOk -and (Test-Path -LiteralPath $job.CurrentExe -PathType Leaf)) {
                            Start-Process -FilePath $job.CurrentExe -WorkingDirectory ([IO.Path]::GetDirectoryName($job.CurrentExe)) -WindowStyle Hidden
                        }
                    } catch { }
                }
                exit 1
            }
            """;
        string path=Path.Combine(work,"apply.ps1");File.WriteAllText(path,script,new System.Text.UTF8Encoding(true));
        var start=new ProcessStartInfo {FileName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe"),
            UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetTempPath()};
        foreach(string arg in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",path})start.ArgumentList.Add(arg);
        return start;
    }
}
