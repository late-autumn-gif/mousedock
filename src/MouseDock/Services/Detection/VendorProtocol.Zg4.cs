using System;
using System.Buffers.Binary;
using System.IO;
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

    internal static bool IsZg4Channel(ushort vid, ushort pid, string product, ushort page, int input, int output) =>
        vid == 0x3654 && pid == 0x5055 && VendorMap.IsZhuAudioZg4(product) &&
        page >= 0xFF00 && input is >= 64 and <= 65 && output is >= 64 and <= 65;

    private static bool ReadZg4DongleStage(ReferenceBatteryProtocol.ILink link, byte[] request, byte reportId,
        bool identity, BatteryReplay.Trace trace)
    {
        string stage = identity ? "zg4-identity" : "zg4-online";
        bool sent = link.Write(request, 300);
        trace?.Add(stage + "-write", sent, raw: request);
        if (!sent) return false;
        long deadline = link.Now + 400;
        while (link.Now < deadline)
        {
            var reply = link.Read((int)Math.Max(1, deadline - link.Now));
            bool valid = identity ? IsZg4Identity(reply, reportId) : IsZg4Online(reply, reportId);
            trace?.Add(stage, valid, raw: reply, keep: valid);
            if (valid) return true;
            if (reply == null) break;
        }
        return false;
    }

    internal static ReferenceBatteryProtocol.Reading ReadZg4Exchange(ReferenceBatteryProtocol.ILink link,
        byte inputId, byte outputId, int length, BatteryReplay.Trace trace)
    {
        if (!ReadZg4DongleStage(link, BuildZg4DongleRequest(outputId, length, 2), inputId, false, trace))
            return ReferenceBatteryProtocol.Reading.Failed("ZG4 未确认耳机在线（发送失败或无有效在线应答）");
        if (!ReadZg4DongleStage(link, BuildZg4DongleRequest(outputId, length, 6), inputId, true, trace))
            return ReferenceBatteryProtocol.Reading.Failed("ZG4 已在线，但未取得匹配型号的应答");
        byte sequence = (byte)link.Now;
        var request = BuildZg4BatteryRequest(outputId, length, sequence);
        bool sent = link.Write(request, 300);
        trace?.Add("zg4-battery-write", sent, sequence, request);
        if (!sent) return ReferenceBatteryProtocol.Reading.Failed("ZG4 已确认型号，电量查询发送失败");
        long deadline = link.Now + 500;
        while (link.Now < deadline)
        {
            var reply = link.Read((int)Math.Max(1, deadline - link.Now));
            int level = ParseZg4Battery(reply, inputId, sequence, out bool charging);
            trace?.Add("zg4-battery", level >= 0, sequence, reply, keep: level >= 0);
            if (level >= 0)
                return new(level, charging, true, charging ? ReferenceBatteryProtocol.State.Charging :
                    ReferenceBatteryProtocol.State.Online, "ZG4 在线、型号与电量应答已匹配", "ZhuAudio ZG4");
            if (reply == null) break;
        }
        return ReferenceBatteryProtocol.Reading.Failed("ZG4 已确认型号，未取得有效电量回包");
    }

    internal static ReferenceBatteryProtocol.Reading ReadZg4(HidDiscovery.VendorCollectionInfo channel)
    {
        if (!IsZg4Channel(channel.Vid, channel.Pid, channel.Product, channel.UsagePage, channel.Input, channel.Output) ||
            InstanceScope.Current == null || !InstanceScope.Allows(channel.Path))
            return ReferenceBatteryProtocol.Reading.Failed("ZG4 接口或物理实例不符");
        var trace = BatteryReplay.Current?.Active;
        return ReferenceQueryGate.Run(channel.Path, () =>
        {
            IntPtr handle = OpenOverlapped(channel.Path, out IntPtr signal);
            if (handle == IntPtr.Zero)
                return ReferenceBatteryProtocol.Reading.Failed("ZG4 私有接口打开失败 Win32=" + Marshal.GetLastWin32Error());
            try
            {
                if (signal == IntPtr.Zero) throw new IOException("ZG4 读取事件创建失败");
                var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                if (!HidD_GetAttributes(handle, ref attributes) || attributes.VendorID != channel.Vid ||
                    attributes.ProductID != channel.Pid) throw new IOException("ZG4 打开后 VID/PID 不符");
                var caps = Caps(handle);
                if (caps.UsagePage != channel.UsagePage || caps.Usage != channel.Usage ||
                    caps.InputReportByteLength != channel.Input || caps.OutputReportByteLength != channel.Output ||
                    caps.FeatureReportByteLength != channel.Feature) throw new IOException("ZG4 打开后接口能力与扫描不符");
                if (!HidD_GetPreparsedData(handle, out IntPtr prep)) throw new IOException("ZG4 报告描述符不可读");
                byte inputId, outputId;
                try
                {
                    if (!TryGetReportId(prep, 0, caps.NumberInputValueCaps, caps.NumberInputButtonCaps, out inputId) ||
                        !TryGetReportId(prep, 1, caps.NumberOutputValueCaps, caps.NumberOutputButtonCaps, out outputId))
                        throw new IOException("ZG4 报告编号无法唯一确认");
                }
                finally { HidD_FreePreparsedData(prep); }
                trace?.Add("zg4-open", true, inputId);
                if (!HidD_FlushQueue(handle)) throw new IOException("ZG4 无法清空输入队列");
                return ReadZg4Exchange(new G7Link(handle, signal, caps.InputReportByteLength),
                    inputId, outputId, caps.OutputReportByteLength, trace);
            }
            finally
            {
                if (signal != IntPtr.Zero) CloseHandle(signal);
                CloseHandle(handle);
            }
        }, 4000);
    }
}
