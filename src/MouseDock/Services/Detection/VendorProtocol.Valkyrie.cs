using System;
using System.Collections.Generic;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// 瓦尔基里（Valkyrie）电量读取。三套来源，两套协议。
    ///
    /// A. 网页驱动（vkhub / qmk.top，feature report 上的 F6 / F7）
    ///    https://vkhub.valkyrie.com.cn/js/index.a2394467.js
    ///    https://qmk.top/js/index.BNcmQK86.js
    ///
    ///      F7 查 24G 状态，响应：
    ///        [0] == 1   可读
    ///        [1]        键盘电量
    ///        [2]        鼠标电量
    ///        [3] == 0   键盘在线
    ///        [4] == 0   鼠标在线
    ///        [5] == 1   可发
    ///
    ///      F6 切当前 24G 设备：[246, 10] 键盘 / [246, 5] 鼠标 / [246, 13] 两个都读
    ///
    ///      帧补到 9 字节，校验 Bit7: s[7] = 255 - (sum(s[0..6]) & 0xFF)
    ///      reportId 动态取，找第一个 type == feature 的报告。
    ///
    /// B. 桌面驱动（MFC，DeviceDriver.exe）
    ///    反编译自 D:\瓦尔基里\Valkyrie Driver\DeviceDriver.exe
    ///    关键函数 FUN_00443ae0（读电量调度）与 FUN_0045deb0（收发）
    ///
    ///      帧 65 字节，全零起步：
    ///        [0]  = 0x00
    ///        [1]  = 0x20        命令
    ///        [2]  = 0x01        子命令
    ///        [32] = sum(65 字节) & 0xFF
    ///      只发前 33 字节。
    ///
    ///      回包（report id 被传输层剥掉，整体左移一位）：
    ///        [0] = 0x20   命令回显
    ///        [1] = 0x01   子命令回显
    ///        [3] = 电量 0..100
    ///
    ///      充电状态读不到，原程序 SetBatteryInfo(ctrl, level, 0) 写死传 0。
    ///      读电量有 15 秒节流（14999ms），复现时不必照抄。
    ///
    /// 设备身份（config.xml 原文）：
    ///   VK87        05AC:024F  MI_02
    ///   VK99        05AC:024F  MI_02 (USB) / MI_03 (2.4G)
    ///   VK99 TFT    05AC:024F  MI_02 / MI_03
    ///   Valkyrie 75 05AC:024F  MI_02 / MI_03
    ///               0C45:FEFE  MI_03  （Sonix 2.4G Dongle）
    ///
    /// 05AC 是 Apple 的公版 VID，多家共用，只能靠产品名分。
    /// 两套协议都未经真机验证。
    /// </summary>
    internal static partial class VendorProtocol
    {
        // ---------- 设备身份 ----------

        private const ushort ValkyrieAppleVid = 0x05AC;
        private const ushort ValkyrieApplePid = 0x024F;
        private const ushort ValkyrieSonixVid = 0x0C45;
        private const ushort ValkyrieSonixPid = 0xFEFE;

        /// <summary>网页驱动里的私有号段（十进制 13434 = 0x343A）。</summary>
        private const ushort ValkyrieHubVid = 0x343A;

        /// <summary>产品名里出现这些就算瓦尔基里。公版 VID 只能这么认。</summary>
        private static readonly string[] ValkyrieNames =
        {
            "VK87", "VK99", "VK78", "VK75", "Valkyrie"
        };

        public static bool IsValkyrieDevice(ushort vid, ushort pid, string product)
        {
            if (vid == ValkyrieHubVid) return true;
            if (vid == ValkyrieSonixVid && pid == ValkyrieSonixPid) return true;

            if (vid == ValkyrieAppleVid && pid == ValkyrieApplePid)
            {
                if (string.IsNullOrEmpty(product)) return false;
                foreach (var n in ValkyrieNames)
                {
                    if (product.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            return false;
        }

        // ---------- 对外入口 ----------

        /// <summary>读电量。网页那套优先，读不到再试桌面驱动那套。读不到返回 -1。</summary>
        public static int TryReadValkyrie(ushort vid, ushort pid, bool keyboard)
        {
            int pct = TryReadValkyrieF7(vid, pid, keyboard);
            if (pct >= 0) return pct;

            return TryReadValkyrieMfc(vid, pid);
        }

        // ---------- A. 网页驱动 F7 ----------

        private const byte ValkyrieCmdStatus = 0xF7;
        private const byte ValkyrieCmdSelect = 0xF6;

        private static int TryReadValkyrieF7(ushort vid, ushort pid, bool keyboard)
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
                    if (caps.FeatureReportByteLength == 0) continue;

                    int frameLen = caps.FeatureReportByteLength;
                    if (frameLen < 9) frameLen = 9;

                    var req = BuildValkyrieFrame(new byte[] { ValkyrieCmdStatus }, frameLen, true);
                    var resp = Transact(h, ev, req, 600);
                    if (resp == null || resp.Length < 6) continue;

                    int off = resp[0] == 0x00 ? 1 : 0;
                    if (resp.Length < off + 6) continue;

                    if (resp[off] != 1) continue;

                    int kbLevel = resp[off + 1];
                    int msLevel = resp[off + 2];

                    int pick = keyboard ? kbLevel : msLevel;

                    // 键盘那格空但鼠标有值，反着取一次
                    if ((pick < 1 || pick > 100) && !keyboard && kbLevel >= 1 && kbLevel <= 100) pick = kbLevel;
                    if ((pick < 1 || pick > 100) && keyboard && msLevel >= 1 && msLevel <= 100) pick = msLevel;

                    if (pick >= 1 && pick <= 100) return pick;
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }

        /// <summary>构造网页驱动帧：补到 frameLen，命令在 [0]，校验按 Bit7 写 [7]。</summary>
        private static byte[] BuildValkyrieFrame(byte[] cmd, int frameLen, bool useBit7)
        {
            int len = Math.Max(9, Math.Max(frameLen, cmd.Length));
            var frame = new byte[len];

            Array.Copy(cmd, frame, Math.Min(cmd.Length, len));

            if (useBit7)
            {
                int sum = 0;
                for (int i = 0; i < 7 && i < len; i++) sum += frame[i];
                if (len > 7) frame[7] = (byte)(255 - (sum & 0xFF));
            }
            else
            {
                int sum = 0;
                for (int i = 0; i < 8 && i < len; i++) sum += frame[i];
                if (len > 8) frame[8] = (byte)(255 - (sum & 0xFF));
            }

            return frame;
        }

        /// <summary>切当前 24G 设备：keyboard=10 / mouse=5 / both=13。</summary>
        private static byte[] BuildValkyrieSelectFrame(bool keyboard, bool both, int frameLen)
        {
            byte dev = both ? (byte)13 : keyboard ? (byte)10 : (byte)5;
            return BuildValkyrieFrame(new byte[] { ValkyrieCmdSelect, dev }, frameLen, true);
        }

        // ---------- B. 桌面驱动 MFC ----------

        private const int ValkyrieMfcFrameBytes = 65;
        private const byte ValkyrieMfcCmd = 0x20;
        private const byte ValkyrieMfcSub = 0x01;
        private const int ValkyrieMfcChecksumIndex = 32;
        private const int ValkyrieMfcSendBytes = 33;

        private static int TryReadValkyrieMfc(ushort vid, ushort pid)
        {
            foreach (var path in Paths(vid, pid))
            {
                IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) continue;

                IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                try
                {
                    var frame = new byte[ValkyrieMfcFrameBytes];
                    frame[1] = ValkyrieMfcCmd;
                    frame[2] = ValkyrieMfcSub;

                    byte sum = 0;
                    for (int i = 0; i < frame.Length; i++) sum += frame[i];
                    frame[ValkyrieMfcChecksumIndex] = sum;

                    var send = new byte[ValkyrieMfcSendBytes];
                    Array.Copy(frame, send, ValkyrieMfcSendBytes);

                    var resp = Transact(h, ev, send, 600);
                    if (resp == null || resp.Length < 4) continue;

                    if (resp[0] == ValkyrieMfcCmd)
                    {
                        int level = resp[3];
                        if (level >= 1 && level <= 100) return level;
                    }
                }
                catch { }
                finally { CloseHandle(ev); CloseHandle(h); }
            }
            return -1;
        }
    }
}
