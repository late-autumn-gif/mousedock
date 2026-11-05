using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// 罗技 HID++ 2.0 读取。同一套请求走两条物理通道：
    /// ① feature report（HidD_SetFeature / HidD_GetFeature）；
    /// ② output/input report（WriteFile + 后台线程 ReadFile 限时）。
    /// 046D:C53F 这类接收器 FeatureReportByteLength 是 0，只能走第二条。
    /// </summary>
    internal static class LogitechHidpp
    {
        private const byte ReportShort = 0x10;
        private const byte ReportLong = 0x11;
        private const byte SwId = 0x0F;

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_RW = 1 | 2;
        private const uint OPEN_EXISTING = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct HidpCaps
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                          NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                          NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] private static extern bool HidD_SetFeature(IntPtr h, byte[] b, int n);
        [DllImport("hid.dll")] private static extern bool HidD_GetFeature(IntPtr h, byte[] b, int n);
        [DllImport("hid.dll")] private static extern bool HidD_GetInputReport(IntPtr h, byte[] b, int n);
        [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr prep);
        [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr prep);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr prep, ref HidpCaps caps);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec,
            uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr h, byte[] buf, int n, out int written, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr h, byte[] buf, int n, out int read, IntPtr ov);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

        internal struct LogiResult
        {
            public int Battery;
            public bool Charging;
            public string Name;
            public string BatteryNote;
        }

        private sealed class Link
        {
            public IntPtr Handle;
            public bool UseFeature;
            public int Size;

            public bool Send(byte[] buf)
            {
                if (UseFeature) return HidD_SetFeature(Handle, buf, buf.Length);
                return WriteFile(Handle, buf, buf.Length, out _, IntPtr.Zero);
            }

            public byte[] Receive(int timeoutMs)
            {
                if (UseFeature)
                {
                    var b = new byte[Size];
                    return HidD_GetFeature(Handle, b, b.Length) ? b : null;
                }
                return ReadSync(Handle, Size, timeoutMs);
            }
        }

        /// <summary>同步 ReadFile 放在后台线程里跑，Join 限时；超时返回 null，不卡界面。</summary>
        private static byte[] ReadSync(IntPtr h, int size, int timeoutMs)
        {
            byte[] result = null;
            var worker = new Thread(() =>
            {
                try
                {
                    var buf = new byte[size];
                    if (ReadFile(h, buf, size, out int got, IntPtr.Zero) && got > 0)
                    {
                        if (got < size) Array.Resize(ref buf, got);
                        result = buf;
                    }
                }
                catch { }
            });
            worker.IsBackground = true;
            worker.Start();
            if (!worker.Join(timeoutMs)) return null;
            return result;
        }

        /// <summary>打开一个罗技 HID 接口，读型号和电量。读不到返回 -1 / null。</summary>
        public static LogiResult Probe(string path)
        {
            var result = new LogiResult { Battery = -1, Name = null };
            IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_RW,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == IntPtr.Zero || h == new IntPtr(-1)) return result;

            try
            {
                var caps = new HidpCaps();
                if (HidD_GetPreparsedData(h, out IntPtr prep))
                {
                    HidP_GetCaps(prep, ref caps);
                    HidD_FreePreparsedData(prep);
                }

                // 只碰厂商集合里的 20 字节长报文接口，别把 33 个接口挨个打一遍
                if (caps.UsagePage != 0xFF00) return result;

                var link = new Link { Handle = h };
                if (caps.FeatureReportByteLength >= 20)
                {
                    link.UseFeature = true;
                    link.Size = caps.FeatureReportByteLength;
                }
                else if (caps.OutputReportByteLength >= 20)
                {
                    link.UseFeature = false;
                    link.Size = Math.Max(caps.InputReportByteLength, caps.OutputReportByteLength);
                }
                else if (caps.OutputReportByteLength >= 7)
                {
                    // 7 字节接口也是 HID++ 长报文（0x11），只是报文被截短。
                    // G304 的电量（feature 0x1000）就挂在这条通道上。
                    link.UseFeature = false;
                    link.Size = caps.OutputReportByteLength;
                }
                else
                {
                    return result;
                }

                foreach (byte idx in new byte[] { 0x01, 0x02, 0xFF })
                {
                    string name = ReadName(link, idx);
                    bool gpw = IsOriginalGpwName(name) || path.IndexOf("vid_046d&pid_c088", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool charging = ReadBattery(link, idx, out int level, out string note, gpw);
                    if (level >= 0)
                    {
                        result.Battery = level; result.Charging = charging; result.BatteryNote = note;
                        result.Name = name; break; // Battery and name always belong to the same receiver slot.
                    }
                    if (string.IsNullOrWhiteSpace(result.Name)) result.Name = name;
                }
                return result;
            }
            catch { return result; }
            finally { CloseHandle(h); }
        }

        /// <summary>
        /// HID++ 1.0 寄存器读电量。0x0D = Battery Level Status，0x07 = Battery Status，
        /// 响应第 4 个字节是百分比。
        /// </summary>
        private static int ReadBattery10(Link link, byte deviceIndex)
        {
            // 0x07 是老寄存器，0x0D 是较新的 Battery Level Status；返回 0 通常表示「未知」，
            // 换下一个试，别把 0% 当成电量。
            foreach (byte reg in new byte[] { 0x07, 0x0D })
            {
                var req = new byte[link.Size];
                req[0] = ReportShort;
                req[1] = deviceIndex;
                req[2] = reg;
                req[3] = 0x00;
                if (!link.Send(req)) continue;

                var r = AwaitShort(link, deviceIndex, reg);
                if (r == null || r.Length < 4) continue;

                int level = r[3];
                if (level > 0 && level <= 100) return level;
            }
            return -1;
        }

        private static byte[] AwaitShort(Link link, byte deviceIndex, byte register, int timeoutMs = 400)
        {
            // 7 字节接口上没有 feature 通道，用 HidD_GetInputReport 轮询最近一条输入报告，
            // 它是非阻塞的；用 ReadFile 会挂住界面线程。
            int deadline = Environment.TickCount + timeoutMs;
            while (Environment.TickCount < deadline)
            {
                var buf = new byte[link.Size];
                if (HidD_GetInputReport(link.Handle, buf, buf.Length))
                {
                    if (buf.Length >= 4 && buf[0] == ReportShort && buf[1] == deviceIndex && buf[2] == register)
                        return buf;
                }
                Thread.Sleep(20);
            }
            return null;
        }

        /// <summary>
        /// 读取设备百分比，或在已确认初代 GPW 时按官方电池模型推算。
        /// 电压模型推算结果单独标注，不把电压的高字节误当百分比。
        /// </summary>
        private static bool ReadBattery(Link link, byte deviceIndex, out int level, out string note, bool originalGpw)
        {
            level = -1;
            note = "";
            // 1004 function 0 is capabilities, function 1 is status.
            foreach (ushort feature in new ushort[] { 0x1004, 0x1000, 0x1001 })
            {
                byte fi = FeatureIndex(link, deviceIndex, feature);
                if (fi == 0) continue;
                var r = Call(link, deviceIndex, fi, feature == 0x1004 ? (byte)1 : (byte)0);
                if (TryParseBattery(feature, r, out level, out bool charging, originalGpw))
                {
                    note = feature == 0x1001 ? "Logitech GPW 官方电压模型推算（非设备直接百分比）" : "Logitech HID++ 设备电量";
                    return charging;
                }
            }
            return false;
        }

        internal static bool TryParseBattery(ushort feature, ReadOnlySpan<byte> r, out int level, out bool charging, bool originalGpw = false)
        {
            level = -1; charging = false;
            if (r.Length < 7 || r[0] is not (ReportLong or ReportShort)) return false;
            if (feature == 0x1004 || feature == 0x1000)
            {
                if (r[4] > 100 || r[6] > 7) return false;
                // BatteryStatus encodes 0 as unknown; Unified encodes 0 plus empty level as drained.
                if (r[4] == 0 && (feature == 0x1000 || r[5] != 0)) return false;
                level = r[4]; charging = r[6] is 1 or 2; return true;
            }
            if (feature != 0x1001) return false;
            int millivolts = (r[4] << 8) | r[5]; // documented big-endian voltage, not a percentage byte
            if ((r[6] & 0x80) != 0)
            {
                int status = r[6] & 7;
                if (status > 2) return false;
                if (status == 1) { if (!originalGpw) return false; level = 100; return true; }
                charging = status == 0;
            }
            return originalGpw && LogitechGpwBattery.TryEstimateGpwPercentage(millivolts, charging, out level);
        }

        private static string ReadName(Link link, byte deviceIndex)
        {
            byte fi = FeatureIndex(link, deviceIndex, 0x0005);
            if (fi == 0) return null;

            var head = Call(link, deviceIndex, fi, 0x00);   // function 0 = count
            if (head == null) return null;
            int total = head[4];
            if (total <= 0) return null;
            if (total > 64) total = 64;

            var bytes = new byte[total];
            int filled = 0;
            for (int offset = 0; offset < total;)
            {
                var req = new byte[link.Size];
                req[0] = link.Size >= 20 ? ReportLong : ReportShort;
                req[1] = deviceIndex;
                req[2] = fi;
                req[3] = (byte)((0x01 << 4) | SwId);   // function 1 = name chunk
                req[4] = (byte)offset;
                if (!link.Send(req)) break;

                var r = Await(link, deviceIndex, fi, 0x01);
                if (r == null) break;

                int n = Math.Min(Math.Min(16, total - offset), r.Length - 4);
                if (n <= 0) break;
                for (int i = 0; i < n && filled < total; i++) bytes[filled++] = r[4 + i];
                offset += n;
            }

            int end = filled;
            while (end > 0 && bytes[end - 1] == 0) end--;
            if (end == 0) return null;

            string text = Encoding.UTF8.GetString(bytes, 0, end).Trim('\0', ' ');
            return text.Length > 0 ? text : null;
        }

        /// <summary>
        /// 发完请求后等一条匹配的响应。HID 输入队列里可能还压着上一次的超时残包，
        /// 不按 report id / 设备索引 / feature 索引过滤就会把电量当成长度读，名字变乱码。
        /// </summary>
        private static byte[] Await(Link link, byte deviceIndex, byte featureIndex, byte function, int timeoutMs = 400)
        {
            int deadline = Environment.TickCount + timeoutMs;
            byte[] loose = null;
            while (Environment.TickCount < deadline)
            {
                var r = link.Receive(120);
                if (r == null) continue;
                if (r.Length < 7) continue;
                if (r[0] is not (ReportLong or ReportShort) || r[1] != deviceIndex || r[2] != featureIndex) continue;

                byte head = r[3];
                // HID++ 的 swId 在低 4 位、function 在高 4 位。优先要回显请求 swId 的帧，
                // 但部分接收器不回显；没有严格匹配时，超时前退回只匹配 function 的旧行为。
                byte fn = (byte)((head >> 4) & 0x0F);
                if (fn != function) continue;
                if ((head & 0x0F) == SwId) return r;
                if (loose == null) loose = r;
            }
            return loose;
        }

        private static byte FeatureIndex(Link link, byte deviceIndex, ushort featureId)
        {
            var req = new byte[link.Size];
            req[0] = link.Size >= 20 ? ReportLong : ReportShort;
            req[1] = deviceIndex;
            req[2] = 0x00;                                  // IRoot
            req[3] = (byte)((0x00 << 4) | SwId);            // getFeature
            req[4] = (byte)(featureId >> 8);
            req[5] = (byte)(featureId & 0xFF);
            if (!link.Send(req)) return 0;

            var r = Await(link, deviceIndex, 0x00, 0x00);
            if (r == null) return 0;

            byte index = r[4];
            return index == 0x00 || index == 0xFF ? (byte)0 : index;
        }

        private static byte[] Call(Link link, byte deviceIndex, byte featureIndex, byte function)
        {
            var req = new byte[link.Size];
            req[0] = link.Size >= 20 ? ReportLong : ReportShort;
            req[1] = deviceIndex;
            req[2] = featureIndex;
            req[3] = (byte)((function << 4) | SwId);
            if (!link.Send(req)) return null;
            return Await(link, deviceIndex, featureIndex, function);
        }

        internal static bool IsOriginalGpwName(string name)
        {
            string clean = System.Text.RegularExpressions.Regex.Replace(name ?? "", "[^A-Za-z0-9]", "").ToUpperInvariant();
            return clean is "GPROWIRELESS" or "GPROWIRELESSGAMINGMOUSE" or "PROWIRELESSGAMINGMOUSE"
                or "LOGITECHGPROWIRELESS" or "LOGITECHGPROWIRELESSGAMINGMOUSE";
        }
    }
}
