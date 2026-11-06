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
        // Wire-level percentage/charging offsets are not recovered; old parsers stay retired.
        internal const byte Cloud3SBatteryCommand = 0x1D;
        internal const byte Cloud3SDeviceInfoCommand = 0x02; // Legacy research opcode, not validated link status

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
            // Retired speculative wire offset. The official enum proves command1D,
            // but not that this envelope's byte6 is a percentage. Synthetic replies
            // are not hardware evidence; audio-only policy must not resurrect it.
            return false;
        }

        private static bool IsCloud3SReply(ReadOnlySpan<byte> reply, ReadOnlySpan<byte> request, byte command) =>
            reply.Length == request.Length && reply.Length >= 7 && reply[0] == 0x0C &&
            reply[1] == 2 && reply[2] == 3 && reply[3] == 1 && reply[4] == 0 &&
            reply[5] == command && reply[6] != 0xFF && !reply.SequenceEqual(request);

        internal static bool TryParseCloud3SDeviceInfo(ReadOnlySpan<byte> reply, ReadOnlySpan<byte> request) =>
            false; // No verified wire-level online mapping for this old research frame.

        internal static bool TryParseCloud3Battery(ReadOnlySpan<byte> reply, out int level)
        {
            level = -1;
            return false; // Separate legacy protocol, not revalidated by Cloud3S DLL evidence.
        }

        internal static int TryReadCloud3S(ushort vid, ushort pid, out bool charging)
        {
            charging = false;
            if (!RecognitionPolicy.CanQuery(vid,pid,null)) return -1;
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
