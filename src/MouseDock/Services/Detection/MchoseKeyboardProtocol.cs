using System;
using System.Linq;
using System.Threading;

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
            if (safeMode || !IsSupportedDevice(vid, pid, product) ||
                string.IsNullOrWhiteSpace(InstanceScope.Current)) return Unknown;
            try
            {
                var nodes = HidProbe.Enumerate(vid, pid).Where(node => InstanceScope.Allows(node.Path) &&
                    IsSupportedDevice(node.Vid, node.Pid, node.Product) &&
                    IsSupportedInterface(node.Caps.UsagePage, node.Caps.Usage,
                        node.Caps.InputReportByteLength, node.Caps.OutputReportByteLength)).ToArray();
                if (nodes.Length != 1) return Unknown;
                var result = VendorProtocol.ReadK87(nodes[0].Path);
                return (result.Level, result.ChargingKnown && result.Charging);
            }
            catch (Exception) { }
            return Unknown;
        }

        internal static ReferenceBatteryProtocol.Reading Read(ReferenceBatteryProtocol.ILink link)
        {
            bool drained = false;
            for (int i = 0; i < 32; i++)
                if (link.Read(1) is not { Length: > 0 }) { drained = true; break; }
            if (!drained) return ReferenceBatteryProtocol.Reading.Failed("K87 旧输入队列未清空");
            byte sequence = unchecked((byte)Interlocked.Increment(ref _sequence));
            if (!link.Write(CreateBatteryQuery(sequence), 250))
                return ReferenceBatteryProtocol.Reading.Failed("K87 电量查询未完整发送");
            long until = link.Now + 700;
            for (int i = 0; i < 64 && link.Now < until; i++)
            {
                var report = link.Read((int)Math.Max(1, Math.Min(100, until - link.Now)));
                if (report?.Length != 64) continue;
                var result = ParseBatteryResponse(report, sequence);
                if (result.Level >= 0)
                    return new(result.Level, result.Charging, true,
                        result.Charging ? ReferenceBatteryProtocol.State.Charging : ReferenceBatteryProtocol.State.Online,
                        "MCHOSE K87 V3 QHW 电量查询；RX=" + Convert.ToHexString(report));
            }
            return ReferenceBatteryProtocol.Reading.Failed("K87 未取得本轮有效电量响应");
        }
    }
}
