using System;
using System.Collections.Generic;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// 前行者（Eweadn）电量读取。两套协议，来自官方两次网页驱动。
    ///
    /// A. CH315（2.0 驱动 / 3.0 的「江盟」方案，同一套）
    ///    来源 https://hub.eweadn.cn/  assets/index-DYNLTg1s.js
    ///         https://new-hub.eweadn.cn/ main.1407.sdk-mouse-jiangmeng.js
    ///    帧 16 字节，report id 0x08：
    ///        [0]  0x04  BatteryLevel
    ///        [15] (0x55 - sum(frame[0..14]) & 0xFF) - 8
    ///    响应：
    ///        [5]      电量百分比
    ///        [6] == 1 充电中
    ///        [7..8]   电压 mV，大端
    ///    有电压时用 VoltageToPercent 换算，没有就直取 [5]。
    ///
    /// B. 北赢 SDK（3.0 的「beiying」方案）
    ///    来源 main.112f49b7c4d10f367d47.js 的模块 68572（字符串表旋转 39 次后可读）
    ///    帧 63 字节：
    ///        [0]  命令 0x87 查电量 / 0x84 子命令 19 查电压
    ///        [1]  参数
    ///        [3]  总包长
    ///        [4]  当前包序
    ///        [5]  数据长度
    ///        [62] 校验 = 255 - (sum(frame) % 256)
    ///
    /// 两套都未经真机验证。
    /// </summary>
    internal static partial class VendorProtocol
    {
        // ---------- 设备身份 ----------

        /// <summary>前行者 X87 三模键盘。</summary>
        public const ushort EweadnX87Vid = 0x0C45;
        public const ushort EweadnX87Pid = 0x8006;

        /// <summary>前行者用过的 VID 号段。0C45 = Sonix，其余见官方清单。</summary>
        private static readonly ushort[] EweadnVids = { 0x0C45, 0x3554, 0xA8A4, 0xA8A5, 0x04F3 };

        public static bool IsEweadnVid(ushort vid) => Array.IndexOf(EweadnVids, vid) >= 0;

        // ---------- 对外入口 ----------

        /// <summary>依次试两套协议，谁先读到 1..100 就用谁。全失败返回 -1。</summary>
        public static int TryReadEweadn(ushort vid, ushort pid)
        {
            int pct = TryReadEweadnCh315(vid, pid);
            if (pct >= 0) return pct;

            pct = TryReadEweadnBeiying(vid, pid);
            if (pct >= 0) return pct;

            return -1;
        }

        // ---------- A. CH315 ----------

        private const byte Ch315ReportId = 0x08;
        private const byte Ch315CmdBattery = 0x04;
        private const int Ch315FrameBytes = 16;

        private static int TryReadEweadnCh315(ushort vid, ushort pid)
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
                    foreach (int outLen in new[] { 17, 16, 64 })
                    {
                        if (caps.OutputReportByteLength != outLen &&
                            caps.FeatureReportByteLength != outLen) continue;

                        var frame = BuildCh315Frame(outLen);
                        var resp = Transact(h, ev, frame, 600);
                        if (resp == null || resp.Length < 9) continue;

                        int off = resp[0] == Ch315ReportId ? 1 : 0;
                        if (resp.Length < off + 9) continue;

                        int level = resp[off + 5];
                        int voltage = (resp[off + 7] << 8) | resp[off + 8];

                        int pct = voltage > 0
                            ? EweadnVoltageToPercent(voltage)
                            : (level >= 1 && level <= 100 ? level : -1);

                        if (pct >= 1 && pct <= 100) return pct;
                    }
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }

        private static byte[] BuildCh315Frame(int totalLen)
        {
            var frame = new byte[totalLen];
            frame[0] = Ch315ReportId;

            var data = new byte[Ch315FrameBytes];
            data[0] = Ch315CmdBattery;
            data[15] = 0xEF;
            data[15] = (byte)((Ch315Checksum(data) - Ch315ReportId) & 0xFF);

            Array.Copy(data, 0, frame, 1, Math.Min(Ch315FrameBytes, totalLen - 1));
            return frame;
        }

        /// <summary>CH315 校验：前 15 字节求和 &amp; 0xFF，再 0x55 减它。</summary>
        private static byte Ch315Checksum(byte[] data)
        {
            int sum = 0;
            for (int i = 0; i < data.Length - 1; i++) sum += data[i];
            return (byte)(0x55 - (sum & 0xFF));
        }

        // ---------- B. 北赢 ----------

        private const byte BeiyingReportId = 0x08;
        private const byte BeiyingCmdBattery = 0x87;
        private const byte BeiyingCmdVoltage = 0x84;
        private const byte BeiyingVoltageParam = 19;
        private const int BeiyingFrameBytes = 63;

        private static int TryReadEweadnBeiying(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;

                IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                try
                {
                    var req = BuildBeiyingFrame(BeiyingCmdBattery, 0);
                    var resp = Transact(h, ev, req, 600);
                    if (resp != null && resp.Length >= 8)
                    {
                        int off = resp[0] == BeiyingReportId ? 1 : 0;
                        if (resp.Length > off + 1)
                        {
                            int cmd = resp[off];
                            int pct = resp[off + 1];

                            if (cmd == BeiyingCmdBattery && pct >= 1 && pct <= 100) return pct;

                            if (cmd == BeiyingCmdBattery && resp.Length > 5)
                            {
                                pct = resp[5];
                                if (pct >= 1 && pct <= 100) return pct;
                            }
                        }
                    }

                    var vreq = BuildBeiyingFrame(BeiyingCmdVoltage, BeiyingVoltageParam);
                    var vresp = Transact(h, ev, vreq, 600);
                    if (vresp != null && vresp.Length >= 10)
                    {
                        int off = vresp[0] == BeiyingReportId ? 1 : 0;
                        if (vresp.Length >= off + 2)
                        {
                            int mv = (vresp[off] << 8) | vresp[off + 1];
                            if (mv >= 2500 && mv <= 5000) return EweadnVoltageToPercent(mv);
                        }
                    }
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>构造北赢帧：report id 在 [0]，命令从 [1] 开始，校验在 [62]。</summary>
        private static byte[] BuildBeiyingFrame(byte cmd, byte param)
        {
            var frame = new byte[BeiyingFrameBytes];
            frame[0] = BeiyingReportId;
            frame[1] = cmd;
            frame[2] = param;
            frame[4] = BeiyingFrameBytes;
            frame[5] = 0;
            frame[6] = 0;
            frame[62] = 0;
            frame[62] = BeiyingChecksum(frame);
            return frame;
        }

        /// <summary>北赢校验：255 - (sum % 256)。</summary>
        private static byte BeiyingChecksum(byte[] frame)
        {
            int sum = 0;
            for (int i = 0; i < frame.Length; i++) sum += frame[i];
            return (byte)(255 - (sum % 256));
        }

        // ---------- y0：电压转百分比 ----------

        /// <summary>江盟 y0 的查找表。21 个点，每档 5 个百分点，3050mV 到 4110mV。</summary>
        private static readonly int[] EweadnVoltageTable =
        {
            3050, 3420, 3480, 3540, 3600, 3660, 3720, 3760, 3800, 3840,
            3880, 3920, 3940, 3960, 3980, 4000, 4020, 4040, 4060, 4080, 4110
        };

        /// <summary>电压转百分比，逐行照抄 y0()。超过 4110 直接 100，低于 3050 直接 0。</summary>
        private static int EweadnVoltageToPercent(int mv)
        {
            var a = EweadnVoltageTable;

            if (mv > a[a.Length - 1]) return 100;

            int i = 0;
            int r = -1;
            for (int t = 0; t < a.Length; t++)
            {
                if (mv < a[t]) { r = t; break; }
            }

            if (r != -1)
            {
                if (r == 0)
                {
                    i = 0;
                }
                else
                {
                    double step = (a[r] - a[r - 1]) / 5.0;
                    i = (int)((mv - a[r - 1]) / step + 5 * (r - 1));
                }
            }

            if (i == 0 || i == 15) i++;
            return Math.Max(0, Math.Min(100, (int)Math.Round((double)i)));
        }
    }
}