using System;
using System.Collections.Generic;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// VGN 键盘电量读取。
    ///
    /// 来源：D:\VGN VHUB\resources\app.asar 里带的 TypeScript 源码
    ///   node_modules/@sparklinkplayjoy/protocol-keyboard-v2/
    ///   node_modules/@sparklinkplayjoy/sdk-keyboard-v2/
    ///
    /// 这是方案商 SDK，命令字和组帧都是源码级公开的。
    ///
    ///   命令字（enum 里的模块 ID）：
    ///     0x0D  ThreeMode
    ///
    ///   子命令：
    ///     0x01  GetBasicInfo      ← 电量在这里
    ///     0x02  SetSleepTime
    ///     0x03  GetSleepTime
    ///
    ///   帧：64 字节全零，命令填前两个字节
    ///     [0] = 0x0D
    ///     [1] = 0x01
    ///   **没有校验和**，源码里的 fe() 就是补零拷贝。
    ///
    ///   响应：
    ///     [2] = mode     0=USB 1=2.4G 2=蓝牙1 3=蓝牙2 4=蓝牙3
    ///     [3] = charge   充电标志
    ///     [4] = battery  电量百分比
    ///
    /// SDK 原代码：
    ///   fe = e => { const t = new Uint8Array(64).fill(0);
    ///               e.forEach((r, i) => { t[i] = r }); return t }
    ///   getBasicInfo() { return fe([ThreeMode, GetBasicInfo]) }
    ///   getBasicInfoData(e) { return { mode: e[2], charge: e[3], battery: e[4] } }
    ///
    /// 未经真机验证。
    /// </summary>
    internal static partial class VendorProtocol
    {
        // ---------- 设备身份 ----------

        private const ushort VgnVid = 0x391D;

        public static bool IsVgnVid(ushort vid) => vid == VgnVid;

        // ---------- 命令 ----------

        private const int  VgnFrameBytes = 64;
        private const byte VgnCmdThreeMode = 0x0D;
        private const byte VgnSubGetBasicInfo = 0x01;

        /// <summary>读电量。读不到返回 -1。</summary>
        public static int TryReadVgn(ushort vid, ushort pid)
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
                    if (!IsVgnQueryInterface(caps.UsagePage, caps.OutputReportByteLength)) continue;

                    var frame = new byte[VgnFrameBytes];
                    frame[0] = VgnCmdThreeMode;
                    frame[1] = VgnSubGetBasicInfo;

                    var resp = Transact(h, ev, frame, 600);
                    int battery = ParseVgnBattery(resp);
                    if (battery >= 0) return battery;
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }

        private static bool IsVgnQueryInterface(ushort usagePage, ushort outputLength) =>
            usagePage >= 0xFF00 && outputLength == VgnFrameBytes;

        private static int ParseVgnBattery(byte[] response)
        {
            if (response == null) return -1;
            int offset = response.Length == VgnFrameBytes ? 0
                : response.Length == VgnFrameBytes + 1 && response[0] == 0 ? 1 : -1;
            if (offset < 0 || response[offset] != VgnCmdThreeMode ||
                response[offset + 1] != VgnSubGetBasicInfo)
                return -1;
            int battery = response[offset + 4];
            return battery <= 100 ? battery : -1;
        }
    }
}
