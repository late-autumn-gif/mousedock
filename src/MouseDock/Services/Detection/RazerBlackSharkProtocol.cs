using System;

namespace MouseDock.Services.Detection;

internal static class RazerBlackSharkProtocol
{
    internal const string Protocol = "RazerBlackSharkV2HyperSpeed";
    internal const string Model = "Razer BlackShark V2 HyperSpeed";
    internal const ushort WirelessPid = 0x0565;
    internal const ushort WiredPid = 0x056E;

    internal static bool IsCandidate(ushort vid, ushort pid) =>
        vid == 0x1532 && pid is WirelessPid or WiredPid;

    // OpenRazer #2316's descriptor: FF14/0001, Input2/Output2 = 63+ID, Feature2 = 8+ID.
    internal static bool MatchesChannel(ushort vid, ushort pid, ushort page, ushort usage,
        int input, int output, int feature) =>
        IsCandidate(vid, pid) && page == 0xFF14 && usage == 1 && input == 64 && output == 64 && feature == 9;

    // Wire format/register facts: justik13/razer-blackshark-v2-hyperspeed-webhid,
    // revision aeb0d90b35124671abf614fc0368522bcb8dcb48. This reader sends GET commands only.
    internal static byte[] Request(byte sequence, byte domain, byte command)
    {
        if (domain is not (0 or 0x80) || command is not (0x20 or 0x21 or 0x2A))
            throw new ArgumentOutOfRangeException(nameof(command));
        var request = new byte[64];
        request[0] = 2;
        request[2] = (byte)(0x60 | (sequence & 31));
        request[6] = 4;
        request[9] = domain;
        request[10] = command;
        for (int i = 0; i < 62; i++) request[62] ^= request[i];
        return request;
    }

    internal static bool TryValue(byte[] raw, byte sequence, byte domain, byte command, bool wired, out byte value)
    {
        value = 0;
        if (raw is not { Length: 64 } || raw[0] != 2 || raw[1] != 2 ||
            (raw[2] & 31) != (sequence & 31) || raw[6] != 5 || raw[8] != 0x80 ||
            (raw[9] != domain && !(wired && domain == 0 && raw[9] == 0x80)) ||
            raw[10] != command || raw[11] != 1 || raw[12] != 1) return false;
        byte checksum = 0;
        for (int i = 1; i < 62; i++) checksum ^= raw[i];
        if (raw[62] != checksum && raw[62] != (checksum ^ 2)) return false;
        value = raw[13];
        return true;
    }

    // Only known single-byte status replies with empty padding may be exported.
    internal static bool CanExport(byte[] raw) => raw is { Length: 64 } &&
        raw.AsSpan(3, 3).IndexOfAnyExcept((byte)0) < 0 && raw[7] == 0 &&
        raw.AsSpan(14, 48).IndexOfAnyExcept((byte)0) < 0 && raw[63] == 0;

    internal static ReferenceBatteryProtocol.Reading Read(ReferenceBatteryProtocol.ILink link,
        ushort pid, BatteryReplay.Trace trace = null, byte sequence = 0)
    {
        try
        {
            long deadline = link.Now + 3200;
            byte? Query(string stage, byte domain, byte command)
            {
                for (int i = 0; i < 8 && link.Now < deadline; i++)
                    if (link.Read(5) == null) break;
                byte seq = (byte)(++sequence & 31);
                var request = Request(seq, domain, command);
                bool sent = link.Now < deadline && link.Write(request, (int)Math.Max(1, deadline - link.Now));
                trace?.Add("write-" + stage, sent, seq, request, true);
                if (!sent) return null;
                long until = Math.Min(deadline, link.Now + 650);
                for (int i = 0; i < 64 && link.Now < until; i++)
                {
                    var raw = link.Read((int)Math.Max(1, until - link.Now));
                    if (raw == null) continue;
                    bool valid = TryValue(raw, seq, domain, command, pid == WiredPid, out byte value);
                    trace?.Add("input-" + stage, valid, seq, raw, valid && CanExport(raw));
                    if (valid) return value;
                }
                trace?.Add("input-" + stage, false, seq, result: "未收到匹配的成功应答");
                return null;
            }
            return ReadValues(pid, Query);
        }
        catch (Exception ex)
        {
            return ReferenceBatteryProtocol.Reading.Failed("黑鲨 V2 极速版查询失败：" + ex.GetType().Name);
        }
    }

    internal static ReferenceBatteryProtocol.Reading ReadValues(ushort pid, Func<string, byte, byte, byte?> query)
    {
        if (pid is not (WirelessPid or WiredPid))
            return ReferenceBatteryProtocol.Reading.Failed("不是已收录的黑鲨 V2 极速版");
        bool wireless = pid == WirelessPid;
        if (wireless && query("link-before", 0, 0x20) != 1)
            return ReferenceBatteryProtocol.Reading.Failed("已发现黑鲨 V2 极速版接收器，耳机未在线或在线状态未知");
        byte domain = wireless ? (byte)0x80 : (byte)0;
        byte? level = query("battery", domain, 0x21);
        if (level is null or > 100)
            return ReferenceBatteryProtocol.Reading.Failed("黑鲨 V2 极速版未取得有效电量应答");
        byte? charge = query("charging", domain, 0x2A);
        if (wireless && query("link-after", 0, 0x20) != 1)
            return ReferenceBatteryProtocol.Reading.Failed("黑鲨 V2 极速版读取期间断连，未采用电量");
        bool charging = charge > 0;
        return new(level.Value, charging, charge.HasValue,
            charging ? ReferenceBatteryProtocol.State.Charging : ReferenceBatteryProtocol.State.Online,
            "黑鲨 V2 极速版本轮 report2 电量查询" + (charge.HasValue ? "" : "；充电状态未知"), Model);
    }
}
