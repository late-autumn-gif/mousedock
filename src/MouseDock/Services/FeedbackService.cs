using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using MouseDock.Models;

namespace MouseDock.Services
{
    /// <summary>
    /// 反馈与错误报告。
    ///
    /// 错误报告做什么：
    ///   1. 枚举本机所有 HID 设备（VID/PID/产品名/用法页/能力）
    ///   2. 标出哪些被 MouseDock 认出来了、哪些读不到电量
    ///   3. 附上识别结果、设置、系统信息
    ///   4. 打包成 zip
    ///
    /// 报告放哪：
    ///   不往 C 盘的 %APPDATA% 里塞。导出时弹一次「另存为」对话框，
    ///   默认目录就是 MouseDock.exe 自己所在的文件夹，文件名也填好了 ——
    ///   想收下直接回车，想换地方就换。
    ///   报告是用户自己要拿去发邮件的东西，放在他挑得到的地方更合适；
    ///   exe 目录不可写（装在 Program Files、只读介质）时才退回「文档」。
    ///
    /// 反馈做什么：
    ///   用 mailto 打开系统默认邮件客户端，收件人预填，主题和正文预填。
    ///   附件（zip）需要用户手动拖进去 —— mailto 协议不支持附件，
    ///   这是 Windows 的限制，不是偷懒。
    ///
    /// 不做的事：
    ///   不自动发邮件。不联网上传。所有内容都只落在本地，由用户自己决定发不发。
    /// </summary>
    internal static class FeedbackService
    {
        public const string ContactEmail = "2653880019@qq.com";

        private static string DataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MouseDock");

        // ─────────────────────────── 报告放哪 ───────────────────────────

        /// <summary>
        /// 报告默认放在程序自己所在目录 —— 也就是 MouseDock.exe 旁边。
        /// 该目录写不进去时退回「文档」，再不行退回临时目录。
        /// </summary>
        public static string DefaultExportDir
        {
            get
            {
                string exeDir = ExeDir();
                if (CanWriteTo(exeDir)) return exeDir;

                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrEmpty(docs) && CanWriteTo(docs)) return docs;

                return Path.GetTempPath();
            }
        }

        /// <summary>默认的完整 zip 路径，界面用来预热对话框。</summary>
        public static string DefaultExportPath => Path.Combine(DefaultExportDir, SuggestedFileName());

        /// <summary>建议的文件名，带时间戳，不会覆盖上一份报告。</summary>
        private static string SuggestedFileName()
            => "MouseDock-report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip";

