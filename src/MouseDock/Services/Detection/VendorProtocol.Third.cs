using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MouseDock.Services.Detection
{
    internal static partial class VendorProtocol
    {
        /// <summary>
        /// 第三批：把 battery_vendors 表里剩下的读取器补齐。
        /// 被动型（SC900、VT2 MAX）不发帧，只在输入报告里等。
        /// </summary>
        public static int TryReadThird(ushort vid, ushort pid)
        {
            switch (vid)
            {
                case 0x33E4:
                case 0x3854: return ReadGwolves(vid, pid);
                case 0x372E: return ReadIpi(vid, pid);
                case 0x1915: return ReadRawm(vid, pid);
            }
            return -1;
        }

        /// <summary>按接口形状认的两家：WLMouse Beast X（report4 + CRC16）和 Angry Miao（report F7）。</summary>
        public static int TryReadLegacyShape(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = OpenOverlapped(path, out IntPtr ev);
                if (h == IntPtr.Zero) continue;
                try
                {
                    var caps = Caps(h);
                    if (caps.OutputReportByteLength != 64) continue;

                    int pct = ReadAngryMiao(h, ev);
                    if (pct >= 0) return pct;

                    pct = ReadWlmouse(h, ev);
                    if (pct >= 0) return pct;
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>CRC16/MODBUS：poly 0xA001，init 0xFFFF，覆盖 packet[3:32]。</summary>
        private static ushort Crc16Modbus(byte[] data, int offset, int count)
        {
            ushort crc = 0xFFFF;
            for (int i = offset; i < offset + count && i < data.Length; i++)
            {
                crc ^= (ushort)data[i];
                for (int b = 0; b < 8; b++)
                    crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
            return crc;
        }

        /// <summary>WLMouse Beast X：64 字节 report 4，b[1:3] 是 CRC16 小端，电量 b[8]。</summary>
        private static int ReadWlmouse(IntPtr h, IntPtr ev)
        {
            var req = new byte[64];
            req[0] = 0x04;
            req[3] = 0x1A;
            req[4] = 0x06;
            ushort crc = Crc16Modbus(req, 3, 29);
            req[1] = (byte)(crc & 0xFF);
            req[2] = (byte)(crc >> 8);

            var resp = Transact(h, ev, req, 600);
            if (resp == null || resp.Length < 64) return -1;
            if (resp[0] != 0x04 || resp[3] != 0x1A || resp[4] != 0x06) return -1;
            if (resp[1] == req[1] && resp[2] == req[2] && resp[8] == 0) return -1;  // 回显

            int pct = resp[8];
            return pct >= 1 && pct <= 100 ? pct : -1;
        }

        /// <summary>Angry Miao：report 0xF7，b[0]==0xF7 → 电量 b[2]；b[0]==0 且 b[1]==0xF7 → b[3]。</summary>
        private static int ReadAngryMiao(IntPtr h, IntPtr ev)
        {
            var req = new byte[64];
            req[0] = 0xF7;

            var resp = Transact(h, ev, req, 300);
            if (resp == null || resp.Length < 4) return -1;

            int pct = -1;
            if (resp[0] == 0xF7) pct = resp[2];
            else if (resp[0] == 0x00 && resp[1] == 0xF7) pct = resp[3];

            return pct >= 1 && pct <= 100 ? pct : -1;
        }

        /// <summary>G-Wolves：两代命令，Feature 65 字节，响应剥首字节后校验。</summary>
        private static int ReadGwolves(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;
                try
                {
                    if (Caps(h).FeatureReportByteLength != 65) continue;

                    // 新代：00 00 00 02 02 00 83
                    var req = new byte[65];
                    req[3] = 0x02;
                    req[4] = 0x02;
                    req[6] = 0x83;
                    int pct = FeaturePct(h, req, 7, 6);
                    if (pct >= 0) return pct;

                    // 老代：00 00 02 8F 01
                    var old = new byte[65];
                    old[2] = 0x02;
                    old[3] = 0x8F;
                    old[4] = 0x01;
                    pct = FeaturePct(h, old, 5, 4);
                    if (pct >= 0) return pct;
                }
                catch { }
                finally { CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>发 feature、剥首字节、按 b[pctAt] 取电量，b[chargeAt] 须为 0/1。</summary>
        private static int FeaturePct(IntPtr h, byte[] req, int pctAt, int chargeAt)
        {
            if (!HidD_SetFeature(h, req, req.Length)) return -1;
            var raw = new byte[req.Length];
            if (!HidD_GetFeature(h, raw, raw.Length)) return -1;
            if (raw[0] != 0x00) return -1;

            var body = new byte[raw.Length - 1];
            Array.Copy(raw, 1, body, 0, body.Length);
            if (body[0] != 0xA1) return -1;
            if (body[pctAt] > 100) return -1;
            if (body[chargeAt] > 1) return -1;

            int pct = body[pctAt];
            return pct >= 1 && pct <= 100 ? pct : -1;
        }

        /// <summary>ipi_pix 家族：64 字节 Output 0x03，b[1] 是 (255-sum) 校验。</summary>
        private static int ReadIpi(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = OpenOverlapped(path, out IntPtr ev);
                if (h == IntPtr.Zero) continue;
                try
                {
                    if (Caps(h).OutputReportByteLength != 64) continue;

                    var req = new byte[64];
                    req[0] = 0x03;
                    req[2] = 0x50;
                    req[4] = 0x02;
                    req[5] = 0x4F;
                    req[6] = 0x81;
                    int s = 0;
                    for (int i = 2; i < 64; i++) s += req[i];
                    req[1] = (byte)((255 - s % 256) & 0xFF);

                    var resp = Transact(h, ev, req, 600);
                    if (resp == null || resp.Length < 5) continue;
                    if (resp[0] != 0x03 || resp[4] != 0x02) continue;
                    // 电量偏移未确认，只认帧头，不猜数
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>
        /// RAWM：65 字节命令，响应是 JSON 文本，按 8 片读、拼起来找 "battery"。
        /// 每片前 4 字节是 FF FF FF FF + 长度头，跳过。
        /// </summary>
        private static int ReadRawm(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = OpenOverlapped(path, out IntPtr ev);
                if (h == IntPtr.Zero) continue;
                try
                {
                    int outLen = Caps(h).OutputReportByteLength;
                    if (outLen < 64) continue;

                    var req = new byte[outLen];
                    req[0] = 0x00;
                    req[2] = 0x8D;
                    req[3] = 0x01;
                    req[4] = 0x0D;
                    req[5] = 0x03;
                    long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    for (int i = 0; i < 8; i++) req[8 + i] = (byte)(ts >> (8 * i));

                    if (!WriteFrame(h, ev, req)) continue;

                    var sb = new StringBuilder();
                    for (int i = 0; i < 10; i++)
                    {
                        var raw = ReadReport(h, ev, 400);
                        if (raw == null || raw.Length == 0) break;

                        int start = 0;
                        if (raw.Length >= 6 && raw[0] == 0xFF && raw[1] == 0xFF) start = 6;
                        sb.Append(Encoding.UTF8.GetString(raw, start, raw.Length - start));
                        if (sb.ToString().IndexOf('}') >= 0) break;
                    }

                    int pct = ParseBatteryJson(sb.ToString());
                    if (pct >= 0) return pct;
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>从 JSON 文本里抠 "battery" 后面的整数，1..100 才算。</summary>
        private static int ParseBatteryJson(string text)
        {
            if (string.IsNullOrEmpty(text)) return -1;
            int at = text.IndexOf("\"battery\"", StringComparison.Ordinal);
            if (at < 0) at = text.IndexOf("battery", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return -1;

            int i = at + 7;
            while (i < text.Length && (text[i] == '"' || text[i] == ':' || text[i] == ' ' || text[i] == '=')) i++;
            int startNum = i;
            while (i < text.Length && char.IsDigit(text[i])) i++;
            if (i == startNum) return -1;

            if (!int.TryParse(text.Substring(startNum, i - startNum), out int pct)) return -1;
            return pct >= 1 && pct <= 100 ? pct : -1;
        }

        /// <summary>VGN ldMs（Falcon 3 那套）：report 0xA0，33/64 字节，b[3] 电量。</summary>
        private static int ReadVgnLd(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = OpenOverlapped(path, out IntPtr ev);
                if (h == IntPtr.Zero) continue;
                try
                {
                    int outLen = Caps(h).OutputReportByteLength;
                    if (outLen != 33 && outLen != 64) continue;

                    var req = new byte[outLen];
                    req[0] = 0xA0;
                    req[1] = 0x02;
                    req[2] = 0x25;

                    var resp = Transact(h, ev, req, 600);
                    if (resp == null || resp.Length < 4) continue;
                    if (resp[0] != 0xA0 || resp[1] != 0x02 || resp[2] != 0x25) continue;

                    int pct = resp[3];
                    if (pct >= 1 && pct <= 100) return pct;
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>AULA SC900：完全被动，只等 report 0x09（33 字节），raw[1] 低 7 位是电量。</summary>
        private static int ReadAulaSc900(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = OpenOverlapped(path, out IntPtr ev);
                if (h == IntPtr.Zero) continue;
                try
                {
                    if (Caps(h).InputReportByteLength != 33) continue;

                    for (int i = 0; i < 3; i++)
                    {
                        var raw = ReadReport(h, ev, 500);
                        if (raw == null || raw.Length < 2) continue;
                        if (raw[0] != 0x09) continue;

                        int pct = raw[1] & 0x7F;
                        if (pct >= 1 && pct <= 100) return pct;
                    }
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>雷柏 VT2 MAX：被动 report 0x07（13 字节），b[8] 电量、b[7] 状态 0/1/2。</summary>
        private static int ReadRapooVt2(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = OpenOverlapped(path, out IntPtr ev);
                if (h == IntPtr.Zero) continue;
                try
                {
                    if (Caps(h).InputReportByteLength != 13) continue;

                    for (int i = 0; i < 3; i++)
                    {
                        var raw = ReadReport(h, ev, 500);
                        if (raw == null || raw.Length < 9) continue;
                        if (raw[0] != 0x07) continue;
                        if (raw[7] > 2) continue;

                        int pct = raw[8];
                        if (pct >= 1 && pct <= 100) return pct;
                    }
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>只写不读，给分片协议用。</summary>
        private static bool WriteFrame(IntPtr h, IntPtr ev, byte[] frame)
        {
            var ov = new NativeOverlapped { EventHandle = ev };
            if (!WriteFile(h, frame, frame.Length, IntPtr.Zero, ref ov) &&
                Marshal.GetLastWin32Error() != 997) return false;
            WaitForSingleObject(ev, 300);
            GetOverlappedResult(h, ref ov, out _, false);
            ResetEvent(ev);
            return true;
        }
    }
}
