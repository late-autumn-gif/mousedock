using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    internal const string RogScopeRxWired = "ROG 游侠2 RX";
    internal const string RogScopeRxWireless = "ROG 游侠2 RX 98 无线版";

    internal static string RogScopeName(string product)
    {
        string name = product?.Trim().ToUpperInvariant();
        return name switch
        {
            "ROG STRIX SCOPE II RX" or "ASUS ROG STRIX SCOPE II RX" => RogScopeRxWired,
            "ROG STRIX SCOPE II 96 RX" or "ROG STRIX SCOPE II 96 RX WIRELESS" or
            "ROG STRIX SCOPE II 96 RX WIRELESS OPTICAL GAMING KEYBOARD" => RogScopeRxWireless,
            _ => null
        };
    }

    internal static bool IsRogOmni(ushort vid, ushort pid) => vid == 0x0B05 && pid == 0x1ACE;

    internal static bool IsRogOmniReport(ushort page, ushort usage, int input, int output, int reportId) =>
        (reportId == 1 && page == 0xFF02 || reportId == 2 && page == 0xFF00) &&
        usage == 1 && input == 64 && output == 64;

    internal static bool IsRogOmniChannel(string path, ushort page, ushort usage, int input, int output, int reportId)
    {
        if (!IsRogOmniReport(page, usage, input, output, reportId)) return false;
        int end = path.IndexOf('#', path.IndexOf('#') + 1);
        return end > 0 && path[..end].EndsWith(reportId == 1 ? "&mi_02&col01" : "&mi_02&col02",
            StringComparison.OrdinalIgnoreCase);
    }

    internal static bool RogOmniHasScopeRx(ReadOnlySpan<byte> reply)
    {
        if (reply.Length != 64 || !reply[..3].SequenceEqual(new byte[] { 1, 0xA0, 0 })) return false;
        for (int i = 5; i + 3 < reply.Length; i += 4)
        {
            int pid = reply[i] | (reply[i + 1] << 8);
            if (pid == 0) break;
            if (pid == 0x1B7A) return true;
        }
        return false;
    }

    internal static bool TryParseRogScopeBattery(ReadOnlySpan<byte> reply, out int percent, out bool charging)
    {
        percent = -1;
        charging = false;
        if (reply.Length != 64 || !reply[..3].SequenceEqual(new byte[] { 2, 0x12, 1 }) ||
            reply[6] > 100 || reply[9] > 1 || reply[3..].IndexOfAnyExcept((byte)0) < 0) return false;
        percent = reply[6];
        charging = reply[9] == 1;
        return true;
    }

    internal static ReferenceBatteryProtocol.Reading ReadRogScope(string pairingPath, string batteryPath, BatteryReplay.Trace trace = null)
    {
        if (InstanceScope.Current == null || !InstanceScope.Allows(pairingPath) || !InstanceScope.Allows(batteryPath))
            return ReferenceBatteryProtocol.Reading.Failed("Omni 接收器实例不匹配");

        return ReferenceQueryGate.Run(pairingPath, () =>
        {
            var paired = ReadRogOmniReply(pairingPath, 1, 0xA0, 0);
            bool identified = RogOmniHasScopeRx(paired);
            trace?.Add("omni-paired", identified, raw: paired, keep: paired != null);
            if (!identified)
                return ReferenceBatteryProtocol.Reading.Failed(paired == null ? "Omni 配对列表无应答" : "Omni 配对列表未报告 1B7A");
            byte[] battery;
            try { battery = ReadRogOmniReply(batteryPath, 2, 0x12, 1); }
            catch (IOException ex)
            {
                return new(-1, false, false, ReferenceBatteryProtocol.State.Failed, ex.Message, RogScopeRxWireless);
            }
            bool decoded = TryParseRogScopeBattery(battery, out int percent, out bool charging);
            trace?.Add("omni-battery", decoded, raw: battery, keep: battery != null);
            if (!decoded)
                return new(-1, false, false, ReferenceBatteryProtocol.State.Sleep, "Omni 已配对 1B7A，未取得有效在线电量", RogScopeRxWireless);
            return new(percent, charging, true, charging ? ReferenceBatteryProtocol.State.Charging :
                ReferenceBatteryProtocol.State.Online, "Omni 键盘电量 02/12/01", RogScopeRxWireless);
        }, 2000);
    }

    private static byte[] ReadRogOmniReply(string path, byte reportId, byte command, byte subcommand)
    {
        IntPtr handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            throw new IOException("Omni 接口打开失败 Win32=" + Marshal.GetLastWin32Error());
        IntPtr signal = IntPtr.Zero;
        try
        {
            var attrs = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
            var caps = Caps(handle);
            if (!HidD_GetAttributes(handle, ref attrs) || !IsRogOmni(attrs.VendorID, attrs.ProductID) ||
                !IsRogOmniChannel(path, caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.OutputReportByteLength, reportId))
                throw new IOException("Omni 集合与已知查询通道不符");
            signal = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
            if (signal == IntPtr.Zero) throw new IOException("Omni 查询事件创建失败");
            var response = new byte[64];
            int drain = 0;
            while (drain++ < 32 && Transfer(handle, signal, response, false, 1, out int queued) && queued > 0) { }
            if (drain > 32) throw new IOException("Omni 输入队列未清空");

            var request = new byte[64];
            request[0] = reportId;
            request[1] = command;
            request[2] = subcommand;
            if (!Transfer(handle, signal, request, true, 300, out int written) || written != request.Length)
                throw new IOException("Omni 查询写入失败");
            long deadline = Environment.TickCount64 + 500;
            while (Environment.TickCount64 < deadline)
            {
                int timeout = (int)Math.Max(1, deadline - Environment.TickCount64);
                if (!Transfer(handle, signal, response, false, timeout, out int received)) return null;
                if (received != 64 || response[0] != reportId) continue;
                if (response[1] == 0xFF && response[2] == 0xAA) return null;
                if (response[1] == command && response[2] == subcommand && !response.AsSpan().SequenceEqual(request))
                    return response;
            }
            return null;
        }
        finally
        {
            if (signal != IntPtr.Zero) CloseHandle(signal);
            CloseHandle(handle);
        }
    }
}