        /// <summary>exe 所在目录。用 dotnet MouseDock.dll 跑调试时退回程序基目录。</summary>
        private static string ExeDir()
        {
            try
            {
                string exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe) &&
                    !string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase))
                {
                    string dir = Path.GetDirectoryName(exe);
                    if (!string.IsNullOrEmpty(dir)) return dir;
                }
            }
            catch { }

            return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        /// <summary>真去写一个临时文件试一下，别只信 ACL：U 盘、受控文件夹访问都会在这里露馅。</summary>
        private static bool CanWriteTo(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".mousedock-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1,
                                      FileOptions.DeleteOnClose))
                { }
                return true;
            }
            catch { return false; }
        }

        // ─────────────────────────── 错误报告 ───────────────────────────

        /// <summary>
        /// 弹「另存为」让用户挑位置，默认就在 exe 同目录。
        /// 返回用户选定的路径；取消返回 null。
        /// </summary>
        public static string AskSavePath(Window owner = null)
        {
            string dir = DefaultExportDir;

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存错误报告",
                Filter = "压缩包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
                DefaultExt = ".zip",
                AddExtension = true,
                OverwritePrompt = true,
                CheckPathExists = true,
                FileName = SuggestedFileName(),
                InitialDirectory = dir
            };

            bool? ok = owner != null && owner.IsVisible ? dlg.ShowDialog(owner) : dlg.ShowDialog();
            return ok == true ? dlg.FileName : null;
        }

        /// <summary>
        /// 弹框选位置，然后导出。三个入口（设置页 / 关于 / 反馈）都走这里。
        /// </summary>
        public static ReportExportResult ExportReportWithPrompt(Window owner, AppSettings settings,
                                                               IEnumerable<DeviceInfo> detected)
        {
            string target = AskSavePath(owner);
            if (string.IsNullOrEmpty(target)) return ReportExportResult.CanceledByUser;

            string path = ExportReport(settings, detected, target);
            return path == null ? ReportExportResult.Failed : ReportExportResult.Ok(path);
        }

        /// <summary>
        /// 生成错误报告 zip。zipPath 为空时落在 <see cref="DefaultExportPath"/>。
        /// 返回 zip 路径，失败返回 null。
        /// </summary>
        public static string ExportReport(AppSettings settings, IEnumerable<DeviceInfo> detected, string zipPath = null)
        {
            if (string.IsNullOrWhiteSpace(zipPath)) zipPath = DefaultExportPath;
            zipPath = Path.GetFullPath(zipPath);
            bool touched = false;

            try
            {
                string dir = Path.GetDirectoryName(zipPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var detectedList = detected?.ToList() ?? new List<DeviceInfo>();

                // 直接写进目标 zip，不再先摊一个临时目录出来：
                // 少一次复制，也不会在用户的目录里留下 work-xxxx 之类的残骸。
                using (var stream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    touched = true;
                    AddText(zip, "hid-devices.txt", BuildHidReport(detectedList));
                    AddText(zip, "summary.txt", BuildSummary(settings, detectedList));

                    // 现有的缓存与诊断文件，有就捎上
                    foreach (var name in new[] { "settings.json", "battery-cache.json", "vendor-identity.json",
                                                 "detect.txt", "hid-probe.txt", "ble-scan.txt" })
                    {
                        string src = Path.Combine(DataDir, name);
                        if (!File.Exists(src)) continue;
                        try { zip.CreateEntryFromFile(src, name, CompressionLevel.Optimal); } catch { }
                    }
                }

                return zipPath;
            }
            catch
            {
                // 写了一半失败的话别留下一个坏 zip 让用户当附件发出去
                if (touched) { try { File.Delete(zipPath); } catch { } }
                return null;
            }
        }

        private static void AddText(ZipArchive zip, string name, string text)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(text);
            }
        }

        /// <summary>
        /// 枚举本机全部 HID 设备，逐台标注 MouseDock 认不认得。
        /// </summary>
        private static string BuildHidReport(List<DeviceInfo> detected)
        {
            var sb = new StringBuilder();
            sb.AppendLine("MouseDock 设备报告");
            sb.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            // 已经被 MouseDock 认出来、读到电量的
            sb.AppendLine("========== 已识别（能读到电量） ==========");
            if (detected.Count == 0)
            {
                sb.AppendLine("  （无）");
            }
            else
            {
                foreach (var d in detected)
                {
                    sb.AppendLine($"  [{d.Kind}] {d.Name}");
                    if (!string.IsNullOrWhiteSpace(d.Detail)) sb.AppendLine($"      编号     = {d.Detail}");
                    sb.AppendLine($"      通道     = {d.Transport}");
                    sb.AppendLine($"      电量     = {(d.Level < 0 ? "读不到" : d.Level + "%")}");
                    sb.AppendLine();
                }
            }

            // 全部 HID 设备
            sb.AppendLine("========== 本机全部 HID 设备 ==========");
            sb.AppendLine("格式: VID:PID  产品名  [用法页/用法]  能力(in/out/feature)");
            sb.AppendLine();

            try
            {
                var nodes = Detection.HidProbe.Enumerate(null, null);
                var seen = new HashSet<string>();
                int idx = 0;

                foreach (var n in nodes)
                {
                    string key = $"{n.Vid:X4}:{n.Pid:X4}|{n.Product}|{n.Caps.UsagePage}|{n.Caps.Usage}";
                    if (!seen.Add(key)) continue;

                    idx++;
                    string up = $"0x{n.Caps.UsagePage:X4}";
                    string us = $"0x{n.Caps.Usage:X4}";
                    string caps = $"in={n.Caps.InputReportByteLength} out={n.Caps.OutputReportByteLength} feat={n.Caps.FeatureReportByteLength}";

                    // 标出标准电量用法页
                    string tag = "";
                    if (n.Caps.UsagePage == 0x06 && n.Caps.Usage == 0x20) tag = "  ← 标准电量";
                    else if (n.Caps.UsagePage >= 0xFF00) tag = "  ← 厂商私有接口";
                    else if (n.Caps.UsagePage == 0x0C) tag = "  （消费类，通常无电量）";
                    else if (n.Caps.UsagePage == 0x01) tag = "  （通用桌面）";

                    sb.AppendLine($"  {idx:D2}. {n.Vid:X4}:{n.Pid:X4}  {n.Product}");
                    sb.AppendLine($"      用法页 {up}/{us}  {caps}{tag}");
                    if (n.Caps.UsagePage >= 0xFF00)
                    {
                        sb.AppendLine($"      路径: {n.Path}");
                    }
                    sb.AppendLine();
                }

                if (idx == 0) sb.AppendLine("  （枚举不到任何 HID 设备）");
            }
            catch (Exception ex)
            {
                sb.AppendLine("  枚举异常: " + ex.Message);
            }

            return sb.ToString();
        }

        private static string BuildSummary(AppSettings settings, List<DeviceInfo> detected)
        {
            var sb = new StringBuilder();
            sb.AppendLine("MouseDock 反馈摘要");
            sb.AppendLine("==================");
            sb.AppendLine();
            sb.AppendLine("版本: " + UpdateService.CurrentVersionText);
            sb.AppendLine("系统: " + Environment.OSVersion.VersionString);
            sb.AppendLine("64位: " + Environment.Is64BitOperatingSystem);
            sb.AppendLine("运行时长: " + (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMinutes.ToString("F0") + " 分钟");
            sb.AppendLine();
            sb.AppendLine("已识别设备: " + detected.Count + " 台");
            foreach (var d in detected)
            {
                sb.AppendLine($"  - [{d.Kind}] {d.Name}  {(d.Level < 0 ? "读不到" : d.Level + "%")}");
            }
            sb.AppendLine();
            sb.AppendLine("设置:");
            sb.AppendLine("  刷新间隔 = " + settings?.RefreshSeconds + " 秒");
            sb.AppendLine("  低电量提醒 = " + settings?.LowBatteryWarn + "%");
            sb.AppendLine("  自动更新 = " + (settings?.AutoUpdate == true ? "开" : "关"));
            sb.AppendLine();
            sb.AppendLine("说明:");
            sb.AppendLine("  hid-devices.txt 里列出了本机全部 HID 设备。");
            sb.AppendLine("  如果某台设备电量显示「—」，把它的 VID:PID 和产品名发我就能补。");
            sb.AppendLine("  只有型号名不行 —— 同型号不同批次可能换芯片。");

            return sb.ToString();
        }

        // ─────────────────────────── 反馈邮件 ───────────────────────────

        /// <summary>
        /// 用系统默认邮件客户端写一封反馈邮件。附件需要手动添加。
        /// </summary>
        public static bool OpenFeedbackMail(string reportPath = null, string userText = null)
        {
            try
            {
                string version = UpdateService.CurrentVersionText;
                string subject = "MouseDock " + version + " 反馈";

                var body = new StringBuilder();
                body.AppendLine("（这条下面写你的问题和建议）");
                body.AppendLine();
                body.AppendLine(userText ?? "");
                body.AppendLine();
                body.AppendLine("---------- 以下自动生成，请勿删除 ----------");
                body.AppendLine("版本: " + version);
                body.AppendLine("系统: " + Environment.OSVersion.VersionString);
                body.AppendLine();

                if (!string.IsNullOrEmpty(reportPath) && File.Exists(reportPath))
                {
                    body.AppendLine("错误报告已生成，请把它作为附件加入邮件：");
                    body.AppendLine(reportPath);
                    body.AppendLine();
                }

                body.AppendLine("如果设备电量显示「—」，附上错误报告最有用。");

                // 把收件人、主题、正文全部塞进剪贴板
                string clip = "收件人: " + ContactEmail + "\r\n"
                            + "主题: " + subject + "\r\n"
                            + "\r\n"
                            + body.ToString();
                try { System.Windows.Clipboard.SetText(clip); } catch { }

                // 直接打开网页版 QQ 邮箱
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://mail.qq.com/",
                    UseShellExecute = true
                });

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 打开系统默认邮件客户端（备用方案）。
        /// </summary>
        public static bool OpenFeedbackMailClient(string reportPath = null)
        {
            try
            {
                string subject = "MouseDock " + UpdateService.CurrentVersionText + " 反馈";
                var body = new StringBuilder();
                body.AppendLine("（这条下面写你的问题和建议）");
                body.AppendLine();

                if (!string.IsNullOrEmpty(reportPath) && File.Exists(reportPath))
                {
                    body.AppendLine("错误报告请作为附件加入：");
                    body.AppendLine(reportPath);
                }

                string url = "mailto:" + ContactEmail
                           + "?subject=" + Uri.EscapeDataString(subject)
                           + "&body=" + Uri.EscapeDataString(body.ToString());

                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                return true;
            }
            catch { return false; }
        }

        /// <summary>打开报告所在目录，方便用户拖附件。</summary>
        public static void OpenReportFolder(string reportPath)
        {
            try
            {
                if (!string.IsNullOrEmpty(reportPath) && File.Exists(reportPath))
                {
                    Process.Start("explorer.exe", "/select,\"" + reportPath + "\"");
                }
                else
                {
                    string dir = DefaultExportDir;
                    Directory.CreateDirectory(dir);
                    Process.Start("explorer.exe", "\"" + dir + "\"");
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// 导出的结果。三态要分清楚：
    ///   用户自己取消了 → 什么都不用提示，别弹「失败」，
    ///   写不进去 → 要提示换个目录，
    ///   成功 → 把路径拿去开目录、写邮件正文。
    /// </summary>
    internal sealed class ReportExportResult
    {
        public string Path { get; private set; }

        public bool Canceled { get; private set; }

        public bool Succeeded => !Canceled && !string.IsNullOrEmpty(Path);

        public static readonly ReportExportResult CanceledByUser = new ReportExportResult { Canceled = true };

        public static readonly ReportExportResult Failed = new ReportExportResult();

        public static ReportExportResult Ok(string path) => new ReportExportResult { Path = path };
    }
}
