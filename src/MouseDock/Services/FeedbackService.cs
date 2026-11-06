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
        /// 选「全部设备」保留旧版全量报告。三个入口（设置页 / 关于 / 反馈）都走这里。
        /// HID 枚举、只读查询和压缩放到后台线程。
        /// </summary>
        public static async Task<ReportExportResult> ExportReportWithPromptAsync(
            Window owner, AppSettings settings, IEnumerable<DeviceInfo> detected)
        {
            var detectedList = detected?.ToList() ?? new List<DeviceInfo>();
            var selected = Views.ReportDevicePicker.Pick(owner, detectedList, out bool canceled);
            if (canceled) return ReportExportResult.CanceledByUser;

            string target = AskSavePath(owner, selected);
            if (string.IsNullOrEmpty(target)) return ReportExportResult.CanceledByUser;

            return await Task.Run(() => TryExportReport(settings, detectedList, target, selected))
                             .ConfigureAwait(true);
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
                                                         string zipPath = null, DeviceInfo target = null)
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

            var detectedList = detected?.ToList() ?? new List<DeviceInfo>();

            // 内容先在内存里生成完，再一次性写盘 —— 生成失败和写盘失败不会混在一起。
            string hidReport;
            string summary;
            string deviceReport = null;
            Detection.DeviceProbeResult probe = null;
            try
            {
                hidReport = BuildHidReport(detectedList, target);
                summary = BuildSummary(settings, detectedList, target);

                if (target != null)
                {
                    if (TryGetVidPid(target, out ushort vid, out ushort pid))
                    {
                        deviceReport = BuildDeviceSnapshot(settings, target, vid, pid);
                        probe = Detection.VendorProtocol.ProbeDevice(vid, pid, target.Name,
                            settings?.SafeMode == true);
                    }
                    else
                    {
                        deviceReport = BuildDeviceSnapshot(settings, target, 0, 0);
                    }
                }
            }
            catch (Exception ex)
            {
                return ReportExportResult.Failed(ReportExportFailureKind.GenerateFailed, resolvedTarget,
                                                 ex.Message, ex);
            }

            try
            {
                Directory.CreateDirectory(dir);

                // 临时文件也必须是 .zip：CanWriteTo 探的就是 .zip，按扩展名拦的杀软
                // 放行 .tmp、拦 .zip，探针通过不等于这里能写。
                temporaryPath = Path.Combine(dir, $".mousedock-{Guid.NewGuid():N}.zip");
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    AddText(zip, "hid-devices.txt", hidReport);
                    AddText(zip, "summary.txt", summary);
                    if (!string.IsNullOrEmpty(deviceReport)) AddText(zip, "device.txt", deviceReport);
                    if (probe != null)
                    {
                        AddText(zip, "probe.txt", probe.Text);
                        AddText(zip, "probe-events.jsonl", probe.Jsonl);
                    }

                    // 全量报告才捎带全局缓存；单设备报告只留选中设备的文件。
                    if (target == null)
                    {
                        foreach (var name in new[] { "settings.json", "battery-cache.json", "vendor-identity.json",
                                                     "detect.txt", "hid-probe.txt", "ble-scan.txt" })
                        {
                            string src = Path.Combine(DataDir, name);
                            if (!File.Exists(src)) continue;
                            try { zip.CreateEntryFromFile(src, name, CompressionLevel.Optimal); } catch { }
                        }
                    }
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

        /// <summary>
        /// 枚举本机全部 HID 设备，逐台标注 MouseDock 认不认得。
        /// </summary>
        private static string BuildHidReport(List<DeviceInfo> detected, DeviceInfo target = null)
        {
            bool single = target != null;
            var selected = single
                ? detected.Where(d => SameDevice(d, target)).ToList()
                : detected;
            if (single && selected.Count == 0) selected.Add(target);

            var sb = new StringBuilder();
            sb.AppendLine(single ? "MouseDock 单设备报告" : "MouseDock 设备报告");
            sb.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            TryGetVidPid(target, out ushort targetVid, out ushort targetPid);

            if (single)
            {
                sb.AppendLine("========== 抓取目标 ==========");
                sb.AppendLine($"只抓这一台：{target.Name}");
                sb.AppendLine($"设备 ID = {target.Id}；标识 = {target.Detail}；通道 = {target.Transport}");
                sb.AppendLine("报告里不会出现其他设备的 HID 集合，也不会向其他设备发查询帧。");
                sb.AppendLine();
            }

            // 能发现设备与能读到电量是两个独立结果。
            sb.AppendLine("========== 已发现设备（含电量未读到） ==========");
            sb.AppendLine($"电量已读到: {selected.Count(d => d.Level >= 0)}；电量未读到: {selected.Count(d => d.Level < 0)}");
            if (selected.Count == 0)
            {
                sb.AppendLine("  （无）");
            }
            else
            {
                foreach (var d in selected)
                {
                    sb.AppendLine($"  [{d.Kind}] {d.Name}");
                sb.AppendLine($"      原始名称={d.RawName}；确认型号={d.ConfirmedModel}；接口={d.InterfaceDiscovered}；接收器={d.ReceiverDiscovered}；外设确认={d.PeripheralConfirmed}；电量有效={d.Level >= 0 && !d.IsStale}");
                    sb.AppendLine($"      设备 ID  = {d.Id}");
                    if (!string.IsNullOrWhiteSpace(d.Detail)) sb.AppendLine($"      标识     = {d.Detail}");
                    sb.AppendLine($"      通道     = {d.Transport}");
                    sb.AppendLine($"      电量     = {(d.Level < 0 ? "读不到" : d.Level + "%")}");
                    sb.AppendLine($"      连接     = {(d.Connected ? "已连接" : "未连接")}");
                    if (d.IsStale) sb.AppendLine("      读数状态 = 上一次成功读取的缓存值");
                    if (!string.IsNullOrWhiteSpace(d.Note)) sb.AppendLine($"      读取说明 = {d.Note}");
                    else if (d.Level < 0) sb.AppendLine("      读取说明 = 未记录具体失败原因，请参阅下方扫描诊断");
                    sb.AppendLine();
                }
            }

            // HID 集合：单设备模式只列该 VID:PID
            sb.AppendLine(single ? "========== 该设备的 HID 集合 ==========" : "========== 本机全部 HID 集合 ==========");
            sb.AppendLine("格式: VID:PID  产品名  [用法页/用法]  能力(in/out/feature)");
            sb.AppendLine("同一物理设备可暴露多个集合；集合数量不等于物理设备数量。");
            if (!single) sb.AppendLine("上方标识可能是蓝牙地址，不一定是 VID:PID。");
            sb.AppendLine();

            try
            {
                var nodes = single && targetVid != 0
                    ? Detection.HidProbe.Enumerate(targetVid, targetPid)
                    : single ? new List<Detection.HidProbe.Node>() : Detection.HidProbe.Enumerate(null, null);
                var seen = new HashSet<string>();
                int idx = 0;

                foreach (var n in nodes)
                {
                    string key = n.Path;
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
                    sb.AppendLine($"      路径: {n.Path}");
                    sb.AppendLine();
                }

                if (single && targetVid == 0)
                    sb.AppendLine("  （蓝牙设备：没有 HID 路径，身份与电量看 device.txt）");
                else if (idx == 0)
                    sb.AppendLine(single ? "  （这台设备没有枚举到 HID 接口）" : "  （枚举不到任何 HID 设备）");
            }
            catch (Exception ex)
            {
                sb.AppendLine("  枚举异常: " + ex.Message);
            }

            sb.AppendLine();
            sb.AppendLine(single ? "========== 该设备相关的 HID 扫描诊断 ==========" : "========== 最近一次 HID 自动扫描诊断 ==========");
            sb.AppendLine("包含过滤依据与读取结果；来自最近一次扫描，可能早于本次报告导出。");
            var diagnostics = Detection.HidDiscovery.LastDiagnostics.ToArray();
            var filteredDiagnostics = single
                ? diagnostics.Where(line => MentionsDevice(line, target, targetVid, targetPid)).ToArray()
                : diagnostics;
            if (filteredDiagnostics.Length == 0) sb.AppendLine("  （本次运行尚无该设备的 HID 扫描诊断）");
            foreach (string line in filteredDiagnostics) sb.AppendLine("  " + line);

            sb.AppendLine();
            sb.AppendLine(single ? "========== 该设备相关的蓝牙扫描诊断 ==========" : "========== 最近一次蓝牙自动扫描诊断 ==========");
            var bluetoothDiagnostics = Detection.BluetoothDiscovery.LastErrors.ToArray();
            var filteredBluetooth = single
                ? bluetoothDiagnostics.Where(line => MentionsDevice(line, target, 0, 0) ||
                    (!string.IsNullOrWhiteSpace(target.Detail) && line.Contains(target.Detail))).ToArray()
                : bluetoothDiagnostics;
            if (filteredBluetooth.Length == 0) sb.AppendLine("  （本次运行尚无该设备的蓝牙扫描诊断）");
            foreach (string line in filteredBluetooth) sb.AppendLine("  " + line);

            return sb.ToString();
        }

        private static string BuildDeviceSnapshot(AppSettings settings, DeviceInfo device, ushort vid, ushort pid)
        {
            var sb = new StringBuilder();
            sb.AppendLine("MouseDock 单设备快照");
            sb.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("==== 已识别结果 ====");
            sb.AppendLine("名称: " + device.Name);
            sb.AppendLine("类型: " + device.Kind);
            sb.AppendLine("通道: " + device.Transport);
            sb.AppendLine("设备 ID: " + device.Id);
            sb.AppendLine("标识: " + device.Detail);
            sb.AppendLine("连接: " + (device.Connected ? "是" : "否"));
            sb.AppendLine("电量: " + (device.Level < 0 ? "未读到" : device.Level + "%"));
            sb.AppendLine("充电: " + (device.Charging ? "是" : "否"));
            sb.AppendLine("缓存读数: " + (device.IsStale ? "是" : "否"));
            if (!string.IsNullOrWhiteSpace(device.Note)) sb.AppendLine("读取说明: " + device.Note);
            if (device.DetectionIds != null && device.DetectionIds.Length > 0)
                sb.AppendLine("DetectionIds: " + string.Join(", ", device.DetectionIds));
            sb.AppendLine();

            var nodes = new List<Detection.HidProbe.Node>();
            try
            {
                if (vid != 0) nodes = Detection.HidProbe.Enumerate(vid, pid);
            }
            catch { }
            string product = nodes.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n.Product))?.Product
                             ?? device.Name;

            sb.AppendLine("==== 名称判定 ====");
            sb.AppendLine("USB 产品串: " + product);
            try
            {
                string brand = Detection.VendorMap.Brand(vid);
                if (!string.IsNullOrEmpty(brand)) sb.AppendLine("品牌表: " + brand);
            }
            catch { }
            try
            {
                if (Detection.VendorMap.TryKnownReceiver($"{vid:X4}:{pid:X4}", out string known, out DeviceKind knownKind, out string knownKey))
                    sb.AppendLine($"已知接收器: {known} ({knownKind}, key={knownKey})");
            }
            catch { }
            try
            {
                string identity = Detection.VendorProtocol.IdentityFor(vid, pid);
                if (!string.IsNullOrEmpty(identity)) sb.AppendLine("型号串(IdentityFor): " + identity);
            }
            catch { }
            try
            {
                sb.AppendLine("英菲克判定: " + Detection.VendorProtocol.IsInphicDevice(vid, pid, product));
                sb.AppendLine("ATK V75X 判定: " + Detection.VendorProtocol.IsAtkV75X(vid, pid, product));
                sb.AppendLine("Rainy 候选: " + Detection.VendorProtocol.IsRainy75Candidate(vid, pid) +
                              " / 名字含 Rainy/WOB: " + Detection.VendorProtocol.IsRainy75Name(product));
                sb.AppendLine("ATK ZERO 判定: " + Detection.VendorProtocol.IsAtkZeroBitMouse(vid, pid));
                sb.AppendLine("MCHOSE G7 判定: " + Detection.VendorProtocol.IsMchoseG7(vid, pid, product));
                sb.AppendLine("MCHOSE V9 Pro 判定: " + Detection.VendorProtocol.IsMchoseV9Pro(vid, pid, product));
                sb.AppendLine("雷柏 VT7 判定: " + Detection.VendorProtocol.IsRapooVt7(vid, pid));
                sb.AppendLine("AULA F99 判定: " + Detection.VendorProtocol.IsAulaF99(vid, pid, product));
                sb.AppendLine("AULA SC630 判定: " + Detection.VendorProtocol.IsAulaSc630(product));
                sb.AppendLine("初代 GPW 名称: " + Detection.LogitechHidpp.IsOriginalGpwName(product));
            }
            catch { }
            sb.AppendLine();

            sb.AppendLine("==== 该设备的全部 HID 接口 ====");
            if (nodes.Count == 0)
            {
                sb.AppendLine("（没有匹配 VID:PID 的 HID 接口；蓝牙设备走 BLE/GATT）");
            }
            foreach (var n in nodes)
            {
                sb.AppendLine($"--- {n.Vid:X4}:{n.Pid:X4}  {n.Product}  版本 {n.Version:X4}");
                sb.AppendLine($"    Usage = 0x{n.Caps.UsagePage:X4}/0x{n.Caps.Usage:X4}");
                sb.AppendLine($"    In/Out/Feature = {n.Caps.InputReportByteLength}/{n.Caps.OutputReportByteLength}/{n.Caps.FeatureReportByteLength}");
                sb.AppendLine("    路径: " + n.Path);
            }
            sb.AppendLine();

            sb.AppendLine("==== 该设备相关的 HID 扫描诊断 ====");
            var diagnostics = Detection.HidDiscovery.LastDiagnostics
                .Where(line => MentionsDevice(line, device, vid, pid)).ToArray();
            if (diagnostics.Length == 0) sb.AppendLine("（无）");
            foreach (string line in diagnostics) sb.AppendLine("  " + line);
            sb.AppendLine();

            sb.AppendLine("==== 该设备相关的蓝牙扫描诊断 ====");
            var bluetooth = Detection.BluetoothDiscovery.LastErrors
                .Where(line => MentionsDevice(line, device, vid, pid) ||
                               (!string.IsNullOrWhiteSpace(device.Detail) && line.Contains(device.Detail)))
                .ToArray();
            if (bluetooth.Length == 0) sb.AppendLine("（无）");
            foreach (string line in bluetooth) sb.AppendLine("  " + line);
            sb.AppendLine();

            sb.AppendLine("==== 当前设置 ====");
            sb.AppendLine("安全模式: " + (settings?.SafeMode == true ? "开" : "关"));
            sb.AppendLine("刷新间隔: " + settings?.RefreshSeconds + " 秒");
            sb.AppendLine("报告范围: 只抓这一台");
            return sb.ToString();
        }

        private static bool SameDevice(DeviceInfo a, DeviceInfo b)
        {
            if (a == null || b == null) return false;
            if (string.Equals(a.Id, b.Id, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrWhiteSpace(a.Detail) && !string.IsNullOrWhiteSpace(b.Detail) &&
                string.Equals(a.Detail, b.Detail, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool TryGetVidPid(DeviceInfo device, out ushort vid, out ushort pid)
        {
            vid = pid = 0;
            if (device == null) return false;
            var match = System.Text.RegularExpressions.Regex.Match(device.Detail ?? "",
                @"\b([0-9A-Fa-f]{4}):([0-9A-Fa-f]{4})\b");
            if (!match.Success)
                match = System.Text.RegularExpressions.Regex.Match(device.Id ?? "",
                    @"hid-([0-9A-Fa-f]{4})-([0-9A-Fa-f]{4})");
            if (!match.Success) return false;
            return ushort.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out vid)
                & ushort.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.HexNumber, null, out pid);
        }

        private static bool MentionsDevice(string line, DeviceInfo target, ushort vid, ushort pid)
        {
            if (string.IsNullOrEmpty(line) || target == null) return false;
            if (!string.IsNullOrWhiteSpace(target.Name) &&
                line.IndexOf(target.Name, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!string.IsNullOrWhiteSpace(target.Id) &&
                line.IndexOf(target.Id, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (vid != 0)
            {
                string vidPid = $"{vid:X4}:{pid:X4}";
                string pathVidPid = $"VID_{vid:X4}&PID_{pid:X4}";
                string hidId = $"hid-{vid:X4}-{pid:X4}";
                if (line.IndexOf(vidPid, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (line.IndexOf(pathVidPid, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (line.IndexOf(hidId, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static string BuildSummary(AppSettings settings, List<DeviceInfo> detected, DeviceInfo target = null)
        {
            bool single = target != null;
            var selected = single
                ? detected.Where(d => SameDevice(d, target)).ToList()
                : detected;
            if (single && selected.Count == 0) selected.Add(target);

            var sb = new StringBuilder();
            sb.AppendLine(single ? "MouseDock 单设备抓取摘要" : "MouseDock 反馈摘要");
            sb.AppendLine("==================");
            sb.AppendLine();
            sb.AppendLine("版本: " + UpdateService.CurrentVersionText);
            sb.AppendLine("系统: " + Environment.OSVersion.VersionString);
            sb.AppendLine("64位: " + Environment.Is64BitOperatingSystem);

            // StartTime 在受限令牌、沙箱、部分 EDR 环境里会抛 Win32Exception。
            // 它只是一行运行时长，没有理由拖垮整份报告。
            string uptime;
            try
            {
                uptime = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMinutes.ToString("F0") + " 分钟";
            }
            catch (Exception ex)
            {
                uptime = "读取不到（" + ex.GetType().Name + "）";
            }
            sb.AppendLine("运行时长: " + uptime);
            sb.AppendLine();

            if (single)
            {
                sb.AppendLine("抓取目标: " + target.Name);
                sb.AppendLine("设备 ID: " + target.Id);
                sb.AppendLine("标识: " + target.Detail);
                sb.AppendLine("通道: " + target.Transport);
                sb.AppendLine("报告只包含这一台设备；probe.txt 是只读查询帧与原始应答。");
                sb.AppendLine();
            }

            sb.AppendLine("已识别设备: " + selected.Count + " 台");
            sb.AppendLine("电量已读到: " + selected.Count(d => d.Level >= 0) + " 台；未读到: " + selected.Count(d => d.Level < 0) + " 台");
            foreach (var d in selected)
            {
                sb.AppendLine($"  - [{d.Kind}] {d.Name}  {(d.Level < 0 ? "读不到" : d.Level + "%")}");
                if (!string.IsNullOrWhiteSpace(d.Note)) sb.AppendLine("      读取说明: " + d.Note);
            }
            sb.AppendLine();
            sb.AppendLine("设置:");
            sb.AppendLine("  刷新间隔 = " + settings?.RefreshSeconds + " 秒");
            sb.AppendLine("  低电量提醒 = " + settings?.LowBatteryWarn + "%");
            sb.AppendLine("  自动更新 = " + (settings?.AutoUpdate == true ? "开" : "关"));
            sb.AppendLine("  安全模式 = " + (settings?.SafeMode == true ? "开（跳过需要发送命令的厂商读取）" : "关"));
            sb.AppendLine();
            sb.AppendLine("说明:");
            sb.AppendLine(single
                ? "  device.txt 是这一台的名称/身份/接口快照；probe.txt 是只读查询与原始应答。"
                : "  hid-devices.txt 列出 HID 集合、完整路径及最近扫描中的过滤/读取诊断。");
            sb.AppendLine("  电量显示「—」表示尚未取得有效读数；设备已发现并不代表电量协议已适配。");
            sb.AppendLine("  反馈时请提供报告、型号和连接方式；适配可能仍需官方协议或实际响应数据。");

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
