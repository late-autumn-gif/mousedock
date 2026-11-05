using System;
using System.Threading;

namespace MouseDock.Services.Detection
{
    internal static partial class VendorProtocol
    {
        // Official AULA-SC630Pro Gaming Mouse 1.0.5 desktop driver:
        // FUN_00483190 command 81 / offset 0 / length 3; FUN_00480fe0 builds
        // an eight-byte header, FUN_00480b90 prepends report 6 (Feature 41).
        // FUN_00480f30 reads Feature 6; FUN_0044e055 decodes raw[9] online,
        // raw[10] & 7F percent / raw[10] >> 7 charging. No hardware validation.
        internal static bool IsAulaSc630(string product) =>
            string.Equals(product?.Trim(), "AULA-SC630Pro Gaming Mouse", StringComparison.OrdinalIgnoreCase);

        internal static byte[] BuildAulaSc630BatteryRequest(byte sequence)
        {
            var request = new byte[41];
            request[0] = 6;
            request[3] = 0x81;
            request[4] = sequence;
            request[8] = 3;
            return request;
        }

        internal static bool TryParseAulaSc630Battery(ReadOnlySpan<byte> reply, byte sequence,
            out int level, out bool charging)
        {
            level = -1;
            charging = false;
            // Official FUN_00483190 only gates on wire[8] (length echo). It does not
            // compare the sequence byte; accept 0 (not echoed) or our own sequence.
            if (reply.Length != 41 || reply[0] != 6 || reply[3] != 0x81 ||
                (reply[4] != 0 && reply[4] != sequence) || reply[8] != 3 || reply[9] != 1) return false;
            int value = reply[10] & 0x7F;
            if (value > 100) return false;
            level = value;
            charging = (reply[10] & 0x80) != 0;
            return true;
        }

        private static int _sc630Sequence;
        internal static int TryReadAulaSc630(ushort vid, ushort pid, string product, out bool charging)
        {
            charging = false;
            if (!IsAulaSc630(product)) return -1;
            foreach (string path in Paths(vid, pid))
            {
                IntPtr handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1)) continue;
                try
                {
                    var caps = Caps(handle);
                    if (caps.UsagePage != 0xFF01 || caps.Usage != 1 || caps.FeatureReportByteLength != 41 ||
                        !IsAulaSc630(HidDiscovery.ReadHidProduct(handle, path))) continue;
                    // Official timing is 50 ms after SetFeature plus 80 ms before GetFeature; retry a few times.
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        byte sequence = unchecked((byte)Interlocked.Increment(ref _sc630Sequence));
                        var request = BuildAulaSc630BatteryRequest(sequence);
                        if (!HidD_SetFeature(handle, request, request.Length)) continue;
                        System.Threading.Thread.Sleep(130);
                        var reply = new byte[41];
                        reply[0] = 6;
                        if (HidD_GetFeature(handle, reply, reply.Length) &&
                            TryParseAulaSc630Battery(reply, sequence, out int level, out charging)) return level;
                    }
                }
                finally { CloseHandle(handle); }
            }
            return -1;
        }
    }
}
