using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using MouseDock.Models;

namespace MouseDock.Services.Detection
{
    internal sealed class ProbeEvent
    {
        public long Ms { get; set; }
        public string Dir { get; set; }
        public string Label { get; set; }
        public string Hex { get; set; }
        public string Note { get; set; }
    }

    internal sealed class ProbeExchange
    {
        public string Label;
        public string Channel;
        public byte[] Tx;
        public int Sequence;
        public readonly List<byte[]> Rx = new List<byte[]>();
    }

    internal sealed class DeviceProbeResult
    {
        public string Text = "";
        public string Jsonl = "";
        public int Exchanges;
        public int Responses;
    }

    internal static partial class VendorProtocol
    {
        private sealed class ProbeSpec
        {
            public string Label;
            public string Channel;
            public byte[] Frame;
            public bool Feature;
            public bool ReadOnlyFeature;
            public byte ReportId;
            public int Sequence;
            public int DelayMs;
        }

        /// <summary>
        /// 单设备只读抓取：枚举该 VID:PID 的全部 HID 接口，按集合签名发已知查询帧，
        /// 记录 TX/RX 原始字节，再用现有解析器给出「像不像某型号」的判定。
        /// 不写配置、不改设备状态；安全模式下只枚举和被动听，不发任何帧。
        /// </summary>
        internal static DeviceProbeResult ProbeDevice(ushort vid, ushort pid, string requestedName,
                                                      bool safeMode, int listenMs = 400, bool scanOnly = false)
        {
            var sw = Stopwatch.StartNew();
            var text = new StringBuilder();
            var events = new List<ProbeEvent>();
            var exchanges = new List<ProbeExchange>();
            var jsonl = new StringBuilder();

            text.AppendLine("MouseDock 单设备只读抓取");
            text.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            text.AppendLine($"目标: {requestedName}");
            text.AppendLine($"VID:PID = {vid:X4}:{pid:X4}");
            text.AppendLine($"安全模式: {(safeMode ? "开（只枚举，不发查询帧）" : "关")}");
            text.AppendLine();

            if (vid == 0x3554 && pid is 0xF503 or 0xFA09)
            {
                text.AppendLine("此接收器仅由自动扫描按物理实例读取；查询及原始响应见 hid-devices.txt。");
                return new DeviceProbeResult { Text = text.ToString() };
            }
            // This diagnostic API has no physical-instance key. Do not reopen every
            // receiver sharing this public ID; dedicated scan routes already log
            // their exact channel, report metadata and transaction result.
            if (ReceiverIdentityProtocol.IsCandidate(vid, pid) || (vid == 0x0B05 && pid == 0x1ACE) || IsAtkZeroBitMouse(vid,pid) || IsRainy75Candidate(vid,pid) || (IsScopedModernMouse(vid,pid,requestedName)||IsScopedG7(vid,pid,requestedName)) || IsMchoseV9Pro(vid,pid,requestedName) || IsScopedInphic(vid,pid,requestedName) || scanOnly || (vid == 0xA8A5 && pid == 0x2255) || AulaBatteryProtocol.IsCandidate(vid, pid, requestedName) || OriginalGpwProtocol.IsCandidate(vid,pid))
            {
                text.AppendLine("此设备不运行跨实例通用探针。专用协议仅由自动扫描的精确实例/集合读取；请查看 hid-devices.txt 的诊断。");
                return new DeviceProbeResult { Text = text.ToString() };
            }

            text.AppendLine("==== 名称线索 ====");
            text.AppendLine("选中名称: " + (requestedName ?? ""));
            try
            {
                string identity = IdentityFor(vid, pid);
                if (!string.IsNullOrEmpty(identity)) text.AppendLine("型号串(IdentityFor): " + identity);
            }
            catch { }
            try
            {
                if (VendorMap.TryKnownReceiver($"{vid:X4}:{pid:X4}", out string known, out DeviceKind kind, out _))
                    text.AppendLine($"已知接收器表: {known} ({kind})");
            }
            catch { }
            try
            {
                string brand = VendorMap.Brand(vid);
                if (!string.IsNullOrEmpty(brand)) text.AppendLine("品牌表: " + brand);
            }
            catch { }
            text.AppendLine();

            var paths = Paths(vid, pid);
            text.AppendLine($"接口路径数: {paths.Count}");
            text.AppendLine();

            bool hasFf1c = false, hasFf05 = false, hasFf00 = false, hasFf13 = false;
            bool hasFeature41 = false, hasFeature520 = false, hasInphicSplit = false;
            bool hasFf01 = false, hasFf90 = false;

            foreach (string path in paths)
            {
                IntPtr h = OpenOverlapped(path, out IntPtr ev);
                if (h == IntPtr.Zero)
                {
                    text.AppendLine("--- 打不开接口");
                    text.AppendLine("路径: " + path);
                    text.AppendLine();
                    continue;
                }

                try
                {
                    var caps = Caps(h);
                    string product = HidDiscovery.ReadHidProduct(h, path) ?? "";
                    if (caps.UsagePage == 0xFF1C && caps.Usage == 0x0092 &&
                        caps.InputReportByteLength == 64 && caps.OutputReportByteLength == 64) hasFf1c = true;
                    if (caps.UsagePage == 0xFF05 && caps.Usage == 1 &&
                        caps.InputReportByteLength == 64 && caps.OutputReportByteLength == 64) hasFf05 = true;
                    if (caps.UsagePage == 0xFF00) hasFf00 = true;
                    if (caps.UsagePage == 0xFF13 && caps.Usage == 1) hasFf13 = true;
                    if (caps.FeatureReportByteLength == 41 && caps.UsagePage == 0xFF01) hasFeature41 = true;
                    if (caps.FeatureReportByteLength == 520) hasFeature520 = true;
                    if ((caps.UsagePage == 0x000A && caps.InputReportByteLength == 5) ||
                        (caps.UsagePage == 0x000B && caps.FeatureReportByteLength >= 10)) hasInphicSplit = true;
                    if (caps.UsagePage == 0xFF01 && caps.Usage == 1 &&
                        caps.OutputReportByteLength is 64 or 65) hasFf01 = true;
                    if (caps.UsagePage == 0xFF90 && caps.Usage == 1) hasFf90 = true;
                    text.AppendLine("--- HID 接口");
                    text.AppendLine("路径: " + path);
                    text.AppendLine("产品串: " + (string.IsNullOrEmpty(product) ? "（空）" : product));
                    text.AppendLine($"UsagePage/Usage = 0x{caps.UsagePage:X4}/0x{caps.Usage:X4}");
                    text.AppendLine($"Input/Output/Feature = {caps.InputReportByteLength}/{caps.OutputReportByteLength}/{caps.FeatureReportByteLength}");
                    text.AppendLine();

                    if (safeMode)
                    {
                        if (caps.InputReportByteLength > 0)
                            Listen(h, ev, caps, listenMs, sw, text, events, exchanges);
                        continue;
                    }

                    foreach (var spec in BuildProbes(vid, pid, caps, product))
                    {
                        if (spec.Feature) RunFeatureProbe(path, caps, spec, sw, text, events, exchanges);
                        else RunOutputProbe(h, ev, caps, spec, sw, text, events, exchanges);
                    }

                    if (caps.InputReportByteLength > 0)
                        Listen(h, ev, caps, listenMs, sw, text, events, exchanges);
                }
                catch (Exception ex)
                {
                    text.AppendLine("接口异常: " + ex.GetType().Name + " " + ex.Message);
                }
                finally
                {
                    CloseHandle(ev);
                    CloseHandle(h);
                }
            }

            text.AppendLine("==== 协议前置条件 ====");
            text.AppendLine("Rainy 75: 320F:5055/5088 + FF1C/0092 64/64" +
                            ((vid == 0x320F && hasFf1c) ? " 已满足" : " 缺少"));
            text.AppendLine("ATK V75X: 320F:5055/5088 + 产品串含 V75X + FF1C/0092 64/64" +
                            ((vid == 0x320F && hasFf1c && (requestedName ?? "").IndexOf("V75X", StringComparison.OrdinalIgnoreCase) >= 0)
                                ? " 已满足" : " 缺少"));
            text.AppendLine("ATK ZERO: 373B:1154/1155/124F + FF05/0001 64/64 + 0x89 身份 cid/mid" +
                            ((vid == 0x373B && hasFf05) ? " 已满足" : " 缺少"));
            text.AppendLine("GPW: 046D + FF00 + HID++ 0x1001 电压" +
                            ((vid == 0x046D && hasFf00) ? " 已满足" : " 缺少"));
            text.AppendLine("AULA SC630 Pro: 产品串含 SC630 + FF01/0001 41B Feature" +
                            ((hasFeature41 && (requestedName ?? "").IndexOf("SC630", StringComparison.OrdinalIgnoreCase) >= 0)
                                ? " 已满足" : " 缺少"));
            text.AppendLine("AULA F99: 520B Feature（2.4G 接收器通常没有）" +
                            (hasFeature520 ? " 已满足" : " 缺少"));
            text.AppendLine("HyperX Cloud III S: 03F0 + FF13/0001 + 0x1D" +
                            ((vid == 0x03F0 && hasFf13) ? " 已满足" : " 缺少"));
            text.AppendLine("英菲克 IN9 Pro: 1D57:FA60/FA65 + 000A/000B 分集合" +
                            (hasInphicSplit ? " 已满足" : " 缺少"));
            text.AppendLine("迈从 G7: 3837/A8A4 + FF01/0010 + 64/65" +
                            (((vid == 0x3837 || vid == 0xA8A4) && hasFf01 &&
                              (requestedName ?? "").IndexOf("G7", StringComparison.OrdinalIgnoreCase) >= 0)
                                ? " 已满足" : " 缺少"));
            text.AppendLine("迈从 V9 Pro: 291D:385D + 产品串 MCHOSE V9 PRO + FF90/0001" +
                            (hasFf90 ? " 已满足" : " 缺少"));
            text.AppendLine();

            text.AppendLine("==== 解析判定 ====");
            int matched = 0;
            foreach (var ex in exchanges)
            {
                foreach (var rx in ex.Rx)
                {
                    bool rainyLabel = ex.Label != null && ex.Label.Contains("Rainy");
                    bool v75Label = ex.Label != null && ex.Label.Contains("V75X");
                    bool zeroLabel = ex.Label != null && ex.Label.Contains("ZERO");
                    bool g7Label = ex.Label != null && ex.Label.Contains("G7");
                    bool v9Label = ex.Label != null && ex.Label.Contains("V9");
                    bool sc630Label = ex.Label != null && ex.Label.Contains("SC630");
                    bool f99Label = ex.Label != null && ex.Label.Contains("F99");

                    if (rainyLabel && TryParseRainy75Battery(rx, out int rainyLevel, out bool rainyCharging))
                    {
                        text.AppendLine($"[{ex.Label}] Rainy 75 应答匹配: {rainyLevel}% 充电={rainyCharging}");
                        matched++;
                    }
                    if (v75Label && ex.Tx != null && TryParseAtkV75XBattery(rx, ex.Tx, out int atkLevel, out bool atkCharging))
                    {
                        text.AppendLine($"[{ex.Label}] ATK V75X 应答匹配: {atkLevel}% 充电={atkCharging}");
                        matched++;
                    }
                    if (zeroLabel && TryParseAtkZeroBattery(rx, out int zeroLevel))
                    {
                        text.AppendLine($"[{ex.Label}] ATK ZERO 电量应答匹配: {zeroLevel}%");
                        matched++;
                    }
                    if (g7Label && TryParseMchoseG7Battery(rx, out int g7Level, out bool g7Charging))
                    {
                        text.AppendLine($"[{ex.Label}] MCHOSE G7 应答匹配: {g7Level}% 充电={g7Charging}");
                        matched++;
                    }
                    if (v9Label && TryParseMchoseV9Battery(rx, out int v9Level, out bool v9Charging))
                    {
                        text.AppendLine($"[{ex.Label}] MCHOSE V9 Pro 应答匹配: {v9Level}% 充电={v9Charging}");
                        matched++;
                    }
                    if (sc630Label && ex.Sequence > 0 && TryParseAulaSc630Battery(rx, (byte)ex.Sequence, out int scLevel, out bool scCharging))
                    {
                        text.AppendLine($"[{ex.Label}] AULA SC630 Pro 应答匹配: {scLevel}% 充电={scCharging}");
                        matched++;
                    }
                    if (f99Label && TryParseAulaF99Battery(rx, out int f99Level))
                    {
                        text.AppendLine($"[{ex.Label}] AULA F99 520B 应答匹配: {f99Level}%");
                        matched++;
                    }
                    if (ex.Tx != null && ex.Tx.Length > 1 && ex.Tx[1] == 0x30 &&
                        TryParseIdentifyBattery(rx, out int idLevel, out bool idCharging))
                    {
                        text.AppendLine($"[{ex.Label}] AULA/英菲克 30 Identify 匹配: {idLevel}% 充电={idCharging}");
                        matched++;
                    }
                    if (ex.Tx != null && ex.Tx.Length > 1 && ex.Tx[1] == 0x10 &&
                        TryParseInphicLegacy(rx, out int legacyLevel, out bool legacyCharging, out string legacyModel))
                    {
                        text.AppendLine($"[{ex.Label}] 英菲克老协议匹配: {legacyModel} {legacyLevel}% 充电={legacyCharging}");
                        matched++;
                    }
                    if (ex.Tx != null && ex.Label != null && ex.Label.Contains("HyperX") &&
                        TryParseCloud3SBattery(rx, ex.Tx, out int hxLevel, out _))
                    {
                        text.AppendLine($"[{ex.Label}] HyperX Cloud III S 应答匹配: {hxLevel}%");
                        matched++;
                    }
                    if (ex.Label != null && ex.Label.Contains("Logitech HID++"))
                    {
                        if (LogitechHidpp.TryParseBattery(0x1000, rx, out int l1000, out bool c1000))
                        {
                            text.AppendLine($"[{ex.Label}] HID++ 0x1000 百分比: {l1000}% 充电={c1000}");
                            matched++;
                        }
                        if (LogitechHidpp.TryParseBattery(0x1001, rx, out int l1001, out bool c1001, true))
                        {
                            text.AppendLine($"[{ex.Label}] HID++ 0x1001 GPW 电压模型: {l1001}% 充电={c1001}");
                            matched++;
                        }
                    }
                }
            }
            if (matched == 0) text.AppendLine("（没有已知解析器匹配到应答；看上面的原始字节）");
            text.AppendLine();

            if (!safeMode)
            {
                text.AppendLine("==== 现有读取器实时采样 ====");
                for (int i = 1; i <= 2; i++)
                {
                    int level = -1;
                    try { level = TryReadBattery(vid, pid); } catch { }
                    text.AppendLine($"{i}. {(level < 0 ? "未读到" : level + "%")}  ({DateTime.Now:HH:mm:ss.fff})");
                    if (i < 2) Thread.Sleep(400);
                }
                text.AppendLine();
            }

            foreach (var e in events)
            {
                jsonl.AppendLine(JsonSerializer.Serialize(new
                {
                    ms = e.Ms,
                    dir = e.Dir,
                    label = e.Label,
                    hex = e.Hex,
                    note = e.Note
                }));
            }

            return new DeviceProbeResult
            {
                Text = text.ToString(),
                Jsonl = jsonl.ToString(),
                Exchanges = exchanges.Count,
                Responses = events.Count(e => e.Dir == "RX")
            };
        }

