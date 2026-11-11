using System;
using System.Buffers.Binary;
using System.Linq;
using System.Runtime.InteropServices;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FlushQueue(IntPtr handle);

    private const ushort Zg4Mid = 4101;
    private const byte Zg4BatteryProperty = 7;
    internal const string Zg4Protocol = "竹林鸟 ZG4 接收器电量";

    internal static byte[] BuildZg4DongleRequest(byte reportId, int length, byte command)
    {
        var report = new byte[length];
        report[0] = reportId;
        report[1] = 7;
        report[2] = 0xAA;
        report[3] = 0x5A;
        report[4] = 0xCC;
        report[7] = 1;
        report[8] = command;
        BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(5), Zg4Crc(report.AsSpan(7, 2)));
        return report;
    }

    internal static byte[] BuildZg4BatteryRequest(byte reportId, int length, byte sequence)
    {
        var report = new byte[length];
        report[0] = reportId;
        report[1] = 0xEE;
        report[2] = 0x80;
        report[4] = 6;
        report[6] = 0xFF;
        report[7] = sequence;
        report[8] = 2;
        report[9] = 0xFE;
        report[10] = Zg4BatteryProperty;
        report[11] = 0xAA;
        return report;
    }

    internal static bool IsZg4Online(ReadOnlySpan<byte> report, byte reportId) =>
        TryZg4DonglePayload(report, reportId, 2, out var payload) && payload.Length >= 1 && payload[0] == 1;

    internal static bool IsZg4Identity(ReadOnlySpan<byte> report, byte reportId) =>
        TryZg4DonglePayload(report, reportId, 6, out var payload) && payload.Length >= 4 &&
        BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2)) == Zg4Mid;

    private static bool TryZg4DonglePayload(ReadOnlySpan<byte> report, byte reportId, byte command,
        out ReadOnlySpan<byte> payload)
    {
        payload = default;
        if (report.Length < 8 || report[0] != reportId) return false;
        var frame = report.Slice(1);
        if (frame[0] != 0xAA) frame = frame.Slice(1);
        if (frame.Length < 7 || frame[0] != 0xAA || frame[1] != 0xCC || frame[6] != command)
            return false;
        int length = frame[3];
        if (length is < 1 or > 57) return false;
        ushort checksum = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(4));
        foreach (int size in new[] { length, length + 1 })
        {
            if (frame.Length < 6 + size || Zg4Crc(frame.Slice(6, size)) != checksum) continue;
            payload = frame.Slice(7, size - 1);
            return true;
        }
        return false;
    }

    internal static int ParseZg4Battery(ReadOnlySpan<byte> report, byte reportId, byte sequence, out bool charging)
    {
        charging = false;
        if (report.Length < 2 || report[0] != reportId) return -1;
        var frame = report.Slice(1);
        if (frame[0] is 0xEE or 0xEA)
        {
            if (frame.Length < 5 || frame[1] != 0x80 || frame[2] != 0) return -1;
            int size = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(3));
            if (size > frame.Length - 5) return -1;
            frame = frame.Slice(5, size);
        }
        if (frame.Length < 9 || frame[0] != 0xFF || frame[1] != sequence ||
            frame[2] < 5 || frame[2] + 4 > frame.Length || frame[frame[2] + 3] != 0xAA ||
            frame[3] != 0xFE || frame[4] != Zg4BatteryProperty) return -1;
        int level = frame[5] & 0x7F;
        if (level > 100) return -1;
        charging = (frame[5] & 0x80) != 0;
        return level;
    }

    private static ushort Zg4Crc(ReadOnlySpan<byte> bytes)
    {
        int crc = 0;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xA001 : 0);
        }
        return (ushort)crc;
    }

    internal static int Zg4ChannelPriority(string interfaceLabel) =>
        string.Equals(interfaceLabel, "MI_04/COL_03", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

    private static string ReadZg4Stage(IntPtr handle, IntPtr signal, byte[] request, int timeoutMs,
        Func<byte[], bool> accepts)
    {
        if (!Transfer(handle, signal, request, true, 300, out _)) return "write-failed";
        long deadline = Environment.TickCount64 + timeoutMs;
        bool received = false;
        while (Environment.TickCount64 < deadline)
        {
            var reply = ReadReport(handle, signal, (int)Math.Max(1, deadline - Environment.TickCount64));
            if (reply == null) break;
            received = true;
            if (accepts(reply)) return "accepted";
        }
        return received ? "response-rejected" : "no-response";
    }

    internal static int TryReadZg4(string instance, out bool charging, System.Collections.Generic.List<string> trace)
    {
        charging = false;
        var paths = Paths(0x3654, 0x5055).OrderBy(path =>
        {
            HidInventory.ParsePath(path, out _, out _, out string label);
            return Zg4ChannelPriority(label);
        });
        foreach (string path in paths)
        {
            if (InstanceScope.ForPath(path, "") != instance) continue;
            HidInventory.ParsePath(path, out _, out _, out string label);
            void Record(string status, int level = -1)
            {
                BatteryReplay.Current?.AddQuery(instance, path, Zg4Protocol, status,
                    level >= 0 ? "ZhuAudio ZG4" : null, level);
                trace.Add($"{label ?? "HID"}：{status}");
            }

            IntPtr handle = OpenOverlapped(path, out IntPtr signal);
            if (handle == IntPtr.Zero)
            {
                Record("open-failed");
                continue;
            }
            try
            {
                var caps = Caps(handle);
                if (caps.UsagePage < 0xFF00 ||
                    caps.InputReportByteLength is < 64 or > 65 || caps.OutputReportByteLength is < 64 or > 65 ||
                    !VendorMap.IsZhuAudioZg4(HidDiscovery.ReadHidProduct(handle, path))) continue;
                if (signal == IntPtr.Zero || !HidD_GetPreparsedData(handle, out IntPtr prep))
                {
                    Record("setup-failed");
                    continue;
                }
                byte inputId, outputId;
                try
                {
                    if (!TryGetReportId(prep, 0, caps.NumberInputValueCaps, caps.NumberInputButtonCaps, out inputId) ||
                        !TryGetReportId(prep, 1, caps.NumberOutputValueCaps, caps.NumberOutputButtonCaps, out outputId))
                    {
                        Record("report-id-unconfirmed");
                        continue;
                    }
                }
                finally { HidD_FreePreparsedData(prep); }

                int length = caps.OutputReportByteLength;
                if (!HidD_FlushQueue(handle))
                {
                    Record("flush-failed");
                    continue;
                }
                string status = ReadZg4Stage(handle, signal, BuildZg4DongleRequest(outputId, length, 2),
                    1500, reply => IsZg4Online(reply, inputId));
                if (status != "accepted")
                {
                    Record("online-" + status);
                    continue;
                }
                status = ReadZg4Stage(handle, signal, BuildZg4DongleRequest(outputId, length, 6),
                    400, reply => IsZg4Identity(reply, inputId));
                if (status != "accepted")
                {
                    Record("identity-" + status);
                    continue;
                }

                byte sequence = (byte)Environment.TickCount;
                int level = -1;
                bool isCharging = false;
                status = ReadZg4Stage(handle, signal, BuildZg4BatteryRequest(outputId, length, sequence),
                    500, reply => (level = ParseZg4Battery(reply, inputId, sequence, out isCharging)) >= 0);
                Record("battery-" + status, level);
                if (level >= 0)
                {
                    charging = isCharging;
                    return level;
                }
            }
            finally { CloseHandle(signal); CloseHandle(handle); }
        }
        return -1;
    }
}
