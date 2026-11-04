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
            // Neon75 Air uses Rongyuan Feature F7, not this keyboard-v2 Output protocol.
            if (IsNeon75Air(vid, pid)) return -1;
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;

                IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                try
                {
                    var frame = new byte[VgnFrameBytes];
                    frame[0] = VgnCmdThreeMode;
                    frame[1] = VgnSubGetBasicInfo;

                    var resp = Transact(h, ev, frame, 600);
                    if (resp == null || resp.Length < 5) continue;

                    // 有些固件回包首字节是 report id，剥一位再对齐
                    int off = resp.Length >= 6 && resp[0] != VgnCmdThreeMode && resp[1] == VgnCmdThreeMode ? 1 : 0;
                    if (resp.Length < off + 5) continue;

                    if (resp[off] != VgnCmdThreeMode) continue;

                    int charge  = resp[off + 3];
                    int battery = resp[off + 4];

                    _ = charge;   // 充电位暂不向上暴露，MouseDock 目前只显示百分比

                    if (battery >= 1 && battery <= 100) return battery;
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }
    }
}
