using System;
using System.Threading;

namespace MouseDock.Services.Detection
{
    internal static partial class VendorProtocol
    {
        // Official ATK v3 web driver, 2026-11-05:
        // keyboard-info.util-vnAWcNN2.js: sr/ln(power_info=26), xr and Ge.createBattery.
        // base-hid-device-D0mJQAE9.js: 16-bit request-code correlation.
        // Unlike the Rainy desktop query, ATK sends a zero-length payload and
        // decodes charging == 2. Do not infer identity from this shared protocol.
        // DEVICE_PATHS: 0x320F:0x5055 is the wired port (ATK V75X / VXE V75X /
        // VGN VXE75 V2 / VXE V87 / VGN V98 Pro); 0x320F:0x5088 is the 2.4G dongle.
        internal static bool IsAtkV75X(ushort vid, ushort pid, string product) =>
            vid == 0x320F && pid is 0x5055 or 0x5088 &&
            product != null &&
            (product.IndexOf("V75X", StringComparison.OrdinalIgnoreCase) >= 0 ||
             string.Equals(product.Trim(), "ATK V75X", StringComparison.OrdinalIgnoreCase));

        private static int _evisionRequestCode;

        internal static byte[] BuildAtkV75XBatteryRequest(ushort requestCode, int length = 64)
        {
            if (requestCode == 0 || length is not (32 or 64)) throw new ArgumentOutOfRangeException();
            var request = new byte[length];
            request[0] = 4;
            request[1] = (byte)requestCode;
            request[2] = (byte)(requestCode >> 8);
            request[3] = 0x1A;
            return request;
        }

        internal static bool TryParseAtkV75XBattery(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request,
            out int level, out bool charging)
        {
            level = -1;
            charging = false;
            if (response.Length != request.Length || response.Length is not (32 or 64) ||
                response[0] != 4 || response[1] != request[1] || response[2] != request[2] ||
                response[3] == 0xFF || response.SequenceEqual(request) || response[8] > 100 || response[9] > 2)
                return false;
            level = response[8];
            charging = response[9] == 2;
            return true;
        }

        // Official Rainy75 RGB native driver: FUN_0047db80 requests six bytes
        // at offset zero; FUN_00433dc0 displays body[0] as percentage and
        // body[1] == 1 using power/powering.png. This is NOT ATK's enum == 2.
        internal static bool TryParseRainy75OfficialBattery(ReadOnlySpan<byte> response,
            out int level, out bool charging)
        {
            level = -1;
            charging = false;
            if (response.Length != 64 || response[0] != 4 || response[3] != 0x1A ||
                response[4] != 6 || response[5] != 0 || response[6] != 0 ||
                response[7] is 0xFE or 0xFF ||
                response[RainyBatteryIndex] > 100 || response[RainyChargingIndex] > 2 ||
                response.SequenceEqual(BuildRainy75BatteryRequest())) return false;
            level = response[RainyBatteryIndex];
            charging = response[RainyChargingIndex] == 1;
            return true;
        }

        internal static int TryReadAtkV75X(ushort vid, ushort pid, string product, out bool charging)
        {
            charging = false;
            if (!IsAtkV75X(vid, pid, product)) return -1;
            foreach (string path in Paths(vid, pid))
            {
                IntPtr handle = OpenOverlapped(path, out IntPtr signal);
                if (handle == IntPtr.Zero) continue;
                try
                {
                    var caps = Caps(handle);
                    // The official filter uses FF1C/0092. The SDK buffer size is
                    // 63 payload bytes; the wireless subclass reduces it to 31.
                    int length = caps.OutputReportByteLength;
                    if (signal == IntPtr.Zero || caps.UsagePage != 0xFF1C || caps.Usage != 0x0092 ||
                        length is not (32 or 64) || caps.InputReportByteLength != length) continue;
                    ushort code = (ushort)((Interlocked.Increment(ref _evisionRequestCode) - 1) % 65535 + 1);
                    var request = BuildAtkV75XBatteryRequest(code, length);
                    if (!Transfer(handle, signal, request, true, 300, out _)) continue;
                    int deadline = Environment.TickCount + 600;
                    while (deadline - Environment.TickCount > 0)
                    {
                        var response = ReadReport(handle, signal, Math.Max(1, deadline - Environment.TickCount));
                        if (response == null) break;
                        if (TryParseAtkV75XBattery(response, request, out int level, out charging)) return level;
                    }
                }
                finally { CloseHandle(signal); CloseHandle(handle); }
            }
            return -1;
        }
    }
}
