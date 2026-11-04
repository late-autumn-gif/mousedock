using System;

namespace MouseDock.Services.Detection
{
    internal static partial class VendorProtocol
    {
        internal static bool IsAulaF99(ushort vid, ushort pid, string product) =>
            ((vid == 0x3554 && pid == 0xFA09) || (vid == 0x258A && pid == 0x010C)) &&
            (string.Equals(product?.Trim(), "AULA F99", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(product?.Trim(), "AULA-F99", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(product?.Trim(), "AULA F99 Wired", StringComparison.OrdinalIgnoreCase));

        internal static bool IsRainy75Wired(ushort vid, ushort pid) => vid == 0x320F && pid == 0x5055;

        internal static byte[] BuildAulaF99BatteryRequest()
        {
            var request = new byte[520];
            new byte[] { 0x09, 0x0C, 0x09, 0x01, 0x01, 0x00, 0x01 }.CopyTo(request, 0);
            return request;
        }

        internal static bool TryParseAulaF99Battery(ReadOnlySpan<byte> reply, out int level)
        {
            level = -1;
            if (reply.Length != 520 || reply[0] != 0x09 || reply[2] != 0x09 || reply[8] > 100 ||
                reply.SequenceEqual(BuildAulaF99BatteryRequest())) return false;
            level = reply[8];
            return true;
        }

        internal static int TryReadAulaF99(ushort vid, ushort pid)
        {
            foreach (string path in Paths(vid, pid))
            {
                IntPtr handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1)) continue;
                try
                {
                    if (Caps(handle).FeatureReportByteLength != 520 ||
                        !IsAulaF99(vid, pid, HidDiscovery.ReadHidProduct(handle, path))) continue;
                    var request = BuildAulaF99BatteryRequest();
                    if (!HidD_SetFeature(handle, request, request.Length)) continue;
                    System.Threading.Thread.Sleep(20);
                    var reply = new byte[520];
                    reply[0] = 0x09;
                    if (HidD_GetFeature(handle, reply, reply.Length) && TryParseAulaF99Battery(reply, out int level))
                        return level;
                }
                finally { CloseHandle(handle); }
            }
            return -1;
        }

        internal static byte[] BuildRainy75BatteryRequest()
        {
            var request = new byte[64];
            new byte[] { 0x04, 0x20, 0x00, 0x1A, 0x06 }.CopyTo(request, 0);
            return request;
        }

        internal static bool TryParseRainy75Battery(ReadOnlySpan<byte> reply, out int level, out bool charging)
        {
            level = -1;
            charging = false;
            if (reply.Length != 64 || reply[0] != 0x04 || reply[3] != 0x1A || reply[4] != 0x06 ||
                reply[5] != 0 || reply[6] != 0 || reply[7] is 0xFE or 0xFF || reply[8] > 100 || reply[9] > 2 ||
                reply.SequenceEqual(BuildRainy75BatteryRequest())) return false;
            level = reply[8];
            charging = reply[9] == 1;
            return true;
        }

        internal static int TryReadRainy75Wired(ushort vid, ushort pid, out bool charging)
        {
            charging = false;
            if (!IsRainy75Wired(vid, pid)) return -1;
            foreach (string path in Paths(vid, pid))
            {
                IntPtr handle = OpenOverlapped(path, out IntPtr signal);
                if (handle == IntPtr.Zero) continue;
                try
                {
                    var caps = Caps(handle);
                    if (signal == IntPtr.Zero || caps.UsagePage != 0xFF1C || caps.Usage != 0x0092 ||
                        caps.InputReportByteLength != 64 || caps.OutputReportByteLength != 64) continue;
                    var reply = Transact(handle, signal, BuildRainy75BatteryRequest(), 600);
                    if (TryParseRainy75Battery(reply, out int level, out charging)) return level;
                }
                finally { CloseHandle(signal); CloseHandle(handle); }
            }
            return -1;
        }
    }
}
