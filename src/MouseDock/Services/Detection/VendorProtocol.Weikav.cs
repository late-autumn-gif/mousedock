using System;

namespace MouseDock.Services.Detection
{
    internal static partial class VendorProtocol
    {
        internal static string IdentifyWeikav(ushort vid, ushort pid, string product)
        {
            string name = product?.Trim().ToUpperInvariant();
            if (name?.StartsWith("WEIKAV ", StringComparison.Ordinal) == true) name = name.Substring(7);
            // Shared VID/PIDs are only named when the USB product also agrees with the official table.
            return (vid, pid, name) switch
            {
                (0x0C45, 0x880C, "NUT75" or "NUT87") => name,
                (0x0C45, 0x80D3, "NUT65V2" or "NUT65 V2") => "NUT65 V2",
                (0x0C45, 0x8802, "VEGA108") => name,
                (0x0C45, 0x800E, "NUT21") => name,
                (0x0C45, 0xFEF9, "NUT75 2.4G" or "NUT75 2.4G WIRELESS") => "NUT75",
                (0x0C45, 0xFEF9, "NUT87 2.4G") => "NUT87",
                (0x0C45, 0xFEF9, "NUT65V2 2.4G" or "NUT65 V2 2.4G") => "NUT65 V2",
                (0x0C45, 0xFEF9, "VEGA108 2.4G") => "VEGA108",
                (0x0C45, 0xFEFA, "NUT21 2.4G") => "NUT21",
                _ => null
            };
        }

        internal static string IdentifyWeikavVia(ushort vid, ushort pid, string product)
        {
            if (vid != 0x342D) return null;
            string name = product?.Trim().ToUpperInvariant();
            if (name?.StartsWith("WEIKAV ", StringComparison.Ordinal) == true) name = name.Substring(7);
            return (pid, name) switch
            {
                (0xE407, "LUCKY65V2" or "LUCKY65 V2") => "Lucky65 V2",
                (0xE538, "LUCKY65V3" or "LUCKY65 V3") => "Lucky65 V3",
                (0xE401, "LUCKY80" or "LUCKY 80") => "Lucky80",
                (0xE401, "STARS80" or "STARS 80") => "Stars80",
                (0xE554, "NUT108" or "NUT 108") => "NUT108",
                (0xE51A, "NUT65" or "NUT 65") => "NUT65",
                (0xE545, "NUT65 CU" or "NUT65CU") => "NUT65 CU",
                (0xE537, "MAX102" or "MAX 102") => "MAX102",
                (0xE543, "S21" or "STARS21-S") => "Stars21-S",
                (0xE403, "STARS21" or "LUCKY21" or "RUIN21") => "Stars21",
                (0xE409, "STARS21" or "LUCKY21") => "Stars21",
                (0xE402, "STARS75" or "STARS75S") => "Stars75",
                (0xE406, "WK87") => "WK87",
                (0xE408, "RECORD ALICE" or "RECORD ALICE V2") => "Record Alice",
                _ => null
            };
        }

        internal static bool IsWeikavDriveAllCollection(ushort page, ushort input, ushort output) =>
            page is 0xFF68 or 0xFF80 or 0xFF60 or 0xFF00 or 0xFF01 or 0xFF1B &&
            input == output && output is 33 or 65;

        internal static byte[] BuildWeikavBatteryRequest(int reportLength)
        {
            // Read the first chunk of GET_DEVICE_INFO. Battery is in that chunk even with 32-byte payloads.
            var request = new byte[reportLength];
            request[1] = 0xAA;
            request[2] = 0x10;
            request[3] = reportLength == 65 ? (byte)48 : (byte)24;
            request[7] = reportLength == 65 ? (byte)1 : (byte)0;
            return request;
        }

        internal static bool TryParseWeikavBattery(ReadOnlySpan<byte> reply, out int level)
        {
            level = -1;
            if (reply.Length is not (33 or 65) || reply[0] != 0 || reply[1] != 0x55 ||
                reply[2] != 0x10 || reply[4] != 0 || reply[5] != 0 || reply[26] > 100) return false;
            // A successful device-info response carries identity fields as well as the battery byte.
            if ((reply[13] | reply[14] | reply[15] | reply[16] |
                 reply[21] | reply[22] | reply[23] | reply[24]) == 0) return false;
            level = reply[26];
            return true;
        }

        internal static int TryReadWeikavBattery(ushort vid, ushort pid, string product)
        {
            string model = IdentifyWeikav(vid, pid, product);
            if (model == null) return -1;
            foreach (string path in Paths(vid, pid))
            {
                IntPtr handle = OpenOverlapped(path, out IntPtr signal);
                if (handle == IntPtr.Zero) continue;
                try
                {
                    var caps = Caps(handle);
                    if (signal == IntPtr.Zero || !IsWeikavDriveAllCollection(caps.UsagePage,
                        caps.InputReportByteLength, caps.OutputReportByteLength) ||
                        IdentifyWeikav(vid, pid, HidDiscovery.ReadHidProduct(handle, path)) != model) continue;

                    var reply = Transact(handle, signal, BuildWeikavBatteryRequest(caps.OutputReportByteLength), 600);
                    if (TryParseWeikavBattery(reply, out int level)) return level;
                }
                finally { CloseHandle(signal); CloseHandle(handle); }
            }
            return -1;
        }
    }
}
