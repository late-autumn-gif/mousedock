using System;
using System.Runtime.InteropServices;

namespace MouseDock.Services.Detection
{
    internal static partial class VendorProtocol
    {
        /// <summary>
        /// 第二批厂商读取器。帧形已经从反汇编里确认过，报告 ID 和命令字都是明确的；
        /// 报告 ID 没确认的那几个（SCYROX、WAIZOWL、Sora V3、WEBBRANDS）不在这里，别硬猜。
        /// </summary>
        public static int TryReadFamily(ushort vid, ushort pid)
        {
            switch (vid)
            {
                case 0x363C: return ReadAusdom(vid, pid);
                case 0x3151: return ReadInfinity100(vid, pid);
                case 0x3886: return ReadZynoo(vid, pid);
                case 0x32C2: return ReadAulaSc525(vid, pid);
                case 0x391D:
                case 0x362D:
                    if (pid == 0x102E || pid == 0x1A2A || pid == 0x1037 || pid == 0x1A29)
                        return ReadVgnLd(vid, pid);
                    return ReadTwoStep(vid, pid);
                case 0x24AE: return pid == 0x1423 ? ReadRapooVt2(vid, pid) : ReadRapooV2(vid, pid);
            }
            return -1;
        }

        private static IntPtr OpenOverlapped(string path, out IntPtr ev)
        {
            ev = IntPtr.Zero;
            IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (h == IntPtr.Zero || h == new IntPtr(-1)) return IntPtr.Zero;
            ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
            return h;
        }

        /// <summary>AUSDOM G03 Pro 8K：64 字节，请求 0B 13，电量 b[18]，充电 b[17]&amp;1。</summary>
        private static int ReadAusdom(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = OpenOverlapped(path, out IntPtr ev);
                if (h == IntPtr.Zero) continue;
                try
                {
                    if (Caps(h).OutputReportByteLength != 64) continue;

                    var req = new byte[64];
                    req[0] = 0x0B;
                    req[1] = 0x13;
                    var resp = Transact(h, ev, req, 600);
                    if (resp == null || resp.Length < 21) continue;
                    if (resp[0] != 0x0B || resp[1] != 0x13) continue;
                    if (resp[3] != 0x00) continue;

                    int pct = resp[18];
                    if (pct >= 1 && pct <= 100) return pct;
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>AM Infinity .100：65 字节 feature，命令字就是首字节 0xD6，校验 b[7]=255-sum(b[:7])，电量 b[3]。</summary>
        private static int ReadInfinity100(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;
                try
                {
                    int len = Caps(h).FeatureReportByteLength;
                    if (len < 8 || len > 128) continue;

                    var req = new byte[len];
                    req[0] = 0xD6;
                    int sum = 0;
                    for (int i = 0; i < 7; i++) sum += req[i];
                    req[7] = (byte)(255 - sum % 256);

                    if (!HidD_SetFeature(h, req, len)) continue;
                    var resp = new byte[len];
                    if (!HidD_GetFeature(h, resp, len)) continue;
                    if (resp[0] != 0xD6) continue;

                    int pct = resp[3];
                    if (pct >= 1 && pct <= 100) return pct;
                }
                catch { }
                finally { CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>ZYNOO GP1：17 字节，请求 00 9A 00 00 08，响应剥首字节后 b[10] 低 7 位是电量、最高位是充电。</summary>
        private static int ReadZynoo(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = OpenOverlapped(path, out IntPtr ev);
                if (h == IntPtr.Zero) continue;
                try
                {
                    if (Caps(h).OutputReportByteLength != 17) continue;

                    var req = new byte[17];
                    req[1] = 0x9A;
                    req[4] = 0x08;
                    var raw = Transact(h, ev, req, 600);
                    if (raw == null || raw.Length < 13) continue;
                    if (raw[1] != 0x9A) continue;

                    int pct = raw[11] & 0x7F;
                    if (pct >= 1 && pct <= 100) return pct;
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>AULA / INPHIC SC525：16 字节输出、32 字节输入，请求 0E 20，电量 b[8] 低 7 位。</summary>
        private static int ReadAulaSc525(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = OpenOverlapped(path, out IntPtr ev);
                if (h == IntPtr.Zero) continue;
                try
                {
                    var caps = Caps(h);
                    if (caps.OutputReportByteLength != 16) continue;

                    var req = new byte[16];
                    req[0] = 0x0E;
                    req[1] = 0x20;
                    var resp = Transact(h, ev, req, 600);
                    if (resp == null || resp.Length < 9) continue;
                    if (resp[0] != 0x0E || resp[1] != 0x20) continue;

                    int pct = resp[8] & 0x7F;
                    if (pct >= 1 && pct <= 100) return pct;
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>
        /// RIDLAG Owl X 8K / VGN F1 V5：两步。B5 03 先列出配对设备，B3 06 再读电量，
        /// 电量在 b[20] 低 7 位、最高位是充电。
        /// </summary>
        private static int ReadTwoStep(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = OpenOverlapped(path, out IntPtr ev);
                if (h == IntPtr.Zero) continue;
                try
                {
                    int outLen = Caps(h).OutputReportByteLength;
                    if (outLen != 21 && outLen != 62 && outLen != 64) continue;

                    var list = new byte[outLen];
                    list[0] = 0xB5;
                    list[1] = 0x03;
                    Transact(h, ev, list, 400);   // 设备列表本身不解，只用来让接收器进入可读状态

                    var req = new byte[outLen];
                    req[0] = 0xB3;
                    req[1] = 0x06;
                    var resp = Transact(h, ev, req, 600);
                    if (resp == null || resp.Length < 21) continue;

                    int pct = resp[20] & 0x7F;
                    if (pct >= 1 && pct <= 100) return pct;
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>雷柏 0x14A5：写 33 字节 06 A5 AA，读 19 字节，b[7] 状态、b[8] 百分比。</summary>
        private static int ReadRapooV2(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = OpenOverlapped(path, out IntPtr ev);
                if (h == IntPtr.Zero) continue;
                try
                {
                    if (Caps(h).OutputReportByteLength != 33) continue;

                    var req = new byte[33];
                    req[0] = 0x06;
                    req[1] = 0xA5;
                    req[2] = 0xAA;
                    var resp = Transact(h, ev, req, 600);
                    if (resp == null || resp.Length < 9) continue;

                    int state = resp[7];
                    if (state > 2) continue;

                    int pct = resp[8];
                    if (pct >= 1 && pct <= 100) return pct;
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }
    }
}
