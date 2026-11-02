using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

        private static string ReportDir =>
            Path.Combine(DataDir, "reports");

        // ─────────────────────────── 错误报告 ───────────────────────────

        /// <summary>
        /// 生成错误报告 zip。返回 zip 路径，失败返回 null。
        /// </summary>
        public static string ExportReport(AppSettings settings, IEnumerable<DeviceInfo> detected)
        {
            try
            {
                Directory.CreateDirectory(ReportDir);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string work = Path.Combine(ReportDir, "work-" + stamp);
                Directory.CreateDirectory(work);

                var detectedList = detected?.ToList() ?? new List<DeviceInfo>();

                string hidText = BuildHidReport(detectedList);
                File.WriteAllText(Path.Combine(work, "hid-devices.txt"), hidText, new UTF8Encoding(false));

                string summary = BuildSummary(settings, detectedList);
                File.WriteAllText(Path.Combine(work, "summary.txt"), summary, new UTF8Encoding(false));

                // 现有的缓存与诊断文件，有就捎上
                foreach (var name in new[] { "settings.json", "battery-cache.json", "vendor-identity.json",
                                             "detect.txt", "hid-probe.txt", "ble-scan.txt" })
                {
                    string src = Path.Combine(DataDir, name);
                    if (File.Exists(src))
                    {
                        try { File.Copy(src, Path.Combine(work, name), true); } catch { }
                    }
                }

                string zip = Path.Combine(ReportDir, $"MouseDock-report-{stamp}.zip");
                if (File.Exists(zip)) File.Delete(zip);
                ZipFile.CreateFromDirectory(work, zip, CompressionLevel.Optimal, false);

                try { Directory.Delete(work, true); } catch { }
                return zip;
            }
            catch
            {
                return null;
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
                    Directory.CreateDirectory(ReportDir);
                    Process.Start("explorer.exe", "\"" + ReportDir + "\"");
                }
            }
            catch { }
        }

        /// <summary>报告目录，界面用来显示。</summary>
        public static string ReportFolder => ReportDir;
    }
}
