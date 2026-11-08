using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MouseDock.Services;

/// <summary>
/// 把一次更新从「下载完的 zip」变成「新版本已经在跑」。
///
/// 顺序是刻意的：先在临时目录里解压并校验，确认新 exe 能拿起来，再动旧文件。
/// 旧版本、旧注册表项、旧桌面入口都在替换阶段一起清掉，避免留下指向已删除路径的入口。
/// 失败时把旧目录还原，不留半新半旧的状态。
/// </summary>
internal static class UpdateApplier
{
    /// <summary>更新器看到这个参数就进入应用更新模式，不启动主界面。</summary>
    public const string ApplySwitch = "--apply-update";

    /// <summary>更新完成、主程序重新拉起时带这个参数，用来清理临时目录。</summary>
    public const string CleanupSwitch = "--cleanup-update";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "MouseDock";
    private const string ShortcutFileName = "MouseDock.lnk";

    internal static bool TryGetApplyTarget(string[] args, out string packagePath)
    {
        packagePath = null;
        int index = Array.FindIndex(args, a => string.Equals(a, ApplySwitch, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length) return false;
        packagePath = args[index + 1];
        return !string.IsNullOrWhiteSpace(packagePath);
    }

    /// <summary>
    /// 执行一次更新。返回非空字符串表示失败原因（此时旧版本保持原样）。
    /// </summary>
    internal static string Apply(string packagePath, Action<string> progress = null)
    {
        string exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("拿不到当前进程路径");
        string installDir = Path.GetDirectoryName(exePath)
            ?? throw new InvalidOperationException("拿不到安装目录");
        string workDir = Path.Combine(Path.GetTempPath(), "MouseDock-update-" + Guid.NewGuid().ToString("N"));
        string backupDir = installDir + ".old-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        try
        {
            progress?.Invoke("准备临时目录");
            Directory.CreateDirectory(workDir);

            progress?.Invoke("解压更新包");
            var staged = Stage(packagePath, workDir, progress);
            if (staged == null) return "更新包里没有找到 MouseDock.exe";

            progress?.Invoke("校验新版本");
            string stagedVersion = ReadVersion(staged);
            if (string.IsNullOrEmpty(stagedVersion)) return "新版本文件无法读取版本信息";

            progress?.Invoke("写入新版本");
            var replaced = new StringBuilder();
            try
            {
                // 只保留用户数据（设置在 %APPDATA%），安装目录整体换掉。
                // 先把整个目录挪走做备份，再铺新文件；任何一步失败都能原路退回。
                if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);
                Directory.Move(installDir, backupDir);
                Directory.CreateDirectory(installDir);
                CopyTree(backupDir, installDir, staged, replaced);
            }
            catch (Exception ex)
            {
                // 回滚：清掉半成品，把备份挪回原位。
                try
                {
                    if (Directory.Exists(installDir)) Directory.Delete(installDir, true);
                    if (Directory.Exists(backupDir)) Directory.Move(backupDir, installDir);
                }
                catch { /* 回滚失败时保留备份目录，交给用户手工处理 */ }
                return "替换文件失败：" + ex.Message;
            }

            progress?.Invoke("重建启动入口");
            string newExe = Path.Combine(installDir, Path.GetFileName(exePath));
            RebuildAutostart(newExe);
            RebuildDesktopShortcut(newExe);

            progress?.Invoke("启动新版本");
            Process.Start(new ProcessStartInfo(newExe)
            {
                Arguments = CleanupSwitch + " \"" + backupDir + "\"",
                UseShellExecute = true,
                WorkingDirectory = installDir
            });

            return null;
        }
        catch (Exception ex)
        {
            return "更新失败：" + ex.Message;
        }
        finally
        {
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, true); } catch { }
        }
    }

    /// <summary>清理上一次更新留下的备份目录。</summary>
    internal static void CleanupPrevious(string[] args)
    {
        int index = Array.FindIndex(args, a => string.Equals(a, CleanupSwitch, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length) return;
        string backup = args[index + 1];
        if (string.IsNullOrWhiteSpace(backup) || !Directory.Exists(backup)) return;
        // 只删自己命名规则下的目录，防止参数被塞进别的路径。
        if (!Path.GetFileName(backup).Contains(".old-", StringComparison.Ordinal)) return;
        try { Directory.Delete(backup, true); } catch { /* 有文件被占用就留着，下次再说 */ }
    }

    /// <summary>
    /// 在临时目录解压并返回可执行文件路径。更新包里可能是单个 exe，也可能是完整目录。
    /// </summary>
    private static string Stage(string packagePath, string workDir, Action<string> progress)
    {
        if (!File.Exists(packagePath))
            throw new FileNotFoundException("更新包不存在", packagePath);

        if (packagePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            string target = Path.Combine(workDir, "MouseDock.exe");
            File.Copy(packagePath, target, true);
            return target;
        }

        string extractDir = Path.Combine(workDir, "payload");
        Directory.CreateDirectory(extractDir);
        ZipFile.ExtractToDirectory(packagePath, extractDir, true);

        // 有的包把 exe 放在一层目录里。
        var candidates = Directory.GetFiles(extractDir, "MouseDock.exe", SearchOption.AllDirectories);
        return candidates.FirstOrDefault();
    }

    private static string ReadVersion(string exePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            return info.ProductVersion ?? info.FileVersion;
        }
        catch { return null; }
    }

    /// <summary>
    /// 铺新文件：先放更新包里的内容，再补齐备份里其它文件（例如运行时需要的资源）。
    /// 这样单文件 exe 的包也能工作，同时不会把安装目录里别的东西弄丢。
    /// </summary>
    private static void CopyTree(string backupDir, string installDir, string stagedExe,
        StringBuilder replaced)
    {
        if (!Directory.Exists(backupDir)) return;

        string stagedRoot = Path.GetDirectoryName(stagedExe);
        // 更新包里除 exe 之外的文件先铺开。
        if (stagedRoot != null && Directory.Exists(stagedRoot))
        {
            foreach (string source in Directory.GetFiles(stagedRoot, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(stagedRoot, source);
                string target = Path.Combine(installDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? installDir);
                File.Copy(source, target, true);
                replaced.Append(relative).Append(';');
            }
        }

        // 备份里未被更新包覆盖的文件补回来（设置、文档、附加组件）。
        foreach (string source in Directory.GetFiles(backupDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(backupDir, source);
            string target = Path.Combine(installDir, relative);
            if (File.Exists(target)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? installDir);
            try { File.Copy(source, target, false); }
            catch { /* 备份里的只读或占用文件跳过，不影响新 exe */ }
        }
    }

    /// <summary>
    /// 重建注册表 Run 项。旧值先删再写，这样路径变更后不会留下指向旧 exe 的入口。
    /// </summary>
    private static void RebuildAutostart(string exePath)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            if (key == null) return;
            key.DeleteValue(RunValueName, false);
            key.SetValue(RunValueName, "\"" + exePath + "\" --tray");
        }
        catch { /* 无权限时跳过，主程序的设置界面还能再写一次 */ }
    }

    /// <summary>
    /// 重建桌面快捷方式。旧的全部删掉再建，避免出现「MouseDock (1).lnk」这种重复。
    /// </summary>
    private static void RebuildDesktopShortcut(string exePath)
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktop)) return;

            // 清理桌面上所有指向旧安装目录的 MouseDock 快捷方式。
            foreach (string old in Directory.GetFiles(desktop, "MouseDock*.lnk"))
            {
                try { File.Delete(old); } catch { }
            }

            string link = Path.Combine(desktop, ShortcutFileName);
            CreateShortcut(link, exePath, Path.GetDirectoryName(exePath), "外设电量，一起看见");
        }
        catch { /* 建不了快捷方式不影响主程序运行 */ }
    }

    /// <summary>
    /// 通过 WScript.Shell 写 .lnk。用后期绑定，免得为一次写入引入 COM 互操作程序集。
    /// </summary>
    private static void CreateShortcut(string linkPath, string targetPath, string workingDir, string description)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;
        object shell = null;
        object shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell == null) return;
            shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                new object[] { linkPath });
            if (shortcut == null) return;
            Type linkType = shortcut.GetType();
            linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
            linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut,
                new object[] { workingDir ?? "" });
            linkType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut,
                new object[] { description ?? "" });
            linkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut,
                new object[] { targetPath + ",0" });
            linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, Array.Empty<object>());
        }
        finally
        {
            if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.ReleaseComObject(shortcut);
            if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
    }

    /// <summary>
    /// 下载更新包到临时目录，并在给出 Sha256 时校验。
    /// </summary>
    internal static async Task<string> DownloadAsync(UpdateService.UpdateInfo info, IProgress<double> progress = null,
        CancellationToken ct = default)
    {
        if (info == null || string.IsNullOrWhiteSpace(info.AssetUrl)) return null;
        string target = Path.Combine(Path.GetTempPath(),
            "MouseDock-" + (info.Version ?? "update").Replace('/', '_') + ".zip");

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var response = await client.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? info.AssetSize;
        await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var dest = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (total > 0) progress?.Report((double)done / total);
            }
        }

        if (!string.IsNullOrWhiteSpace(info.Sha256))
        {
            string actual = ComputeSha256(target);
            if (!string.Equals(actual, info.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(target); } catch { }
                throw new InvalidDataException("更新包校验失败：期望 " + info.Sha256 + "，实际 " + actual);
            }
        }
        return target;
    }

    internal static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }
}

