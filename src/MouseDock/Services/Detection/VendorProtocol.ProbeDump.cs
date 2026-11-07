using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MouseDock.Services.Detection;

// 用户报告里「这台到底是什么鼠标、为什么没识别」这两问，靠现有的 probe.txt 答不全：
// 报告里 probe-events.jsonl 常常是 0 字节，因为程序根本没发查询帧。
// 这里补一段不依赖任何已知协议的取证：
//   1) 被动听所有 33 字节输入集合，收到的每一帧都进 jsonl；
//   2) 只读 GetFeature(0) 拉一次 Feature 集合；
//   3) 只有在公版接收器号段、且集合本身就是 33 字节 Feature 时，才允许写一帧身份请求
//      （0x10 / 0x30 两种帧头各试一次），因为这类接收器没有 OUTPUT 集合，不写就永远拿不到型号；
//   4) 无论成败都打印结论，把「缺哪个条件」写清楚。
internal static partial class VendorProtocol
{
    private const int DumpFrame = 33;
    private const int DumpListenMs = 1500;

    /// <summary>
    /// 专用读取器（ROG Omni / 迈从 A7 V2 / 英菲克 / 狼蛛等）的查询不经过通用探针，
    /// 以前 probe.txt 里只留一句「请看 hid-devices.txt」，等于没给证据。
    /// 这里把这几条专用通道实际发一帧、把原始应答写进报告。
    /// 只发只读查询；安全模式下一律跳过。
    /// </summary>
    internal static void AppendDedicatedProtocolConclusion(StringBuilder text, ushort vid, ushort pid,
        string requestedName, bool safeMode)
    {
        if (IsRogOmni(vid, pid))
        {
            text.AppendLine("==== ROG Omni 专用通道 ====");
            text.AppendLine("要求：MI_02 的 col01 = 0xFF02/0001 64/64（配对表），col02 = 0xFF00/0001 64/64（电量）。");
            var paths = Paths(vid, pid);
            string pairPath = paths.FirstOrDefault(p => p.IndexOf("mi_02&col01", StringComparison.OrdinalIgnoreCase) >= 0);
            string batteryPath = paths.FirstOrDefault(p => p.IndexOf("mi_02&col02", StringComparison.OrdinalIgnoreCase) >= 0);
            text.AppendLine($"本机命中：配对集合={(pairPath == null ? "没有" : "有")}；电量集合={(batteryPath == null ? "没有" : "有")}。");
            if (safeMode)
            {
                text.AppendLine("安全模式：不发查询帧。");
                text.AppendLine();
                return;
            }
            if (pairPath == null || batteryPath == null)
            {
                text.AppendLine("结论：Omni 的两个专用集合不完整，无法读取；看上面的接口清单确认是缺失还是打不开。");
                text.AppendLine();
                return;
            }
            try
            {
                var paired = ReadRogOmniReply(pairPath, 1, 0xA0, 0);
                if (paired == null)
                {
                    text.AppendLine("配对表（report 1 / A0 / 0）：无应答");
                }
                else
                {
                    text.AppendLine("配对表（report 1 / A0 / 0）：" + Convert.ToHexString(paired));
                    if (TryReadRogOmniPairing(paired, out var pids))
                        text.AppendLine("配对型号：" + RogPairedModelText(pids) +
                            (RogPairedModel(paired, out _) == null ? "（没有唯一的已确认键盘型号）" : "（已确认键盘）"));
                    else
                        text.AppendLine("配对表格式不符：期望 01 A0 00 开头的 64 字节帧。");
                }
                var battery = ReadRogOmniReply(batteryPath, 2, 0x12, 1);
                if (battery == null)
                {
                    text.AppendLine("电量（report 2 / 12 / 1）：无应答");
                }
                else
                {
                    text.AppendLine("电量（report 2 / 12 / 1）：" + Convert.ToHexString(battery));
                    text.AppendLine(TryParseRogScopeBattery(battery, out int percent, out bool charging)
                        ? $"解析：{percent}%，{(charging ? "充电中" : "未充电")}"
                        : "解析：帧头或取值不符合 02/12/01 布局，未采用。");
                }
            }
            catch (Exception ex)
            {
                text.AppendLine("查询异常：" + ex.GetType().Name + " " + ex.Message);
            }
            text.AppendLine();
            return;
        }

        if (IsMchoseA7V2(vid, pid))
        {
            text.AppendLine("==== 迈从 A7 V2 专用通道 ====");
            text.AppendLine("要求：FF01/0001 65/65/65 + 产品串 " + MchoseA7V2ModelName(pid) + "，只读 Feature 11/F9。");
            var nodes = HidProbe.Enumerate(0x3837, pid).Where(IsMchoseA7V2Interface).ToArray();
            text.AppendLine($"本机命中接口：{nodes.Length} 个。");
            if (safeMode)
            {
                text.AppendLine("安全模式：不发查询帧。");
                text.AppendLine();
                return;
            }
            foreach (var node in nodes)
            {
                IntPtr h = CreateFile(node.Path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1))
                {
                    text.AppendLine("打开失败 Win32=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                    continue;
                }
                try
                {
                    var request = BuildMchoseA7V2Request();
                    if (!HidD_SetFeature(h, request, request.Length))
                    {
                        text.AppendLine("11/F9 发送失败 Win32=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                        continue;
                    }
                    System.Threading.Thread.Sleep(120);
                    var response = new byte[Math.Max(21, (int)node.Caps.FeatureReportByteLength)];
                    response[0] = 0x11;
                    if (!HidD_GetFeature(h, response, response.Length))
                    {
                        text.AppendLine("11/F9 读回失败 Win32=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                        continue;
                    }
                    text.AppendLine("11/F9 应答：" + Convert.ToHexString(response.AsSpan(0, Math.Min(21, response.Length))));
                    text.AppendLine(TryParseMchoseLegacy(response.AsSpan(0, Math.Min(21, response.Length)), vid, pid,
                                        out int level, out bool charging) && level >= 0
                        ? $"解析：{level}%，{(charging ? "充电中" : "未充电")}"
                        : "解析：型号/长度/连接位校验不通过，未采用。");
                }
                finally { CloseHandle(h); }
            }
            text.AppendLine();
            return;
        }

        text.AppendLine("==== 专用读取器 ====");
        text.AppendLine("这台设备命中专用协议，跨实例通用探针不发送查询帧；上面的接口清单已列出全部集合与打开结果。");
        text.AppendLine();
        return;
    }

    internal static void AppendReceiverIdentityCapture(StringBuilder text, List<ProbeEvent> events,
        ushort vid, ushort pid, bool allowWrite)
    {
        long start = Environment.TickCount64;
        void Event(string dir, string label, byte[] bytes, string note) =>
            events.Add(new ProbeEvent
            {
                Ms = Environment.TickCount64 - start,
                Dir = dir,
                Label = label,
                Hex = bytes == null ? "" : Convert.ToHexString(bytes),
                Note = note
            });

        var inputs = new List<string>();
        var features = new List<string>();
        int otherIn = 0, otherFeature = 0;

        foreach (string path in Paths(vid, pid))
        {
            IntPtr probe = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (probe == IntPtr.Zero || probe == new IntPtr(-1)) continue;
            HIDP_CAPS caps;
            try { caps = Caps(probe); }
            finally { CloseHandle(probe); }
            if (caps.InputReportByteLength == DumpFrame) inputs.Add(path);
            else if (caps.InputReportByteLength > 0) otherIn++;
            if (caps.FeatureReportByteLength == DumpFrame) features.Add(path);
            else if (caps.FeatureReportByteLength > 0) otherFeature++;
        }

        text.AppendLine("==== 接收器身份取证（不依赖已知协议）====");
        text.AppendLine($"33 字节输入集合: {inputs.Count}；33 字节 Feature 集合: {features.Count}；其他输入={otherIn} 其他Feature={otherFeature}");
        if (inputs.Count + features.Count == 0)
        {
            text.AppendLine("没有 33 字节集合可取证：这台接收器不暴露身份通道，无法靠 HID 取证确定型号。");
            text.AppendLine();
            return;
        }

        ReferenceBatteryProtocol.Reading best = null;
        string bestFrame = null;

        foreach (string path in inputs)
        {
            IntPtr h = CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;
            IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
            try
            {
                if (ev == IntPtr.Zero) continue;
                long until = Environment.TickCount64 + DumpListenMs;
                int got = 0;
                while (Environment.TickCount64 < until)
                {
                    var buf = new byte[DumpFrame];
                    int wait = (int)Math.Max(1, Math.Min(200, until - Environment.TickCount64));
                    if (!Transfer(h, ev, buf, false, wait, out int n) || n <= 0) continue;
                    got++;
                    var raw = buf.AsSpan(0, n).ToArray();
                    Event("RX", "被动上报", raw, Short(path));
                    if (TryParseReceiver33Frame(raw, out int level, out bool charging, out string model, out bool aula))
                    {
                        var reading = ReceiverDumpReading(level, charging, model, aula, "被动上报");
                        if (best == null) { best = reading; bestFrame = Convert.ToHexString(raw); }
                    }
                }
                text.AppendLine($"监听 {DumpListenMs}ms {Short(path)}：收到 {got} 帧");
            }
            finally
            {
                if (ev != IntPtr.Zero) CloseHandle(ev);
                CloseHandle(h);
            }
        }

        foreach (string path in features)
        {
            IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;
            try
            {
                var caps = Caps(h);
                var ids = FeatureReportIds(h, caps);
                text.AppendLine($"Feature 集合 {Short(path)} 报告编号候选: {string.Join(",", ids)}（V/B={caps.NumberFeatureValueCaps}/{caps.NumberFeatureButtonCaps}）");
                foreach (byte id in ids)
                {
                    var buf = new byte[DumpFrame];
                    buf[0] = id;
                    bool ok = HidD_GetFeature(h, buf, buf.Length);
                    int win32 = ok ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    Event(ok ? "RX" : "ERR", $"GET_FEATURE({id:X2})", ok ? buf : null,
                        Short(path) + (ok ? "" : " Win32=" + win32));
                    if (!ok) continue;
                    text.AppendLine($"GET_FEATURE({id:X2}) 成功: {Convert.ToHexString(buf)}");
                    if (best == null && TryParseReceiver33Frame(buf, out int level, out bool charging, out string model, out bool aula))
                    {
                        best = ReceiverDumpReading(level, charging, model, aula, $"GET_FEATURE({id:X2})");
                        bestFrame = Convert.ToHexString(buf);
                    }
                }
            }
            finally { CloseHandle(h); }
        }

        // 走到这里说明只读通道没给型号。公版接收器（248A/249A:5B2F/5C2F）没有 OUTPUT 集合，
        // 只能往 33 字节 Feature 里写一帧身份请求。写入仅限这个号段，且每帧都留在报告里。
        bool family = IsGenericReceiver33(vid, pid, null) || IsGenericReceiver33(vid, pid, "");
        if (best == null && allowWrite && family && features.Count > 0)
        {
            string path = features[0];
            IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h != IntPtr.Zero && h != new IntPtr(-1))
            {
                try
                {
                    var ids = FeatureReportIds(h, Caps(h));
                    for (int round = 0; round < 2 && best == null; round++)
                    {
                        byte head = round == 0 ? (byte)0x10 : (byte)0x30;
                        foreach (byte id in ids)
                        {
                            var tx = new byte[DumpFrame];
                            tx[0] = id;
                            tx[1] = head;
                            bool written = HidD_SetFeature(h, tx, tx.Length);
                            int win32 = written ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                            Event(written ? "TX" : "ERR", $"身份请求 {head:X2} id={id:X2}", tx,
                                Short(path) + (written ? "" : " Win32=" + win32));
                            text.AppendLine($"写入身份帧 0x{head:X2}/id=0x{id:X2}：{(written ? "已发送" : "发送失败 Win32=" + win32)}");
                            if (!written) continue;
                            System.Threading.Thread.Sleep(80);
                            for (int i = 0; i < 2; i++)
                            {
                                var rx = new byte[DumpFrame];
                                rx[0] = id;
                                bool ok = HidD_GetFeature(h, rx, rx.Length);
                                Event(ok ? "RX" : "ERR", $"身份应答 {head:X2}/id={id:X2}#{i + 1}", ok ? rx : null, Short(path));
                                if (ok && TryParseReceiver33Frame(rx, out int level, out bool charging, out string model, out bool aula))
                                {
                                    best = ReceiverDumpReading(level, charging, model, aula, $"身份请求 0x{head:X2}/id=0x{id:X2}");
                                    bestFrame = Convert.ToHexString(rx);
                                    break;
                                }
                            }
                            if (best != null) break;
                        }
                    }
                }
                finally { CloseHandle(h); }
            }
        }
        else if (best == null && family)
        {
            text.AppendLine(allowWrite
                ? "没有可写的 33 字节 Feature 集合，身份查询无法发出。"
                : "安全模式下不发身份查询帧，只保留只读结果。");
        }

        if (best != null)
        {
            text.AppendLine($"取证结论：{(best.Model ?? "未知型号")}，电量 {(best.Level >= 0 ? best.Level + "%" : "未取到")}，来源={best.Diagnostic}，帧={bestFrame}");
        }
        else
        {
            text.AppendLine("取证结论：没有解析出型号。这台接收器对被动上报、GET_FEATURE 和身份写入都没有给出可识别的 33 字节身份帧。");
            text.AppendLine("为什么没识别：现有协议按 VID/PID + 精确集合签名匹配，本机都未命中；报告里已列出缺哪一条。");
        }
        text.AppendLine();
    }

    private static string Short(string path)
    {
        int at = path.IndexOf("hid#", StringComparison.OrdinalIgnoreCase);
        return at >= 0 && at + 4 < path.Length ? path.Substring(at + 4) : path;
    }

    // Feature 报告编号不能默认当 0：248A:5B2F 那份报告里 GET_FEATURE(0) 直接失败。
    // 先按能力表取真实编号，取不到再把 0..5 依次试一遍，每次结果都留在报告里。
    private static List<byte> FeatureReportIds(IntPtr h, HIDP_CAPS caps)
    {
        var ids = new List<byte>();
        if (HidD_GetPreparsedData(h, out IntPtr prep))
        {
            try
            {
                if (TryGetReportId(prep, 2, caps.NumberFeatureValueCaps, caps.NumberFeatureButtonCaps, out byte id))
                    ids.Add(id);
            }
            finally { HidD_FreePreparsedData(prep); }
        }
        foreach (byte b in new byte[] { 0, 1, 2, 3, 4, 5 })
            if (!ids.Contains(b)) ids.Add(b);
        return ids;
    }

    private static ReferenceBatteryProtocol.Reading ReceiverDumpReading(int level, bool charging,
        string model, bool aula, string source) =>
        new(level, charging, true,
            charging ? ReferenceBatteryProtocol.State.Charging : ReferenceBatteryProtocol.State.Online,
            source, (aula ? (model == "M137" ? "狼蛛 SC580" : "狼蛛 " + model) : "英菲克 " + model), model);
}

