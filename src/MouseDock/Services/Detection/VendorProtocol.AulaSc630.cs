using System;

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
            if (!IsAulaSc630(product) || InstanceScope.Current == null) return -1;
            foreach (string path in Paths(vid, pid))
            {
                var result = ReadAulaBattery(AulaBatteryProtocol.Route.Sc630Pro, path, vid, pid);
                if (result.Level < 0) continue;
                charging = result.ChargingKnown && result.Charging;
                return result.Level;
            }
            return -1;
        }
    }
}
