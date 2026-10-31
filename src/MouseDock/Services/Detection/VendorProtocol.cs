using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// 厂商协议读取器：复刻 AULA/雷柏系 2.4G 接收器的 32 字节命令帧。
    /// 已用 USBPcap 抓到官方驱动的真实帧并逐字节验证：
    ///   30 00 01 … → 30 00 01 0B "M137" 01 16 11 AA 00 41 01   （Identify，0x41=65% 电量）
    ///   12 00 01 … → 12 00 01 01 01
    ///   13 00 01 … → 13 00 01 19 06 10/20/30/40/80/DC…          （DPI 档位）
    ///   D1 00 41 … ← 设备主动推送的状态（第 3 字节同样是电量）
    /// </summary>
    internal static partial class VendorProtocol
    {
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr h, byte[] buf, int len, IntPtr written, ref NativeOverlapped ov);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr h, byte[] buf, int len, IntPtr read, ref NativeOverlapped ov);
        [DllImport("kernel32.dll")] private static extern IntPtr CreateEvent(IntPtr a, bool manual, bool init, IntPtr name);
        [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")] private static extern bool GetOverlappedResult(IntPtr h, ref NativeOverlapped ov, out uint transferred, bool wait);
        [DllImport("kernel32.dll")] private static extern bool ResetEvent(IntPtr h);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int needed, IntPtr info);
        [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES attr);
        [DllImport("hid.dll")] private static extern bool HidD_GetFeature(IntPtr h, byte[] buf, int len);
        [DllImport("hid.dll")] private static extern bool HidD_SetFeature(IntPtr h, byte[] buf, int len);
        [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr prep);
        [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr prep);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr prep, ref HIDP_CAPS caps);

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA { public int Size; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }
        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
            public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        /// <summary>读一次 2.4G 电量；失败返回 -1。接收器偶尔回 status=0x02，每次重试用新句柄。</summary>
        public static int TryReadBattery(ushort vid, ushort pid)
        {
            var paths = Paths(vid, pid);

            for (int attempt = 0; attempt < 3; attempt++)
            {
                foreach (var path in paths)
                {
                    IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                        IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                    if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;

                    try
                    {
                        var caps = Caps(h);
                        if (caps.OutputReportByteLength != 33) continue;   // 只处理 32 字节厂商集合
                        int level = Session(h, $"{vid:X4}:{pid:X4}");
                        if (level >= 0) return level;
                    }
                    catch { }
                    finally { CloseHandle(h); }
                }

                if (attempt < 2) Thread.Sleep(120);
            }
            return -1;
        }

        /// <summary>
        /// 0xFFFF 厂商集合上的只读状态：GET_FEATURE 一次，不写任何命令。
        /// 实测对象：VGN Neon75 Air 2.4G 接收器（391D:2A05），
        ///   if1 FFFF:0001 输入报告 id=5 31 字节；if2 FFFF:0002 无编号 feature 64 字节。
        /// 两个通道回同一份状态：00 64 00 01 01 01 01 …（在这里读到 byte1 = 100）。
        /// 只认「首字节 0x00、次字节 1..100、第三字节 0/1」这一种形状，对不上就返回 -1，不猜。
        /// </summary>
        public static int TryReadStatus(ushort vid, ushort pid)
        {
            LoadBatteryCache();
            string key = $"{vid:X4}:{pid:X4}";

            // 设备从低功耗爬起来需要一点时间，单次读空不代表没有；连敲三次再认输。
            for (int attempt = 0; attempt < 3; attempt++)
            {
                foreach (var path in Paths(vid, pid))
                {
                    IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                        IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                    if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;

                    try
                    {
                        var caps = Caps(h);
                        if (caps.UsagePage != 0xFFFF || caps.Usage != 0x0002) continue;
                        int len = caps.FeatureReportByteLength;
                        if (len < 8 || len > 1024) continue;

                        var buf = new byte[len];
                        if (!HidD_GetFeature(h, buf, len)) continue;

                        // 有编号报告时首字节是报告 ID，无编号时直接是载荷，两种对齐都试
                        foreach (int at in new[] { 0, 1 })
                        {
                            if (buf.Length < at + 4) break;
                            if (buf[at] != 0x00) continue;
                            int pct = buf[at + 1];
                            int charge = buf[at + 2];
                            if (pct >= 1 && pct <= 100 && charge <= 1)
                            {
                                if (!_lastGood.TryGetValue(key, out int old) || old != pct)
                                {
                                    _lastGood[key] = pct;
                                    SaveBatteryCache();
                                }
                                StatusStale = false;
                                return pct;
                            }
                        }
                    }
                    catch { }
                    finally { CloseHandle(h); }
                }

                if (attempt < 2) Thread.Sleep(60);
            }

            // 键盘/鼠标进低功耗后厂商集合不再应答。用上次读到的值顶上，
            // 别把面板抹成横杠——上次值会通过 StatusStale 标出来。
            if (_lastGood.TryGetValue(key, out int prev))
            {
                StatusStale = true;
                return prev;
            }
            StatusStale = false;
            return -1;
        }

        /// <summary>最近一次 TryReadStatus 的结果是不是「上次值兜底」。调用方读它来标注。</summary>
        public static bool StatusStale { get; private set; }

        // ── 上次值落盘：跨进程也要能兜底（--detect 每次都是新进程，内存缓存活不过去）──
        private static bool _cacheLoaded;

        private static string BatteryCacheFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MouseDock", "battery-cache.json");

        private static void LoadBatteryCache()
        {
            if (_cacheLoaded) return;
            _cacheLoaded = true;
            try
            {
                if (!File.Exists(BatteryCacheFile)) return;
                var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(
                    File.ReadAllText(BatteryCacheFile));
                if (map == null) return;
                foreach (var kv in map) _lastGood[kv.Key] = kv.Value;
            }
            catch { }
        }

        private static void SaveBatteryCache()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BatteryCacheFile));
                File.WriteAllText(BatteryCacheFile,
                    System.Text.Json.JsonSerializer.Serialize(_lastGood));
            }
            catch { }
        }

        /// <summary>
        /// M HUB 家族（迈从 / RealTek 方案的 2.4G 接收器，例如 5253:1021 MCHOSE A7）。
        /// 实测还原：FF01 集合、feature 报告 0x11 共 21 字节。
        ///   请求 11 F9 FF×19 → 应答 11 F9 &lt;载荷 19 字节，逐字节 XOR 0xFF&gt;
        ///   body[0:2] 小端 = 厂商号（0x5253 或 0x3837）
        ///   body[8]&amp;7 = 连接模式（0/1/2），模式 1 且 body[8]&amp;8==0 表示鼠标休眠
        ///   body[9] = 电量百分比，body[10] = 充电标志
        /// </summary>
        public static int TryReadMchose(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;

                try
                {
                    var caps = Caps(h);
                    if (caps.UsagePage != 0xFF01) continue;
                    if (caps.FeatureReportByteLength < 21) continue;

                    var req = new byte[21];
                    req[0] = 0x11;
                    req[1] = 0xF9;
                    for (int i = 2; i < req.Length; i++) req[i] = 0xFF;

                    if (!HidD_SetFeature(h, req, req.Length)) continue;

                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        Thread.Sleep(80);
                        var resp = new byte[21];
                        resp[0] = 0x11;
                        if (!HidD_GetFeature(h, resp, resp.Length)) continue;
                        if (resp[0] != 0x11 || resp[1] != 0xF9) continue;

                        var body = new byte[19];
                        for (int i = 0; i < body.Length; i++) body[i] = (byte)(resp[2 + i] ^ 0xFF);

                        int vendorId = body[0] | (body[1] << 8);
                        if (vendorId != 0x5253 && vendorId != 0x3837) continue;

                        int mode = body[8] & 0x07;
                        if (mode == 1 && (body[8] & 0x08) == 0) return -1;   // 休眠

                        int pct = body[9];
                        if (pct >= 1 && pct <= 100) return pct;
                    }
                }
                catch { }
                finally { CloseHandle(h); }
            }
            return -1;
        }

        private static HIDP_CAPS Caps(IntPtr h)        {
            var caps = new HIDP_CAPS();
            if (HidD_GetPreparsedData(h, out IntPtr prep))
            {
                HidP_GetCaps(prep, ref caps);
                HidD_FreePreparsedData(prep);
            }
            return caps;
        }

        /// <summary>上次成功读到的电量，键是 "VID:PID"。鼠标休眠时用它顶上，不把面板抹成「—」。</summary>
        private static readonly Dictionary<string, int> _lastGood = new Dictionary<string, int>();

        /// <summary>Identify 应答 [4:8] 的型号串，键是 "VID:PID"。M137=狼蛛 SC580，M230=前行者 X23。</summary>
        private static readonly Dictionary<string, string> _identity = new Dictionary<string, string>();

        /// <summary>读到的型号串；没读过返回 null。VID:PID 是接收器的号，型号只能看这 4 个字符。</summary>
        public static string IdentityFor(ushort vid, ushort pid)
        {
            LoadIdentities();
            return _identity.TryGetValue($"{vid:X4}:{pid:X4}", out var s) ? s : null;
        }

        private static bool _identLoaded;
        private static string IdentityFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MouseDock", "vendor-identity.json");

        /// <summary>型号串学到一次就写盘，下次开机鼠标睡着也认得它。</summary>
        private static void LoadIdentities()
        {
            if (_identLoaded) return;
            _identLoaded = true;
            try
            {
                if (!File.Exists(IdentityFile)) return;
                var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(IdentityFile));
                if (map != null)
                    foreach (var kv in map) _identity[kv.Key] = kv.Value;
            }
            catch { }
        }

        private static void SaveIdentities()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(IdentityFile));
                File.WriteAllText(IdentityFile,
                    System.Text.Json.JsonSerializer.Serialize(_identity, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }


        private static int Session(IntPtr h, string key)
        {
            IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
            try
            {
                // 1) Identify：应答 30 00 01 0B "Mxxx" … charge[12] battery[13] online[14]
                var r30 = Exchange(h, ev, 0x30, 0x00);
                if (r30 != null && r30.Length >= 3 && r30[0] == 0x30 && r30[1] == 0x02)
                {
                    // 实测：鼠标静默后接收器只回 30 02 01 00 …（32 字节全零状态），
                    // 这不是协议坏了，是鼠标在休眠。用上次读到的值顶上。
                    return _lastGood.TryGetValue(key, out int prev) ? prev : -1;
                }
                if (r30 != null && r30.Length >= 16)
                {
                    // 有的驱动会在前面补一个报告 ID 0x00，两种对齐都接受
                    int at = (r30[0] == 0x00 && r30[1] == 0x30) ? 1 : 0;
                    if (r30.Length >= at + 15 && r30[at] == 0x30 && r30[at + 1] == 0x00)
                    {
                        string model = Encoding.ASCII.GetString(r30, at + 4, 4).Trim('\0', ' ');
                        if (model.Length == 4) { _identity[key] = model; SaveIdentities(); }

                        // online 位在鼠标静默几秒后会变 0，但电量字节仍是鼠标最后上报的值，
                        // 这里照样采用，避免「每隔几秒掉一次电量」
                        int v = r30[at + 13];
                        if (v >= 1 && v <= 100)
                        {
                            _lastGood[key] = v;
                            return v;
                        }
                    }
                }

                // 2) 顺带握手，让设备进入可推送状态
                Exchange(h, ev, 0x12, 0x00);
                Exchange(h, ev, 0x13, 0x00);

                // 3) 收 400ms 内的 D1 推送（第 3 字节同样是电量）
                var deadline = DateTime.UtcNow.AddMilliseconds(400);
                while (DateTime.UtcNow < deadline)
                {
                    var push = ReadReport(h, ev, 150);
                    if (push == null) continue;
                    int at = (push.Length > 32 && push[0] == 0x00) ? 1 : 0;   // 去掉报告 ID
                    if (push.Length > at + 3 && push[at] == 0xD1)
                    {
                        int v = push[at + 2];
                        if (v >= 1 && v <= 100) return v;
                    }
                }
            }
            catch { }
            finally { CloseHandle(ev); }
            return -1;
        }

        private static byte[] Exchange(IntPtr h, IntPtr ev, byte cmd, byte sub)
        {
            var frame = new byte[33];
            frame[0] = 0x00;   // 报告 ID
            frame[1] = cmd;
            frame[2] = sub;
            frame[3] = 0x01;

            var ov = new NativeOverlapped { EventHandle = ev };
            if (!WriteFile(h, frame, frame.Length, IntPtr.Zero, ref ov) && Marshal.GetLastWin32Error() != 997) return null;
            WaitForSingleObject(ev, 400);
            GetOverlappedResult(h, ref ov, out _, false);
            ResetEvent(ev);
            return ReadReport(h, ev, 400);
        }

        private static byte[] ReadReport(IntPtr h, IntPtr ev, int ms)
        {
            var buf = new byte[128];
            var ov = new NativeOverlapped { EventHandle = ev };
            if (!ReadFile(h, buf, buf.Length, IntPtr.Zero, ref ov) && Marshal.GetLastWin32Error() != 997) return null;
            if (WaitForSingleObject(ev, (uint)ms) != 0) { ResetEvent(ev); return null; }
            GetOverlappedResult(h, ref ov, out uint got, false);
            ResetEvent(ev);
            if (got == 0) return null;
            var slice = new byte[got];
            Array.Copy(buf, slice, got);
            return slice;
        }

        // ── 第一批厂商读取器：写一帧、读一帧 ─────────────────────────────

        /// <summary>写一帧再读一帧。frame 含报告 ID，读不到返回 null。</summary>
        private static byte[] Transact(IntPtr h, IntPtr ev, byte[] frame, int timeoutMs)
        {
            var ov = new NativeOverlapped { EventHandle = ev };
            if (!WriteFile(h, frame, frame.Length, IntPtr.Zero, ref ov) && Marshal.GetLastWin32Error() != 997) return null;
            WaitForSingleObject(ev, 300);
            GetOverlappedResult(h, ref ov, out _, false);
            ResetEvent(ev);
            return ReadReport(h, ev, timeoutMs);
        }

        /// <summary>
        /// Razer 控制传输。DeathAdder V4 Pro Carbon 接收器（0x1532:0x00F0）实测：
        /// feature 0x5B 91 字节，b[89]=XOR(b[3:89])，transaction 0..30 轮转，
        /// 响应[1]==2 表示成功，电量在响应[10]。
        /// </summary>
        public static int TryReadRazer(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;
                try
                {
                    var caps = Caps(h);
                    if (caps.FeatureReportByteLength != 91) continue;

                    for (byte tx = 0; tx <= 30; tx++)
                    {
                        var req = new byte[91];
                        req[2] = tx;
                        req[6] = 0x02;
                        req[7] = 0x07;
                        req[8] = 0x5B;
                        byte x = 0;
                        for (int i = 3; i < 89; i++) x ^= req[i];
                        req[89] = x;
                        if (!HidD_SetFeature(h, req, req.Length)) break;

                        var resp = new byte[91];
                        if (!HidD_GetFeature(h, resp, resp.Length)) continue;
                        if (resp[1] == 2)
                        {
                            int pct = resp[10];
                            if (pct >= 1 && pct <= 100) return pct;
                            break;
                        }
                        if (resp[1] != 0 && resp[1] != 1) break;
                    }
                }
                catch { }
                finally { CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>
        /// ROG 的两种 64 字节布局：
        /// 8K（03 12 07 00 00，电量 b[5]）、OMNI（03 12 07，电量 b[5]）。
        /// 帧头对不上就换下一种，成功判据带充电位检查。
        /// </summary>
        public static int TryReadRog(ushort vid, ushort pid)
        {
            var heads = new[]
            {
                new byte[] { 0x03, 0x12, 0x07, 0x00, 0x00 },   // rog_8k
                new byte[] { 0x03, 0x12, 0x07 }                // rog_omni
            };

            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;
                IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                try
                {
                    var caps = Caps(h);
                    if (caps.OutputReportByteLength != 64) continue;

                    foreach (var head in heads)
                    {
                        var req = new byte[64];
                        Array.Copy(head, req, head.Length);
                        var resp = Transact(h, ev, req, 600);
                        if (resp == null || resp.Length < 11) continue;

                        bool match = true;
                        for (int i = 0; i < head.Length; i++) if (resp[i] != head[i]) { match = false; break; }
                        if (!match) continue;

                        int pct = resp[5];
                        if (pct >= 1 && pct <= 100) return pct;
                    }
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>
        /// SteelSeries Aerox：report 0xD2，64 字节。电量是 21 级台阶，step 1..21 → (step-1)*5，
        /// 不是线性百分比。step==0 是休眠。
        /// </summary>
        public static int TryReadSteelSeries(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;
                IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                try
                {
                    var caps = Caps(h);
                    if (caps.OutputReportByteLength != 64) continue;

                    var req = new byte[64];
                    req[0] = 0xD2;
                    var resp = Transact(h, ev, req, 600);
                    if (resp == null || resp.Length < 2) continue;
                    if (resp[0] != 0xD2) continue;

                    int step = resp[1] & 0x7F;
                    if (step >= 1 && step <= 21) return (step - 1) * 5;
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>
        /// Finalmouse：ULX 走 tx=0x04/rx=0x05，SLX 走 tx=0x01/rx=0x02。
        /// cmd=38 直接给百分比（payload[0]），cmd=5 给电压 mV 再按 7 点表插值。
        /// </summary>
        public static int TryReadFinalmouse(ushort vid, ushort pid)
        {
            var channels = new[] { (tx: (byte)0x04, rx: (byte)0x05), (tx: (byte)0x01, rx: (byte)0x02) };

            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;
                IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                try
                {
                    var caps = Caps(h);
                    if (caps.OutputReportByteLength != 64) continue;

                    foreach (var (tx, rx) in channels)
                    {
                        foreach (byte cmd in new byte[] { 38, 5 })
                        {
                            var req = new byte[64];
                            req[0] = tx;
                            req[1] = 0x02;
                            req[2] = (byte)(0x80 | cmd);
                            var resp = Transact(h, ev, req, 600);
                            if (resp == null || resp.Length < 5) continue;
                            if (resp[0] != rx) continue;
                            if (resp[1] != (byte)(cmd + 2)) continue;

                            if (cmd == 38)
                            {
                                int pct = resp[4];
                                if (pct >= 1 && pct <= 100) return pct;
                            }
                            else
                            {
                                int mv = resp[4] | (resp[5] << 8);
                                int pct = VoltageToPercent(mv);
                                if (pct >= 0) return pct;
                            }
                        }
                    }
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>
        /// COMPX（ATK / Lamzu / G-Wolves 共用）：17 字节帧，08 04 … 49，
        /// 整帧 Σ%256 必须等于 0x55，电量在 b[6]，b[7]&1 是充电位。
        /// </summary>
        public static int TryReadCompx(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;
                IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                try
                {
                    var caps = Caps(h);
                    if (caps.OutputReportByteLength != 17) continue;

                    var req = new byte[17];
                    req[0] = 0x08;
                    req[1] = 0x04;
                    req[16] = 0x49;
                    var resp = Transact(h, ev, req, 600);
                    if (resp == null || resp.Length < 17) continue;
                    if (resp[0] != 0x08 || resp[1] != 0x04) continue;

                    int sum = 0;
                    for (int i = 0; i < 17; i++) sum += resp[i];
                    if (sum % 256 != 0x55) continue;

                    int pct = resp[6];
                    if (pct >= 1 && pct <= 100) return pct;
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>锂电电压→百分比，仅用于 Finalmouse cmd=5 这种报 mV 的路径。</summary>
        private static int VoltageToPercent(int mv)
        {
            if (mv < 2500 || mv > 4600) return -1;
            if (mv >= 4380) return 100;
            if (mv >= 4170) return 75 + (mv - 4170) * 25 / (4380 - 4170);
            if (mv >= 3880) return 50 + (mv - 3880) * 25 / (4170 - 3880);
            if (mv >= 3740) return 25 + (mv - 3740) * 25 / (3880 - 3740);
            if (mv >= 3660) return 10 + (mv - 3660) * 15 / (3740 - 3660);
            if (mv >= 3620) return 5 + (mv - 3620) * 5 / (3660 - 3620);
            if (mv >= 3000) return 1 + (mv - 3000) * 4 / (3620 - 3000);
            return 0;
        }

        private static List<string> Paths(ushort vid, ushort pid)
        {
            var list = new List<string>();
            HidD_GetHidGuid(out Guid guid);
            IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12);
            try
            {
                for (uint i = 0; ; i++)
                {
                    var did = new SP_DEVICE_INTERFACE_DATA { Size = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)) };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref did)) break;
                    SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out int need, IntPtr.Zero);
                    if (need <= 0) continue;

                    IntPtr buf = Marshal.AllocHGlobal(need);
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(set, ref did, buf, need, out _, IntPtr.Zero))
                        {
                            string path = Marshal.PtrToStringUni(buf + 4) ?? "";
                            IntPtr hh = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                            if (hh != IntPtr.Zero && hh != new IntPtr(-1))
                            {
                                var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)) };
                                if (HidD_GetAttributes(hh, ref a) && a.VendorID == vid && a.ProductID == pid) list.Add(path);
                                CloseHandle(hh);
                            }
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { CloseHandle(set); }
            return list;
        }
    }
}
