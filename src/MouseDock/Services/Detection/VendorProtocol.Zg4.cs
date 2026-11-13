using System;
using System.Buffers.Binary;
using System.Collections.Generic;
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
    internal const string Zg4Protocol = "竹林鸟 ZG4 接收器电量";

    internal static int Zg4ChannelPriority(string interfaceLabel) =>
        string.Equals(interfaceLabel, "MI_04/COL_03", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

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
        report[4] = 18;
        report[6] = 0xFF;
        report[7] = sequence;
        report[8] = 14;
        report[9] = 0xFE;
        // The official driver's initial read includes battery among these properties.
        new byte[] { 5, 7, 4, 6, 0x24, 0x1B, 0x22, 0x2C, 0x1C, 0x2D, 0x26, 0x27, 0x1F }
            .CopyTo(report, 10);
        report[23] = 0xAA;
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
        => new Zg4BatteryInput().Read(report, reportId, sequence, 0, out charging, out _);

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
        long deadline = link.Now + (identity ? 400 : 1500);
        bool rejectionRecorded = false;
        while (link.Now < deadline)
        {
            var reply = link.Read((int)Math.Max(1, deadline - link.Now));
            if (link.Now > deadline) break;
            bool valid = identity ? IsZg4Identity(reply, reportId) : IsZg4Online(reply, reportId);
            if (valid || reply == null || !rejectionRecorded)
            {
                byte[] replay = null;
                if (trace != null && valid &&
                    TryZg4DonglePayload(reply, reportId, identity ? (byte)6 : (byte)2, out var payload) &&
                    payload.Length == (identity ? 4 : 1))
                {
                    int headerLength = reply[1] == 0xAA ? 8 : 9;
                    replay = reply.AsSpan(0, headerLength + payload.Length).ToArray();
                }
                trace?.Add(stage, valid, raw: replay ?? reply, keep: replay != null,
                    result: valid ? "accepted" : reply == null ? "no-response" : "response-rejected");
            }
            if (valid) return true;
            if (reply == null) break;
            rejectionRecorded = true;
        }
        return false;
    }

    internal static ReferenceBatteryProtocol.Reading ReadZg4Exchange(ReferenceBatteryProtocol.ILink link,
        byte inputId, byte outputId, int length, BatteryReplay.Trace trace, IReadOnlySet<byte> inputIds = null)
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
        long deadline = link.Now + 2000;
        var inputs = new Dictionary<byte, Zg4BatteryInput>();
        bool rejectionRecorded = false;
        while (link.Now < deadline)
        {
            var reply = link.Read((int)Math.Max(1, deadline - link.Now));
            if (link.Now > deadline) break;
            int level = -1;
            bool charging = false;
            byte[] replay = null;
            if (reply is { Length: > 0 } && (reply[0] == inputId || inputIds?.Contains(reply[0]) == true))
            {
                if (!inputs.TryGetValue(reply[0], out var input))
                    inputs[reply[0]] = input = new Zg4BatteryInput();
                level = input.Read(reply, reply[0], sequence, link.Now, out charging, out replay);
            }
            if (level >= 0 || reply == null || !rejectionRecorded)
            {
                trace?.Add("zg4-battery", level >= 0, sequence, replay ?? reply, keep: replay != null,
                    result: level >= 0 ? "accepted" : reply == null ? "no-response" : "response-rejected");
            }
            if (level >= 0)
                return new(level, charging, true, charging ? ReferenceBatteryProtocol.State.Charging :
                    ReferenceBatteryProtocol.State.Online, "ZG4 在线、型号与电量应答已匹配", "ZhuAudio ZG4");
            if (reply == null) break;
            rejectionRecorded = true;
        }
        return ReferenceBatteryProtocol.Reading.Failed("ZG4 已确认型号，未取得有效电量回包");
    }

    internal static ReferenceBatteryProtocol.Reading ReadZg4(HidDiscovery.VendorCollectionInfo channel,
        IReadOnlyList<HidDiscovery.VendorCollectionInfo> channels)
    {
        if (!IsZg4Channel(channel.Vid, channel.Pid, channel.Product, channel.UsagePage, channel.Input, channel.Output) ||
            InstanceScope.Current == null || !InstanceScope.Allows(channel.Path))
            return ReferenceBatteryProtocol.Reading.Failed("ZG4 接口或物理实例不符");
        var trace = BatteryReplay.Current?.Active;
        // ReferenceQueryGate uses a worker; InstanceScope is local to the scan thread.
        string instance = InstanceScope.Current;
        var pathsByInstance = InstanceScope.SnapshotPaths();
        return ReferenceQueryGate.Run(channel.Path, () =>
        {
            IntPtr handle = OpenOverlapped(channel.Path, out IntPtr signal);
            if (handle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                trace?.Add("zg4-open", false, result: "open-failed", nativeError: error);
                return ReferenceBatteryProtocol.Reading.Failed("ZG4 私有接口打开失败 Win32=" + error);
            }
            try
            {
                if (signal == IntPtr.Zero) throw new IOException("ZG4 读取事件创建失败");
                var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                if (!HidD_GetAttributes(handle, ref attributes) || attributes.VendorID != channel.Vid ||
                    attributes.ProductID != channel.Pid) throw new IOException("ZG4 打开后 VID/PID 不符");
                if (!VendorMap.IsZhuAudioZg4(HidDiscovery.ReadHidProduct(handle, channel.Path)))
                    throw new IOException("ZG4 打开后产品名称不符");
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
                if (!HidD_FlushQueue(handle)) throw new IOException("ZG4 无法清空输入队列");
                trace?.Add("zg4-open", true, inputId);
                using var link = new Zg4InputLink(new Zg4HidLink(handle, signal, caps.InputReportByteLength),
                    inputId, channel, channels, instance, pathsByInstance, trace);
                return ReadZg4Exchange(link, inputId, outputId, caps.OutputReportByteLength, trace, link.InputIds);
            }
            catch (IOException ex)
            {
                trace?.Add("zg4-open", false, result: ex.Message);
                return ReferenceBatteryProtocol.Reading.Failed(ex.Message);
            }
            finally
            {
                if (signal != IntPtr.Zero) CloseHandle(signal);
                CloseHandle(handle);
            }
        }, 6000);
    }
}
