using System;

namespace MouseDock.Services.Detection
{
    internal static partial class VendorProtocol
    {
        // Official M HUB web main-DCRwBIvm.js: audioHidProducts + blkBattery.
        // 291D:385D is shared with S9/G9; product string is required.
        internal static bool IsMchoseV9Pro(ushort vid, ushort pid, string product) =>
            vid == 0x291D && pid == 0x385D &&
            string.Equals(product?.Trim(), "MCHOSE V9 PRO", StringComparison.OrdinalIgnoreCase);

        internal static byte[] BuildMchoseV9BatteryRequest()
        {
            var request = new byte[64];
            request[0] = 0x55; request[1] = 0x65; request[2] = 1;
            return request;
        }

        internal static bool TryParseMchoseV9Battery(ReadOnlySpan<byte> reply, out int level, out bool charging)
        {
            level = -1; charging = false;
            if (reply.Length != 64 || reply[0] != 0x55 || reply[1] != 0x65 || reply[2] > 100 ||
                reply[3] is not (2 or 3 or 4)) return false; // 26 means asleep; never fresh.
            level = reply[2]; charging = reply[3] == 3;
            return true;
        }

        internal static int TryReadMchoseV9Pro(ushort vid, ushort pid, string product, out bool charging)
        {
            charging = false;
            if (!IsMchoseV9Pro(vid, pid, product)) return -1;
            foreach (string path in Paths(vid, pid))
            {
                IntPtr handle = OpenOverlapped(path, out IntPtr signal);
                if (handle == IntPtr.Zero) continue;
                try
                {
                    var caps = Caps(handle);
                    if (signal == IntPtr.Zero || caps.UsagePage != 0xFF90 || caps.Usage != 1 ||
                        caps.InputReportByteLength != 64 || caps.OutputReportByteLength != 64 ||
                        !IsMchoseV9Pro(vid, pid, HidDiscovery.ReadHidProduct(handle, path))) continue;
                    if (!Transfer(handle, signal, BuildMchoseV9BatteryRequest(), true, 300, out _)) continue;
                    int deadline = Environment.TickCount + 1000;
                    while (deadline - Environment.TickCount > 0)
                    {
                        var reply = ReadReport(handle, signal, Math.Max(1, deadline - Environment.TickCount));
                        if (reply == null) break;
                        if (TryParseMchoseV9Battery(reply, out int level, out charging)) return level;
                    }
                }
                finally { CloseHandle(signal); CloseHandle(handle); }
            }
            return -1;
        }

        // Official /gmouse lazy chunk 610.4e361f3235ebcfe0.js:
        // eS.getBatteryInfo, eS.addListeners, V.hid_write. G7 and G7 Pro share this driver.
        // Wireless receivers live under 0x3837; the wired body is 0xA8A4.
        internal static bool IsMchoseG7(ushort vid, ushort pid, string product) =>
            (vid == 0x3837 || vid == 0xA8A4) &&
            (string.Equals(product?.Trim(), "MCHOSE G7", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(product?.Trim(), "MCHOSE G7 Pro", StringComparison.OrdinalIgnoreCase));

        // Official sendReport data is 64 bytes: 55 30 A5 0B 2E 01 01 01 00 00 …
        // Windows HID may expose it as an unnumbered 64-byte report or with a
        // leading report-ID byte (65). Build whichever length the descriptor uses.
        internal static byte[] BuildMchoseG7BatteryRequest(int reportLength = 65)
        {
            int length = reportLength == 64 ? 64 : 65;
            var request = new byte[length];
            int at = length == 65 ? 1 : 0;
            new byte[] { 0x55, 0x30, 0xA5, 0x0B, 0x2E, 1, 1, 1, 0, 0 }.CopyTo(request, at);
            return request;
        }

        internal static bool TryParseMchoseG7Battery(ReadOnlySpan<byte> reply, out int level, out bool charging)
        {
            level = -1; charging = false;
            int at;
            if (reply.Length is 64 or 65 && reply[0] == 0xAA && reply[1] == 0x30) at = 0;
            else if (reply.Length == 65 && reply[0] == 0 && reply[1] == 0xAA && reply[2] == 0x30) at = 1;
            else return false;
            if (reply.Length < at + 10 || reply[at + 8] > 100 || reply[at + 9] > 1) return false;
            level = reply[at + 8]; charging = reply[at + 9] == 1;
            return true;
        }

        internal static int TryReadMchoseG7(ushort vid, ushort pid, string product, out bool charging)
        {
            charging = false;
            if (!IsMchoseG7(vid, pid, product)) return -1;
            foreach (string path in Paths(vid, pid))
            {
                IntPtr handle = OpenOverlapped(path, out IntPtr signal);
                if (handle == IntPtr.Zero) continue;
                try
                {
                    var caps = Caps(handle);
                    int inputLength = caps.InputReportByteLength;
                    int outputLength = caps.OutputReportByteLength;
                    if (signal == IntPtr.Zero || caps.UsagePage != 0xFF01 || caps.Usage != 0x10 ||
                        inputLength is not (64 or 65) || outputLength is not (64 or 65) ||
                        !IsMchoseG7(vid, pid, HidDiscovery.ReadHidProduct(handle, path))) continue;
                    if (!Transfer(handle, signal, BuildMchoseG7BatteryRequest(outputLength), true, 300, out _)) continue;
                    int deadline = Environment.TickCount + 1000;
                    while (deadline - Environment.TickCount > 0)
                    {
                        var reply = ReadReport(handle, signal, Math.Max(1, deadline - Environment.TickCount));
                        if (reply == null) break;
                        if (TryParseMchoseG7Battery(reply, out int level, out charging)) return level;
                    }
                }
                finally { CloseHandle(signal); CloseHandle(handle); }
            }
            return -1;
        }
    }
}
