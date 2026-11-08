using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    internal const string RogScopeRxWired = "ROG 游侠2 RX";
    internal const string RogScopeRxWireless = "ROG 游侠2 RX 98 无线版";
    internal const string RogScopeRxWirelessUnknown = "ROG 游侠2 RX 无线版";
    /// <summary>ROG 夜魔 X（Azoth X）。</summary>
    internal const string RogAzothX = "ROG 夜魔 X";

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

    /// <summary>
    /// Omni 接收器配对表里出现过、且已确认是 ROG 键盘 2.4G 型号的编号。
    /// 1B7A 是游侠2 RX 98 的既有型号。
    /// 1C25 来自本机 0B05:1ACE 实测（配对表唯一型号 = 0x1C25，电量应答 02/12/01 → [6] 百分比）；
    /// 型号依据是 ASUS 官方 Armoury Crate 的 AZOTH-X 插件设备表
    /// （D:\ROG\AZOTH-X\index.html 的 loaderData 列出设备 ID 7205，即 0x1C25），
    /// 本机实际键盘是 ROG 夜魔 X。之前把它标成游侠2 RX 无线版是错的。
    /// 新机型只增加映射，不改判断逻辑；不认识的型号不猜名称、也不读电量。
    /// </summary>
    private static readonly Dictionary<ushort, string> RogPairedKeyboards = new()
    {
        [0x1B7A] = RogScopeRxWireless,
        [0x1C25] = RogAzothX
    };

    internal static bool IsRogOmni(ushort vid, ushort pid) => vid == 0x0B05 && pid == 0x1ACE;

    internal static bool IsRogOmniReport(ushort page, ushort usage, int input, int output, int reportId) =>
        (reportId == 1 && page == 0xFF02 || reportId == 2 && page == 0xFF00) &&
        usage == 1 && input == 64 && output == 64;

    internal static bool IsRogOmniChannel(string path, string product, ushort page, ushort usage, int input, int output, int feature, int reportId)
    {
        if (feature != 0 || !string.Equals(product?.Trim(), "ROG OMNI RECEIVER", StringComparison.OrdinalIgnoreCase) ||
            !IsRogOmniReport(page, usage, input, output, reportId)) return false;
        int start = path.IndexOf('#') + 1;
        int end = path.IndexOf('#', start);
        return start > 0 && end > start && path.AsSpan(start, end - start).Equals(
            reportId == 1 ? "vid_0b05&pid_1ace&mi_02&col01" : "vid_0b05&pid_1ace&mi_02&col02",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 解析 01 A0 00 的配对表应答：reply[3] 起是 4 字节一项（型号 PID 小端 + 2 字节附加），
    /// 遇到 0x0000 结束。返回每一项的 PID，保留原始顺序。
    /// </summary>
    internal static bool TryReadRogOmniPairing(ReadOnlySpan<byte> reply, out List<ushort> pids)
    {
        pids = new List<ushort>();
        if (reply.Length != 64 || reply[0] != 1 || reply[1] != 0xA0 || reply[2] != 0) return false;
        for (int i = 5; i + 3 < reply.Length; i += 4)
        {
            int pid = reply[i] | reply[i + 1] << 8;
            if (pid == 0) break;
            pids.Add((ushort)pid);
        }
        return pids.Count > 0;
    }

    /// <summary>配对表里是否恰好有一台已确认的 ROG 键盘型号（重复或两台都拒绝）。</summary>
    internal static bool RogOmniHasScopeRx(ReadOnlySpan<byte> reply) => RogPairedModel(reply, out _) != null;

    /// <summary>
    /// 取配对表里唯一那台已知键盘型号的名字；未知型号、或已知型号出现多次时返回 null。
    /// Omni 可以同时配鼠标和键盘，未知型号不参与判定。
    /// </summary>
    internal static string RogPairedModel(ReadOnlySpan<byte> reply, out IReadOnlyList<ushort> pids)
    {
        pids = null;
        if (!TryReadRogOmniPairing(reply, out var all)) return null;
        pids = all;
        string found = null;
        int known = 0;
        for (int i = 0; i < all.Count; i++)
        {
            if (!RogPairedKeyboards.TryGetValue(all[i], out string model)) continue;
            known++;
            found ??= model;
        }
        return known == 1 ? found : null;
    }

    internal static string RogPairedModelText(IReadOnlyList<ushort> pids)
    {
        var parts = new List<string>();
        for (int i = 0; i < pids.Count; i++)
        {
            string model = RogPairedKeyboards.TryGetValue(pids[i], out var known)
                ? known
                : "未知型号 0x" + pids[i].ToString("X4");
            if (!parts.Contains(model)) parts.Add(model);
        }
        return string.Join("、", parts);
    }

    internal static bool RogOmniPairingUnchanged(ReadOnlySpan<byte> before, ReadOnlySpan<byte> after)
    {
        if (!TryReadRogOmniPairing(before, out var a) || !TryReadRogOmniPairing(after, out var b)) return false;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
        return true;
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
            trace?.Add("omni-paired", paired != null, raw: paired, keep: paired != null);
            if (paired == null)
                return ReferenceBatteryProtocol.Reading.Failed("Omni 配对列表无应答");
            if (!TryReadRogOmniPairing(paired, out var pids))
                return ReferenceBatteryProtocol.Reading.Failed("Omni 配对列表格式不符（期望 01 A0 00 帧）");
            string model = RogPairedModel(paired, out _);
            if (model == null)
                return ReferenceBatteryProtocol.Reading.Failed(
                    "Omni 已配对，但没有唯一的已确认 ROG 键盘型号：" + RogPairedModelText(pids));
            byte[] battery;
            try { battery = ReadRogOmniReply(batteryPath, 2, 0x12, 1); }
            catch (IOException ex)
            {
                return new(-1, false, false, ReferenceBatteryProtocol.State.Failed, ex.Message, model);
            }
            bool decoded = TryParseRogScopeBattery(battery, out int percent, out bool charging);
            trace?.Add("omni-battery", decoded, raw: battery, keep: battery != null);
            if (!decoded)
                return new(-1, false, false, ReferenceBatteryProtocol.State.Sleep,
                    "Omni 已配对 " + RogPairedModelText(pids) + "，未取得有效在线电量", model);
            var confirmed = ReadRogOmniReply(pairingPath, 1, 0xA0, 0);
            bool unchanged = RogOmniPairingUnchanged(paired, confirmed);
            trace?.Add("omni-paired-after", unchanged, raw: confirmed, keep: confirmed != null);
            if (!unchanged)
                return ReferenceBatteryProtocol.Reading.Failed("Omni 配对列表变化或复核失败，未采用本次电量");
            return new(percent, charging, true, charging ? ReferenceBatteryProtocol.State.Charging :
                ReferenceBatteryProtocol.State.Online,
                "Omni 键盘电量 02/12/01 配对=" + RogPairedModelText(pids) +
                " PID=" + string.Join("/", pids.ConvertAll(p => "0x" + p.ToString("X4"))), model);
        }, 3500);
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
                !IsRogOmniChannel(path, HidDiscovery.ReadHidProduct(handle, path), caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength, reportId))
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
            long deadline = Environment.TickCount64 + 700;
            while (Environment.TickCount64 < deadline)
            {
                int timeout = (int)Math.Max(1, deadline - Environment.TickCount64);
                if (!Transfer(handle, signal, response, false, timeout, out int received)) return null;
                if (received != 64 || response[0] != reportId) continue;
                if (response[1] == 0xFF && response[2] == 0xAA) return null;
                if (response[1] == command && response[2] == subcommand && !response.AsSpan().SequenceEqual(request) &&
                    (reportId == 1 || TryParseRogScopeBattery(response, out _, out _)))
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
