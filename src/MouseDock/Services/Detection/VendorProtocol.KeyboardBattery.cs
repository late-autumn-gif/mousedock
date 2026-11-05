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

        internal static bool IsAulaF99Wired(ushort vid, ushort pid, string product) =>
            IsAulaF99(vid, pid, product) &&
            (product ?? "").IndexOf("wired", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>320F:5055 —— Rainy 75 的有线口，但 ATK V75X / VXE / VGN 也挂这个号，不能只凭 VID:PID 定性。</summary>
        internal static bool IsRainy75Wired(ushort vid, ushort pid) => vid == 0x320F && pid == 0x5055;

        /// <summary>
        /// 320F:5088 —— Rainy 75 的 2.4G 口，但 VGN V98 Pro / ATK V75X 也挂这个号。
        /// 不能凭号定性，只能发查询再按应答结构（04 20 00 1A 06）采信。
        /// </summary>
        internal static bool IsRainy75Wireless(ushort vid, ushort pid) => vid == 0x320F && pid == 0x5088;

        internal static bool IsRainy75Candidate(ushort vid, ushort pid) =>
            IsRainy75Wired(vid, pid) || IsRainy75Wireless(vid, pid);

        /// <summary>产品名里出现 Rainy / WOB 才敢在共用号上直接挂型号。</summary>
        internal static bool IsRainy75Name(string product) =>
            !string.IsNullOrEmpty(product) &&
            (product.IndexOf("Rainy", StringComparison.OrdinalIgnoreCase) >= 0 ||
             product.IndexOf("WOB", StringComparison.OrdinalIgnoreCase) >= 0);

        internal static byte[] BuildAulaF99BatteryRequest()
        {
            var request = new byte[520];
            new byte[] { 0x09, 0x0C, 0x09, 0x01, 0x01, 0x00, 0x01 }.CopyTo(request, 0);
            return request;
        }

        internal static bool TryParseAulaF99Battery(ReadOnlySpan<byte> reply, out int level)
        {
            level = -1;
            if (reply.Length != 520 || reply[0] != 0x09 || reply[2] != 0x09 ||
                reply[3] != 1 || reply[4] != 1 || reply[5] != 0 || reply[6] != 1 || reply[7] != 0 ||
                reply[8] > 100 ||
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

        /// <summary>应答里的电量字节。抓包确认后改这里，apply-capture.ps1 会自动替换。</summary>
        internal const int RainyBatteryIndex = 8;

        /// <summary>应答里的充电位。0=未充电，1=充电中。</summary>
        internal const int RainyChargingIndex = 9;

        internal static bool TryParseRainy75Battery(ReadOnlySpan<byte> reply, out int level, out bool charging)
        {
            return TryParseRainy75OfficialBattery(reply, out level, out charging);
        }

        internal static int TryReadRainy75Wired(ushort vid, ushort pid, out bool charging)
        {
            charging = false;
            foreach (string path in Paths(vid, pid))
            {
                IntPtr handle = OpenOverlapped(path, out IntPtr signal);
                if (handle == IntPtr.Zero) continue;
                try
                {
                    var caps = Caps(handle);
                    if (signal == IntPtr.Zero || caps.UsagePage != 0xFF1C || caps.Usage != 0x0092 ||
                        caps.InputReportByteLength != 64 || caps.OutputReportByteLength != 64) continue;
                    if (!Transfer(handle, signal, BuildRainy75BatteryRequest(), true, 300, out _)) continue;
                    int deadline = Environment.TickCount + 600;
                    while (deadline - Environment.TickCount > 0)
                    {
                        var reply = ReadReport(handle, signal, Math.Max(1, deadline - Environment.TickCount));
                        if (reply == null) break;
                        if (TryParseRainy75Battery(reply, out int level, out charging)) return level;
                    }
                }
                finally { CloseHandle(signal); CloseHandle(handle); }
            }
            return -1;
        }
    }
}
