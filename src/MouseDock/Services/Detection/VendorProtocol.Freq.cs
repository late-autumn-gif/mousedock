using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// 前行者（EWEADN）/ FREQ 方案鼠标的 2.4G 电量查询。
    ///
    /// 证据链（都不需要真机就能核对）：
    /// - 参考实现 `wireless_x23_family.py`（另一作者 MouseDock 1.0.2 的 FREQ 传输）：
    ///   `_freq_read` 发 0x30 电量命令，`freq_decode` 校验方案 a/c 的应答；
    /// - 官方 E HUB 目录 + FREQ SDK 把 A8A4/A8A5:2255 的 X23 系列标成 `x23_freq`，
    ///   页面 FF01/0010、接口 2，产品串必须精确匹配；
    /// - 参考设备表里 A8A5:2255 / “EWEADN X23SE”是 `freq_scheme = dual`：
    ///   64/64 的 F0 描述符先用 0x52 读一代信息决定 a/c，其它尺寸按 a 方案。
    ///
    /// 用户报告（2026-11-05 09:23）里 A8A5:2255 EWEADN X23SE 的 FF01/0010 是 65/65，
    /// 说明输出报告带 1 字节编号、载荷 64 字节。这里从 HID 描述符（HidP_GetValueCaps）
    /// 读真实报告编号，再按参考实现组帧；不猜编号、不猜字节。
    /// </summary>
    internal static partial class VendorProtocol
    {
        // HIDP_VALUE_CAPS 在 x64 上是 72 字节；只用第 2 字节的 ReportID 和开头的 UsagePage。
        private const int HidpValueCapsStride = 72;
        private const int HidpValueCapsReportIdOffset = 2;
        private const byte FreqBatteryCommand = 0x30;

        [DllImport("hid.dll")] private static extern int HidP_GetValueCaps(int reportType, byte[] caps, ref ushort length, IntPtr prep);
        [DllImport("hid.dll")] private static extern int HidP_GetButtonCaps(int reportType, byte[] caps, ref ushort length, IntPtr prep);

        /// <summary>参考设备表里的 freq_scheme，键是 USB 产品串（大小写不敏感）。</summary>
        private static readonly Dictionary<string, string> FreqSchemes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["EWEADN X23"] = "a",
                ["EWEADN X23Max"] = "a",
                ["EWEADN X23SE"] = "dual",
                ["EWEADN X23Pro"] = "c",
                ["EWEADN X23 carbon"] = "dual",
                ["EWEADN X23L carbon"] = "a"
            };

        internal static bool IsFreqMouse(ushort vid, ushort pid, string product) =>
            (vid == 0xA8A4 || vid == 0xA8A5) && pid == 0x2255 &&
            FreqSchemeFor(product) != null;

        internal static string FreqSchemeFor(string product)
        {
            if (string.IsNullOrWhiteSpace(product)) return null;
            string key = NormalizeProduct(product);
            return FreqSchemes.TryGetValue(key, out string scheme) ? scheme : null;
        }

        private static string NormalizeProduct(string product)
        {
            var sb = new StringBuilder(product.Length);
            bool space = false;
            foreach (char c in product.Trim())
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!space && sb.Length > 0) sb.Append(' ');
                    space = true;
                    continue;
                }
                sb.Append(c);
                space = false;
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>参考实现的方案 a 请求头（连同报告编号一起写入）。</summary>
        internal static byte[] BuildFreqSchemeAPacket(byte outputReportId, int outputSize)
        {
            if (outputReportId == 0 || outputSize < 11 || outputSize > 65) return null;
            var packet = new byte[outputSize];
            packet[0] = outputReportId;
            new byte[] { 0x55, 0x30, 0x01, 0x0B, 0x2E, 0x01, 0x01, 0x01, 0x00, 0x00 }
                .CopyTo(packet, 1);
            return packet;
        }

        /// <summary>参考实现的方案 c 请求：报告编号 F0 + 30 A5 0B 2E 01 01 01，补零到 64。</summary>
        internal static byte[] BuildFreqSchemeCPacket()
        {
            var packet = new byte[64];
            new byte[] { 0xF0, 0x30, 0xA5, 0x0B, 0x2E, 0x01, 0x01, 0x01, 0x00, 0x00 }.CopyTo(packet, 0);
            return packet;
        }

        /// <summary>参考实现的 0x52 一代信息查询（只用于 64/64 的 F0 布局）。</summary>
        internal static byte[] BuildFreqGenerationPacket()
        {
            var packet = new byte[64];
            packet[0] = 0xF0;
            packet[1] = 0x52;
            return packet;
        }

        /// <summary>应答是否是电量帧：方案 c 看 body[0]==0x30；方案 a 看 body[0..1] 里出现 AA 30 或 F0 30。</summary>
        internal static bool IsFreqBatteryFrame(ReadOnlySpan<byte> raw, string scheme, byte inputReportId)
        {
            int at = inputReportId != 0 ? 1 : 0;
            if (scheme is not ("a" or "c") || raw.Length <= at || (inputReportId != 0 && raw[0] != inputReportId)) return false;
            var body = raw.Slice(at);
            if (scheme == "c") return body[0] == FreqBatteryCommand;
            for (int i = 0; i <= 1 && i + 1 < body.Length; i++)
            {
                if ((body[i] == 0xAA || body[i] == 0xF0) && body[i + 1] == FreqBatteryCommand) return true;
            }
            return false;
        }

        /// <summary>严格解析参考实现的 freq_decode：长度、报告编号、命令标记、电量/充电范围。</summary>
        internal static bool TryParseFreqFrame(ReadOnlySpan<byte> raw, string scheme, byte inputReportId, int inputSize,
            out int level, out bool charging)
        {
            level = -1;
            charging = false;
            if (scheme is not ("a" or "c") || inputSize <= 0 || raw.IsEmpty || raw.Length != inputSize) return false;
            if (inputReportId != 0 && raw[0] != inputReportId) return false;
            var body = inputReportId != 0 ? raw.Slice(1) : raw;

            int percent;
            int charge;
            if (scheme == "c")
            {
                if (inputReportId != 0xF0 || body.Length < 9 || body[0] != FreqBatteryCommand) return false;
                percent = body[7];
                charge = body[8];
            }
            else
            {
                int found = -1;
                for (int i = 0; i <= 1; i++)
                {
                    if (body.Length < i + 10) continue;
                    if ((body[i] == 0xAA || body[i] == 0xF0) && body[i + 1] == FreqBatteryCommand)
                    {
                        if (found >= 0) return false; // 两个偏移都像标记：歧义，拒绝
                        found = i;
                    }
                }
                if (found < 0) return false;
                percent = body[found + 8];
                charge = body[found + 9];
            }

            if (percent < 0 || percent > 100 || charge is not (0 or 1)) return false;
            level = percent;
            charging = charge == 1;
            return true;
        }

        /// <summary>合并 HID 值/按钮能力表的报告编号；每个方向必须只有一个，否则不猜。</summary>
        internal static bool TrySingleFreqReportId(byte[] values, int valueCount, byte[] buttons, int buttonCount, out byte reportId)
        {
            reportId = 0;
            var ids = GetDescriptorReportIds(values, valueCount, buttons, buttonCount);
            if (ids.Count != 1) return false;
            foreach (byte id in ids) reportId = id;
            return true;
        }

        internal static HashSet<byte> GetDescriptorReportIds(byte[] values, int valueCount, byte[] buttons, int buttonCount)
        {
            var ids = new HashSet<byte>();
            if (valueCount < 0 || buttonCount < 0 || valueCount + buttonCount == 0 ||
                valueCount > (values?.Length ?? 0) / HidpValueCapsStride ||
                buttonCount > (buttons?.Length ?? 0) / HidpValueCapsStride) return ids;
            for (int i = 0; i < valueCount; i++) ids.Add(values[i * HidpValueCapsStride + HidpValueCapsReportIdOffset]);
            for (int i = 0; i < buttonCount; i++) ids.Add(buttons[i * HidpValueCapsStride + HidpValueCapsReportIdOffset]);
            return ids;
        }

        private static bool TryGetReportId(IntPtr prep, int reportType, ushort valueDeclared, ushort buttonDeclared, out byte reportId)
        {
            reportId = 0;
            var ids = ReadDescriptorReportIds(prep, reportType, valueDeclared, buttonDeclared);
            if (ids.Count != 1) return false;
            foreach (byte id in ids) reportId = id;
            return true;
        }

        internal static HashSet<byte> ReadDescriptorReportIds(IntPtr prep, int reportType, ushort valueDeclared, ushort buttonDeclared)
        {
            if (prep == IntPtr.Zero || valueDeclared + buttonDeclared == 0) return new HashSet<byte>();
            byte[] ReadCaps(bool buttons, ushort declared, out int count)
            {
                count = 0;
                if (declared == 0) return Array.Empty<byte>();
                ushort length = declared;
                var buffer = new byte[declared * HidpValueCapsStride];
                int status = buttons ? HidP_GetButtonCaps(reportType, buffer, ref length, prep)
                    : HidP_GetValueCaps(reportType, buffer, ref length, prep);
                if (status != 0x110000 || length == 0 || length > declared) return null;
                count = length;
                return buffer;
            }
            var values = ReadCaps(false, valueDeclared, out int valueCount);
            var buttons = ReadCaps(true, buttonDeclared, out int buttonCount);
            return values != null && buttons != null ? GetDescriptorReportIds(values, valueCount, buttons, buttonCount) : new HashSet<byte>();
        }

        /// <summary>
        /// 读一次前行者 FREQ 鼠标电量。返回 -1 表示读不到；diagnostic 给反馈报告看。
        /// 只发参考实现里的 0x30 电量查询和 0x52 一代信息查询（都是只读）。
        /// </summary>
        public static int TryReadFreqMouse(ushort vid, ushort pid, string product, out bool charging)
            => TryReadFreqMouse(vid, pid, product, out charging, out _);

        public static int TryReadFreqMouse(ushort vid, ushort pid, string product, out bool charging, out string diagnostic)
        {
            charging = false;
            string configured = FreqSchemeFor(product);
            if (!IsFreqMouse(vid, pid, product) || configured == null) { diagnostic = "不是 FREQ 白名单里的型号"; return -1; }

            string last = null;
            foreach (string path in Paths(vid, pid))
            {
                IntPtr h = OpenOverlapped(path, out IntPtr signal);
                if (h == IntPtr.Zero) continue;
                try
                {
                    var caps = Caps(h);
                    if (caps.UsagePage != 0xFF01 || caps.Usage != 0x0010) continue;
                    int inputSize = caps.InputReportByteLength;
                    int outputSize = caps.OutputReportByteLength;
                    if (inputSize is < 10 or > 65 || outputSize is < 10 or > 65) { last = "接口尺寸不符"; continue; }

                    if (!HidD_GetPreparsedData(h, out IntPtr prep)) { last = "预解析失败"; continue; }
                    byte inputId, outputId;
                    try
                    {
                        bool okInput = TryGetReportId(prep, 0, caps.NumberInputValueCaps, caps.NumberInputButtonCaps, out inputId);
                        bool okOutput = TryGetReportId(prep, 1, caps.NumberOutputValueCaps, caps.NumberOutputButtonCaps, out outputId);
                        if (!okInput || !okOutput)
                        {
                            last = $"报告编号不唯一（in={caps.NumberInputValueCaps} out={caps.NumberOutputValueCaps}）";
                            continue;
                        }
                    }
                    finally { HidD_FreePreparsedData(prep); }

                    // 参考实现的 c_layout：输入输出都是报告 F0、都是 64 字节。
                    bool cLayout = inputId == 0xF0 && outputId == 0xF0 && inputSize == 64 && outputSize == 64;
                    string scheme = configured;
                    if (scheme == "dual")
                    {
                        scheme = "a";
                        if (cLayout)
                        {
                            if (TryGenerationProbe(h, signal, inputSize, out bool printable, out string probeDiag))
                            {
                                if (printable) scheme = "c";
                            }
                            else last = probeDiag;
                        }
                    }
                    if (scheme == "c" && !cLayout) { last = "方案 c 需要 F0/64 接口"; continue; }

                    byte[] packet = scheme == "c"
                        ? BuildFreqSchemeCPacket()
                        : BuildFreqSchemeAPacket(outputId, outputSize);
                    if (packet == null) { last = "报告编号无法组帧"; continue; }
                    if (packet.Length != outputSize) { last = "请求长度与描述符不符"; continue; }

                    int level = -1;
                    bool charge = false;
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        Drain(h, signal, inputSize);
                        if (!Transfer(h, signal, packet, true, 300, out _)) { last = "电量查询发送失败"; level = -1; break; }
                        byte[] raw = WaitFreqFrame(h, signal, inputSize, scheme, inputId, 1200);
                        if (raw == null) { last = "未收到电量应答"; level = -1; break; }
                        if (raw.AsSpan().SequenceEqual(packet) ||
                            (inputId == 0 && raw.Length == packet.Length - 1 && raw.AsSpan().SequenceEqual(packet.AsSpan(1))))
                        {
                            last = "收到的是请求回显";
                            level = -1;
                            break;
                        }
                        if (!TryParseFreqFrame(raw, scheme, inputId, inputSize, out level, out charge))
                        {
                            last = "电量帧校验失败";
                            level = -1;
                            break;
                        }
                    }

                    if (level >= 0)
                    {
                        charging = charge;
                        diagnostic = $"FREQ 方案{scheme}（in=0x{inputId:X2}/{inputSize} out=0x{outputId:X2}/{outputSize}）";
                        return level;
                    }
                }
                catch (Exception ex)
                {
                    last = "异常 " + ex.GetType().Name;
                }
                finally
                {
                    if (signal != IntPtr.Zero) CloseHandle(signal);
                    CloseHandle(h);
                }
            }

            diagnostic = string.IsNullOrEmpty(last) ? "未找到 FF01/0010 接口" : last;
            return -1;
        }

        /// <summary>0x52 一代信息查询：应答第 10 字节起是可打印型号串就按方案 c 走。</summary>
        private static bool TryGenerationProbe(IntPtr h, IntPtr signal, int inputSize, out bool printable, out string diagnostic)
        {
            printable = false;
            diagnostic = null;
            Drain(h, signal, inputSize);
            if (!Transfer(h, signal, BuildFreqGenerationPacket(), true, 300, out _))
            {
                diagnostic = "0x52 一代信息查询发送失败";
                return false;
            }
            int deadline = Environment.TickCount + 600;
            while (deadline - Environment.TickCount > 0)
            {
                var raw = ReadReport(h, signal, Math.Max(1, Math.Min(200, deadline - Environment.TickCount)));
                if (raw == null) continue;
                if (raw.Length != 64 || raw[0] != 0xF0 || raw[1] != 0x52) continue;
                if (raw[9] == 0) return true; // 空串：按方案 a
                int end = 9;
                while (end < raw.Length && end < 33 && raw[end] != 0) end++;
                if (end == 9) return true;
                for (int i = 9; i < end; i++)
                {
                    if (raw[i] < 32 || raw[i] > 126) return true;
                }
                printable = true;
                return true;
            }
            diagnostic = "0x52 一代信息查询无应答";
            return false;
        }

        private static byte[] WaitFreqFrame(IntPtr h, IntPtr signal, int size, string scheme, byte inputId, int timeoutMs)
        {
            int deadline = Environment.TickCount + timeoutMs;
            while (deadline - Environment.TickCount > 0)
            {
                var raw = ReadReport(h, signal, Math.Max(1, Math.Min(200, deadline - Environment.TickCount)));
                if (raw == null) continue;
                if (raw.Length != size) continue;
                if (!IsFreqBatteryFrame(raw, scheme, inputId)) continue;
                return raw;
            }
            return null;
        }

        /// <summary>丢弃上一次查询残留的应答，避免把旧帧当新读数。</summary>
        private static void Drain(IntPtr h, IntPtr signal, int size)
        {
            for (int i = 0; i < 8; i++)
            {
                var raw = ReadReport(h, signal, 20);
                if (raw == null) return;
            }
        }
    }
}
