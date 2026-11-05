using System;

namespace MouseDock.Services.Detection
{
    internal static partial class VendorProtocol
    {
        // NGENUITY 3.1.0 AirohaHidCoreDll.dll (SHA256 fc59cdf5…):
        // FUN_1800a3d60 maps 0x1D = GET_BATTERY_INFO (payload 0x10),
        // 0x06 = COMPARE_PARTITION_STORAGE (FOTA) and 0x48 = GET_GAME_CHAT_BALANCE_SETTINGS.
        // The hub's hand-built 0C 02 03 01 00 06 frame belongs to the FOTA constructor group,
        // not to the battery API; the Airoha DLL itself emits 0x1D.
        // Response layout below reply[6] is not recovered; do not invent a charge flag.
        internal const byte Cloud3SBatteryCommand = 0x1D;
        internal const byte Cloud3SDeviceInfoCommand = 0x02; // wireless link status

        internal static bool IsHyperXHeadset(ushort vid, ushort pid) => vid == 0x03F0 &&
            pid is 0x06BE or 0x02CC or 0x05B7 or 0x0C9D or 0x0FCC or 0x069F or 0x018B or 0x08BE or 0x0AC1;
        internal static bool IsCloud3S(ushort vid, ushort pid) => vid == 0x03F0 && pid is 0x06BE or 0x02CC;
        internal static bool IsCloud3Wireless(ushort vid, ushort pid) => vid == 0x03F0 && pid is 0x05B7 or 0x0C9D or 0x0FCC;
        internal static bool IsCloud3SControlCollection(ushort page, ushort usage, ushort input, ushort output) =>
            page == 0xFF13 && usage == 1 && input is 62 or 64 && output is 62 or 64;

        internal static byte[] BuildCloud3SRequest(int reportLength, byte command)
        {
            var frame = new byte[Math.Max(7, reportLength)];
            new byte[] { 0x0C, 2, 3, 1, 0, command }.CopyTo(frame, 0);
            return frame;
        }

        internal static bool TryParseCloud3SBattery(ReadOnlySpan<byte> reply, ReadOnlySpan<byte> request,
            out int level, out bool charging)
        {
            level = -1; charging = false;
            if (!IsCloud3SReply(reply, request, Cloud3SBatteryCommand) || reply[6] > 100) return false;
            level = reply[6];
            return true; // charging is a separate 48 query, never reply[7].
        }

        private static bool IsCloud3SReply(ReadOnlySpan<byte> reply, ReadOnlySpan<byte> request, byte command) =>
            reply.Length == request.Length && reply.Length >= 7 && reply[0] == 0x0C &&
            reply[1] == 2 && reply[2] == 3 && reply[3] == 1 && reply[4] == 0 &&
            reply[5] == command && reply[6] != 0xFF && !reply.SequenceEqual(request);

        internal static bool TryParseCloud3SDeviceInfo(ReadOnlySpan<byte> reply, ReadOnlySpan<byte> request) =>
            IsCloud3SReply(reply, request, Cloud3SDeviceInfoCommand) && reply[6] == 2;

        internal static bool TryParseCloud3Battery(ReadOnlySpan<byte> reply, out int level)
        {
            level = -1;
            if (reply.Length < 5 || reply[0] != 0x66 || reply[1] is not (0x89 or 0x0D) ||
                (reply[2] == 0 && reply[3] == 0) || reply[4] > 100) return false;
            level = reply[4];
            return true;
        }

        internal static int TryReadCloud3S(ushort vid, ushort pid, out bool charging)
        {
            charging = false;
            bool cloudS = IsCloud3S(vid, pid);
            if (!cloudS && !IsCloud3Wireless(vid, pid)) return -1;
            foreach (string path in Paths(vid, pid))
            {
                IntPtr handle = OpenOverlapped(path, out IntPtr signal);
                if (handle == IntPtr.Zero) continue;
                try
                {
                    var caps = Caps(handle);
                    if (signal == IntPtr.Zero || !IsCloud3SControlCollection(caps.UsagePage, caps.Usage,
                        caps.InputReportByteLength, caps.OutputReportByteLength)) continue;
                    var request = cloudS ? BuildCloud3SRequest(caps.OutputReportByteLength, Cloud3SBatteryCommand)
                        : new byte[caps.OutputReportByteLength];
                    if (!cloudS) { request[0] = 0x66; request[1] = 0x89; }
                    if (!Transfer(handle, signal, request, true, 300, out _)) continue;
                    int level = -1;
                    int deadline = Environment.TickCount + 800;
                    while (deadline - Environment.TickCount > 0)
                    {
                        var reply = ReadReport(handle, signal, Math.Max(1, deadline - Environment.TickCount));
                        if (reply == null) break;
                        if (cloudS ? TryParseCloud3SBattery(reply, request, out level, out _) : TryParseCloud3Battery(reply, out level)) break;
                    }
                    if (level < 0) continue;
                    // 0x1D only gives a percentage in this implementation. The charge
                    // flag offset is not recovered, and 0x48 is a game/chat setting.
                    return level;
                }
                catch { }
                finally { CloseHandle(signal); CloseHandle(handle); }
            }
            return -1;
        }
    }
}
