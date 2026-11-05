using System;
using System.Collections.Generic;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// 英菲克（Inphic）电量读取。
    ///
    /// 协议来源：英菲克网页驱动 HUB WEB（http://hub.inphic.cn/）的本地桥 Inphic.exe。
    /// 桥装完注册 myapp:// 协议，监听 ws://localhost:10085，自己用 hidapi 打开接收器，
    /// hid_read 到输入报告后把前 5 字节拼成 "8x01" + 5×%02x 的 hex 串推给页面。
    /// 逆向位置：Inphic.exe FUN_0040e3d0 / FUN_0040e750 / FUN_0040f9e0，格式串
    ///   8001%02x%02x%02x%02x%02x / 8101… / 8201… / 8301… / 8501…
    /// 五套只是通道号不同（0x80..0x83、0x85），后面 5 个字节的取法一模一样。
    ///
    /// 页面侧（hub.inphic.cn 主包）把这 7 字节拆成：
    ///   [0] 通道 0x80..0x85     [1] 0x01 状态帧
    ///   [2] 未知（报告 ID 位）   [3] 机型号
    ///   [4] 0x40 / 0x41 电量类子命令
    ///   [5] 状态 1=按百分比 2=满电 3=充电中
    ///   [6] 电量 0..100
    ///
    /// 所以原始 HID 输入报告就是：
    ///   [0] report id（无编号报告时这一位就是机型号）  [1] 机型号
    ///   [2] 0x40 / 0x41                                [3] 状态
    ///   [4] 电量
    /// 桥不发查询帧——页面全站没有 getBattery，电量是接收器主动上报的。
    ///
    /// 设备身份：
    ///   VID 0x1D57  PID 0xFA60 / 0xFA65   网页驱动认的 2.4G 接收器（Inphic.exe 里的硬编码）
    ///   VID 0x248A  PID 0x5C2E / 0x5D2E / 0x5E2E（有线）、0x5C2F（2.4G）
    ///   VID 0x249A  PID 0x5C2F（同一颗接收器的另一套号）
    ///
    /// 撞号提醒：0x248A:0x5C2F 在 device-db 里还被记成 AJAZZ AJ159 / AJ159 P 的接收器。
    /// 0x248A 是共用 OEM 号，光凭 VID:PID 定性会误判，产品名要看。
    ///
    /// 未经真机验证。
    /// </summary>
    internal static partial class VendorProtocol
    {
        // ---------- 设备身份 ----------

        public const ushort InphicVid = 0x248A;
        public const ushort InphicAltVid = 0x249A;

        /// <summary>网页驱动本地桥认的接收器号段。</summary>
        public const ushort InphicHubVid = 0x1D57;

        /// <summary>2.4G 接收器用的那颗 PID；有线本体是另外三个。</summary>
        public const ushort InphicReceiverPid = 0x5C2F;

        private static readonly ushort[] InphicPids =
        {
            0x5C2E, 0x5D2E, 0x5E2E,   // 有线
            InphicReceiverPid         // 2.4G
        };

        private static readonly ushort[] InphicHubPids = { 0xFA60, 0xFA65 };

        public static bool IsInphicVid(ushort vid) =>
            vid == InphicVid || vid == InphicAltVid || vid == InphicHubVid;

        public static bool IsInphicPid(ushort pid) =>
            Array.IndexOf(InphicPids, pid) >= 0 || Array.IndexOf(InphicHubPids, pid) >= 0;

        /// <summary>VID:PID 都在英菲克清单里就算。有线本体不跟别家撞号，可以直接用这个。</summary>
        public static bool IsInphicDevice(ushort vid, ushort pid)
        {
            if (vid == InphicHubVid) return Array.IndexOf(InphicHubPids, pid) >= 0;
            if (vid != InphicVid && vid != InphicAltVid) return false;
            return Array.IndexOf(InphicPids, pid) >= 0;
        }

        /// <summary>
        /// 带产品名的判定，给接收器用。0x248A:0x5C2F 与 AJAZZ AJ159 系列撞号，
        /// 名字里能认出是黑爵就别抢；名字为空（接收器常只报 "Wireless Receiver"）时仍然放行，
        /// 宁可多读一轮读不到，也别让真设备的电量漏掉。
        /// </summary>
        public static bool IsInphicDevice(ushort vid, ushort pid, string product)
        {
            if (!IsInphicDevice(vid, pid)) return false;
            // 1D57:FA65 is also used by DELUX's official Pro driver. Do not claim it
            // when the product string identifies another brand.
            if (vid == InphicHubVid && pid == 0xFA65 && LooksLikeDeluxReceiver(product)) return false;
            if (pid != InphicReceiverPid) return true;
            return !LooksLikeAjazzReceiver(product);
        }

        private static bool LooksLikeAjazzReceiver(string product) =>
            !string.IsNullOrEmpty(product) &&
            product.IndexOf("AJ159", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool LooksLikeDeluxReceiver(string product) =>
            !string.IsNullOrEmpty(product) &&
            (product.IndexOf("DELUX", StringComparison.OrdinalIgnoreCase) >= 0 ||
             product.IndexOf("多彩", StringComparison.Ordinal) >= 0);

        // ---------- 对外入口 ----------

        internal static bool IsInphicSplitReceiver(ushort vid, ushort pid) =>
            vid == InphicHubVid && pid is 0xFA60 or 0xFA65;

        public static int TryReadInphic(ushort vid, ushort pid) => TryReadInphic(vid, pid, out _);

        /// <summary>
        /// FA60 按官方协议在两个独立集合上初始化和监听。
        /// 其他型号沿用原有取数顺序：
        ///   1) 先听接收器主动上报（网页驱动走的就是这条路）
        ///   2) 没等到就发一次官方开设备时的那帧 feature（10 字节），再听 800ms
        ///   3) 还没有就试 SC525 那套 0E 20 一问一答
        /// 全失败返回 -1，界面显示未知。
        /// </summary>
        public static int TryReadInphic(ushort vid, ushort pid, out bool charging)
        {
            charging = false;

            if (IsInphicSplitReceiver(vid, pid))
                return TryReadInphicSplitReceiver(vid, pid, out charging);

            // 0x248A / 0x249A 的 5C2F 与 AJAZZ AJ159 系列撞号。名字认不出来时旧流程要在这里
            // 耗 1.5s 被动 + 0.8s 唤醒 + 0.6s SC525 才让给黑爵，一轮扫描白拖三秒。
            // 撞号号段只开一个短窗口，拿不到立刻退出。
            int legacy = TryReadInphicLegacy(vid, pid, out charging);
            if (legacy >= 0) return legacy;

            bool contested = pid == InphicReceiverPid;
            int pct = TryReadInphicPush(vid, pid,
                contested ? InphicContestedWindowMs : InphicPushWindowMs, out charging);
            if (pct >= 0) return pct;
            if (contested) return -1;

            pct = TryReadInphicWake(vid, pid, out charging);
            if (pct >= 0) return pct;

            return TryReadInphicSc525(vid, pid, out charging);
        }

        // Official Inphic Mouse 1.0.2.5: FUN_0044a5c0 sends report0/command10,
        // FUN_0044c700 strips report0; FUN_00432b80 passes payload[13]/[12]
        // to BatteryCtrl and reads model[4..7]. config.xml maps these four IDs.
        internal static bool TryParseInphicLegacy(ReadOnlySpan<byte> report,
            out int level, out bool charging, out string model)
        {
            level = -1; charging = false; model = null;
            int at = report.Length > 1 && report[0] == 0 && report[1] == 0x10 ? 1 : 0;
            if (report.Length < at + 15 || report[at] != 0x10 || report[at + 1] != 0 ||
                report[at + 12] > 1 || report[at + 13] > 100 || report[at + 14] != 1) return false;
            string id = System.Text.Encoding.ASCII.GetString(report.Slice(at + 4, 4));
            if (id is not ("M909" or "M910" or "IN90" or "M900")) return false;
            model = id; level = report[at + 13]; charging = report[at + 12] == 1;
            return true;
        }

        internal static int TryReadInphicLegacy(ushort vid, ushort pid, out bool charging)
        {
            charging = false;
            if ((vid != InphicVid && vid != InphicAltVid) || Array.IndexOf(InphicPids, pid) < 0) return -1;
            foreach (string path in Paths(vid, pid))
            {
                if (path.IndexOf("mi_02", StringComparison.OrdinalIgnoreCase) < 0) continue;
                IntPtr handle = OpenOverlapped(path, out IntPtr signal);
                if (handle == IntPtr.Zero) continue;
                try
                {
                    var caps = Caps(handle);
                    if (signal == IntPtr.Zero || caps.OutputReportByteLength != 33) continue;
                    var request = new byte[33]; request[1] = 0x10;
                    var reply = Transact(handle, signal, request, 600);
                    if (!TryParseInphicLegacy(reply, out int level, out charging, out string model)) continue;
                    _identity[$"{vid:X4}:{pid:X4}"] = model;
                    SaveIdentities();
                    return level;
                }
                finally { CloseHandle(signal); CloseHandle(handle); }
            }
            return -1;
        }

        // FA60 的初始化和电量上报分属同一 USB 接口下的两个顶层集合。
        internal static bool IsInphicSplitInput(ushort page, ushort usage, ushort inputLength) =>
            page == 0x000A && usage == 0 && inputLength == 5;

        internal static bool IsInphicSplitFeature(ushort page, ushort usage, ushort featureLength) =>
            page == 0x000B && usage == 0 && featureLength >= InphicWakeFrame.Length;

        internal static string InphicCollectionParent(string path)
        {
            var parts = path.Split('#');
            if (parts.Length < 3) return null;
            int collection = parts[1].LastIndexOf("&col", StringComparison.OrdinalIgnoreCase);
            int instanceSuffix = parts[2].LastIndexOf('&');
            if (collection < 0 || instanceSuffix < 0) return null;
            return parts[1].Substring(0, collection) + "#" + parts[2].Substring(0, instanceSuffix);
        }

        private static int TryReadInphicSplitReceiver(ushort vid, ushort pid, out bool charging)
        {
            charging = false;
            var features = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var inputs = new List<(string Parent, string Path)>();
            foreach (var path in Paths(vid, pid))
            {
                string parent = InphicCollectionParent(path);
                if (parent == null) continue;
                IntPtr probe = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (probe == new IntPtr(-1)) continue;
                try
                {
                    var caps = Caps(probe);
                    if (IsInphicSplitInput(caps.UsagePage, caps.Usage, caps.InputReportByteLength))
                        inputs.Add((parent, path));
                    if (IsInphicSplitFeature(caps.UsagePage, caps.Usage, caps.FeatureReportByteLength))
                        features.TryAdd(parent, path);
                }
                finally { CloseHandle(probe); }
            }

            foreach (var input in inputs)
            {
                IntPtr handle = CreateFile(input.Path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (handle == new IntPtr(-1)) continue;
                IntPtr signal = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                try
                {
                    if (signal == IntPtr.Zero) continue;
                    if (features.TryGetValue(input.Parent, out string featurePath))
                    {
                        IntPtr feature = CreateFile(featurePath, GENERIC_READ | GENERIC_WRITE,
                            FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                        if (feature != new IntPtr(-1))
                        {
                            try
                            {
                                var caps = Caps(feature);
                                if (IsInphicSplitFeature(caps.UsagePage, caps.Usage, caps.FeatureReportByteLength))
                                {
                                    var request = BuildInphicWakeRequest(caps.FeatureReportByteLength);
                                    HidD_SetFeature(feature, request, request.Length);
                                }
                            }
                            finally { CloseHandle(feature); }
                        }
                    }

                    int level = ListenForStatus(handle, signal, InphicPushWindowMs, out charging, $"{vid:X4}:{pid:X4}");
                    if (level >= 0) return level;
                }
                finally
                {
                    if (signal != IntPtr.Zero) CloseHandle(signal);
                    CloseHandle(handle);
                }
            }
            return -1;
        }

        // ---------- 主路径：读接收器主动上报的状态帧 ----------

        private const byte InphicStatusTagA = 0x40;   // 电量类子命令
        private const byte InphicStatusTagB = 0x41;   // 同上，页面把两者当等价

        private const byte InphicBatteryPercent = 1;  // 按 [6] 的百分比显示
        private const byte InphicBatteryFull = 2;     // 固定 100%
        private const byte InphicBatteryCharging = 3; // 充电中

        /// <summary>听够这么久还没等到状态帧就放弃，交给下一条协议。两轮以内的上报周期都够用。</summary>
        private const int InphicPushWindowMs = 1500;

        private const int InphicSliceMs = 250;

        /// <summary>撞号号段（248A/249A:5C2F）的短窗口，拿不到就让给 AJAZZ。</summary>
        private const int InphicContestedWindowMs = 500;

        /// <summary>网页驱动 mouseNum 表里的机型号，用来压掉 0x40 撞到别的报告的概率。</summary>
        private static readonly byte[] InphicModelIds =
        {
            1, 6, 7, 16, 48, 61, 65, 66, 67, 77, 78, 85,
            113, 114, 115, 116, 133, 144, 146, 147,
            149, 152, 153, 177, 188, 190, 195, 201
        };

        /// <summary>IN9 Pro 在网页驱动里的机型号，留着给断言和日志用。</summary>
        public const byte InphicModelIn9Pro = 149;

        private static int TryReadInphicPush(ushort vid, ushort pid, int windowMs, out bool charging)
        {
            charging = false;

            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;

                IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                try
                {
                    int pct = ListenForStatus(h, ev, windowMs, out charging, $"{vid:X4}:{pid:X4}");
                    if (pct >= 0) return pct;
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }

            return -1;
        }

        private static int ListenForStatus(IntPtr h, IntPtr ev, int windowMs, out bool charging, string identityKey)
        {
            charging = false;

            var caps = Caps(h);
            int len = caps.InputReportByteLength;
            if (len <= 0) return -1;

            var buf = new byte[len];
            int deadline = Environment.TickCount + windowMs;

            while (true)
            {
                int remain = deadline - Environment.TickCount;
                if (remain <= 0) return -1;

                int slice = remain < InphicSliceMs ? remain : InphicSliceMs;
                if (!Transfer(h, ev, buf, false, slice, out int got)) continue;   // 超时就再等一轮
                if (got < 4) continue;

                if (TryParseInphicStatus(new ReadOnlySpan<byte>(buf, 0, got), out int level, out bool chg, out byte model))
                {
                    if (model == InphicModelIn9Pro)
                    {
                        _identity[identityKey] = "I149";
                        SaveIdentities();
                    }
                    charging = chg;
                    return level;
                }

                Array.Clear(buf, 0, buf.Length);
            }
        }

        // ---------- 主动查询：模仿官方 open 时那一帧 feature，再听一轮 ----------

        /// <summary>
        /// 桥在 hid_open_path 之后对 usage 0x0B 的集合发过一帧 10 字节 feature：
        ///   0C 0A 01 FE 01 FE 00 00 00 00
        /// 出自 FUN_00409820：local_6c = 0xFE010A0C、local_68 = 0xFE01，随后 hid_send_feature_report(h, buf, 10)。
        /// 官方全程只主动写这一次，当初始化/唤醒用。鼠标厂商集合上发同一帧没有实证，
        /// 所以只在被动监听失败后才发，失败就当没有，不影响后面的兜底。
        /// </summary>
        private static readonly byte[] InphicWakeFrame =
        {
            0x0C, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0x00, 0x00, 0x00, 0x00
        };

        private const int InphicWakeWindowMs = 800;

        /// <summary>按 feature 报告长度补齐的唤醒帧；离线可直接断言。</summary>
        public static byte[] BuildInphicWakeRequest(int featureReportLength)
        {
            int len = featureReportLength > InphicWakeFrame.Length
                ? featureReportLength
                : InphicWakeFrame.Length;
            var req = new byte[len];
            Array.Copy(InphicWakeFrame, req, InphicWakeFrame.Length);
            return req;
        }

        private static int TryReadInphicWake(ushort vid, ushort pid, out bool charging)
        {
            charging = false;

            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;

                IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                try
                {
                    var caps = Caps(h);
                    if (caps.FeatureReportByteLength <= 0) continue;

                    int len = caps.FeatureReportByteLength > InphicWakeFrame.Length
                        ? caps.FeatureReportByteLength
                        : InphicWakeFrame.Length;
                    var req = BuildInphicWakeRequest(len);
                    if (!HidD_SetFeature(h, req, len)) continue;

                    int pct = ListenForStatus(h, ev, InphicWakeWindowMs, out charging, $"{vid:X4}:{pid:X4}");
                    if (pct >= 0) return pct;
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }

            return -1;
        }

        /// <summary>
        /// 状态帧解析。原始报告两种排布：
        ///   有编号报告：[id] [机型] [0x40/0x41] [状态] [电量]
        ///   无编号报告：[机型] [0x40/0x41] [状态] [电量]
        /// 统一按「先找到 0x40/0x41，再看它前一位是不是机型号」处理，两种都能中。
        /// 电量 &gt; 100 直接判假，避免把别的 0x40 当成电量。
        /// </summary>
        public static bool TryParseInphicStatus(ReadOnlySpan<byte> report, out int level, out bool charging) =>
            TryParseInphicStatus(report, out level, out charging, out _);

        private static bool TryParseInphicStatus(ReadOnlySpan<byte> report, out int level, out bool charging, out byte model)
        {
            model = 0;
            level = -1;
            charging = false;

            for (int i = 1; i + 2 < report.Length; i++)
            {
                if (report[i] != InphicStatusTagA && report[i] != InphicStatusTagB) continue;
                if (Array.IndexOf(InphicModelIds, report[i - 1]) < 0) continue;

                byte status = report[i + 1];
                byte pct = report[i + 2];

                switch (status)
                {
                    case InphicBatteryPercent when pct <= 100:
                        level = pct;
                        charging = false;
                        model = report[i - 1];
                        return true;

                    case InphicBatteryFull:
                        level = 100;
                        charging = false;
                        model = report[i - 1];
                        return true;

                    case InphicBatteryCharging when pct <= 100:
                        level = pct;
                        charging = true;
                        model = report[i - 1];
                        return true;
                }
            }

            return false;
        }

        // ---------- 兜底：SC525 那套 16 字节输出 → 32 字节输入 ----------

        private const byte InphicSc525ReportId = 0x0E;
        private const byte InphicSc525SubCommand = 0x20;
        private const int InphicSc525OutputBytes = 16;
        private const int InphicSc525MinInputBytes = 9;

        /// <summary>
        /// 候选 A 的离线解析，不发帧，可以先拿去写断言。
        /// 判据照 device-db 里 SC525 那条原文：
        ///   raw[0]==0x0E 且 raw[1]==0x20   回显请求
        ///   raw[8] &amp; 0x7F                 电量，0..100
        ///   raw[8] &amp; 0x80                 充电位
        ///   raw[9] &amp; 0x80 == 0            休眠（方向照原文抄，真机还要复核）
        /// 休眠时电量字节仍是鼠标最后上报的值，采不采由调用方定。
        /// </summary>
        public static bool TryParseInphicSc525(ReadOnlySpan<byte> report,
            out int level, out bool charging, out bool sleeping)
        {
            level = -1;
            charging = false;
            sleeping = false;

            if (report.Length < InphicSc525MinInputBytes) return false;
            if (report[0] != InphicSc525ReportId || report[1] != InphicSc525SubCommand) return false;

            int raw = report[8];
            int pct = raw & 0x7F;
            if (pct > 100) return false;

            level = pct;
            charging = (raw & 0x80) != 0;
            sleeping = (report[9] & 0x80) == 0;
            return true;
        }

        private static int TryReadInphicSc525(ushort vid, ushort pid, out bool charging)
        {
            charging = false;

            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;

                IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                try
                {
                    var caps = Caps(h);
                    int frameLen = caps.OutputReportByteLength > 0
                        ? caps.OutputReportByteLength
                        : caps.FeatureReportByteLength;
                    if (frameLen < InphicSc525OutputBytes) frameLen = InphicSc525OutputBytes;

                    var req = new byte[frameLen];
                    req[0] = InphicSc525ReportId;
                    req[1] = InphicSc525SubCommand;
                    if (req.Length > 2) req[2] = 0x00;

                    var resp = Transact(h, ev, req, 600);
                    if (resp == null) continue;

                    if (TryParseInphicSc525(resp, out int level, out bool chg, out _))
                    {
                        charging = chg;
                        return level;
                    }
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }

            return -1;
        }
    }
}
