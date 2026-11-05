using System;
using System.Collections.Generic;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// 黑爵 AJAZZ 电量读取。
    ///
    /// 来源：https://ajazz.shinetek-ic.cn/ 的网页驱动（ajazz.shinetek-ic.net）
    /// 混淆程度很低，协议是明文。
    ///
    /// 判定条件（原文）：
    ///   170 === e[0] && 14 === e[4]
    ///   即命令字节 0xAA，第 5 字节（索引 4）为 14 表示状态帧长度
    ///
    /// 响应字段：
    ///   [0]      0xAA        命令回显
    ///   [4]      14          状态帧标识
    ///   [5]      电量原始值   0..100，保留设备报告的百分比
    ///   [6]      在线标志     USB 或 2.4G 视为在线
    ///   [7]      充电标志
    ///   [8..9]   实际 VID     大端
    ///   [10..11] 实际 PID     大端
    ///   [12..]   固件版本
    ///
    /// 设备号段（原文的 vendorId / productId 过滤）：
    ///   VID 6700 = 0x1A2C
    ///   PID 27777 28289 28545 25217 25473 25729 25985 26241
    ///       = 0x6C81 0x6E81 0x6F81 0x6281 0x6381 0x6481 0x6581 0x6681
    ///
    /// 未经真机验证。
    /// </summary>
    internal static partial class VendorProtocol
    {
        // ---------- 设备身份 ----------

        private const ushort AjazzVid = 0x1A2C;

        private static readonly ushort[] AjazzPids =
        {
            0x6281, 0x6381, 0x6481, 0x6581, 0x6681,
            0x6C81, 0x6E81, 0x6F81
        };

        public static bool IsAjazzVid(ushort vid) => vid == AjazzVid;

        public static bool IsAjazzPid(ushort pid) => Array.IndexOf(AjazzPids, pid) >= 0;

        // ---------- 协议常量 ----------

        private const byte AjazzCmdStatus = 0xAA;   // 170
        private const byte AjazzStatusTag = 14;     // [4] 的状态帧标识
        private const int  AjazzMinReport = 14;

        /// <summary>读电量。读不到返回 -1。</summary>
        public static int TryReadAjazz(ushort vid, ushort pid)
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
                    if (!IsAjazzQueryInterface(caps.UsagePage, caps.OutputReportByteLength)) continue;

                    // 请求帧：命令 0xAA，长度标识 14，其余补零
                    var req = new byte[caps.OutputReportByteLength];
                    req[0] = AjazzCmdStatus;
                    req[4] = AjazzStatusTag;

                    var resp = Transact(h, ev, req, 600);
                    int battery = ParseAjazzBattery(resp, vid, pid);
                    if (battery >= 0) return battery;
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }

        private static bool IsAjazzQueryInterface(ushort usagePage, ushort outputLength) =>
            usagePage >= 0xFF00 && outputLength >= AjazzMinReport;

        private static int ParseAjazzBattery(byte[] response, ushort vid, ushort pid)
        {
            if (response == null || response.Length < AjazzMinReport) return -1;
            int offset = response[0] == AjazzCmdStatus ? 0
                : response[0] == 0 && response[1] == AjazzCmdStatus ? 1 : -1;
            if (offset < 0 || response.Length < offset + AjazzMinReport ||
                response[offset + 4] != AjazzStatusTag)
                return -1;

            int responseVid = (response[offset + 8] << 8) | response[offset + 9];
            int responsePid = (response[offset + 10] << 8) | response[offset + 11];
            if (responseVid != vid || responsePid != pid) return -1;
            int battery = response[offset + 5];
            return battery <= 100 ? battery : -1;
        }
    }
}
