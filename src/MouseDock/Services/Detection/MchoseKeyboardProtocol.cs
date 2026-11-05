using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace MouseDock.Services.Detection
{
    internal static class MchoseKeyboardProtocol
    {
        // Official QHW getBattery / QhwCmdQueueItem, verified against K87 V3 firmware 0x0027:
        // https://www.mchose.com.cn/cizhou/CZ_SHARED_DATA/main.ab825b5a5ad18b374c0a.js
        private static int _sequence = Environment.TickCount;
        private static readonly (int Level, bool Charging) Unknown = (-1, false);

        public static bool IsSupportedDevice(ushort vid, ushort pid, string product) =>
            vid == 0x3837 && pid == 0x1015 &&
            string.Equals(product?.Trim(), "MCHOSE K87 V3 2.4G", StringComparison.OrdinalIgnoreCase);

        public static bool IsSupportedInterface(ushort page, ushort usage, ushort input, ushort output) =>
            page == 0xFF80 && usage == 1 && input == 64 && output == 64;

        public static byte[] CreateBatteryQuery(byte sequence)
        {
            // QHW 0x0904 is the battery query. Windows includes the report ID and HID padding.
            var report = new byte[64];
            report[0] = 0x4D;
            report[1] = 1;
            report[2] = 3;
            report[4] = 4;
            report[5] = 9;
            report[7] = sequence;
            report[8] = (byte)(0x0E ^ sequence);
            return report;
        }

        public static (int Level, bool Charging) ParseBatteryResponse(byte[] report, byte sequence)
        {
            if (report == null || report.Length < 11 ||
                report[0] != 0x4D || report[1] != 1 || report[2] != 3 || report[3] != 2 ||
                report[4] != 4 || report[5] != 9 || report[6] != 0 || report[7] != sequence ||
                report[8] > 2 || report[9] > 100)
                return Unknown;

            byte checksum = 0;
            for (int i = 2; i < 10; i++) checksum ^= report[i];
            if (checksum != report[10]) return Unknown;
            return (report[9], report[8] == 1);
        }

        public static (int Level, bool Charging) TryRead(ushort vid, ushort pid, string product, bool safeMode)
        {
            if (safeMode || !IsSupportedDevice(vid, pid, product)) return Unknown;
            try
            {
                foreach (var node in HidProbe.Enumerate(vid, pid))
                {
                    var caps = node.Caps;
                    if (!IsSupportedDevice(node.Vid, node.Pid, node.Product) ||
                        !IsSupportedInterface(caps.UsagePage, caps.Usage,
                            caps.InputReportByteLength, caps.OutputReportByteLength))
                        continue;
                    var result = ReadAsync(node.Path).GetAwaiter().GetResult();
                    if (result.Level >= 0) return result;
                }
            }
            catch (Exception) { }
            return Unknown;
        }

        private static async Task<(int Level, bool Charging)> ReadAsync(string path)
        {
            using var handle = OpenDevice(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
            if (handle.IsInvalid) return Unknown;
            using var stream = new FileStream(handle, FileAccess.ReadWrite, 64, true);
            using var timeout = new CancellationTokenSource(700);
            byte sequence = unchecked((byte)Interlocked.Increment(ref _sequence));
            await stream.WriteAsync(CreateBatteryQuery(sequence), timeout.Token).ConfigureAwait(false);
            while (!timeout.IsCancellationRequested)
            {
                var report = new byte[64];
                int count = await stream.ReadAsync(report, timeout.Token).ConfigureAwait(false);
                if (count == 0) break;
                if (count < 11) continue;
                var result = ParseBatteryResponse(report, sequence);
                if (result.Level >= 0) return result;
            }
            // FileStream cancellation completes native I/O before buffers and handles are released.
            return Unknown;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle OpenDevice(string path, uint access, uint share,
            IntPtr security, uint creation, uint flags, IntPtr template);
    }
}
