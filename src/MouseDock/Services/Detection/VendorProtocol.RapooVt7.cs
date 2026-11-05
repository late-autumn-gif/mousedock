using System;

namespace MouseDock.Services.Detection
{
    internal static partial class VendorProtocol
    {
        // RAPOO AHUB 2.0 resources/web/assets/js/index-B1AV7SwT.js:
        // t6e manifest, Kx.handleReport(report 7), Sx/hm parser, Ui/An UI state mapping.
        // VT7 is Nordic54L: do not send the unrelated legacy A5 AA query.
        internal static bool IsRapooVt7(ushort vid, ushort pid) =>
            vid == 0x24AE && pid is 0x4613 or 0x1413;

        internal static bool TryParseRapooVt7Battery(ReadOnlySpan<byte> report, out int level, out bool charging)
        {
            level = -1; charging = false;
            if (report.Length < 9 || report.Length > 65 || report[0] != 7) return false;
            // WebHID payload[6] status: 0 disconnected, 1 battery, 2 charging, 3 full/charging.
            // Payload[7] is an actual percentage including zero, not a voltage estimate.
            int status = report[7];
            if (status is not (1 or 2 or 3) || report[8] > 100) return false;
            if ((report[1] & 0x0F) is not (0 or 2)) return false; // RF / USB.
            level = report[8]; charging = status is 2 or 3;
            return true;
        }

        internal static int TryReadRapooVt7(ushort vid, ushort pid, out bool charging)
        {
            charging = false;
            if (!IsRapooVt7(vid, pid)) return -1;
            foreach (string path in Paths(vid, pid))
            {
                IntPtr handle = OpenOverlapped(path, out IntPtr signal);
                if (handle == IntPtr.Zero) continue;
                try
                {
                    var caps = Caps(handle);
                    if (signal == IntPtr.Zero || caps.UsagePage != 0xFF00 || caps.Usage != 2 ||
                        caps.InputReportByteLength < 9 || caps.InputReportByteLength > 65) continue;
                    // Official Nordic54L driver updates batteries from report 7 notifications.
                    // Passive only: if the mouse sleeps or sends no fresh notification, return unknown.
                    int deadline = Environment.TickCount + 1000;
                    while (deadline - Environment.TickCount > 0)
                    {
                        var reply = ReadReport(handle, signal, Math.Max(1, deadline - Environment.TickCount));
                        if (reply == null) break;
                        if (TryParseRapooVt7Battery(reply, out int level, out charging)) return level;
                    }
                }
                finally { CloseHandle(signal); CloseHandle(handle); }
            }
            return -1;
        }
    }
}
