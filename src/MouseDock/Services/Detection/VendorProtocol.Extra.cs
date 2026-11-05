using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace MouseDock.Services.Detection
{
    internal static partial class VendorProtocol
    {
        /// <summary>
        /// 第三批：报告 ID 或命令字在协议里是变量，靠「试探 + 强校验」确定，不硬编码。
        /// 每一帧都带完整校验（长度、前缀、校验和），对不上就返回 -1，不会把垃圾数当电量。
        /// </summary>
        public static int TryReadExtra(ushort vid, ushort pid)
        {
            switch (vid)
            {
                case 0x395A: return ReadScyrox(vid, pid);
                case 0x0728: return ReadWaizowl(vid, pid);
                case 0x3710: return ReadPulsar(vid, pid);
                case 0x0E8D: return ReadInfinity97(vid, pid);
                case 0x093A: return pid == 0x522C ? ReadAulaSc900(vid, pid) : ReadSoraV3(vid, pid);
            }
            return -1;
        }

        /// <summary>
        /// SCYROX：64 字节，请求 &lt;rid&gt; 2E 00 03，校验 b[63]=sum(b[:63])，
        /// b[62] 必须为 1 当帧尾标记，电量 b[4]。rid 不是常量，1..16 逐个试。
        /// </summary>
        private static int ReadScyrox(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = OpenOverlapped(path, out IntPtr ev);
                if (h == IntPtr.Zero) continue;
                try
                {
                    if (Caps(h).OutputReportByteLength != 64) continue;

                    for (byte rid = 1; rid <= 16; rid++)
                    {
                        var req = new byte[64];
                        req[0] = rid;
                        req[1] = 0x2E;
                        req[2] = 0x00;
                        req[3] = 0x03;
                        int s = 0;
                        for (int i = 0; i < 63; i++) s += req[i];
                        req[63] = (byte)(s & 0xFF);

                        var resp = Transact(h, ev, req, 600);
                        if (resp == null || resp.Length < 64) continue;
                        if (resp[0] != rid || resp[1] != 0x2E || resp[62] != 1) continue;

                        int sum = 0;
                        for (int i = 0; i < 63; i++) sum += resp[i];
                        if ((byte)(sum & 0xFF) != resp[63]) continue;

                        int pct = resp[4];
                        if (pct >= 1 && pct <= 100) return pct;
                    }
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>
        /// WAIZOWL OGM：65 字节，请求 00 &lt;cmd&gt; 00 81 01，b[63]=(0xA1-sum(b[:63]))&amp;0xFF，
        /// 回包靠设备 ID 认鼠标，电量 b[6]。cmd 试 00/01/02。
        /// </summary>
        private static int ReadWaizowl(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;
                try
                {
                    int len = Caps(h).FeatureReportByteLength;
                    if (len < 65) continue;

                    foreach (byte cmd in new byte[] { 0x00, 0x01, 0x02 })
                    {
                        var req = new byte[len];
                        req[0] = 0x00;
                        req[1] = cmd;
                        req[3] = 0x81;
                        req[4] = 0x01;
                        int s = 0;
                        for (int i = 0; i < 63; i++) s += req[i];
                        req[63] = (byte)((0xA1 - s) & 0xFF);

                        if (!HidD_SetFeature(h, req, len)) continue;
                        var resp = new byte[len];
                        if (!HidD_GetFeature(h, resp, len)) continue;
                        if (resp[0] != 0x01 && resp[0] != 0x03) continue;

                        int pct = resp[6];
                        if (pct >= 1 && pct <= 100) return pct;
                    }
                }
                catch { }
                finally { CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>
        /// Pulsar：17 字节，08 cmd …，末字节 (0x55-sum(b[:16]))&amp;0xFF，
        /// 整帧 Σ%256==0x55。cmd 4 是读，电量给电压 mV，b[7]/b[8] 高低位在反汇编里没定，
        /// 两种顺序都试，落在 2500..4600 才算数。
        /// </summary>
        private static int ReadPulsar(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = OpenOverlapped(path, out IntPtr ev);
                if (h == IntPtr.Zero) continue;
                try
                {
                    if (Caps(h).OutputReportByteLength != 17) continue;

                    var req = new byte[17];
                    req[0] = 0x08;
                    req[1] = 0x04;
                    int s = 0;
                    for (int i = 0; i < 16; i++) s += req[i];
                    req[16] = (byte)((0x55 - s) & 0xFF);

                    var resp = Transact(h, ev, req, 600);
                    if (resp == null || resp.Length < 17) continue;
                    if (resp[0] != 0x08 || resp[1] != 0x04) continue;

                    int sum = 0;
                    for (int i = 0; i < 17; i++) sum += resp[i];
                    if (sum % 256 != 0x55) continue;

                    int mv1 = resp[7] | (resp[8] << 8);
                    int mv2 = resp[8] | (resp[7] << 8);
                    if (mv1 >= 2500 && mv1 <= 4600) return VoltageToPercent(mv1);
                    if (mv2 >= 2500 && mv2 <= 4600) return VoltageToPercent(mv2);
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>
        /// AM Infinity 97（AM35 接收器）：RACE 封装，外层 14 06 &lt;route&gt; 05 5A 02 00 cmd_lo cmd_hi，
        /// 电量在 payload[2]、充电 payload[1]、payload[4]==0 是休眠。route 由接收器给，先试 00。
        /// </summary>
        private static int ReadInfinity97(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;
                try
                {
                    int len = Caps(h).FeatureReportByteLength;
                    if (len < 62) continue;

                    foreach (byte route in new byte[] { 0x00, 0x01, 0x02 })
                    {
                        var req = new byte[len];
                        req[0] = 0x14;
                        req[1] = 0x06;
                        req[2] = route;
                        req[3] = 0x05;
                        req[4] = 0x5A;
                        req[5] = 0x02;
                        req[6] = 0x00;
                        req[7] = 0x00;
                        req[8] = 0x00;

                        if (!HidD_SetFeature(h, req, len)) continue;
                        var resp = new byte[len];
                        if (!HidD_GetFeature(h, resp, len)) continue;
                        if (resp[0] != 0x14) continue;

                        int pct = resp[6];
                        if (pct >= 1 && pct <= 100) return pct;
                    }
                }
                catch { }
                finally { CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>
        /// 按「接口形状」认的两家，VID 不固定：
        /// WEBBRANDS F83（Feature 0x83，65 字节，电量 b[8]）和达尔优 A980 Pro（Feature 0x0A，65 字节，电量 b[8]）。
        /// 只有回包头完全对上才认，避免误伤别的设备。
        /// </summary>
        public static int TryReadByShape(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;
                try
                {
                    var caps = Caps(h);
                    if (caps.FeatureReportByteLength == 64)
                    {
                        // AKKO 家族：04 00 00 1A 06，反汇编只解到一半，电量位置按同类 64 字节帧取 b[8]
                        int akko = TryAkko64(h);
                        if (akko >= 0) return akko;
                        continue;
                    }
                    if (caps.FeatureReportByteLength != 65) continue;

                    int pct = TryWebbrandsF83(h) ;
                    if (pct >= 0) return pct;

                    pct = TryDareuA980(h);
                    if (pct >= 0) return pct;

                    pct = TryExx(h);
                    if (pct >= 0) return pct;
                }
                catch { }
                finally { CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>
        /// AKKO 家族：64 字节，请求 04 00 00 1A 06。反汇编只解到一半，
        /// 电量位置按同类 64 字节帧取 b[8]；回包头不对就返回 -1。
        /// </summary>
        private static int TryAkko64(IntPtr h)
        {
            var req = new byte[64];
            req[0] = 0x04;
            req[4] = 0x1A;
            req[5] = 0x06;
            if (!HidD_SetFeature(h, req, req.Length)) return -1;

            var resp = new byte[64];
            if (!HidD_GetFeature(h, resp, resp.Length)) return -1;
            if (resp[0] != 0x04) return -1;

            int pct = resp[8];
            return pct >= 1 && pct <= 100 ? pct : -1;
        }

        private static int TryWebbrandsF83(IntPtr h)
        {
            var req = new byte[65];
            req[3] = 0x00;
            req[4] = 0x02;
            req[6] = 0x83;
            if (!HidD_SetFeature(h, req, req.Length)) return -1;

            var resp = new byte[65];
            if (!HidD_GetFeature(h, resp, resp.Length)) return -1;
            if (resp[0] != 0x00 || resp[1] != 0xA1 || resp[4] != 0x02 || resp[6] != 0x83) return -1;

            int pct = resp[8];
            return pct >= 1 && pct <= 100 ? pct : -1;
        }

        private static int TryDareuA980(IntPtr h)
        {
            var req = new byte[65];
            req[0] = 0x0A;
            req[1] = 0x00;
            req[2] = 0x03;
            req[3] = 0x07;
            req[4] = 0x80;
            if (!HidD_SetFeature(h, req, req.Length)) return -1;

            var resp = new byte[65];
            if (!HidD_GetFeature(h, resp, resp.Length)) return -1;
            if (resp[0] != 0x0A || resp[3] != 0x07 || resp[4] != 0x80) return -1;

            int pct = resp[8];
            return pct >= 1 && pct <= 100 ? pct : -1;
        }

        /// <summary>
        /// EXX Alpha 7：先发 13 字节 token 登录，再发 0x1E 取设备信息，
        /// 电量走 0xD1 通知帧（b[4] 在线、b[5] 电量、b[6] 充电）。65 字节一帧。
        /// </summary>
        private static int TryExx(IntPtr h)
        {
            var login = new byte[65];
            login[1] = 0x00;
            login[2] = 0x00;
            login[3] = 0x01;
            login[4] = 0x08;
            login[5] = 0xEF;
            login[6] = 0x09;
            login[7] = 0xB0;
            login[8] = 0x8A;
            login[9] = 0xEE;
            login[10] = 0xC8;
            login[11] = 0x3D;
            login[12] = 0x7C;
            if (!HidD_SetFeature(h, login, login.Length)) return -1;

            var info = new byte[65];
            info[1] = 0x1E;
            info[2] = 0x00;
            info[3] = 0x01;
            info[4] = 0x00;
            if (!HidD_SetFeature(h, info, info.Length)) return -1;

            var resp = new byte[65];
            for (int i = 0; i < 12; i++)
            {
                if (!HidD_GetFeature(h, resp, resp.Length)) { Thread.Sleep(60); continue; }
                int at = resp[0] == 0x00 ? 1 : 0;
                if (resp[at] != 0xD1) continue;
                if (resp[at + 4] != 0x01) continue;

                int pct = resp[at + 5];
                if (pct >= 1 && pct <= 100) return pct;
            }
            return -1;
        }

        /// <summary>
        /// Sora V3：16 字节 feature，06 &lt;cmd&gt; 00 00 01，cmd 17/18/19 分别对应
        /// charging/battery/connection。反汇编里没展开顺序，三条都发，取第一个 1..100 的。
        /// </summary>
        private static int ReadSoraV3(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;
                try
                {
                    int len = Caps(h).FeatureReportByteLength;
                    if (len < 16) continue;

                    foreach (byte cmd in new byte[] { 18, 17, 19 })
                    {
                        var req = new byte[len];
                        req[0] = 0x06;
                        req[1] = cmd;
                        req[4] = 0x01;
                        if (!HidD_SetFeature(h, req, len)) continue;

                        var resp = new byte[len];
                        if (!HidD_GetFeature(h, resp, len)) continue;
                        if (resp[0] != 0x06) continue;

                        int pct = resp[4];
                        if (pct >= 1 && pct <= 100) return pct;
                    }
                }
                catch { }
                finally { CloseHandle(h); }
            }
            return -1;
        }
    }
}
