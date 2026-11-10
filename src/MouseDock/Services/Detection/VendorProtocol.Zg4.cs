using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FlushQueue(IntPtr handle);

    private const ushort Zg4Mid = 4101;
    private const byte Zg4BatteryProperty = 7;

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

    private static bool ReadZg4DongleStage(IntPtr handle, IntPtr signal, byte[] request, byte reportId,
        bool identity)
    {
        if (!Transfer(handle, signal, request, true, 300, out _)) return false;
        long deadline = Environment.TickCount64 + 400;
        while (Environment.TickCount64 < deadline)
        {
            var reply = ReadReport(handle, signal, (int)Math.Max(1, deadline - Environment.TickCount64));
            if (reply == null) return false;
            if (identity ? IsZg4Identity(reply, reportId) : IsZg4Online(reply, reportId)) return true;
        }
        return false;
    }

    internal static int TryReadZg4(string instance, out bool charging, System.Collections.Generic.List<string> trace)
    {
        charging = false;
        foreach (string path in Paths(0x3654, 0x5055))
        {
            if (InstanceScope.ForPath(path, "") != instance) continue;
            IntPtr handle = OpenOverlapped(path, out IntPtr signal);
            if (handle == IntPtr.Zero) continue;
            try
            {
                var caps = Caps(handle);
                if (signal == IntPtr.Zero || caps.UsagePage < 0xFF00 ||
                    caps.InputReportByteLength is < 64 or > 65 || caps.OutputReportByteLength is < 64 or > 65 ||
                    !VendorMap.IsZhuAudioZg4(HidDiscovery.ReadHidProduct(handle, path)) ||
                    !HidD_GetPreparsedData(handle, out IntPtr prep)) continue;
                string channel = $"{caps.UsagePage:X4}/{caps.Usage:X4} {caps.InputReportByteLength}/{caps.OutputReportByteLength}";
                byte inputId, outputId;
                try
                {
                    if (!TryGetReportId(prep, 0, caps.NumberInputValueCaps, caps.NumberInputButtonCaps, out inputId) ||
                        !TryGetReportId(prep, 1, caps.NumberOutputValueCaps, caps.NumberOutputButtonCaps, out outputId))
                    {
                        trace.Add(channel + "：报告编号无法唯一确认");
                        continue;
                    }
                }
                finally { HidD_FreePreparsedData(prep); }

                int length = caps.OutputReportByteLength;
                if (!HidD_FlushQueue(handle))
                {
                    trace.Add(channel + "：无法清空输入队列");
                    continue;
                }
                if (!ReadZg4DongleStage(handle, signal, BuildZg4DongleRequest(outputId, length, 2), inputId, identity: false))
                {
                    trace.Add(channel + "：未确认耳机在线");
                    continue;
                }
                if (!ReadZg4DongleStage(handle, signal, BuildZg4DongleRequest(outputId, length, 6), inputId, identity: true))
                {
                    trace.Add(channel + "：在线，但未确认 ZG4 型号");
                    continue;
                }

                byte sequence = (byte)Environment.TickCount;
                if (!Transfer(handle, signal, BuildZg4BatteryRequest(outputId, length, sequence), true, 300, out _))
                {
                    trace.Add(channel + "：已确认 ZG4，电量查询发送失败");
                    continue;
                }
                int deadline = Environment.TickCount + 500;
                while (deadline - Environment.TickCount > 0)
                {
                    var reply = ReadReport(handle, signal, Math.Max(1, deadline - Environment.TickCount));
                    if (reply == null) break;
                    int level = ParseZg4Battery(reply, inputId, sequence, out charging);
                    if (level >= 0) return level;
                }
                trace.Add(channel + "：已确认 ZG4，未取得有效电量回包");
            }
            finally { CloseHandle(signal); CloseHandle(handle); }
        }
        return -1;
    }
}