        private static List<ProbeSpec> BuildProbes(ushort vid, ushort pid, HIDP_CAPS caps, string product)
        {
            var list = new List<ProbeSpec>();
            if (IsAtkZeroBitMouse(vid,pid) || IsRainy75Candidate(vid,pid) || (IsScopedModernMouse(vid,pid,product)||IsScopedG7(vid,pid,product)) || IsMchoseV9Pro(vid,pid,product) || IsScopedInphic(vid,pid,product) || AulaBatteryProtocol.IsCandidate(vid, pid, product) || OriginalGpwProtocol.IsCandidate(vid,pid)) return list;
            string p = product ?? "";

            // Feature 通道：SC630 / F99 / 英菲克 / 标准电池
            if (caps.FeatureReportByteLength == 41 && caps.UsagePage == 0xFF01 && IsAulaSc630(p))
                list.Add(new ProbeSpec { Label = "AULA SC630 Pro Feature 6 查询", Channel = "feature",
                    Frame = BuildAulaSc630BatteryRequest(1), Feature = true, Sequence = 1 });

            if (caps.FeatureReportByteLength == 520 && IsAulaF99(vid, pid, p))
                list.Add(new ProbeSpec { Label = "AULA F99 520B Feature 查询", Channel = "feature",
                    Frame = BuildAulaF99BatteryRequest(), Feature = true });

            if (caps.UsagePage == 0x000B && caps.FeatureReportByteLength >= InphicWakeFrame.Length &&
                IsInphicDevice(vid, pid, p))
            {
                list.Add(new ProbeSpec { Label = "英菲克唤醒 Feature", Channel = "feature",
                    Frame = BuildInphicWakeRequest(caps.FeatureReportByteLength), Feature = true });
            }

            if (caps.UsagePage == 0x0006 && caps.Usage == 0x0020 && caps.FeatureReportByteLength > 0)
                list.Add(new ProbeSpec { Label = "标准 HID 电池 Feature", Channel = "feature",
                    Feature = true, ReadOnlyFeature = true, ReportId = 0 });

            // 输出通道：按集合签名只发已知只读查询
            int outLen = caps.OutputReportByteLength;
            if (outLen <= 0) return list;

            if (caps.UsagePage == 0xFF1C && caps.Usage == 0x0092 && outLen == 64)
            {
                list.Add(new ProbeSpec { Label = "Rainy 75 官方查询 04 20 00 1A 06", Channel = "output",
                    Frame = BuildRainy75BatteryRequest() });
                list.Add(new ProbeSpec { Label = "ATK V75X 查询 code=1", Channel = "output",
                    Frame = BuildAtkV75XBatteryRequest(1, 64) });
                list.Add(new ProbeSpec { Label = "ATK V75X 查询 code=2", Channel = "output",
                    Frame = BuildAtkV75XBatteryRequest(2, 64) });
            }

            if (caps.UsagePage == 0xFF05 && caps.Usage == 1 && outLen == 64)
            {
                bool receiver = pid != 0x1154;
                list.Add(new ProbeSpec { Label = "ATK ZERO 身份 0x89", Channel = "output",
                    Frame = BuildAtkZeroRequest(0x89, receiver) });
                list.Add(new ProbeSpec { Label = "ATK ZERO 电量 0x07", Channel = "output",
                    Frame = BuildAtkZeroRequest(0x07, receiver) });
                list.Add(new ProbeSpec { Label = "ATK ZERO 充电 0x0F", Channel = "output",
                    Frame = BuildAtkZeroRequest(0x0F, receiver) });
            }

            if (caps.UsagePage == 0xFF01 && caps.Usage == 1 && outLen is 64 or 65 &&
                (vid == 0x3837 || vid == 0xA8A4 || p.IndexOf("G7", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                list.Add(new ProbeSpec { Label = "MCHOSE G7 查询", Channel = "output",
                    Frame = BuildMchoseG7BatteryRequest(outLen) });
            }

            if (caps.UsagePage == 0xFF90 && caps.Usage == 1 && outLen == 64)
            {
                list.Add(new ProbeSpec { Label = "MCHOSE V9 Pro 查询 55 65 01", Channel = "output",
                    Frame = BuildMchoseV9BatteryRequest() });
            }

            if (caps.UsagePage == 0xFF00 && caps.Usage == 2 && outLen is 7 or 20 or 32)
            {
                byte reportId = outLen >= 20 ? (byte)0x11 : (byte)0x10;
                foreach (ushort feature in new ushort[] { 0x0005, 0x1001, 0x1000, 0x1004 })
                {
                    var frame = new byte[outLen];
                    frame[0] = reportId;
                    frame[1] = 0x01;
                    frame[2] = 0x00;
                    frame[3] = 0x0F;
                    frame[4] = (byte)(feature >> 8);
                    frame[5] = (byte)(feature & 0xFF);
                    list.Add(new ProbeSpec
                    {
                        Label = $"Logitech HID++ getFeature 0x{feature:X4}",
                        Channel = "output",
                        Frame = frame
                    });
                }
            }

            if (caps.UsagePage == 0xFF13 && caps.Usage == 1 && outLen is 62 or 64)
            {
                list.Add(new ProbeSpec { Label = "HyperX 0x1D GET_BATTERY_INFO", Channel = "output",
                    Frame = BuildCloud3SRequest(outLen, Cloud3SBatteryCommand) });
            }

            if (outLen == 33 && (vid == 0x089D || vid == 0x3554 || vid == 0x258A ||
                                 vid == InphicVid || vid == InphicAltVid))
            {
                var identify = new byte[33];
                identify[1] = 0x30;
                identify[3] = 0x01;
                list.Add(new ProbeSpec { Label = "AULA/英菲克 30 00 Identify", Channel = "output", Frame = identify });

                foreach (byte cmd in new byte[] { 0x12, 0x13 })
                {
                    var wake = new byte[33];
                    wake[1] = cmd;
                    wake[3] = 0x01;
                    list.Add(new ProbeSpec
                    {
                        Label = $"AULA/英菲克 唤醒 {cmd:X2} 00",
                        Channel = "output",
                        Frame = wake
                    });
                }

                // 鼠标睡着时第一帧通常只回 30 02；唤醒后再问一次才能拿到电量。
                list.Add(new ProbeSpec
                {
                    Label = "AULA/英菲克 唤醒后 30 00",
                    Channel = "output",
                    Frame = identify,
                    DelayMs = 300
                });
            }

            return list;
        }

        private static void RunOutputProbe(IntPtr h, IntPtr ev, HIDP_CAPS caps, ProbeSpec spec,
                                           Stopwatch sw, StringBuilder text, List<ProbeEvent> events,
                                           List<ProbeExchange> exchanges)
        {
            if (spec.Frame == null) return;
            var frame = new byte[caps.OutputReportByteLength];
            Array.Copy(spec.Frame, frame, Math.Min(spec.Frame.Length, frame.Length));

            if (spec.DelayMs > 0) Thread.Sleep(spec.DelayMs);

            var ex = new ProbeExchange { Label = spec.Label, Channel = spec.Channel, Tx = frame, Sequence = spec.Sequence };
            exchanges.Add(ex);
            AddEvent(events, sw, "TX", spec.Label, frame, "写入 " + frame.Length + " 字节");
            text.AppendLine($"发 {spec.Label}: {Hex(frame)}");

            bool ok;
            try { ok = Transfer(h, ev, frame, true, 300, out _); }
            catch { ok = false; }

            if (!ok)
            {
                text.AppendLine("  写入失败或无应答");
                return;
            }

            long deadline = sw.ElapsedMilliseconds + 500;
            int got = 0;
            while (sw.ElapsedMilliseconds < deadline && got < 6)
            {
                int remain = (int)Math.Max(1, deadline - sw.ElapsedMilliseconds);
                var rx = ReadReport(h, ev, remain);
                if (rx == null) break;
                ex.Rx.Add(rx);
                got++;
                AddEvent(events, sw, "RX", spec.Label, rx, "读取 " + rx.Length + " 字节");
                text.AppendLine("  收 " + Hex(rx));
            }
            if (got == 0) text.AppendLine("  （无应答）");
        }

        private static void RunFeatureProbe(string path, HIDP_CAPS caps, ProbeSpec spec,
                                            Stopwatch sw, StringBuilder text, List<ProbeEvent> events,
                                            List<ProbeExchange> exchanges)
        {
            int length = caps.FeatureReportByteLength;
            if (length <= 0) return;

            IntPtr fh = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (fh == IntPtr.Zero || fh == new IntPtr(-1))
            {
                text.AppendLine($"发 {spec.Label}: 打不开 Feature 通道");
                return;
            }

            try
            {
                var frame = new byte[length];
                if (spec.Frame != null) Array.Copy(spec.Frame, frame, Math.Min(spec.Frame.Length, frame.Length));
                else frame[0] = spec.ReportId;

                var ex = new ProbeExchange { Label = spec.Label, Channel = spec.Channel, Tx = frame, Sequence = spec.Sequence };
                exchanges.Add(ex);

                if (spec.ReadOnlyFeature)
                {
                    text.AppendLine($"读 {spec.Label}（{length} 字节）");
                }
                else
                {
                    AddEvent(events, sw, "TX", spec.Label, frame, "Feature 写入 " + length + " 字节");
                    text.AppendLine($"发 {spec.Label}: {Hex(frame)}");
                    if (!HidD_SetFeature(fh, frame, length))
                    {
                        text.AppendLine("  SetFeature 失败");
                        return;
                    }
                    Thread.Sleep(120);
                }

                var rx = new byte[length];
                if (spec.ReadOnlyFeature)
                {
                    rx[0] = spec.ReportId;
                    AddEvent(events, sw, "TX", spec.Label, new[] { spec.ReportId }, "Feature 读取请求");
                }
                if (!HidD_GetFeature(fh, rx, length))
                {
                    text.AppendLine("  GetFeature 失败");
                    return;
                }

                ex.Rx.Add(rx);
                AddEvent(events, sw, "RX", spec.Label, rx, "Feature 读取 " + length + " 字节");
                text.AppendLine("  收 " + Hex(rx));
            }
            catch (Exception ex)
            {
                text.AppendLine("  Feature 异常: " + ex.GetType().Name + " " + ex.Message);
            }
            finally { CloseHandle(fh); }
        }

        private static void Listen(IntPtr h, IntPtr ev, HIDP_CAPS caps, int listenMs,
                                   Stopwatch sw, StringBuilder text, List<ProbeEvent> events,
                                   List<ProbeExchange> exchanges)
        {
            long deadline = sw.ElapsedMilliseconds + Math.Max(80, listenMs);
            int got = 0;
            while (sw.ElapsedMilliseconds < deadline && got < 20)
            {
                int remain = (int)Math.Max(1, deadline - sw.ElapsedMilliseconds);
                var rx = ReadReport(h, ev, remain);
                if (rx == null) break;
                got++;
                AddEvent(events, sw, "RX", "被动通知", rx, "监听 " + caps.UsagePage.ToString("X4") + "/" + caps.Usage.ToString("X4"));
                text.AppendLine("被动收 " + Hex(rx));
            }
        }

        private static void AddEvent(List<ProbeEvent> events, Stopwatch sw, string dir, string label,
                                     byte[] data, string note)
        {
            events.Add(new ProbeEvent
            {
                Ms = sw.ElapsedMilliseconds,
                Dir = dir,
                Label = label,
                Hex = data == null ? "" : Hex(data),
                Note = note
            });
        }

        private static string Hex(byte[] data)
        {
            if (data == null || data.Length == 0) return "";
            var sb = new StringBuilder(data.Length * 3);
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }
    }
}
