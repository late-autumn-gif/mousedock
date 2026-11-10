using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MouseDock.Models;

namespace MouseDock.Services
{
    /// <summary>
    /// 反馈与错误报告。
    ///
    /// 使用正常设备读取器采集有界、脱敏的诊断快照，并导出本地 ZIP。
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

        private static readonly object CaptureGate = new();

        // ─────────────────────────── 报告放哪 ───────────────────────────

        /// <summary>
        /// 报告默认放在程序自己所在目录 —— 也就是 MouseDock.exe 旁边。
        /// 该目录写不进去时退回「文档」，再不行退到 %LOCALAPPDATA%\MouseDock\reports，
        /// 最后才是临时目录。
        ///
        /// 不用 %TEMP% 当第一档兜底：那地方会被系统清理，用户也认不出是自己的目录。
        /// LocalAppData 属于当前用户、不受受控文件夹访问管辖，写权限最稳。
        /// </summary>
        public static string DefaultExportDir
        {
            get
            {
                string exeDir = ExeDir();
                if (CanWriteTo(exeDir)) return exeDir;

                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrEmpty(docs) && CanWriteTo(docs)) return docs;

                string local = LocalReportDir;
                if (CanWriteTo(local)) return local;

                return Path.GetTempPath();
            }
        }

        /// <summary>本机诊断日志和最后的兜底目录，一定属于当前用户。</summary>
        public static string LocalReportDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "MouseDock", "reports");

        /// <summary>默认的完整 zip 路径，界面用来预热对话框。</summary>
        public static string DefaultExportPath => Path.Combine(DefaultExportDir, SuggestedFileName());

        /// <summary>建议的文件名，带时间戳，不会覆盖上一份报告。</summary>
        private static string SuggestedFileName(DeviceInfo target = null)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            if (target == null) return "MouseDock-report-" + stamp + ".zip";
            return "MouseDock-report-" + SafeFileName(target.Name, 32) + "-" + stamp + ".zip";
        }

        private static string SafeFileName(string text, int max)
        {
            if (string.IsNullOrWhiteSpace(text)) return "device";
            var chars = text.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray();
            string clean = new string(chars).Trim('-', ' ', '.');
            if (clean.Length == 0) clean = "device";
            if (clean.Length > max) clean = clean.Substring(0, max).TrimEnd('-', ' ', '.');
            return clean;
        }

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

        /// <summary>
        /// 真去写一个临时文件试一下，别只信 ACL：U 盘、受控文件夹访问都会在这里露馅。
        /// 探针后缀必须跟目标文件一致（.zip）—— 受控文件夹访问和按扩展名查杀的杀软
        /// 会对 .tmp 放行、对 .zip 拦截，探 .tmp 等于没探。
        /// </summary>
        private static bool CanWriteTo(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".mousedock-" + Guid.NewGuid().ToString("N") + ".zip");
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
        public static string AskSavePath(Window owner = null, DeviceInfo target = null)
        {
            string dir = DefaultExportDir;

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = target == null ? "保存错误报告" : "保存单设备抓取报告",
                Filter = "压缩包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
                DefaultExt = ".zip",
                AddExtension = true,
                OverwritePrompt = true,
                CheckPathExists = true,
                FileName = SuggestedFileName(target),
                InitialDirectory = dir
            };

            bool? ok = owner != null && owner.IsVisible ? dlg.ShowDialog(owner) : dlg.ShowDialog();
            return ok == true ? dlg.FileName : null;
        }

        /// <summary>
        /// 先弹设备选择框，再弹「另存为」。选中哪台就只抓哪台；
        /// 全量和单设备采用同一采样格式；查询前按物理实例限定单设备范围。
        /// HID 枚举、只读查询和压缩放到后台线程。
        /// </summary>
        public static async Task<ReportExportResult> ExportReportWithPromptAsync(
            Window owner, AppSettings settings, IEnumerable<DeviceInfo> detected)
        {
            var detectedList = detected?.ToList() ?? new List<DeviceInfo>();
            var selected = Views.ReportDevicePicker.Pick(owner, detectedList, out bool canceled, out bool cableConnected);
            if (canceled) return ReportExportResult.CanceledByUser;

            string target = AskSavePath(owner, selected);
            if (string.IsNullOrEmpty(target)) return ReportExportResult.CanceledByUser;

            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var snapshot = CreateDisplaySnapshot(selected == null ? detectedList : new List<DeviceInfo> { selected }, aliases);
            var snapshotAt = DateTimeOffset.UtcNow;
            var preferences = App.Battery?.Preferences ?? new DevicePreferences(settings ?? new AppSettings());
            var scope = selected == null ? null : DevicePreferences.Keys(selected);
            bool safeMode = settings?.SafeMode == true;
            return await Task.Run(() => TryExportReport(settings, null, target, selected,
                CaptureRounds(preferences, scope, safeMode, aliases), snapshot, snapshotAt, cableConnected)).ConfigureAwait(true);
        }

        internal static Detection.BatteryReplay.Capture[] CaptureRounds(
            DevicePreferences preferences, IEnumerable<string> scope, bool safeMode, Dictionary<string, string> aliases = null)
        {
            lock (CaptureGate)
            {
                aliases ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var rounds = new Detection.BatteryReplay.Capture[3];
                for (int i = 0; i < rounds.Length; i++)
                {
                    if (i > 0) Thread.Sleep(TimeSpan.FromSeconds(8));
                    Detection.HidDiscovery.Enumerate(preferences, scope, out rounds[i], aliases, safeMode);
                }
                return rounds;
            }
        }

        /// <summary>
        /// 生成错误报告 zip。zipPath 为空时落在 <see cref="DefaultExportPath"/>。
        /// 返回 zip 路径，失败返回 null。
        ///
        /// 保留这个签名给 self-check 用；要拿到失败原因请用 TryExportReport。
        /// </summary>
        public static string ExportReport(AppSettings settings, IEnumerable<DeviceInfo> detected, string zipPath = null)
        {
            var result = TryExportReport(settings, detected, zipPath);
            return result.Succeeded ? result.Path : null;
        }

        /// <summary>
        /// 生成错误报告，把成功、写入失败、生成失败分清楚。
        /// 生成阶段和写盘阶段各自 catch：以前一个 try 全包住，StartTime 之类的异常
        /// 会被界面报成「没能写入所选位置」，用户和作者都被带偏。
        /// </summary>
        public static ReportExportResult TryExportReport(AppSettings settings, IEnumerable<DeviceInfo> detected,
                                                         string zipPath = null, DeviceInfo target = null,
                                                         Detection.BatteryReplay.Capture[] rounds = null, DisplayReading[] snapshot = null, DateTimeOffset? snapshotAt = null, bool cableConnected = false)
        {
            string resolvedTarget = null;
            string temporaryPath = null;

            try
            {
                resolvedTarget = Path.GetFullPath(string.IsNullOrWhiteSpace(zipPath) ? DefaultExportPath : zipPath);
            }
            catch (Exception ex)
            {
                return ReportExportResult.Failed(ReportExportFailureKind.WriteFailed, zipPath, ex.Message, ex);
            }

            string dir = Path.GetDirectoryName(resolvedTarget);
            if (string.IsNullOrEmpty(dir))
                return ReportExportResult.Failed(ReportExportFailureKind.WriteFailed, resolvedTarget,
                                                 "目标路径没有上级目录。");

            snapshotAt ??= DateTimeOffset.UtcNow;
            snapshot ??= CreateDisplaySnapshot(target == null ? detected ?? Array.Empty<DeviceInfo>() : new[] { target });
            rounds ??= Array.Empty<Detection.BatteryReplay.Capture>();

            var series = DescribeSeries(rounds);
            try
            {
                Directory.CreateDirectory(dir);

                // 临时文件也必须是 .zip：CanWriteTo 探的就是 .zip，按扩展名拦的杀软
                // 放行 .tmp、拦 .zip，探针通过不等于这里能写。
                temporaryPath = Path.Combine(dir, $".mousedock-{Guid.NewGuid():N}.zip");
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    AddText(zip, "summary.txt", BuildSummary(settings, target != null, snapshot, rounds, series, cableConnected));
                    AddText(zip, "report.json", JsonSerializer.Serialize(new
                    {
                        Schema = 2, Scope = target == null ? "all" : "single",
                        AppVersion = UpdateService.CurrentVersionText,
                        WindowsVersion = Environment.OSVersion.VersionString,
                        SafeMode = settings?.SafeMode == true, RefreshSeconds = settings?.RefreshSeconds,
                        DisplaySnapshotAt = snapshotAt, DisplaySnapshot = snapshot,
                        UserContext = cableConnected ? "cable-connected" : null,
                        Series = series,
                        Rounds = rounds.Select((r, i) => new
                        {
                            Entry = ReplayEntry(i), r.StartedAt, r.FinishedAt, r.ScanError
                        })
                    }, new JsonSerializerOptions { WriteIndented = true }));
                    for (int i = 0; i < rounds.Length; i++)
                        AddText(zip, ReplayEntry(i), Detection.BatteryReplay.Serialize(rounds[i]));
                }

                // 原子替换：目标文件正被预览窗格、压缩软件或同步盘占着时，
                // 这里会抛 IOException，但至少不会留下一份半截的坏 zip。
                File.Move(temporaryPath, resolvedTarget, overwrite: true);
                temporaryPath = null;
                return ReportExportResult.Ok(resolvedTarget);
            }
            catch (Exception ex)
            {
                return ReportExportResult.Failed(ReportExportFailureKind.WriteFailed, resolvedTarget,
                                                 ex.Message, ex);
            }
            finally
            {
                if (temporaryPath != null)
                {
                    try { File.Delete(temporaryPath); } catch { }
                }
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

        internal sealed record DisplayReading(string Name, string Product, string Model, string Kind, string Transport,
            string VidPid, bool Connected, bool ReceiverPresent, bool PeripheralConfirmed, int? Percent,
            string Freshness, bool? Charging, bool? Full, string Reader, string Source, string[] Instances);

        private static DisplayReading[] CreateDisplaySnapshot(IEnumerable<DeviceInfo> devices,
            Dictionary<string, string> aliases = null) =>
            devices.Select(d => new DisplayReading(
                Detection.BatteryReplay.SafeLabel(d.Name), Detection.BatteryReplay.SafeLabel(d.RawName),
                Detection.BatteryReplay.SafeLabel(d.ConfirmedModel), d.Kind.ToString(), d.Transport,
                Regex.Match(d.Detail ?? "", @"^[0-9A-Fa-f]{4}:[0-9A-Fa-f]{4}$").Value,
                d.Connected, d.ReceiverDiscovered, d.PeripheralConfirmed, d.Level is >= 0 and <= 100 ? d.Level : null,
                d.Level < 0 ? "unknown" : d.IsStale || !d.Connected ? "cached" : "display-snapshot",
                d.IsChargingActive ? true : null, d.IsFullActive ? true : null, d.HandledByLabel, d.Source,
                aliases == null ? Array.Empty<string>() : DevicePreferences.Keys(d)
                    .Where(key => key.StartsWith("instance-", StringComparison.OrdinalIgnoreCase))
                    .Select(key => Detection.BatteryReplay.Alias(aliases, key)).ToArray())).ToArray();

        private static string ReplayEntry(int index) => index == 0 ? Detection.BatteryReplay.EntryName :
            $"samples/{index + 1:D2}.json";

        internal sealed record ReadingSeries(string Receiver, string Transport, int Samples, int[] ReportedValues,
            string Assessment);

        private static ReadingSeries[] DescribeSeries(Detection.BatteryReplay.Capture[] rounds) =>
            rounds.SelectMany(r => r.Observations).Where(item => item.Connected && !item.Stale && item.Percent.HasValue)
                .GroupBy(item => (item.Receiver, item.Transport))
                .Select(group =>
                {
                    int[] values = group.Select(item => item.Percent.Value).ToArray();
                    string assessment = values.Length < 2 ? "insufficient-samples" :
                        values.All(value => value == 100) ? "repeated-100-unverified" :
                        values.All(value => value == values[0]) ? "unchanged" : "reported-change";
                    return new ReadingSeries(group.Key.Receiver, group.Key.Transport, values.Length, values, assessment);
                }).ToArray();

        private static string BuildSummary(AppSettings settings, bool single, DisplayReading[] snapshot,
            Detection.BatteryReplay.Capture[] rounds, ReadingSeries[] series, bool cableConnected)
        {
            var sb = new StringBuilder();
            sb.AppendLine("MouseDock 设备诊断报告 v2");
            sb.AppendLine($"版本: {UpdateService.CurrentVersionText} / Windows: {Environment.OSVersion.VersionString}");
            sb.AppendLine($"范围: {(single ? "单设备（按实例限定）" : "全部设备")}");
            if (cableConnected) sb.AppendLine("用户标注: 插线后采样。");
            sb.AppendLine($"安全模式: {(settings?.SafeMode == true ? "开" : "关")}；应用刷新: {settings?.RefreshSeconds} 秒");
            sb.AppendLine("界面快照仅记录采样前的显示状态；蓝牙和其他非 HID 来源在本报告中仅记录界面快照。");
            foreach (var item in snapshot)
                sb.AppendLine($"  {item.Name} {item.VidPid} {item.Transport} {item.Percent?.ToString() ?? "—"} ({item.Freshness})");
            for (int i = 0; i < rounds.Length; i++)
            {
                var round = rounds[i];
                sb.AppendLine($"第 {i + 1} 轮 {round.StartedAt:O} – {round.FinishedAt:O}，集合 {round.Collections.Length}，查询记录 {round.Queries.Length}。");
                if (round.ScanError != null) sb.AppendLine($"  扫描错误: {round.ScanError}");
                else if (round.Collections.Length == 0) sb.AppendLine("  此轮没有找到范围内的 HID 集合。");
                foreach (var item in round.Observations)
                    sb.AppendLine($"  {item.Receiver} {item.Name} {item.Transport} {item.Percent?.ToString() ?? "—"} " +
                        $"{item.Source}；充电 {item.Charging?.ToString() ?? "未知"}");
                foreach (var query in round.Queries)
                    sb.AppendLine($"  {query.Receiver} / 接口 {query.Interface?.ToString() ?? "—"} {query.Protocol}: {query.Status}");
            }
            foreach (var item in series.Where(item => item.Assessment == "repeated-100-unverified"))
                sb.AppendLine($"  {item.Receiver} {item.Transport} 连续 {item.Samples} 次上报 100%；实时电量未验证。");
            sb.AppendLine("仅使用应用现有设备读取器；不试探未知厂商命令，不收集输入按键、全局设置、缓存文件或原始路径。");
            sb.AppendLine("已接入帧采集的协议保存限定范围的查询和响应，脱敏后可离线重放。其他读取器可能只有结果或失败阶段。");
            sb.AppendLine("重复的有效百分比表示设备仍返回相同数值，不能证明固件的更新频率。");
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

        /// <summary>
        /// 把失败结果翻成用户看得懂的一段话。
        /// 生成失败和写入失败要分开说 —— 以前一律写成「没能写入所选位置」，
        /// 明明是内容生成炸了，用户却跑去换目录，怎么换都没用。
        /// </summary>
        public static string BuildFailureMessage(ReportExportResult result)
        {
            if (result == null) return "导出失败：没有拿到结果。";

            var sb = new StringBuilder();
            if (result.FailureKind == ReportExportFailureKind.GenerateFailed)
            {
                sb.AppendLine("导出失败：生成报告内容时出错，跟磁盘权限无关。");
                sb.AppendLine();
                sb.AppendLine(Trim(result.Detail, 400));
                sb.AppendLine();
                sb.AppendLine("把这条消息发到 " + ContactEmail + " 就能定位。");
            }
            else
            {
                sb.AppendLine("导出失败：报告没能写入下面的位置。");
                sb.AppendLine(result.TargetPath ?? "(未定)");
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(result.Detail))
                {
                    sb.AppendLine(Trim(result.Detail, 300));
                    sb.AppendLine();
                }
                sb.AppendLine("换个目录再试一次。文件正被压缩软件、预览窗格或同步盘占着时也会这样。");
            }

            if (!string.IsNullOrEmpty(result.LogPath))
            {
                sb.AppendLine();
                sb.AppendLine("诊断日志：" + result.LogPath);
            }

            return sb.ToString();
        }

        private static string Trim(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= max ? text : text.Substring(0, max) + "…";
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
        /// <summary>
        /// 把失败原因落到本地日志。以前这里是裸 catch，什么都不留，
        /// 用户只能截个「导出失败」的图，作者也看不出是哪一步炸的。
        /// </summary>
        internal static string WriteFailureLog(Exception ex, string target, ReportExportFailureKind kind)
        {
            try
            {
                string dir = LocalReportDir;
                Directory.CreateDirectory(dir);
                string log = Path.Combine(dir, "export-error.log");

                // 日志自增没有上限，超过 256 KB 就从头再来一份。
                try
                {
                    var info = new FileInfo(log);
                    if (info.Exists && info.Length > 256 * 1024) info.Delete();
                }
                catch { }

                var sb = new StringBuilder();
                sb.AppendLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + kind);
                sb.AppendLine("目标: " + (target ?? "(未定)"));
                sb.AppendLine("exe: " + (Environment.ProcessPath ?? "(未知)"));
                sb.AppendLine("异常: " + ex.GetType().FullName);
                sb.AppendLine("消息: " + ex.Message);
                if (ex.StackTrace != null) sb.AppendLine("堆栈:" + Environment.NewLine + ex.StackTrace);
                sb.AppendLine();

                File.AppendAllText(log, sb.ToString(), new UTF8Encoding(false));
                return log;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>导出失败发生在哪一步。界面据此给不同的提示。</summary>
    internal enum ReportExportFailureKind
    {
        None,
        /// <summary>报告内容已经生成，但写不进目标文件。</summary>
        WriteFailed,
        /// <summary>连报告内容都没拼出来，跟磁盘权限无关。</summary>
        GenerateFailed,
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

        /// <summary>失败发生在生成阶段还是写盘阶段。</summary>
        public ReportExportFailureKind FailureKind { get; private set; }

        /// <summary>给用户看的失败原因，通常是异常消息。</summary>
        public string Detail { get; private set; }

        /// <summary>用户当时选的目标路径。</summary>
        public string TargetPath { get; private set; }

        /// <summary>诊断日志落在哪，没写成就是 null。</summary>
        public string LogPath { get; private set; }

        public static readonly ReportExportResult CanceledByUser = new ReportExportResult { Canceled = true };

        public static ReportExportResult Ok(string path) => new ReportExportResult { Path = path };

        public static ReportExportResult Failed(ReportExportFailureKind kind, string target,
                                                string detail, Exception ex = null)
        {
            return new ReportExportResult
            {
                FailureKind = kind,
                TargetPath = target,
                Detail = detail,
                LogPath = ex == null ? null : FeedbackService.WriteFailureLog(ex, target, kind),
            };
        }
    }
}
