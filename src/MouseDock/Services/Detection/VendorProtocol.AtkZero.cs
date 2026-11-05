using System;

namespace MouseDock.Services.Detection
{
    internal static partial class VendorProtocol
    {
        private const ushort AtkZeroWiredPid = 0x1154;
        private const byte AtkZeroBatteryCommand = 0x07;
        private const byte AtkZeroChargeCommand = 0x0F;
        private const byte AtkZeroPairedModelCommand = 0x89;

        internal static bool IsAtkZeroBitMouse(ushort vid, ushort pid) =>
            vid == 0x373B && pid is AtkZeroWiredPid or 0x1155 or 0x124F;

        internal static bool IsAtkZeroBatteryCollection(ushort page, ushort usage, ushort input, ushort output) =>
            page == 0xFF05 && usage == 1 && input == 64 && output == 64;

        // ATK HUB 3.2.31 BITMOUSE uses report ID 8 with a 63-byte WebHID payload.
        internal static byte[] BuildAtkZeroRequest(byte command, bool receiver)
        {
            bool identity = command == AtkZeroPairedModelCommand;
            var request = new byte[64];
            request[0] = 0x08;
            request[2] = 0x72;
            request[3] = identity ? (byte)8 : (byte)2;
            // Official BitMouse transferForResult targets the paired mouse for every
            // receiver command, including GetMouseCidMidDangle (0x89).
            request[5] = receiver ? (byte)1 : (byte)0;
            request[6] = command;
            request[7] = identity ? (byte)6 : (byte)1;
            request[1] = (byte)(request[2] + request[3] + request[5] + request[6] + request[7]);
            return request;
        }

        internal static bool IsAtkZeroPairedModel(ReadOnlySpan<byte> response)
        {
            int at = AtkReplyOffset(response, AtkZeroPairedModelCommand);
            if (at < 0) return false;
            int id = 8 - at;
            return response[id] == 1 && response[id + 1] == 1 &&
                   response[id + 2] == 0 && response[id + 3] == 0 && response[id + 4] == 0;
        }

        internal static bool TryParseAtkZeroBattery(ReadOnlySpan<byte> response, out int level)
        {
            level = -1;
            int at = AtkReplyOffset(response, AtkZeroBatteryCommand);
            if (at < 0) return false;
            int pct = response[7 - at];
            if (pct > 100) return false;
            level = pct;
            return true;
        }

        /// <summary>
        /// 应答里命令字落在哪一位，返回「整帧前移了几格」：
        ///   0 —— Windows 含 report ID 8：命令 [5]、电量 [7]、身份 [8..12]。
        /// 官方 WebHID 的输入去掉 report ID 后命令在 [4]，并不是另一种线缆布局。
        /// 只认整帧 64 字节、报告号 8、状态位不是 2 或 0xFF 的帧。
        /// </summary>
        internal static int AtkReplyOffset(ReadOnlySpan<byte> response, byte command)
        {
            if (response.Length != 64 || response[0] != 0x08) return -1;
            // Windows includes report ID 8; the official WebHID payload command
            // is at [4], battery at [6]. Do not guess a second shifted layout.
            if (response[5] == command && response[2] is not (2 or 0xFF)) return 0;
            return -1;
        }

        // WebHID removes the report ID. The official handler matches the command at
        // payload[4] and then prepends one byte before decoding the result.
        private static bool IsAtkZeroReply(ReadOnlySpan<byte> response, byte command) =>
            AtkReplyOffset(response, command) >= 0;

        internal static int TryReadAtkZero(ushort vid, ushort pid, out bool charging, out bool identified)
        {
            charging = false;
            identified = false;
            if (!IsAtkZeroBitMouse(vid, pid)) return -1;
            bool receiver = pid != AtkZeroWiredPid;
            foreach (string path in Paths(vid, pid))
            {
                IntPtr handle = OpenOverlapped(path, out IntPtr signal);
                if (handle == IntPtr.Zero) continue;
                try
                {
                    var caps = Caps(handle);
                    if (signal == IntPtr.Zero || !IsAtkZeroBatteryCollection(caps.UsagePage, caps.Usage,
                        caps.InputReportByteLength, caps.OutputReportByteLength)) continue;

                    // L0/L1 receivers are shared. A connected ZERO reports CID 1 and MID 1.
                    if (receiver && !IsAtkZeroPairedModel(QueryAtkZero(handle, signal, AtkZeroPairedModelCommand, true)))
                        continue;
                    identified = true;

                    if (!TryParseAtkZeroBattery(QueryAtkZero(handle, signal, AtkZeroBatteryCommand, receiver), out int level))
                        continue;
                    var charge = QueryAtkZero(handle, signal, AtkZeroChargeCommand, receiver);
                    int chargeAt = AtkReplyOffset(charge, AtkZeroChargeCommand);
                    charging = chargeAt >= 0 && charge[7 - chargeAt] == 1;
                    return level;
                }
                finally { CloseHandle(signal); CloseHandle(handle); }
            }
            return -1;
        }

        private static byte[] QueryAtkZero(IntPtr handle, IntPtr signal, byte command, bool receiver)
        {
            if (!Transfer(handle, signal, BuildAtkZeroRequest(command, receiver), true, 300, out _))
                return null;

            int deadline = Environment.TickCount + 600;
            while (deadline - Environment.TickCount > 0)
            {
                var reply = ReadReport(handle, signal, Math.Max(1, deadline - Environment.TickCount));
                if (reply == null || IsAtkZeroReply(reply, command)) return reply;
            }
            return null;
        }
    }
}
