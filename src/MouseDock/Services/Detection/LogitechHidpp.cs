using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// 罗技 HID++ 2.0 读取。同一套请求走两条物理通道：
    /// ① feature report（HidD_SetFeature / HidD_GetFeature）；
    /// ② output/input report（WriteFile + ReadFile，OVERLAPPED 限时）。
    /// 046D:C53F 这类接收器 FeatureReportByteLength 是 0，只能走第二条。
    ///
    /// 1.4.3 的反馈报告里整轮 HID 扫描停在「接口=… 归并=4」之后不再往下走，
    /// 蓝牙扫描一次都没启动——设备列表因此永远是空的。那条路只有这里会做
    /// 同步 HID 写入；无线接收器休眠时同步 WriteFile 没有超时，会一直等下去。
    /// 现在所有读写都带超时，单个接收器还有总预算，读不到就把名字/电量留空。
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
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        private const int WriteTimeoutMs = 250;
        private const int FeatureTimeoutMs = 350;

        /// <summary>单个接收器最多花这么久探测，超时就带空结果返回，别拖住整轮扫描。</summary>
        internal const int ProbeBudgetMs = 2500;

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
        [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr prep);
        [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr prep);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr prep, ref HidpCaps caps);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec,
            uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateEvent(IntPtr attrs, bool manualReset, bool initialState, IntPtr name);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

        /// <summary>类而不是结构体：Probe 的 finally 里还要补诊断字段，结构体的返回值在 return 时已经复制走了。</summary>
        internal sealed class LogiResult
        {
            public int Battery;
            public bool Charging;
            public string Name;
            public string BatteryNote;
            /// <summary>给反馈报告看的：走了哪条通道、哪个槽、花了多久、为什么没读到。</summary>
            public string Diagnostic;
        }

        private sealed class Link : IDisposable
        {
            public IntPtr Handle;
            public IntPtr Event;
            public bool UseFeature;
            public int Size;
            /// <summary>feature 通道一旦超时就别再碰它，避免每次扫描都挂一批线程。</summary>
            public bool Faulted;

            public bool Send(byte[] buf)
            {
                if (Faulted) return false;
                if (!UseFeature)
                {
                    // 复用厂商读取器那套原生内存 + OVERLAPPED 的实现：
                    // 超时后它会 CancelIoEx 并等驱动真正结束，缓冲区不会提前释放。
                    return VendorProtocol.Transfer(Handle, Event, buf, write: true, WriteTimeoutMs, out _);
                }
                bool ok = false;
                if (!RunBounded(() => { ok = HidD_SetFeature(Handle, buf, buf.Length); return true; }, FeatureTimeoutMs))
                {
                    Faulted = true;
                    return false;
                }
                return ok;
            }

            public byte[] Receive(int timeoutMs)
            {
                if (Faulted) return null;
                if (!UseFeature)
                {
                    var buf = new byte[Size];
                    if (!VendorProtocol.Transfer(Handle, Event, buf, write: false, timeoutMs, out int count) || count <= 0)
                        return null;
                    if (count == Size) return buf;
                    var slice = new byte[count];
                    Array.Copy(buf, slice, count);
                    return slice;
                }
                var featureBuf = new byte[Size];
                byte[] result = null;
                if (!RunBounded(() => { if (HidD_GetFeature(Handle, featureBuf, featureBuf.Length)) result = featureBuf; return true; }, FeatureTimeoutMs))
                {
                    Faulted = true;
                    return null;
                }
                return result;
            }

            public void Dispose()
            {
                if (Event != IntPtr.Zero) { CloseHandle(Event); Event = IntPtr.Zero; }
                if (Handle != IntPtr.Zero && Handle != new IntPtr(-1)) { CloseHandle(Handle); Handle = IntPtr.Zero; }
            }
        }

        /// <summary>HidD_SetFeature/GetFeature 没有超时参数，放后台线程里等，超时就丢掉这条通道。</summary>
        private static bool RunBounded(Func<bool> action, int timeoutMs)
        {
            var worker = new Thread(() => { try { action(); } catch { } }) { IsBackground = true };
            worker.Start();
            return worker.Join(timeoutMs);
        }

        /// <summary>打开一个罗技 HID 接口，读型号和电量。读不到返回 -1 / null。</summary>
        public static LogiResult Probe(string path)
        {
            var result = new LogiResult { Battery = -1, Name = null };
            int started = Environment.TickCount;
            var diag = new StringBuilder();

            IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_RW,
                IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (h == IntPtr.Zero || h == new IntPtr(-1))
            {
                result.Diagnostic = "打开失败 Win32=" + Marshal.GetLastWin32Error();
                return result;
            }
            IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
            if (ev == IntPtr.Zero)
            {
                CloseHandle(h);
                result.Diagnostic = "创建事件失败 Win32=" + Marshal.GetLastWin32Error();
                return result;
            }

            var link = new Link { Handle = h, Event = ev };
            try
            {
                var caps = new HidpCaps();
                if (HidD_GetPreparsedData(h, out IntPtr prep))
                {
                    HidP_GetCaps(prep, ref caps);
                    HidD_FreePreparsedData(prep);
                }

                // 只碰厂商集合里的长报文接口，别把 33 个接口挨个打一遍
                if (caps.UsagePage != 0xFF00)
                {
                    result.Diagnostic = "非 FF00 集合";
                    return result;
                }

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
                    // 7 字节接口也是 HID++ 短报文（0x10），只是报文被截短。
                    // G304 的电量（feature 0x1000）就挂在这条通道上。
                    link.UseFeature = false;
                    link.Size = caps.OutputReportByteLength;
                }
                else
                {
                    result.Diagnostic = $"接口过短 in={caps.InputReportByteLength} out={caps.OutputReportByteLength}";
                    return result;
                }

                diag.Append(link.UseFeature ? "通道=feature" : "通道=report").Append(" 长度=").Append(link.Size).Append(' ');

                foreach (byte idx in new byte[] { 0x01, 0x02, 0xFF })
                {
                    if (Environment.TickCount - started > ProbeBudgetMs)
                    {
                        diag.Append("预算用尽 ");
                        break;
                    }
                    if (link.Faulted)
                    {
                        diag.Append("通道超时弃用 ");
                        break;
                    }

                    string name = ReadName(link, idx);
                    bool gpw = IsOriginalGpwName(name) || path.IndexOf("vid_046d&pid_c088", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool charging = ReadBattery(link, idx, out int level, out string note, gpw);
                    if (level >= 0)
                    {
                        result.Battery = level;
                        result.Charging = charging;
                        result.BatteryNote = note;
                        result.Name = name;
                        diag.Append($"slot=0x{idx:X2} 名称={name ?? "（无）"} 电量={level}% ");
                        break; // Battery and name always belong to the same receiver slot.
                    }
                    if (string.IsNullOrWhiteSpace(result.Name)) result.Name = name;
                    diag.Append($"slot=0x{idx:X2} 未读到电量 ");
                }
                return result;
            }
            catch (Exception ex)
            {
                result.Diagnostic = "异常 " + ex.GetType().Name;
                return result;
            }
            finally
            {
                result.Diagnostic = (result.Diagnostic ?? "") + diag + $"用时={Environment.TickCount - started}ms";
                link.Dispose();
            }
        }


        /// <summary>
        /// 读取设备百分比，或在 0x1001 返回电压时按官方电池模型推算。
        ///
        /// 1.3.9 的报告里初代 GPW 读成 16%，正好是 0x1001 电压 4096mV 的高字节 0x10；
        /// 1.4.x 起把 0x1001 当大端 mV 处理。但「是不是 GPW」以前只靠 0x0005 名称，
        /// 名称读不到就整条电压路径作废——用户看到的就是「1.4.3 识别不到一代 GPW」。
        /// 现在名称只决定显示名和优先级，电压照样按量程换算。
        /// </summary>
        private static bool ReadBattery(Link link, byte deviceIndex, out int level, out string note, bool originalGpw)
        {
            level = -1;
            note = "";
            // Official G HUB uses the voltage model for the original GPW, so try 0x1001
            // first there; a device-reported 0x1000 percentage must not short-circuit it.
            ushort[] features = originalGpw
                ? new ushort[] { 0x1001, 0x1004, 0x1000 }
                : new ushort[] { 0x1004, 0x1000, 0x1001 };
            foreach (ushort feature in features)
            {
                byte fi = FeatureIndex(link, deviceIndex, feature);
                if (fi == 0) continue;
                var r = Call(link, deviceIndex, fi, feature == 0x1004 ? (byte)1 : (byte)0);
                if (TryParseBattery(feature, r, out level, out bool charging, originalGpw))
                {
                    note = feature == 0x1001
                        ? "Logitech GPW 官方电压模型推算（非设备直接百分比）"
                        : "Logitech HID++ 设备电量";
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
                // 0x81 = 满电。名字没读到时不拿状态位直接报 100%，先按电压量程判断。
                if (status == 1)
                {
                    if (!originalGpw && millivolts < 4186) return false;
                    level = 100;
                    return true;
                }
                charging = status == 0;
            }
            // 0x1001 按 HID++ 文档就是大端 mV；量程外（比如把百分比塞进这个特征的设备）
            // 由曲线函数自己拒绝，不会编出读数。
            return LogitechGpwBattery.TryEstimateGpwPercentage(millivolts, charging, out level);
        }

        /// <summary>0x0005 Device Name：取长度，再按块读。</summary>
        private static string ReadName(Link link, byte deviceIndex)
        {
            byte fi = FeatureIndex(link, deviceIndex, 0x0005);
            if (fi == 0) return null;

            var head = Call(link, deviceIndex, fi, 0x00);   // function 0 = count
            if (head == null || head.Length < 5) return null;
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

                var r = Await(link, deviceIndex, fi, 0x01, 250);
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
        private static byte[] Await(Link link, byte deviceIndex, byte featureIndex, byte function, int timeoutMs = 250)
        {
            int deadline = Environment.TickCount + timeoutMs;
            byte[] loose = null;
            while (Environment.TickCount - deadline < 0)
            {
                var r = link.Receive(Math.Max(1, Math.Min(120, deadline - Environment.TickCount)));
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

            var r = Await(link, deviceIndex, 0x00, 0x00, 250);
            if (r == null || r.Length < 5) return 0;

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
            return Await(link, deviceIndex, featureIndex, function, 250);
        }

        internal static bool IsOriginalGpwName(string name)
        {
            string clean = System.Text.RegularExpressions.Regex.Replace(name ?? "", "[^A-Za-z0-9]", "").ToUpperInvariant();
            return clean is "GPROWIRELESS" or "GPROWIRELESSGAMINGMOUSE" or "PROWIRELESSGAMINGMOUSE"
                or "LOGITECHGPROWIRELESS" or "LOGITECHGPROWIRELESSGAMINGMOUSE";
        }
    }
}
